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
        var placement = request.Source == ExamCore.Placement;
        var fixedSource = request.Source == "FIXED" || placement;
        if ((!fixedSource && request.Source != "RANDOM") || (fixedSource != request.ExamId.HasValue))
            throw new ExamProblem("INVALID_SOURCE", "Nguồn đề thi không hợp lệ.");
        if (placement && await TryResumePlacementAsync(learnerId, ct) is { } placementId)
            return placementId;
        if (request.Source == "RANDOM" && await TryResumeRandomAsync(learnerId, ct) is { } randomId)
            return randomId;
        if (!placement && fixedSource && await TryResumeFixedAsync(learnerId, request.ExamId!.Value, ct) is { } resumed)
            return resumed;
        IReadOnlyList<PlannedUnit> plan;
        try { plan = await generation.BuildAsync(request.ExamId, ct,
            placement ? "DE_THI_DAU_VAO" : "DE_THI"); }
        catch (ExamProblem) when (fixedSource)
        {
            var concurrent = placement ? await TryResumePlacementAsync(learnerId, ct) :
                await TryResumeFixedAsync(learnerId, request.ExamId!.Value, ct);
            if (concurrent is not null)
                return concurrent.Value;
            throw;
        }
        catch (ExamProblem) when (request.Source == "RANDOM")
        {
            if (await TryResumeRandomAsync(learnerId, ct) is { } concurrent) return concurrent;
            throw;
        }
        var partIds = await db.PartTOEIC.AsNoTracking().ToDictionaryAsync(x => x.SoPart, x => x.MaPart, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (fixedSource || request.Source == "RANDOM")
        {
            await LockLearnerAsync(learnerId, ct);
            var existing = placement ? await ExistingPlacementAsync(learnerId, ct) :
                request.Source == "RANDOM" ? await ExistingRandomAsync(learnerId, ct) :
                await ExistingFixedAsync(learnerId, request.ExamId!.Value, ct);
            if (existing is not null)
            {
                if (existing.TrangThai == "BO_DO") existing.TrangThai = ExamCore.Active;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                if (existing.HetHanLuc <= DateTime.UtcNow)
                    _ = await FinalizeAsync(existing.MaKetQua, learnerId, true, ct);
                return existing.MaKetQua;
            }
            if (fixedSource && !await db.DeThi.AsNoTracking().AnyAsync(x => x.MaDeThi == request.ExamId &&
                x.LoaiDe == (placement ? "DE_THI_DAU_VAO" : "DE_THI") && x.TrangThai == "OPEN", ct))
                throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Đề thi đã đóng hoặc không hợp lệ.", 409);
        }
        var now = DateTime.UtcNow;
        var attempt = new KetQuaLamBai
        {
            MaHocVien = learnerId, MaDeThi = request.ExamId, MaLuotLam = Guid.NewGuid(),
            LoaiBaiLam = placement ? ExamCore.Placement : ExamCore.Mock, TrangThai = ExamCore.Active,
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
                    TaiLieuJson = unit.Documents.Count > 0 ? ExamDocumentCodec.Encode(unit.Documents.Select(d =>
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
        if (request.Source == "RANDOM" ? !ExamGenerationService.ValidRandomPlan(plan) :
            global != 200 || partOrders.Any(x => x.Value != ExamCore.PartCounts[x.Key]))
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Cấu trúc đề thi không hợp lệ.", 409);
        await db.SaveChangesAsync(ct);
        var persisted = await db.CauHoiLuotLam.AsNoTracking()
            .Where(q => q.MaKetQua == attempt.MaKetQua)
            .GroupBy(q => q.MaPart)
            .Select(g => new { PartId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        if (persisted.Sum(x => x.Count) != global || ExamCore.PartCounts.Any(p =>
            !persisted.Any(x => x.PartId == partIds[p.Key] &&
                x.Count == (request.Source == "RANDOM" ? partOrders[p.Key] : p.Value))))
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Không thể lưu đủ câu hỏi của bài thi.", 409);
        await tx.CommitAsync(ct);
        logger.LogInformation("Created {Kind} attempt {AttemptId} with {QuestionCount} question occurrences",
            attempt.LoaiBaiLam, attempt.MaKetQua, global);
        if (request.Source == "RANDOM")
            logger.LogInformation("Random attempt {AttemptId} source difficulty counts: {@Counts}",
                attempt.MaKetQua, RandomExamPlanner.DifficultyCounts(plan));
        return attempt.MaKetQua;
    }

    public async Task<int> StartPlacementAsync(int learnerId, CancellationToken ct)
    {
        if (await TryResumePlacementAsync(learnerId, ct) is { } existing) return existing;
        var examIds = await db.DeThi.AsNoTracking().Where(x => x.LoaiDe == "DE_THI_DAU_VAO" &&
            x.TrangThai == "OPEN" && x.ThoiGianLamBai == ExamCore.DurationMinutes)
            .OrderBy(x => x.MaDeThi).Select(x => x.MaDeThi).ToListAsync(ct);
        foreach (var examId in examIds)
        {
            try { return await StartAsync(learnerId, new StartExamRequest(ExamCore.Placement, examId), ct); }
            catch (ExamProblem e) when (e.Code == "INVALID_EXAM_STRUCTURE") { }
        }
        throw new ExamProblem(ExamCore.Insufficient, "Chưa có đề phân lớp hợp lệ.", 409);
    }

    public async Task<PlacementClassification> UpdatePlacementTargetAsync(int learnerId, int? targetScore,
        CancellationToken ct)
    {
        if (targetScore is < 10 or > 990)
            throw new ExamProblem("INVALID_TARGET", "Điểm mục tiêu phải từ 10 đến 990.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockLearnerAsync(learnerId, ct);
        var attempt = await ExistingPlacementAsync(learnerId, ct);
        if (attempt is null || attempt.TrangThai is not (ExamCore.Submitted or ExamCore.Expired))
            throw new ExamProblem("PLACEMENT_NOT_FINALIZED", "Bài phân lớp chưa kết thúc.", 409);
        var rows = await db.KetQuaPhanLopKNN.Where(x => x.MaKetQua == attempt.MaKetQua)
            .OrderBy(x => x.MaPhanLop).Take(2).ToListAsync(ct);
        if (rows.Count > 1)
            throw new ExamProblem("DUPLICATE_KNN_RESULT", "Kết quả phân lớp bị trùng; cần kiểm tra dữ liệu.", 409);
        var knn = rows.FirstOrDefault() ?? throw new ExamProblem("TARGET_BLOCKED_BY_CURRENT_SCHEMA",
            "Chưa thể lưu điểm mục tiêu khi chưa có kết quả KNN.", 409);
        PlacementTarget.Apply(knn, targetScore);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return PlacementClassification.From(knn);
    }

    private async Task LockLearnerAsync(int learnerId, CancellationToken ct) =>
        _ = await db.NguoiDung.FromSqlInterpolated(
            $"SELECT * FROM dbo.NguoiDung WITH (UPDLOCK, HOLDLOCK) WHERE MaNguoiDung = {learnerId}")
            .AnyAsync(ct);

    private Task<KetQuaLamBai?> ExistingFixedAsync(int learnerId, int examId, CancellationToken ct) =>
        db.KetQuaLamBai.Where(x => x.MaHocVien == learnerId && x.MaDeThi == examId &&
            x.LoaiBaiLam == ExamCore.Mock && (x.TrangThai == ExamCore.Active || x.TrangThai == "BO_DO"))
            .OrderByDescending(x => x.NgayLamBai).FirstOrDefaultAsync(ct);

    private Task<KetQuaLamBai?> ExistingRandomAsync(int learnerId, CancellationToken ct) =>
        db.KetQuaLamBai.Where(x => x.MaHocVien == learnerId && x.LoaiBaiLam == ExamCore.Mock &&
            x.MaDeThi == null && (x.TrangThai == ExamCore.Active || x.TrangThai == "BO_DO"))
            .OrderByDescending(x => x.NgayLamBai).FirstOrDefaultAsync(ct);

    private async Task<int?> TryResumeRandomAsync(int learnerId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockLearnerAsync(learnerId, ct);
        var existing = await ExistingRandomAsync(learnerId, ct);
        if (existing?.TrangThai == "BO_DO")
        {
            existing.TrangThai = ExamCore.Active;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        if (existing is not null && existing.HetHanLuc <= DateTime.UtcNow)
            _ = await FinalizeAsync(existing.MaKetQua, learnerId, true, ct);
        return existing?.MaKetQua;
    }

    private Task<KetQuaLamBai?> ExistingPlacementAsync(int learnerId, CancellationToken ct) =>
        db.KetQuaLamBai.Where(x => x.MaHocVien == learnerId && x.LoaiBaiLam == ExamCore.Placement)
            .OrderBy(x => x.NgayLamBai).ThenBy(x => x.MaKetQua).FirstOrDefaultAsync(ct);

    private async Task<int?> TryResumePlacementAsync(int learnerId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockLearnerAsync(learnerId, ct);
        var existing = await ExistingPlacementAsync(learnerId, ct);
        var action = PlacementLifecycle.Decide(existing, DateTime.UtcNow);
        if (existing?.TrangThai == "BO_DO")
        {
            existing.TrangThai = ExamCore.Active;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        if (existing is not null && action == PlacementAction.FinalizeExpired)
            _ = await FinalizeAsync(existing.MaKetQua, learnerId, true, ct);
        return existing?.MaKetQua;
    }

    private async Task<int?> TryResumeFixedAsync(int learnerId, int examId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockLearnerAsync(learnerId, ct);
        var existing = await ExistingFixedAsync(learnerId, examId, ct);
        if (existing is not null && existing.TrangThai == "BO_DO")
        {
            existing.TrangThai = ExamCore.Active;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        if (existing is not null && existing.HetHanLuc <= DateTime.UtcNow)
            _ = await FinalizeAsync(existing.MaKetQua, learnerId, true, ct);
        return existing?.MaKetQua;
    }

    private async Task ReactivateAbandonedAsync(int id, int learnerId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var attempt = await OwnedForUpdateAsync(id, learnerId, ct);
        if (attempt.TrangThai == "BO_DO")
        {
            attempt.TrangThai = ExamCore.Active;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task FinalizeExpiredOwnedAsync(int learnerId, CancellationToken ct)
    {
        var ids = await db.KetQuaLamBai.AsNoTracking().Where(x => x.MaHocVien == learnerId &&
            x.LoaiBaiLam == ExamCore.Mock && (x.TrangThai == ExamCore.Active || x.TrangThai == "BO_DO") &&
            x.HetHanLuc <= DateTime.UtcNow).Select(x => x.MaKetQua).ToListAsync(ct);
        foreach (var id in ids)
        {
            await ReactivateAbandonedAsync(id, learnerId, ct);
            _ = await FinalizeAsync(id, learnerId, true, ct);
        }
    }

    public async Task<KetQuaLamBai> OwnedAsync(int id, int learnerId, CancellationToken ct)
    {
        var attempt = await db.KetQuaLamBai.FirstOrDefaultAsync(x => x.MaKetQua == id, ct);
        if (attempt is null) throw new ExamProblem("ATTEMPT_NOT_FOUND", "Không tìm thấy lượt làm bài.", 404);
        if (attempt.MaHocVien != learnerId) throw new ExamProblem("ATTEMPT_FORBIDDEN", "Bạn không có quyền xem lượt làm bài này.", 403);
        if (attempt.LoaiBaiLam is not (ExamCore.Mock or ExamCore.Placement))
            throw new ExamProblem("ATTEMPT_NOT_FOUND", "Không tìm thấy lượt làm bài.", 404);
        return attempt;
    }

    private async Task<KetQuaLamBai> OwnedForUpdateAsync(int id, int learnerId, CancellationToken ct)
    {
        var attempt = await db.KetQuaLamBai
            .FromSqlInterpolated($"SELECT * FROM dbo.KetQuaLamBai WITH (UPDLOCK, ROWLOCK) WHERE MaKetQua = {id}")
            .SingleOrDefaultAsync(ct);
        if (attempt is null) throw new ExamProblem("ATTEMPT_NOT_FOUND", "Không tìm thấy lượt làm bài.", 404);
        if (attempt.MaHocVien != learnerId) throw new ExamProblem("ATTEMPT_FORBIDDEN", "Bạn không có quyền xem lượt làm bài này.", 403);
        if (attempt.LoaiBaiLam is not (ExamCore.Mock or ExamCore.Placement))
            throw new ExamProblem("ATTEMPT_NOT_FOUND", "Không tìm thấy lượt làm bài.", 404);
        return attempt;
    }

    public async Task<ExamAttemptDto> GetAsync(int id, int learnerId, CancellationToken ct)
    {
        var attempt = await OwnedAsync(id, learnerId, ct);
        if (attempt.TrangThai == "BO_DO")
        {
            await ReactivateAbandonedAsync(id, learnerId, ct);
            await db.Entry(attempt).ReloadAsync(ct);
        }
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
            return ExamQuestionProjection.BeforeSubmit(q, a);
        }
        var mappedGroups = groups.Select(g => new ExamGroupDto(g.MaNhomLuotLam, g.ThuTu,
            questions.First(q => q.MaNhomLuotLam == g.MaNhomLuotLam).MaPartNavigation.SoPart,
            g.MaPartNavigation?.SoPart is 1 or 2 or 3 or 4 ? null : g.NoiDungNguLieu,
            !string.IsNullOrWhiteSpace(g.DuongDanAudio), !string.IsNullOrWhiteSpace(g.DuongDanAnh),
            MapDocuments(id, g.MaNhomLuotLam, g.TaiLieuJson),
            questions.Where(q => q.MaNhomLuotLam == g.MaNhomLuotLam).Select(Map).ToList())).ToList();
        var examName = attempt.MaDeThi.HasValue ? await db.DeThi.AsNoTracking()
            .Where(x => x.MaDeThi == attempt.MaDeThi).Select(x => x.TenDe).FirstOrDefaultAsync(ct) : null;
        return new(id, attempt.LoaiBaiLam == ExamCore.Placement ? ExamCore.Placement :
            attempt.MaDeThi.HasValue ? "FIXED" : "RANDOM", attempt.TrangThai,
            ExamCore.Utc(attempt.NgayLamBai), attempt.HetHanLuc.HasValue ? ExamCore.Utc(attempt.HetHanLuc.Value) : null,
            questions.Count, mappedGroups, questions.Where(q => q.MaNhomLuotLam == null).Select(Map).ToList())
            { ExamName = examName ?? (attempt.LoaiBaiLam == ExamCore.Placement ? "Phân lớp đầu vào" : "Đề thi ngẫu nhiên") };
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
            if (ExamCore.PartCounts.Any(p =>
                questions.Count(q => q.MaPartNavigation.SoPart == p.Key) is var count &&
                (attempt.MaDeThi.HasValue || attempt.LoaiBaiLam == ExamCore.Placement
                    ? count != p.Value : count < 1 || count > p.Value)) ||
                questions.Sum(q => ExamCore.PartCounts.ContainsKey(q.MaPartNavigation.SoPart) ? 1 : 0) != questions.Count ||
                questions.Select(q => q.MaCauHoiGoc).Distinct().Count() != questions.Count)
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
        if (attempt.TrangThai == "BO_DO" && attempt.HetHanLuc <= DateTime.UtcNow)
        {
            await ReactivateAbandonedAsync(id, learnerId, ct);
            await db.Entry(attempt).ReloadAsync(ct);
        }
        if (attempt.TrangThai == ExamCore.Active && attempt.HetHanLuc <= DateTime.UtcNow)
            return await FinalizeAsync(id, learnerId, true, ct);
        if (attempt.TrangThai is not (ExamCore.Submitted or ExamCore.Expired))
            throw new ExamProblem("ATTEMPT_NOT_FINALIZED", "Bài thi chưa kết thúc.", 409);
        var questions = await Questions(id, ct);
        var examName = attempt.MaDeThi.HasValue ? await db.DeThi.AsNoTracking()
            .Where(x => x.MaDeThi == attempt.MaDeThi).Select(x => x.TenDe).FirstOrDefaultAsync(ct) : null;
        return grading.Grade(attempt, questions, await Answers(questions, ct))
            with { ExamName = examName ?? (attempt.LoaiBaiLam == ExamCore.Placement ? "Phân lớp đầu vào" : "Đề thi ngẫu nhiên"),
                Source = attempt.LoaiBaiLam == ExamCore.Placement ? ExamCore.Placement : ExamCore.Mock };
    }

    public async Task<ExamReviewDto> ReviewAsync(int id, int learnerId, CancellationToken ct)
    {
        var result = await ResultAsync(id, learnerId, ct);
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
            MapDocuments(id, g.MaNhomLuotLam, g.TaiLieuJson),
            questions.Where(q => q.MaNhomLuotLam == g.MaNhomLuotLam).Select(Map).ToList())).ToList(),
            questions.Where(q => q.MaNhomLuotLam == null).Select(Map).ToList())
            { Source = result.Source };
    }

    private async Task<List<CauHoiLuotLam>> Questions(int id, CancellationToken ct) =>
        await db.CauHoiLuotLam.AsNoTracking().Include(q => q.MaPartNavigation)
            .Where(q => q.MaKetQua == id).OrderBy(q => q.ThuTu).ToListAsync(ct);

    private static IReadOnlyList<ExamDocumentDto> MapDocuments(int attemptId, int groupId, string? json) =>
        ExamDocumentCodec.Decode(json).Select(d => new ExamDocumentDto(d.Order, d.Type,
            ExamDocumentCodec.ToContent(d), !string.IsNullOrWhiteSpace(d.ImagePath),
            string.IsNullOrWhiteSpace(d.ImagePath) ? null :
                $"/api/attempts/{attemptId}/groups/{groupId}/documents/{d.Order}/image")).ToList();

    private async Task<Dictionary<int, ChiTietKetQua>> Answers(IReadOnlyList<CauHoiLuotLam> questions, CancellationToken ct)
    {
        var ids = questions.Select(q => q.MaCauHoiLuotLam).ToArray();
        return await db.ChiTietKetQua.AsNoTracking().Where(a => ids.Contains(a.MaCauHoiLuotLam))
            .ToDictionaryAsync(a => a.MaCauHoiLuotLam, ct);
    }
}
