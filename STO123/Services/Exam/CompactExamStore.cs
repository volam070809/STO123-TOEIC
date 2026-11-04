using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Exam;

public sealed record CompactAnswer(string? Selected, bool Flagged);
public sealed record CompactExamData(int Version, List<int> QuestionIds, Dictionary<int, CompactAnswer> Answers,
    Dictionary<int, string>? AnswerKeys = null, Dictionary<int, int>? PartNumbers = null,
    HashSet<int>? FourOptionQuestionIds = null);

/// <summary>Stores answers on the attempt; versioned content is read from its immutable Blob.</summary>
public sealed class CompactExamStore(ToeicDbContext db, ExamVersionStore? versions = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static CompactExamData Decode(string? value)
    {
        try
        {
            var data = JsonSerializer.Deserialize<CompactExamData>(value ?? "", Json);
            var ids = data?.QuestionIds?.ToHashSet();
            if (data is { Version: 1, QuestionIds.Count: 200 } && data.Answers is not null &&
                ids is { Count: 200 } && ids.All(id => id > 0) &&
                data.Answers.Keys.All(ids.Contains) &&
                (data.AnswerKeys is null || data.AnswerKeys.Count == 200 &&
                    data.AnswerKeys.Keys.All(ids.Contains)) &&
                (data.PartNumbers is null || data.PartNumbers.Count == 200 &&
                    data.PartNumbers.Keys.All(ids.Contains) &&
                    data.PartNumbers.Values.All(part => part is >= 1 and <= 7)) &&
                (data.FourOptionQuestionIds is null || data.FourOptionQuestionIds.All(ids.Contains))) return data;
        }
        catch (JsonException) { }
        throw new ExamProblem("INVALID_EXAM_DATA", "Dữ liệu bài thi không hợp lệ. Vui lòng liên hệ quản trị viên.", 409);
    }
    public static string Encode(CompactExamData data) => JsonSerializer.Serialize(data, Json);

    public async Task<int> CreateAsync(int learnerId, int examId, string kind,
        IReadOnlyList<PlannedUnit> plan, CancellationToken ct)
    {
        var ids = plan.SelectMany(unit => unit.Questions.Select(q => q.MaCauHoi)).ToList();
        if (ids.Count != 200 || ids.Distinct().Count() != 200)
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Đề thi phải có đúng 200 câu hỏi khác nhau.", 409);
        var now = DateTime.UtcNow;
        var row = new KetQuaLamBai {
            MaDeThi = examId, MaHocVien = learnerId, MaLuotLam = Guid.NewGuid(), LoaiBaiLam = kind,
            TrangThai = ExamCore.Active, NgayLamBai = now,
            HetHanLuc = now.AddMinutes(ExamCore.DurationMinutes),
            ThoiGianConLaiGiay = ExamCore.DurationMinutes * 60, BatDauPhienLuc = now,
            DuLieuBaiLamJson = Encode(new CompactExamData(1, ids, [],
                plan.SelectMany(unit => unit.Questions).ToDictionary(q => q.MaCauHoi, q => q.PhuongAnDung),
                plan.SelectMany(unit => unit.Questions.Select(q => (q.MaCauHoi, unit.Part)))
                    .ToDictionary(item => item.MaCauHoi, item => item.Part),
                plan.SelectMany(unit => unit.Questions)
                    .Where(q => !string.IsNullOrWhiteSpace(q.PhuongAnD))
                    .Select(q => q.MaCauHoi).ToHashSet()))
        };
        db.KetQuaLamBai.Add(row);
        await db.SaveChangesAsync(ct);
        return row.MaKetQua;
    }

    public async Task<int> CreateVersionedAsync(int learnerId, PhienBanDeThi version,
        VersionedExam snapshot, string kind, CancellationToken ct)
    {
        if (snapshot.Questions.Count != 200 || snapshot.ExamId != version.MaDeThi ||
            snapshot.Version != version.SoPhienBan)
            throw new ExamProblem("EXAM_VERSION_CORRUPT", "Phiên bản đề không hợp lệ.", 503);
        var ids = snapshot.Questions.OrderBy(q => q.Order).Select(q => q.Id).ToList();
        if (ids.Distinct().Count() != 200)
            throw new ExamProblem("EXAM_VERSION_CORRUPT", "Phiên bản đề có câu hỏi trùng.", 503);
        var now = DateTime.UtcNow;
        var row = new KetQuaLamBai {
            MaDeThi = version.MaDeThi, MaPhienBanDeThi = version.MaPhienBanDeThi,
            MaHocVien = learnerId, MaLuotLam = Guid.NewGuid(), LoaiBaiLam = kind,
            TrangThai = ExamCore.Active, NgayLamBai = now,
            HetHanLuc = now.AddMinutes(ExamCore.DurationMinutes),
            ThoiGianConLaiGiay = ExamCore.DurationMinutes * 60, BatDauPhienLuc = now,
            DuLieuBaiLamJson = Encode(new CompactExamData(1, ids, [],
                snapshot.Questions.ToDictionary(q => q.Id, q => q.Correct),
                snapshot.Questions.ToDictionary(q => q.Id, q => q.Part),
                snapshot.Questions.Where(q => q.D is not null).Select(q => q.Id).ToHashSet()))
        };
        db.KetQuaLamBai.Add(row);
        await db.SaveChangesAsync(ct);
        return row.MaKetQua;
    }

    public async Task<ExamSnapshot> LoadAsync(int id, int learnerId, SnapshotView view, bool resume, CancellationToken ct)
    {
        if (view != SnapshotView.Active)
        {
            var saved = await db.KetQuaLamBai.AsNoTracking().Include(x => x.MaDeThiNavigation)
                .SingleOrDefaultAsync(x => x.MaKetQua == id, ct);
            ValidateOwner(saved, learnerId);
            return await BuildSnapshotAsync(saved!, Decode(saved!.DuLieuBaiLamJson), view, ct);
        }
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var row = await db.KetQuaLamBai
            .FromSqlInterpolated($"SELECT * FROM dbo.KetQuaLamBai WITH (UPDLOCK, ROWLOCK) WHERE MaKetQua = {id}")
            .Include(x => x.MaDeThiNavigation).SingleOrDefaultAsync(ct);
        ValidateOwner(row, learnerId);
        if (view == SnapshotView.Active && row.TrangThai is ExamCore.Active or "BO_DO")
        {
            var now = DateTime.UtcNow;
            if (ExamTimer.IsStale(row, now)) ExamTimer.Charge(row, now, false);
            if (resume && row.ThoiGianConLaiGiay > 0)
            { row.TrangThai = ExamCore.Active; row.BatDauPhienLuc = now; }
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        var data = Decode(row.DuLieuBaiLamJson);
        return await BuildSnapshotAsync(row, data, view, ct);
    }

    private static void ValidateOwner([NotNull] KetQuaLamBai? row, int learnerId)
    {
        if (row is null) throw new ExamProblem("ATTEMPT_NOT_FOUND", "Không tìm thấy lượt làm bài.", 404);
        if (row.MaHocVien != learnerId) throw new ExamProblem("ATTEMPT_FORBIDDEN", "Bạn không có quyền xem lượt làm bài này.", 403);
        if (row.LoaiBaiLam is not (ExamCore.Mock or ExamCore.Placement) || row.DuLieuBaiLamJson is null)
            throw new ExamProblem("ATTEMPT_NOT_FOUND", "Không tìm thấy lượt làm bài.", 404);
    }

    public async Task<ExamSnapshot> BuildSnapshotAsync(KetQuaLamBai row, CompactExamData data,
        SnapshotView view, CancellationToken ct)
    {
        if (row.MaPhienBanDeThi is { } versionId)
        {
            if (versions is null)
                throw new ExamProblem("EXAM_VERSION_UNAVAILABLE", "Dịch vụ phiên bản đề chưa sẵn sàng.", 503);
            var version = await db.PhienBanDeThi.AsNoTracking().SingleOrDefaultAsync(
                v => v.MaPhienBanDeThi == versionId && v.MaDeThi == row.MaDeThi, ct)
                ?? throw new ExamProblem("EXAM_VERSION_MISSING", "Không tìm thấy phiên bản của lượt thi.", 503);
            var snapshot = await versions.ReadAsync(version, ct);
            if (!data.QuestionIds.SequenceEqual(snapshot.Questions.OrderBy(q => q.Order).Select(q => q.Id)))
                throw new ExamProblem("EXAM_VERSION_CORRUPT", "Lượt thi không khớp phiên bản đề đã lưu.", 503);
            return ExamVersionStore.ToAttempt(snapshot, row, data, view);
        }
        if (view == SnapshotView.Result && data.AnswerKeys is { Count: 200 } &&
            data.PartNumbers is { Count: 200 })
        {
            var gradingQuestions = data.QuestionIds.Select((id, index) => new CauHoiLuotLam {
                MaCauHoiLuotLam = id, MaCauHoiGoc = id, ThuTu = index + 1,
                MaPartNavigation = new PartTOEIC { SoPart = data.PartNumbers[id] },
                PhuongAnDung = data.AnswerKeys[id]
            }).ToList();
            var savedAnswers = data.Answers.ToDictionary(pair => pair.Key, pair => new ChiTietKetQua {
                MaCauHoiLuotLam = pair.Key, DapAnChon = pair.Value.Selected,
                DanhDau = pair.Value.Flagged
            });
            return new ExamSnapshot(row, row.MaDeThiNavigation?.TenDe, [], gradingQuestions, savedAnswers);
        }
        var source = await db.CauHoi.AsNoTracking().AsSingleQuery()
            .Where(q => data.QuestionIds.Contains(q.MaCauHoi))
            .Include(q => q.MaPartNavigation)
            .Include(q => q.NhomCauHoi).ThenInclude(n => n.MaNguLieuNavigation).ThenInclude(n => n.NguLieuTaiLieu)
            .ToDictionaryAsync(q => q.MaCauHoi, ct);
        if (source.Count != 200)
            throw new ExamProblem("SOURCE_QUESTION_MISSING", "Một số câu hỏi trong ngân hàng đã bị xóa.", 409);
        var questions = new List<CauHoiLuotLam>(200);
        var groups = new List<NhomLuotLam>();
        var seenGroups = new HashSet<int>();
        var partOrders = new Dictionary<int, int>();
        foreach (var id in data.QuestionIds)
        {
            var q = source[id];
            var part = q.MaPartNavigation?.SoPart ?? 0;
            partOrders[part] = partOrders.GetValueOrDefault(part) + 1;
            var groupId = q.NhomCauHoi?.MaNguLieu;
            if (groupId is { } gid && seenGroups.Add(gid))
            {
                var media = q.NhomCauHoi!.MaNguLieuNavigation;
                groups.Add(new NhomLuotLam {
                    MaNhomLuotLam = gid, ThuTu = groups.Count + 1,
                    MaPartNavigation = new PartTOEIC { SoPart = part },
                    NoiDungNguLieu = media.NoiDungNguLieu,
                    NoiDungDich = view == SnapshotView.Review ? media.NoiDungDich : null,
                    DuongDanAudio = media.DuongDanAudio,
                    DuongDanAnh = media.DuongDanAnh,
                    TaiLieuJson = media.NguLieuTaiLieu.Count == 0 ? null : ExamDocumentCodec.Encode(
                        media.NguLieuTaiLieu.Select(d => new ExamDocumentSnapshot(d.ThuTu, d.LoaiTaiLieu,
                            d.NoiDung, d.DuongDanAnh)))
                });
            }
            var hide = view == SnapshotView.Active && part is 1 or 2;
            questions.Add(new CauHoiLuotLam {
                MaCauHoiLuotLam = id, MaCauHoiGoc = id, MaNhomLuotLam = groupId,
                ThuTu = questions.Count + 1, ThuTuTrongPart = partOrders[part],
                MaPartNavigation = new PartTOEIC { SoPart = part },
                NoiDung = hide ? null : q.NoiDung,
                PhuongAnA = hide ? null : q.PhuongAnA, PhuongAnB = hide ? null : q.PhuongAnB,
                PhuongAnC = hide ? null : q.PhuongAnC, PhuongAnD = hide ? null : q.PhuongAnD,
                PhuongAnDung = data.AnswerKeys?.GetValueOrDefault(id) ?? q.PhuongAnDung,
                GiaiThich = q.GiaiThich
            });
        }
        var answers = data.Answers.ToDictionary(pair => pair.Key, pair => new ChiTietKetQua {
            MaCauHoiLuotLam = pair.Key, DapAnChon = pair.Value.Selected,
            DanhDau = pair.Value.Flagged
        });
        return new ExamSnapshot(row, row.MaDeThiNavigation?.TenDe, groups, questions, answers);
    }
}
