using System.Data;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Exam;
using STO123.Models;
using STO123.Services.Scoring;

namespace STO123.Services.Exam;

public sealed class ExamAttemptService(ToeicDbContext db, ExamGenerationService generation,
    ExamGradingService grading, IToeicScoreCalculator scoring, ILogger<ExamAttemptService> logger)
{
    public async Task<int> StartAsync(int learnerId, StartExamRequest request, CancellationToken ct)
    {
        var fixedSource = request.Source == "FIXED";
        if ((!fixedSource && request.Source != "RANDOM") || (fixedSource != request.ExamId.HasValue))
            throw new ExamProblem("INVALID_SOURCE", "Nguồn đề thi không hợp lệ.");
        var plan = await generation.BuildAsync(request.ExamId, ct);
        var partIds = await db.PartTOEIC.AsNoTracking().ToDictionaryAsync(x => x.SoPart, x => x.MaPart, ct);
        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var attempt = new KetQuaLamBai
        {
            MaHocVien = learnerId, MaDeThi = request.ExamId, MaLuotLam = Guid.NewGuid(),
            LoaiBaiLam = ExamCore.Mock, TrangThai = ExamCore.Active,
            NgayLamBai = now, HetHanLuc = now.AddMinutes(ExamCore.DurationMinutes)
        };
        db.KetQuaLamBai.Add(attempt);
        await db.SaveChangesAsync(ct);
        var global = 0;
        var groupOrder = 0;
        var partOrders = ExamCore.PartCounts.Keys.ToDictionary(x => x, _ => 0);
        foreach (var unit in plan)
        {
            NhomLuotLam? group = null;
            if (unit.Resource is { } resource)
            {
                group = new NhomLuotLam
                {
                    MaKetQua = attempt.MaKetQua, MaNguLieuGoc = resource.MaNguLieu,
                    MaPart = partIds[unit.Part], ThuTu = ++groupOrder,
                    NoiDungNguLieu = resource.NoiDungNguLieu, NoiDungDich = resource.NoiDungDich,
                    DuongDanAudio = resource.DuongDanAudio, DuongDanAnh = resource.DuongDanAnh,
                    TaiLieuJson = unit.Part == 7 ? ExamDocumentCodec.Encode(unit.Documents.Select(d =>
                        new ExamDocumentSnapshot(d.ThuTu, d.LoaiTaiLieu, d.NoiDung, d.DuongDanAnh))) : null
                };
                db.NhomLuotLam.Add(group);
                await db.SaveChangesAsync(ct);
            }
            foreach (var source in unit.Questions)
                db.CauHoiLuotLam.Add(new CauHoiLuotLam
                {
                    MaKetQua = attempt.MaKetQua, MaNhomLuotLam = group?.MaNhomLuotLam,
                    MaCauHoiGoc = source.MaCauHoi, MaPart = partIds[unit.Part],
                    ThuTu = ++global, ThuTuTrongPart = ++partOrders[unit.Part],
                    NoiDung = source.NoiDung, PhuongAnA = source.PhuongAnA,
                    PhuongAnB = source.PhuongAnB, PhuongAnC = source.PhuongAnC,
                    PhuongAnD = source.PhuongAnD, PhuongAnDung = source.PhuongAnDung,
                    GiaiThich = source.GiaiThich
                });
        }
        if (global != 200 || partOrders.Any(x => x.Value != ExamCore.PartCounts[x.Key]))
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Cấu trúc đề thi không hợp lệ.", 409);
        await db.SaveChangesAsync(ct);
        var persisted = await db.CauHoiLuotLam.AsNoTracking()
            .Where(q => q.MaKetQua == attempt.MaKetQua)
            .GroupBy(q => q.MaPart)
            .Select(g => new { PartId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        if (persisted.Sum(x => x.Count) != 200 || ExamCore.PartCounts.Any(p =>
            !persisted.Any(x => x.PartId == partIds[p.Key] && x.Count == p.Value)))
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Không thể lưu đủ câu hỏi của bài thi.", 409);
        await tx.CommitAsync(ct);
        logger.LogInformation("Created Mock attempt {AttemptId} with {QuestionCount} question occurrences", attempt.MaKetQua, global);
        return attempt.MaKetQua;
    }

    public async Task<KetQuaLamBai> OwnedAsync(int id, int learnerId, CancellationToken ct)
    {
        var attempt = await db.KetQuaLamBai.FirstOrDefaultAsync(x => x.MaKetQua == id, ct);
        if (attempt is null) throw new ExamProblem("ATTEMPT_NOT_FOUND", "Không tìm thấy lượt làm bài.", 404);
        if (attempt.MaHocVien != learnerId) throw new ExamProblem("ATTEMPT_FORBIDDEN", "Bạn không có quyền xem lượt làm bài này.", 403);
        if (attempt.LoaiBaiLam != ExamCore.Mock) throw new ExamProblem("ATTEMPT_NOT_FOUND", "Không tìm thấy lượt làm bài.", 404);
        return attempt;
    }

    private async Task<KetQuaLamBai> OwnedForUpdateAsync(int id, int learnerId, CancellationToken ct)
    {
        var attempt = await db.KetQuaLamBai
            .FromSqlInterpolated($"SELECT * FROM dbo.KetQuaLamBai WITH (UPDLOCK, ROWLOCK) WHERE MaKetQua = {id}")
            .SingleOrDefaultAsync(ct);
        if (attempt is null) throw new ExamProblem("ATTEMPT_NOT_FOUND", "Không tìm thấy lượt làm bài.", 404);
        if (attempt.MaHocVien != learnerId) throw new ExamProblem("ATTEMPT_FORBIDDEN", "Bạn không có quyền xem lượt làm bài này.", 403);
        if (attempt.LoaiBaiLam != ExamCore.Mock) throw new ExamProblem("ATTEMPT_NOT_FOUND", "Không tìm thấy lượt làm bài.", 404);
        return attempt;
    }

    public async Task<ExamAttemptDto> GetAsync(int id, int learnerId, CancellationToken ct)
    {
        var attempt = await OwnedAsync(id, learnerId, ct);
        if (attempt.TrangThai == ExamCore.Active && attempt.HetHanLuc <= DateTime.UtcNow)
        {
            await FinalizeAsync(id, learnerId, true, ct);
            await db.Entry(attempt).ReloadAsync(ct);
        }
        var questions = await Questions(id, ct);
        var groups = await db.NhomLuotLam.AsNoTracking().Include(x => x.MaPartNavigation).Where(x => x.MaKetQua == id)
            .OrderBy(x => x.ThuTu).ToListAsync(ct);
        var answers = await Answers(questions, ct);
        ExamQuestionDto Map(CauHoiLuotLam q)
        {
            answers.TryGetValue(q.MaCauHoiLuotLam, out var a);
            var part = q.MaPartNavigation.SoPart;
            return new(q.MaCauHoiLuotLam, q.ThuTu, q.ThuTuTrongPart, part,
                part == 2 ? null : q.NoiDung, part == 2 ? null : q.PhuongAnA,
                part == 2 ? null : q.PhuongAnB, part == 2 ? null : q.PhuongAnC,
                part == 2 ? null : q.PhuongAnD, a?.DapAnChon, a?.DanhDau ?? false);
        }
        var mappedGroups = groups.Select(g => new ExamGroupDto(g.MaNhomLuotLam, g.ThuTu,
            questions.First(q => q.MaNhomLuotLam == g.MaNhomLuotLam).MaPartNavigation.SoPart,
            g.MaPartNavigation?.SoPart is 1 or 2 or 3 or 4 ? null : g.NoiDungNguLieu,
            !string.IsNullOrWhiteSpace(g.DuongDanAudio), !string.IsNullOrWhiteSpace(g.DuongDanAnh),
            ExamDocumentCodec.Decode(g.TaiLieuJson).Select(d => new ExamDocumentDto(d.Order, d.Type, d.Text,
                !string.IsNullOrWhiteSpace(d.ImagePath))).ToList(),
            questions.Where(q => q.MaNhomLuotLam == g.MaNhomLuotLam).Select(Map).ToList())).ToList();
        return new(id, attempt.MaDeThi.HasValue ? "FIXED" : "RANDOM", attempt.TrangThai,
            ExamCore.Utc(attempt.NgayLamBai), attempt.HetHanLuc.HasValue ? ExamCore.Utc(attempt.HetHanLuc.Value) : null,
            questions.Count, mappedGroups, questions.Where(q => q.MaNhomLuotLam == null).Select(Map).ToList());
    }

    public async Task SaveAsync(int id, int questionId, int learnerId, SaveAnswerRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var attempt = await OwnedForUpdateAsync(id, learnerId, ct);
        if (attempt.TrangThai != ExamCore.Active)
            throw new ExamProblem("ATTEMPT_FINALIZED", "Bài thi đã kết thúc.", 409);
        if (attempt.HetHanLuc <= DateTime.UtcNow)
        {
            await tx.CommitAsync(ct);
            await FinalizeAsync(id, learnerId, true, ct);
            throw new ExamProblem("ATTEMPT_EXPIRED", "Bài thi đã hết giờ.", 409);
        }
        var question = await db.CauHoiLuotLam.AsNoTracking()
            .FirstOrDefaultAsync(q => q.MaCauHoiLuotLam == questionId && q.MaKetQua == id, ct);
        if (question is null) throw new ExamProblem("QUESTION_NOT_IN_ATTEMPT", "Câu hỏi không thuộc lượt làm bài.", 404);
        var choice = request.SelectedOption?.Trim().ToUpperInvariant();
        if (choice is not null && choice != "A" && choice != "B" && choice != "C" &&
            (choice != "D" || string.IsNullOrWhiteSpace(question.PhuongAnD)))
            throw new ExamProblem("INVALID_OPTION", "Đáp án không hợp lệ.");
        var answer = await db.ChiTietKetQua.FindAsync([questionId], ct);
        if (answer is null)
        {
            answer = new ChiTietKetQua { MaCauHoiLuotLam = questionId, DapAnChon = choice, DanhDau = request.Flagged ?? false };
            db.ChiTietKetQua.Add(answer);
        }
        else
        {
            answer.DapAnChon = choice;
            if (request.Flagged.HasValue) answer.DanhDau = request.Flagged.Value;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task FlagAsync(int id, int questionId, int learnerId, bool flagged, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var attempt = await OwnedForUpdateAsync(id, learnerId, ct);
        if (attempt.TrangThai != ExamCore.Active)
            throw new ExamProblem("ATTEMPT_FINALIZED", "Bài thi đã kết thúc.", 409);
        if (attempt.HetHanLuc <= DateTime.UtcNow)
        {
            await tx.CommitAsync(ct);
            await FinalizeAsync(id, learnerId, true, ct);
            throw new ExamProblem("ATTEMPT_EXPIRED", "Bài thi đã hết giờ.", 409);
        }
        if (!await db.CauHoiLuotLam.AnyAsync(q => q.MaCauHoiLuotLam == questionId && q.MaKetQua == id, ct))
            throw new ExamProblem("QUESTION_NOT_IN_ATTEMPT", "Câu hỏi không thuộc lượt làm bài.", 404);
        var answer = await db.ChiTietKetQua.FindAsync([questionId], ct);
        if (answer is null) db.ChiTietKetQua.Add(new ChiTietKetQua { MaCauHoiLuotLam = questionId, DanhDau = flagged });
        else answer.DanhDau = flagged;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<ExamResultDto> FinalizeAsync(int id, int learnerId, bool expireOnly, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var attempt = await OwnedForUpdateAsync(id, learnerId, ct);
        if (attempt.TrangThai == ExamCore.Active)
        {
            var now = DateTime.UtcNow;
            var expired = attempt.HetHanLuc <= now;
            if (expireOnly && !expired) throw new ExamProblem("ATTEMPT_NOT_FINALIZED", "Bài thi chưa kết thúc.", 409);
            var end = expired ? ExamCore.Utc(attempt.HetHanLuc!.Value) : now;
            var questions = await Questions(id, ct);
            if (questions.Count != 200 || ExamCore.PartCounts.Any(p =>
                questions.Count(q => q.MaPartNavigation.SoPart == p.Key) != p.Value))
                throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Cấu trúc bài thi không hợp lệ.", 409);
            var answers = await Answers(questions, ct);
            var raw = grading.Grade(attempt, questions, answers);
            var scaled = scoring.Calculate(raw.Listening.Correct, raw.Listening.Total,
                raw.Reading.Correct, raw.Reading.Total);
            attempt.TrangThai = expired ? ExamCore.Expired : ExamCore.Submitted;
            attempt.NgayNopBai = end;
            attempt.ThoiGianLamBai = ExamCore.Minutes(ExamCore.Utc(attempt.NgayLamBai), end);
            attempt.DiemNghe = scaled.ListeningScore;
            attempt.DiemDoc = scaled.ReadingScore;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        await db.Entry(attempt).ReloadAsync(ct);
        return await ResultAsync(id, learnerId, ct);
    }

    public async Task<ExamResultDto> ResultAsync(int id, int learnerId, CancellationToken ct)
    {
        var attempt = await OwnedAsync(id, learnerId, ct);
        if (attempt.TrangThai == ExamCore.Active && attempt.HetHanLuc <= DateTime.UtcNow)
            return await FinalizeAsync(id, learnerId, true, ct);
        if (attempt.TrangThai is not (ExamCore.Submitted or ExamCore.Expired))
            throw new ExamProblem("ATTEMPT_NOT_FINALIZED", "Bài thi chưa kết thúc.", 409);
        var questions = await Questions(id, ct);
        return grading.Grade(attempt, questions, await Answers(questions, ct));
    }

    public async Task<ExamReviewDto> ReviewAsync(int id, int learnerId, CancellationToken ct)
    {
        _ = await ResultAsync(id, learnerId, ct);
        var questions = await Questions(id, ct);
        var groups = await db.NhomLuotLam.AsNoTracking().Include(g => g.MaPartNavigation)
            .Where(g => g.MaKetQua == id).OrderBy(g => g.ThuTu).ToListAsync(ct);
        var answers = await Answers(questions, ct);
        ReviewQuestionDto Map(CauHoiLuotLam q)
        {
            answers.TryGetValue(q.MaCauHoiLuotLam, out var a);
            var status = string.IsNullOrWhiteSpace(a?.DapAnChon) ? "UNANSWERED" :
                a.DapAnChon.Trim() == q.PhuongAnDung.Trim() ? "CORRECT" : "INCORRECT";
            return new(q.MaCauHoiLuotLam, q.ThuTu, q.MaPartNavigation.SoPart, q.NoiDung,
                q.PhuongAnA, q.PhuongAnB, q.PhuongAnC, q.PhuongAnD, a?.DapAnChon,
                q.PhuongAnDung, status, q.GiaiThich ?? "Chưa có giải thích cho câu hỏi này.", a?.DanhDau ?? false);
        }
        return new(id, groups.Select(g => new ReviewGroupDto(g.MaNhomLuotLam, g.ThuTu,
            g.MaPartNavigation.SoPart, g.NoiDungNguLieu,
            !string.IsNullOrWhiteSpace(g.DuongDanAudio), !string.IsNullOrWhiteSpace(g.DuongDanAnh),
            ExamDocumentCodec.Decode(g.TaiLieuJson).Select(d => new ExamDocumentDto(d.Order, d.Type, d.Text,
                !string.IsNullOrWhiteSpace(d.ImagePath))).ToList(),
            questions.Where(q => q.MaNhomLuotLam == g.MaNhomLuotLam).Select(Map).ToList())).ToList(),
            questions.Where(q => q.MaNhomLuotLam == null).Select(Map).ToList());
    }

    private async Task<List<CauHoiLuotLam>> Questions(int id, CancellationToken ct) =>
        await db.CauHoiLuotLam.AsNoTracking().Include(q => q.MaPartNavigation)
            .Where(q => q.MaKetQua == id).OrderBy(q => q.ThuTu).ToListAsync(ct);

    private async Task<Dictionary<int, ChiTietKetQua>> Answers(IReadOnlyList<CauHoiLuotLam> questions, CancellationToken ct)
    {
        var ids = questions.Select(q => q.MaCauHoiLuotLam).ToArray();
        return await db.ChiTietKetQua.AsNoTracking().Where(a => ids.Contains(a.MaCauHoiLuotLam))
            .ToDictionaryAsync(a => a.MaCauHoiLuotLam, ct);
    }
}
