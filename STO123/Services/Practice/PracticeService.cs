using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Practice;
using STO123.Models;

namespace STO123.Services.Practice;

public sealed class PracticeService(ToeicDbContext db)
{
    private const string Published = "XUAT_BAN";
    private const string PracticeType = "PRACTICE";
    private const string InProgress = "DANG_LAM";

    // START PRACTICE

    public async Task<object> StartAsync(
        int maHocVien,
        StartPracticeRequest request,
        CancellationToken ct)
    {
        // 1. VALIDATE REQUEST
        var part = await db.PartTOEIC
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.MaPart == request.MaPart,
                ct);

        if (part is null)
        {
            throw new PracticeProblem(
                "PART_NOT_FOUND",
                "Không tìm thấy Part.",
                404);
        }

        if (request.SoCau <= 0)
        {
            throw new PracticeProblem(
                "INVALID_QUESTION_COUNT",
                "Số câu phải lớn hơn 0.",
                400);
        }

        // Phải chọn ít nhất một độ khó
        // Chỉ cho phép 1 = Dễ, 2 = Trung bình, 3 = Khó
        if (request.DoKho is < 1 or > 3)
        {
            throw new PracticeProblem(
                "INVALID_DIFFICULTY",
                "Độ khó chỉ được phép là 1, 2 hoặc 3.",
                400);
        }


        // =====================================================
        // 2. LẤY CÂU HỎI
        //
        // Chỉ lấy:
        // - đúng Part
        // - XUAT_BAN
        // - LUYEN_TAP
        // =====================================================

        var questions = await db.CauHoi
            .AsNoTracking()
            .Where(x =>
                x.MaPart == request.MaPart &&
                x.TrangThai == Published &&
                x.LoaiCauHoi == "LUYEN_TAP")
            .ToListAsync(ct);

        if (questions.Count == 0)
        {
            throw new PracticeProblem(
                "NO_PRACTICE_QUESTIONS",
                "Part này chưa có câu hỏi luyện tập.",
                409);
        }
        // 3. CHỌN CÂU

        List<SelectedQuestion> selected;

        if (part.SoPart == 3)
        {
            // Part 3:
            // Một group = 3 câu
            // Không được tách riêng từng câu.
            selected = await SelectPart3Async(
                questions,
                request,
                ct);
        }
        else
        {
            // Part 1,2,4,5,6,7
            selected = await SelectNormalPartAsync(
                questions,
                request,
                part.SoPart,
                ct);
        }

        if (selected.Count == 0)
        {
            throw new PracticeProblem(
                "NO_SELECTED_QUESTIONS",
                "Không có câu hỏi phù hợp với lựa chọn.",
                409);
        }

        // =====================================================
        // 4. TẠO TRANSACTION
        // =====================================================

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        try
        {
            // =================================================
            // 5. TẠO KETQUA LÀM BÀI
            // =================================================

            var ketQua = new KetQuaLamBai
            {
                MaHocVien = maHocVien,
                MaDeThi = null,
                LoaiBaiLam = PracticeType,
                TrangThai = InProgress,
                NgayLamBai = DateTime.UtcNow
            };

            db.KetQuaLamBai.Add(ketQua);

            await db.SaveChangesAsync(ct);

            // =================================================
            // 6. TẠO NHOMLUOTLAM
            //
            // Logic giống kiến trúc đề thi của nhóm:
            //
            // NguLieu
            //      ↓
            // NhomLuotLam
            //
            // Một NguLieu chỉ tạo một NhomLuotLam
            // trong cùng một phiên.
            // =================================================

            var groups =
                new Dictionary<int, NhomLuotLam>();

            var groupOrder = 0;

            foreach (var item in selected)
            {
                // Part 5 có thể không có NguLieu
                if (item.MaNguLieu is null ||
                    item.NguLieu is null)
                {
                    continue;
                }

                var maNguLieu = item.MaNguLieu.Value;

                // Đã tạo group cho NguLieu này rồi
                if (groups.ContainsKey(maNguLieu))
                {
                    continue;
                }

                var resource = item.NguLieu;

                var group = new NhomLuotLam
                {
                    MaKetQua = ketQua.MaKetQua,

                    MaNguLieuGoc =
                        resource.MaNguLieu,

                    MaPart =
                        part.MaPart,

                    ThuTu =
                        ++groupOrder,

                    NoiDungNguLieu =
                        resource.NoiDungNguLieu,

                    NoiDungDich =
                        resource.NoiDungDich,

                    DuongDanAudio =
                        resource.DuongDanAudio,

                    DuongDanAnh =
                        resource.DuongDanAnh
                };

                db.NhomLuotLam.Add(group);

                groups.Add(
                    maNguLieu,
                    group);
            }

            await db.SaveChangesAsync(ct);

            // =================================================
            // 7. TẠO CAUHOILUOTLAM
            //
            // Quan trọng:
            //
            // MaNhomLuotLam
            // sẽ trỏ tới group vừa tạo ở trên.
            // =================================================

            var cauHoiLuotLamList =
                new List<CauHoiLuotLam>();

            var globalOrder = 0;

            // Practice chỉ có một Part trong một phiên
            var partOrder = 0;

            foreach (var item in selected)
            {
                int? maNhomLuotLam = null;

                // Nếu câu có NguLieu
                // thì tìm group tương ứng.
                if (item.MaNguLieu is not null &&
                    groups.TryGetValue(
                        item.MaNguLieu.Value,
                        out var group))
                {
                    maNhomLuotLam =
                        group.MaNhomLuotLam;
                }

                var cauHoiLuotLam =
                    new CauHoiLuotLam
                    {
                        MaKetQua =
                            ketQua.MaKetQua,

                        // Đây chính là phần quan trọng
                        // để liên kết câu với NhomLuotLam.
                        MaNhomLuotLam =
                            maNhomLuotLam,

                        MaCauHoiGoc =
                            item.Question.MaCauHoi,

                        MaPart =
                            item.Question.MaPart,

                        // Thứ tự toàn bộ phiên
                        ThuTu =
                            ++globalOrder,

                        // Thứ tự trong Part
                        ThuTuTrongPart =
                            ++partOrder,

                        NoiDung =
                            item.Question.NoiDung,

                        PhuongAnA =
                            item.Question.PhuongAnA,

                        PhuongAnB =
                            item.Question.PhuongAnB,

                        PhuongAnC =
                            item.Question.PhuongAnC,

                        PhuongAnD =
                            item.Question.PhuongAnD,

                        // Snapshot đáp án đúng
                        // Không trả về frontend
                        PhuongAnDung =
                            item.Question.PhuongAnDung,

                        GiaiThich =
                            item.Question.GiaiThich
                    };

                cauHoiLuotLamList.Add(
                    cauHoiLuotLam);
            }

            db.CauHoiLuotLam.AddRange(
                cauHoiLuotLamList);

            await db.SaveChangesAsync(ct);

            // =================================================
            // 8. TẠO CHITIETKETQUA
            // =================================================

            foreach (var question in cauHoiLuotLamList)
            {
                db.ChiTietKetQua.Add(
                    new ChiTietKetQua
                    {
                        MaCauHoiLuotLam =
                            question.MaCauHoiLuotLam,

                        DapAnChon = null,

                        DanhDau = false
                    });
            }

            await db.SaveChangesAsync(ct);

            // =================================================
            // 9. COMMIT
            // =================================================

            await transaction.CommitAsync(ct);

            // =================================================
            // 10. TRẢ CÂU HỎI
            // =================================================

            return await BuildResponseAsync(
                ketQua.MaKetQua,
                request.MaPart,
                ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    // =========================================================
    // ANSWER
    // =========================================================

    public async Task<object> AnswerAsync(
        int maHocVien,
        int maKetQua,
        SubmitPracticeAnswerRequest request,
        CancellationToken ct)
    {
        // 1. Kiểm tra đáp án
        var dapAn =
            request.DapAnChon
                .Trim()
                .ToUpperInvariant();

        if (dapAn is not ("A" or "B" or "C" or "D"))
        {
            throw new PracticeProblem(
                "INVALID_ANSWER",
                "Đáp án phải là A, B, C hoặc D.",
                400);
        }

        // 2. Lấy câu hỏi
        var question =
            await db.CauHoiLuotLam
                .FirstOrDefaultAsync(
                    x =>
                        x.MaCauHoiLuotLam ==
                            request.MaCauHoiLuotLam &&
                        x.MaKetQua == maKetQua &&
                        x.MaKetQuaNavigation.MaHocVien ==
                            maHocVien,
                    ct);

        if (question is null)
        {
            throw new PracticeProblem(
                "QUESTION_NOT_FOUND",
                "Không tìm thấy câu hỏi trong phiên luyện tập.",
                404);
        }

        // 3. Kiểm tra phiên
        var ketQua =
            await db.KetQuaLamBai
                .FirstOrDefaultAsync(
                    x =>
                        x.MaKetQua == maKetQua &&
                        x.MaHocVien == maHocVien,
                    ct);

        if (ketQua is null)
        {
            throw new PracticeProblem(
                "PRACTICE_NOT_FOUND",
                "Không tìm thấy phiên luyện tập.",
                404);
        }

        if (ketQua.LoaiBaiLam != PracticeType)
        {
            throw new PracticeProblem(
                "NOT_PRACTICE",
                "Đây không phải phiên luyện tập.",
                400);
        }

        if (ketQua.TrangThai != InProgress)
        {
            throw new PracticeProblem(
                "PRACTICE_FINISHED",
                "Phiên luyện tập đã kết thúc.",
                400);
        }

        // 4. ChiTietKetQua
        var detail =
            await db.ChiTietKetQua
                .FirstOrDefaultAsync(
                    x =>
                        x.MaCauHoiLuotLam ==
                            question.MaCauHoiLuotLam,
                    ct);

        if (detail is null)
        {
            throw new PracticeProblem(
                "RESULT_DETAIL_NOT_FOUND",
                "Không tìm thấy chi tiết kết quả.",
                404);
        }

        // 5. Lưu đáp án
        detail.DapAnChon = dapAn;

        // 6. Chấm ngay
        var dung =
            dapAn == question.PhuongAnDung;

        await db.SaveChangesAsync(ct);

        // 7. Trả kết quả
        return new
        {
            MaCauHoiLuotLam =
                question.MaCauHoiLuotLam,

            DapAnChon =
                dapAn,

            Dung =
                dung,

            DapAnDung =
                question.PhuongAnDung,

            GiaiThich =
                question.GiaiThich
        };
    }

    // =========================================================
    // SUBMIT
    // =========================================================

    public async Task<object> SubmitAsync(
        int maHocVien,
        int maKetQua,
        CancellationToken ct)
    {
        var ketQua =
            await db.KetQuaLamBai
                .FirstOrDefaultAsync(
                    x =>
                        x.MaKetQua == maKetQua &&
                        x.MaHocVien == maHocVien,
                    ct);

        if (ketQua is null)
        {
            throw new PracticeProblem(
                "PRACTICE_NOT_FOUND",
                "Không tìm thấy phiên luyện tập.",
                404);
        }

        if (ketQua.LoaiBaiLam != PracticeType)
        {
            throw new PracticeProblem(
                "NOT_PRACTICE",
                "Đây không phải phiên luyện tập.",
                400);
        }

        if (ketQua.TrangThai != InProgress)
        {
            throw new PracticeProblem(
                "PRACTICE_FINISHED",
                "Phiên luyện tập đã được nộp.",
                400);
        }

        var questions =
            await db.CauHoiLuotLam
                .AsNoTracking()
                .Where(x => x.MaKetQua == maKetQua)
                .Include(x => x.ChiTietKetQua)
                .ToListAsync(ct);

        if (questions.Count == 0)
        {
            throw new PracticeProblem(
                "NO_QUESTIONS",
                "Phiên luyện tập không có câu hỏi.",
                400);
        }

        var soCauDung =
            questions.Count(
                x =>
                    x.ChiTietKetQua != null &&
                    x.ChiTietKetQua.DapAnChon != null &&
                    x.ChiTietKetQua.DapAnChon ==
                        x.PhuongAnDung);

        var soCau =
            questions.Count;

        var soCauSai =
            soCau - soCauDung;

        var diemTong =
            (int)Math.Round(
                soCauDung * 100.0 / soCau);

        ketQua.TrangThai = "DA_NOP";

        ketQua.NgayNopBai =
            DateTime.UtcNow;

        ketQua.ThoiGianLamBai =
            (int)Math.Round(
                (DateTime.UtcNow -
                    ketQua.NgayLamBai)
                .TotalSeconds);

        ketQua.DiemTong =
            diemTong;

        await db.SaveChangesAsync(ct);

        return new
        {
            MaKetQua =
                ketQua.MaKetQua,

            SoCau =
                soCau,

            SoCauDung =
                soCauDung,

            SoCauSai =
                soCauSai,

            DiemTong =
                diemTong,

            TrangThai =
                ketQua.TrangThai
        };
    }

    // =========================================================
    // RESULT
    // =========================================================

    public async Task<object> GetResultAsync(
        int maHocVien,
        int maKetQua,
        CancellationToken ct)
    {
        var ketQua =
            await db.KetQuaLamBai
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.MaKetQua == maKetQua &&
                        x.MaHocVien == maHocVien,
                    ct);

        if (ketQua is null)
        {
            throw new PracticeProblem(
                "PRACTICE_NOT_FOUND",
                "Không tìm thấy phiên luyện tập.",
                404);
        }

        if (ketQua.LoaiBaiLam != PracticeType)
        {
            throw new PracticeProblem(
                "NOT_PRACTICE",
                "Đây không phải phiên luyện tập.",
                400);
        }

        if (ketQua.TrangThai != "DA_NOP")
        {
            throw new PracticeProblem(
                "PRACTICE_NOT_SUBMITTED",
                "Bài luyện tập chưa được nộp.",
                400);
        }

        var questions =
            await db.CauHoiLuotLam
                .AsNoTracking()
                .Where(x => x.MaKetQua == maKetQua)
                .OrderBy(x => x.ThuTu)
                .Include(x => x.ChiTietKetQua)
                .ToListAsync(ct);

        return new
        {
            MaKetQua =
                ketQua.MaKetQua,

            MaPart =
                questions
                    .FirstOrDefault()
                    ?.MaPart,

            SoCau =
                questions.Count,

            SoCauDung =
                questions.Count(
                    x =>
                        x.ChiTietKetQua?.DapAnChon ==
                        x.PhuongAnDung),

            SoCauSai =
                questions.Count(
                    x =>
                        x.ChiTietKetQua?.DapAnChon !=
                        x.PhuongAnDung),

            DiemTong =
                ketQua.DiemTong,

            TrangThai =
                ketQua.TrangThai,

            NgayLamBai =
                ketQua.NgayLamBai,

            NgayNopBai =
                ketQua.NgayNopBai,

            ThoiGianLamBai =
                ketQua.ThoiGianLamBai,

            Questions =
                questions.Select(
                    x => new
                    {
                        x.MaCauHoiLuotLam,
                        x.ThuTu,
                        x.NoiDung,
                        x.PhuongAnA,
                        x.PhuongAnB,
                        x.PhuongAnC,
                        x.PhuongAnD,

                        DapAnChon =
                            x.ChiTietKetQua
                                ?.DapAnChon,

                        DapAnDung =
                            x.PhuongAnDung,

                        Dung =
                            x.ChiTietKetQua
                                ?.DapAnChon ==
                            x.PhuongAnDung,

                        x.GiaiThich
                    })
        };
    }

    // =========================================================
    // HISTORY
    // =========================================================

    public async Task<object> GetHistoryAsync(
        int maHocVien,
        CancellationToken ct)
    {
        var history =
            await db.KetQuaLamBai
                .AsNoTracking()
                .Where(
                    x =>
                        x.MaHocVien == maHocVien &&
                        x.LoaiBaiLam == PracticeType &&
                        x.TrangThai == "DA_NOP")
                .OrderByDescending(
                    x => x.NgayLamBai)
                .Select(
                    x => new
                    {
                        x.MaKetQua,
                        x.NgayLamBai,
                        x.NgayNopBai,
                        x.ThoiGianLamBai,
                        x.DiemTong,
                        x.TrangThai,

                        SoCau =
                            x.CauHoiLuotLam.Count(),

                        SoCauDung =
                            x.CauHoiLuotLam.Count(
                                q =>
                                    q.ChiTietKetQua != null &&
                                    q.ChiTietKetQua.DapAnChon != null &&
                                    q.ChiTietKetQua.DapAnChon ==
                                        q.PhuongAnDung),

                        MaPart =
                            x.CauHoiLuotLam
                                .Select(q => q.MaPart)
                                .FirstOrDefault()
                    })
                .ToListAsync(ct);

        return new
        {
            SoLanLuyenTap =
                history.Count,

            History =
                history
        };
    }

    // =========================================================
    // BOOKMARK
    // =========================================================

    public async Task<object> BookmarkAsync(
        int maHocVien,
        int maKetQua,
        BookmarkPracticeQuestionRequest request,
        CancellationToken ct)
    {
        var ketQua =
            await db.KetQuaLamBai
                .FirstOrDefaultAsync(
                    x =>
                        x.MaKetQua == maKetQua &&
                        x.MaHocVien == maHocVien,
                    ct);

        if (ketQua is null)
        {
            throw new PracticeProblem(
                "PRACTICE_NOT_FOUND",
                "Không tìm thấy phiên luyện tập.",
                404);
        }

        if (ketQua.LoaiBaiLam != PracticeType)
        {
            throw new PracticeProblem(
                "NOT_PRACTICE",
                "Đây không phải phiên luyện tập.",
                400);
        }

        if (ketQua.TrangThai != InProgress)
        {
            throw new PracticeProblem(
                "PRACTICE_FINISHED",
                "Phiên luyện tập đã kết thúc.",
                400);
        }

        var question =
            await db.CauHoiLuotLam
                .FirstOrDefaultAsync(
                    x =>
                        x.MaCauHoiLuotLam ==
                            request.MaCauHoiLuotLam &&
                        x.MaKetQua == maKetQua,
                    ct);

        if (question is null)
        {
            throw new PracticeProblem(
                "QUESTION_NOT_FOUND",
                "Không tìm thấy câu hỏi.",
                404);
        }

        var detail =
            await db.ChiTietKetQua
                .FirstOrDefaultAsync(
                    x =>
                        x.MaCauHoiLuotLam ==
                            request.MaCauHoiLuotLam,
                    ct);

        if (detail is null)
        {
            throw new PracticeProblem(
                "RESULT_DETAIL_NOT_FOUND",
                "Không tìm thấy chi tiết câu hỏi.",
                404);
        }

        detail.DanhDau =
            request.DanhDau;

        await db.SaveChangesAsync(ct);

        return new
        {
            MaCauHoiLuotLam =
                request.MaCauHoiLuotLam,

            DanhDau =
                detail.DanhDau
        };
    }

    // =========================================================
    // NORMAL PART
    //
    // Part 1, 2, 4, 5, 6, 7
    // =========================================================

    private async Task<List<SelectedQuestion>>
        SelectNormalPartAsync(
            List<CauHoi> questions,
            StartPracticeRequest request,
            int partNumber,
            CancellationToken ct)
    {
        // =====================================================
        // 1. LỌC THEO ĐỘ KHÓ
        // =====================================================

        var candidates =questions
                .Where(x => x.DoKho == request.DoKho)
                .ToList();

        if (candidates.Count == 0)
        {
            throw new PracticeProblem(
                "NO_QUESTIONS_BY_DIFFICULTY",
                "Không có câu hỏi phù hợp với độ khó đã chọn.",
                409);
        }

        if (candidates.Count < request.SoCau)
        {
            throw new PracticeProblem(
                "NOT_ENOUGH_QUESTIONS",
                $"Không đủ câu hỏi cho lựa chọn hiện tại. " +
                $"Yêu cầu {request.SoCau}, " +
                $"hiện có {candidates.Count}.",
                409);
        }

        // =====================================================
        // 2. RANDOM CÂU HỎI
        // =====================================================

        var selectedQuestions =
            candidates
                .OrderBy(
                    _ => Random.Shared.Next())
                .Take(request.SoCau)
                .ToList();

        // =====================================================
        // 3. PART 5
        //
        // Part 5 thường không có NguLieu.
        // Không cần map NhomCauHoi.
        // =====================================================

        if (partNumber == 5)
        {
            return selectedQuestions
                .Select(
                    x =>
                        new SelectedQuestion
                        {
                            Question = x,
                            MaNguLieu = null,
                            NguLieu = null!
                        })
                .ToList();
        }

        // =====================================================
        // 4. PART CÓ NGUỒN
        //
        // CauHoi
        //    ↓
        // NhomCauHoi
        //    ↓
        // NguLieu
        //
        // Đây là phần quan trọng để sau này
        // tạo NhomLuotLam.
        // =====================================================

        var questionIds =
            selectedQuestions
                .Select(x => x.MaCauHoi)
                .ToList();

        var memberships =
            await db.NhomCauHoi
                .AsNoTracking()
                .Where(
                    x =>
                        questionIds.Contains(
                            x.MaCauHoi))
                .OrderBy(x => x.ThuTu)
                .ToListAsync(ct);

        var membershipByQuestion =
            memberships
                .GroupBy(x => x.MaCauHoi)
                .ToDictionary(
                    g => g.Key,
                    g =>
                        g.OrderBy(
                            x => x.ThuTu)
                         .First());

        var resourceIds =
            memberships
                .Select(x => x.MaNguLieu)
                .Distinct()
                .ToList();

        var resources =
            await db.NguLieu
                .AsNoTracking()
                .Where(
                    x =>
                        resourceIds.Contains(
                            x.MaNguLieu))
                .ToDictionaryAsync(
                    x => x.MaNguLieu,
                    ct);

        // 5. TẠO SELECTED QUESTION
        var result =
            new List<SelectedQuestion>();

        foreach (var question in selectedQuestions)
        {
            // Không có membership
            if (!membershipByQuestion.TryGetValue(
                    question.MaCauHoi,
                    out var membership))
            {
                // Bỏ câu này vì không xác định được NguLieu
                continue;
            }

            // Không có resource
            if (!resources.TryGetValue(
                    membership.MaNguLieu,
                    out var resource))
            {
                continue;
            }

            result.Add(
                new SelectedQuestion
                {
                    Question = question,

                    MaNguLieu =
                        resource.MaNguLieu,

                    NguLieu =
                        resource
                });
        }
        // Nếu sau khi map resource bị thiếu câu
        if (result.Count < request.SoCau)
        {
            throw new PracticeProblem(
                "INVALID_QUESTION_RESOURCE",
                "Không đủ câu hỏi có NguLieu hợp lệ " +
                "để tạo phiên luyện tập.",
                409);
        }

        // 6. Trộn lại thứ tự

        return result
            .OrderBy(
                _ => Random.Shared.Next())
            .ToList();
    }

    // =========================================================
    // PART 3
    //
    // Một group = 3 câu
    // =========================================================

    private async Task<List<SelectedQuestion>>
        SelectPart3Async(
            List<CauHoi> questions,
            StartPracticeRequest request,
            CancellationToken ct)
    {
        // =====================================================
        // 1. SOCAU PHẢI LÀ BỘI SỐ CỦA 3
        // =====================================================

        if (request.SoCau % 3 != 0)
        {
            throw new PracticeProblem(
                "INVALID_PART3_COUNT",
                "Part 3 phải chọn số câu là bội số của 3.",
                400);
        }

        // =====================================================
        // 2. LẤY MEMBERSHIP
        // =====================================================

        var questionIds =
            questions
                .Select(x => x.MaCauHoi)
                .ToList();

        var memberships =
            await db.NhomCauHoi
                .AsNoTracking()
                .Where(
                    x =>
                        questionIds.Contains(
                            x.MaCauHoi))
                .ToListAsync(ct);

        // =====================================================
        // 3. LẤY NGUỒN
        // =====================================================

        var resourceIds =
            memberships
                .Select(x => x.MaNguLieu)
                .Distinct()
                .ToList();

        var resources =
            await db.NguLieu
                .AsNoTracking()
                .Where(
                    x =>
                        resourceIds.Contains(
                            x.MaNguLieu))
                .ToDictionaryAsync(
                    x => x.MaNguLieu,
                    ct);

        var questionMap =
            questions.ToDictionary(
                x => x.MaCauHoi);

        // =====================================================
        // 4. GOM GROUP
        // =====================================================

        var groups =
            memberships
                .GroupBy(
                    x => x.MaNguLieu)
                .Select(
                    g =>
                    {
                        var members =
                            g.OrderBy(
                                x => x.ThuTu)
                             .ToList();

                        var groupQuestions =
                            members
                                .Where(
                                    x =>
                                        questionMap.ContainsKey(
                                            x.MaCauHoi))
                                .Select(
                                    x =>
                                        questionMap[
                                            x.MaCauHoi])
                                .ToList();

                        return new Part3Group
                        {
                            MaNguLieu =
                                g.Key,

                            Members =
                                members,

                            Questions =
                                groupQuestions
                        };
                    })
                .Where(
                    x =>
                        x.Members.Count == 3 &&
                        x.Questions.Count == 3)
                .ToList();

        // =====================================================
        // 5. XÁC ĐỊNH ĐỘ KHÓ CỦA GROUP
        //
        // 3 câu trong group phải cùng độ khó.
        // =====================================================

        var groupsByDifficulty =
            new Dictionary<byte, List<Part3Group>>
            {
                [1] = new(),
                [2] = new(),
                [3] = new()
            };

        foreach (var group in groups)
        {
            var difficulties =
                group.Questions
                    .Select(x => x.DoKho)
                    .Distinct()
                    .ToList();

            if (difficulties.Count != 1)
                continue;

            var difficulty =
                difficulties[0];

            if (groupsByDifficulty.ContainsKey(
                    difficulty))
            {
                groupsByDifficulty[
                    difficulty]
                    .Add(group);
            }
        }

        var allowedGroups =
            groupsByDifficulty[request.DoKho];

        if (allowedGroups.Count == 0)
        {
            throw new PracticeProblem(
                "NO_PART3_GROUPS",
                "Không có nhóm câu hỏi Part 3 phù hợp với " +
                "độ khó đã chọn.",
                409);
        }

        // =====================================================
        // 7. TÍNH SỐ GROUP CẦN LẤY
        // =====================================================

        var requiredGroups =
            request.SoCau / 3;

        if (allowedGroups.Count <
            requiredGroups)
        {
            throw new PracticeProblem(
                "NOT_ENOUGH_PART3_GROUPS",
                $"Không đủ nhóm câu hỏi Part 3. " +
                $"Cần {requiredGroups} nhóm, " +
                $"hiện có {allowedGroups.Count} nhóm.",
                409);
        }

        // =====================================================
        // 8. RANDOM GROUP
        // =====================================================

        var selectedGroups =
            allowedGroups
                .OrderBy(
                    _ => Random.Shared.Next())
                .Take(requiredGroups)
                .ToList();

        // =====================================================
        // 9. RANDOM THỨ TỰ GROUP
        // =====================================================

        selectedGroups =
            selectedGroups
                .OrderBy(
                    _ => Random.Shared.Next())
                .ToList();

        // =====================================================
        // 10. FLATTEN GROUP
        //
        // QUAN TRỌNG:
        // Không random 3 câu trong group.
        //
        // Giữ ThuTu của NhomCauHoi.
        // =====================================================

        var result =
            new List<SelectedQuestion>();

        foreach (var group in selectedGroups)
        {
            // Resource phải tồn tại
            if (!resources.TryGetValue(
                    group.MaNguLieu,
                    out var resource))
            {
                continue;
            }

            foreach (var member in
                     group.Members.OrderBy(
                         x => x.ThuTu))
            {
                if (!questionMap.TryGetValue(
                        member.MaCauHoi,
                        out var question))
                {
                    continue;
                }

                result.Add(
                    new SelectedQuestion
                    {
                        Question =
                            question,

                        MaNguLieu =
                            resource.MaNguLieu,

                        NguLieu =
                            resource
                    });
            }
        }

        if (result.Count != request.SoCau)
        {
            throw new PracticeProblem(
                "INVALID_PART3_RESULT",
                "Không thể tạo đủ số câu Part 3 " +
                "theo các nhóm hợp lệ.",
                409);
        }

        return result;
    }

    // =========================================================
    // RESPONSE
    // =========================================================

    private async Task<object> BuildResponseAsync(
        int maKetQua,
        int maPart,
        CancellationToken ct)
    {
        var questions =
            await db.CauHoiLuotLam
                .AsNoTracking()
                .Where(
                    x =>
                        x.MaKetQua == maKetQua)
                .OrderBy(
                    x => x.ThuTu)
                .Select(
                    x => new PracticeQuestionDto
                    {
                        MaCauHoiLuotLam =
                            x.MaCauHoiLuotLam,

                        MaPart =
                            x.MaPart,

                        ThuTu =
                            x.ThuTu,

                        ThuTuTrongPart =
                            x.ThuTuTrongPart,

                        MaNhomLuotLam =
                            x.MaNhomLuotLam,

                        NoiDung =
                            x.NoiDung,

                        PhuongAnA =
                            x.PhuongAnA,

                        PhuongAnB =
                            x.PhuongAnB,

                        PhuongAnC =
                            x.PhuongAnC,

                        PhuongAnD =
                            x.PhuongAnD,

                        // Part 5 có thể không có group
                        NoiDungNguLieu =
                            x.MaNhomLuotLamNavigation != null
                                ? x.MaNhomLuotLamNavigation
                                    .NoiDungNguLieu
                                : null,

                        NoiDungDich =
                            x.MaNhomLuotLamNavigation != null
                                ? x.MaNhomLuotLamNavigation
                                    .NoiDungDich
                                : null,

                        DuongDanAudio =
                            x.MaNhomLuotLamNavigation != null
                                ? x.MaNhomLuotLamNavigation
                                    .DuongDanAudio
                                : null,

                        DuongDanAnh =
                            x.MaNhomLuotLamNavigation != null
                                ? x.MaNhomLuotLamNavigation
                                    .DuongDanAnh
                                : null
                    })
                .ToListAsync(ct);

        return new
        {
            MaKetQua =
                maKetQua,

            MaPart =
                maPart,

            SoCau =
                questions.Count,

            Questions =
                questions
        };
    }
}

// =========================================================
// INTERNAL TYPES
// =========================================================

public sealed class SelectedQuestion
{
    public CauHoi Question { get; set; }

    public int? MaNguLieu { get; set; }

    public NguLieu NguLieu { get; set; }

    public SelectedQuestion()
    {
    }

    public SelectedQuestion(
        CauHoi question)
    {
        Question = question;
    }
}

public sealed class Part3Group
{
    public int MaNguLieu { get; set; }

    public List<NhomCauHoi> Members { get; set; } = [];

    public List<CauHoi> Questions { get; set; } = [];
}

// =========================================================
// EXCEPTION
// =========================================================

public sealed class PracticeProblem(
    string code,
    string message,
    int statusCode)
    : Exception(message)
{
    public string Code { get; } =
        code;

    public int StatusCode { get; } =
        statusCode;
}