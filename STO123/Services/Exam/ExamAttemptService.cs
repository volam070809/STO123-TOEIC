using System.Data;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Exam;
using STO123.Models;
using STO123.Services.Scoring;
using STO123.Services.Knn;

namespace STO123.Services.Exam;

public sealed class ExamAttemptService(ToeicDbContext db, ExamGenerationService generation,
    ExamGradingService grading, IToeicScoreCalculator scoring, IKnnClassifier knnClassifier,
    KnnDiagnosticsStore knnDiagnostics,
    ILogger<ExamAttemptService> logger)
{
    public async Task<int> StartAsync(int learnerId, StartExamRequest request, CancellationToken ct)
    {
        var placement = request.Source == ExamCore.Placement;
        var fixedSource = request.Source == "FIXED";
        var partMock = request.Source == "PART";
        if (request.Source is not ("FIXED" or "RANDOM" or "PART" or ExamCore.Placement) ||
            (fixedSource != request.ExamId.HasValue) ||
            (partMock ? request.Part is null || !ExamCore.PartCounts.ContainsKey(request.Part.Value) : request.Part is not null))
            throw new ExamProblem("INVALID_SOURCE", "Nguồn đề thi không hợp lệ.");
        if (placement && await TryResumePlacementAsync(learnerId, ct) is { } placementId)
            return placementId;
        if (!placement && await TryResumeMockAsync(learnerId, request, ct) is { } resumed)
            return resumed;
        IReadOnlyList<PlannedUnit> plan;
        try { plan = await generation.BuildAsync(request.ExamId, ct, partMock ? request.Part : null); }
        catch (ExamProblem)
        {
            var concurrent = placement ? await TryResumePlacementAsync(learnerId, ct) :
                await TryResumeMockAsync(learnerId, request, ct);
            if (concurrent is not null)
                return concurrent.Value;
            throw;
        }
        var partIds = await db.PartTOEIC.AsNoTracking().ToDictionaryAsync(x => x.SoPart, x => x.MaPart, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Serialize creation per learner; reuse only an equivalent unfinished Mock.
        {
            await LockLearnerAsync(learnerId, ct);
            var existing = placement ? await ActivePlacementAsync(learnerId, ct) :
                await ActiveMockAsync(learnerId, request, ct);
            if (existing is not null)
            {
                await tx.CommitAsync(ct);
                await tx.DisposeAsync();
                if (ExamTimer.Remaining(existing, DateTime.UtcNow) == 0)
                    _ = await FinalizeAsync(existing.MaKetQua, learnerId, true, ct);
                if (ExamTimer.Remaining(existing, DateTime.UtcNow) > 0) return existing.MaKetQua;
                return placement ? await StartPlacementAsync(learnerId, ct) :
                    await StartAsync(learnerId, request, ct);
            }
            if (fixedSource && !await db.DeThi.AsNoTracking().AnyAsync(x => x.MaDeThi == request.ExamId &&
                x.LoaiDe == "DE_THI" && x.TrangThai == "OPEN", ct))
                throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Đề thi đã đóng hoặc không hợp lệ.", 409);
        }
        var now = DateTime.UtcNow;
        var attempt = new KetQuaLamBai
        {
            MaHocVien = learnerId, MaDeThi = request.ExamId, MaLuotLam = Guid.NewGuid(),
            LoaiBaiLam = placement ? ExamCore.Placement : ExamCore.Mock, TrangThai = ExamCore.Active,
            NgayLamBai = now, HetHanLuc = now.AddMinutes(ExamCore.DurationMinutes),
            ThoiGianConLaiGiay = ExamCore.DurationMinutes * 60, BatDauPhienLuc = null
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
        if (!fixedSource ? !ExamGenerationService.ValidGeneratedPlan(plan, partMock ? request.Part : null) :
            global != 200 || partOrders.Any(x => x.Value != ExamCore.PartCounts[x.Key]))
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Cấu trúc đề thi không hợp lệ.", 409);
        await db.SaveChangesAsync(ct);
        var persisted = await db.CauHoiLuotLam.AsNoTracking()
            .Where(q => q.MaKetQua == attempt.MaKetQua)
            .GroupBy(q => q.MaPart)
            .Select(g => new { PartId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        if (persisted.Sum(x => x.Count) != global || ExamCore.PartCounts.Any(p =>
            (partOrders[p.Key] > 0) != persisted.Any(x => x.PartId == partIds[p.Key] &&
                x.Count == (fixedSource ? p.Value : partOrders[p.Key]))))
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Không thể lưu đủ câu hỏi của bài thi.", 409);
        attempt.BatDauPhienLuc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Created {Kind} attempt {AttemptId} with {QuestionCount} question occurrences",
            attempt.LoaiBaiLam, attempt.MaKetQua, global);
        if (!fixedSource)
            logger.LogInformation("Random attempt {AttemptId} source difficulty counts: {@Counts}",
                attempt.MaKetQua, RandomExamPlanner.DifficultyCounts(plan));
        return attempt.MaKetQua;
    }

    public async Task<int> StartPlacementAsync(int learnerId, CancellationToken ct)
    {
        return await StartAsync(learnerId, new StartExamRequest(ExamCore.Placement, null), ct);
    }

    public async Task<PlacementClassification> UpdatePlacementTargetAsync(int learnerId, int? targetScore,
        CancellationToken ct)
    {
        if (targetScore is null or < 10 or > 990)
            throw new ExamProblem("INVALID_TARGET", "Điểm mục tiêu phải từ 10 đến 990.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockLearnerAsync(learnerId, ct);
        var attempt = await LatestCompletedPlacementAsync(learnerId, ct);
        if (attempt is null || attempt.TrangThai is not (ExamCore.Submitted or ExamCore.Expired))
            throw new ExamProblem("PLACEMENT_NOT_FINALIZED", "Bài phân lớp chưa kết thúc.", 409);
        var rows = await db.KetQuaPhanLopKNN.Where(x => x.MaKetQuaNavigation.MaHocVien == learnerId)
            .OrderBy(x => x.MaPhanLop).Take(2).ToListAsync(ct);
        if (rows.Count > 1)
            throw new ExamProblem("DUPLICATE_KNN_RESULT", "Kết quả phân lớp bị trùng; cần kiểm tra dữ liệu.", 409);
        var knn = rows.FirstOrDefault() ?? throw new ExamProblem("TARGET_BLOCKED_BY_CURRENT_SCHEMA",
            "Chưa thể lưu điểm mục tiêu khi chưa có kết quả KNN.", 409);
        if (knn.MaKetQua != attempt.MaKetQua)
            throw new ExamProblem("PLACEMENT_NOT_CLASSIFIED", "Bài phân lớp mới nhất chưa có kết quả phân lớp hợp lệ.", 409);
        PlacementTarget.Apply(knn, targetScore);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return PlacementClassification.From(knn);
    }

    private async Task LockLearnerAsync(int learnerId, CancellationToken ct) =>
        _ = await db.NguoiDung.FromSqlInterpolated(
            $"SELECT * FROM dbo.NguoiDung WITH (UPDLOCK, HOLDLOCK) WHERE MaNguoiDung = {learnerId}")
            .AnyAsync(ct);

    private Task<KetQuaLamBai?> ActiveMockAsync(int learnerId, StartExamRequest request, CancellationToken ct)
    {
        var query = db.KetQuaLamBai.Where(x => x.MaHocVien == learnerId && x.LoaiBaiLam == ExamCore.Mock &&
            (x.TrangThai == ExamCore.Active || x.TrangThai == "BO_DO"));
        query = request.Source switch
        {
            "FIXED" => query.Where(x => x.MaDeThi == request.ExamId),
            "RANDOM" => query.Where(x => x.MaDeThi == null &&
                x.CauHoiLuotLam.Select(q => q.MaPart).Distinct().Count() > 1),
            "PART" => query.Where(x => x.MaDeThi == null && x.CauHoiLuotLam.Any() &&
                x.CauHoiLuotLam.All(q => q.MaPartNavigation.SoPart == request.Part)),
            _ => throw new ExamProblem("INVALID_SOURCE", "Nguồn đề thi không hợp lệ.")
        };
        return query.OrderByDescending(x => x.NgayLamBai).ThenByDescending(x => x.MaKetQua)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<int?> TryResumeMockAsync(int learnerId, StartExamRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockLearnerAsync(learnerId, ct);
        var existing = await ActiveMockAsync(learnerId, request, ct);
        await tx.CommitAsync(ct);
        await tx.DisposeAsync();
        if (existing is not null && ExamTimer.Remaining(existing, DateTime.UtcNow) == 0)
        {
            _ = await FinalizeAsync(existing.MaKetQua, learnerId, true, ct);
            return null;
        }
        return existing?.MaKetQua;
    }

    private Task<KetQuaLamBai?> ActivePlacementAsync(int learnerId, CancellationToken ct) =>
        db.KetQuaLamBai.Where(x => x.MaHocVien == learnerId && x.LoaiBaiLam == ExamCore.Placement &&
            (x.TrangThai == ExamCore.Active || x.TrangThai == "BO_DO"))
            .OrderByDescending(x => x.NgayLamBai).ThenByDescending(x => x.MaKetQua).FirstOrDefaultAsync(ct);

    private Task<KetQuaLamBai?> LatestCompletedPlacementAsync(int learnerId, CancellationToken ct) =>
        db.KetQuaLamBai.Where(x => x.MaHocVien == learnerId && x.LoaiBaiLam == ExamCore.Placement &&
            (x.TrangThai == ExamCore.Submitted || x.TrangThai == ExamCore.Expired))
            .OrderByDescending(x => x.NgayNopBai).ThenByDescending(x => x.MaKetQua).FirstOrDefaultAsync(ct);

    private async Task<int?> TryResumePlacementAsync(int learnerId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockLearnerAsync(learnerId, ct);
        var existing = await ActivePlacementAsync(learnerId, ct);
        var action = PlacementLifecycle.Decide(existing, DateTime.UtcNow);
        await tx.CommitAsync(ct);
        await tx.DisposeAsync();
        if (existing is not null && action == PlacementAction.FinalizeExpired)
        {
            _ = await FinalizeAsync(existing.MaKetQua, learnerId, true, ct);
            return null;
        }
        return action == PlacementAction.Resume ? existing?.MaKetQua : null;
    }

    private async Task ReactivateAbandonedAsync(int id, int learnerId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var attempt = await OwnedForUpdateAsync(id, learnerId, ct);
        if (attempt.TrangThai == "BO_DO") attempt.TrangThai = ExamCore.Active;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task FinalizeExpiredOwnedAsync(int learnerId, CancellationToken ct)
    {
        var candidates = await db.KetQuaLamBai.AsNoTracking().Where(x => x.MaHocVien == learnerId &&
            x.LoaiBaiLam == ExamCore.Mock && (x.TrangThai == ExamCore.Active || x.TrangThai == "BO_DO"))
            .ToListAsync(ct);
        var ids = candidates.Where(x => ExamTimer.Remaining(x, DateTime.UtcNow) == 0).Select(x => x.MaKetQua);
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
        if (attempt.TrangThai is ExamCore.Active or "BO_DO" && ExamTimer.IsStale(attempt, DateTime.UtcNow))
        {
            await HeartbeatAsync(id, learnerId, ct);
            await db.Entry(attempt).ReloadAsync(ct);
        }
        if (attempt.TrangThai is ExamCore.Active or "BO_DO" && ExamTimer.Remaining(attempt, DateTime.UtcNow) == 0)
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
        var partMock = IsPartMock(attempt, questions);
        return new(id, attempt.LoaiBaiLam == ExamCore.Placement ? ExamCore.Placement :
            attempt.MaDeThi.HasValue ? "FIXED" : partMock ? "PART" : "RANDOM", attempt.TrangThai,
            ExamCore.Utc(attempt.NgayLamBai), null,
            questions.Count, mappedGroups, questions.Where(q => q.MaNhomLuotLam == null).Select(Map).ToList())
            { ExamName = examName ?? GeneratedName(attempt, questions),
              RemainingSeconds = attempt.TrangThai is ExamCore.Active or "BO_DO" ? ExamTimer.Remaining(attempt, DateTime.UtcNow) : null,
              IsPaused = attempt.BatDauPhienLuc is null };
    }

    public async Task<ExamAttemptDto> PauseAsync(int id, int learnerId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var attempt = await OwnedForUpdateAsync(id, learnerId, ct);
        if (attempt.TrangThai is ExamCore.Active or "BO_DO")
        {
            ExamTimer.Charge(attempt, DateTime.UtcNow, false);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        if (attempt.TrangThai is ExamCore.Active or "BO_DO" && attempt.ThoiGianConLaiGiay == 0)
            _ = await FinalizeAsync(id, learnerId, true, ct);
        return await GetAsync(id, learnerId, ct);
    }

    public async Task<ExamAttemptDto> ResumeAsync(int id, int learnerId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var attempt = await OwnedForUpdateAsync(id, learnerId, ct);
        if (attempt.TrangThai is ExamCore.Active or "BO_DO")
        {
            var now = DateTime.UtcNow;
            if (ExamTimer.IsStale(attempt, now)) ExamTimer.Charge(attempt, now, false);
            if (attempt.ThoiGianConLaiGiay > 0 && attempt.BatDauPhienLuc is null)
                attempt.BatDauPhienLuc = now;
            if (attempt.TrangThai == "BO_DO") attempt.TrangThai = ExamCore.Active;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        if (attempt.TrangThai == ExamCore.Active && attempt.ThoiGianConLaiGiay == 0)
            _ = await FinalizeAsync(id, learnerId, true, ct);
        return await GetAsync(id, learnerId, ct);
    }

    public async Task<ExamAttemptDto> HeartbeatAsync(int id, int learnerId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var attempt = await OwnedForUpdateAsync(id, learnerId, ct);
        if (attempt.TrangThai is ExamCore.Active or "BO_DO" && attempt.BatDauPhienLuc is not null)
        {
            ExamTimer.Charge(attempt, DateTime.UtcNow, true);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        if (attempt.TrangThai is ExamCore.Active or "BO_DO" && attempt.ThoiGianConLaiGiay == 0)
            _ = await FinalizeAsync(id, learnerId, true, ct);
        return await GetAsync(id, learnerId, ct);
    }

    public async Task SaveAsync(int id, int questionId, int learnerId, SaveAnswerRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var attempt = await OwnedForUpdateAsync(id, learnerId, ct);
        if (attempt.TrangThai != ExamCore.Active || attempt.BatDauPhienLuc is null)
            throw new ExamProblem("ATTEMPT_FINALIZED", "Bài thi đã kết thúc.", 409);
        if (ExamTimer.Remaining(attempt, DateTime.UtcNow) == 0)
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
        if (attempt.TrangThai != ExamCore.Active || attempt.BatDauPhienLuc is null)
            throw new ExamProblem("ATTEMPT_FINALIZED", "Bài thi đã kết thúc.", 409);
        if (ExamTimer.Remaining(attempt, DateTime.UtcNow) == 0)
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
        var newlyFinalized = attempt.TrangThai is ExamCore.Active or "BO_DO";
        if (newlyFinalized)
        {
            var now = DateTime.UtcNow;
            var expired = ExamTimer.Remaining(attempt, now) == 0;
            if (expireOnly && !expired) throw new ExamProblem("ATTEMPT_NOT_FINALIZED", "Bài thi chưa kết thúc.", 409);
            var end = now;
            ExamTimer.Charge(attempt, now, false);
            var questions = await Questions(id, ct);
            var testedParts = questions.Select(q => q.MaPartNavigation.SoPart).Distinct().ToArray();
            var partMock = IsPartMock(attempt, questions);
            if (ExamCore.PartCounts.Any(p =>
                questions.Count(q => q.MaPartNavigation.SoPart == p.Key) is var count &&
                (attempt.MaDeThi.HasValue ? count != p.Value :
                    partMock ? (testedParts[0] == p.Key ? count < 1 || count > p.Value : count != 0) :
                    count < 1 || count > p.Value)) ||
                questions.Sum(q => ExamCore.PartCounts.ContainsKey(q.MaPartNavigation.SoPart) ? 1 : 0) != questions.Count ||
                questions.Select(q => q.MaCauHoiGoc).Distinct().Count() != questions.Count)
                throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Cấu trúc bài thi không hợp lệ.", 409);
            var answers = await Answers(questions, ct);
            var raw = grading.Grade(attempt, questions, answers);
            var scaled = partMock ? null : scoring.Calculate(raw.Listening.Correct, raw.Listening.Total,
                raw.Reading.Correct, raw.Reading.Total);
            attempt.TrangThai = expired ? ExamCore.Expired : ExamCore.Submitted;
            attempt.NgayNopBai = end;
            attempt.ThoiGianLamBai = Math.Max(0, (ExamCore.DurationMinutes * 60 - attempt.ThoiGianConLaiGiay!.Value) / 60);
            attempt.DiemNghe = scaled?.ListeningScore;
            attempt.DiemDoc = scaled?.ReadingScore;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        await tx.DisposeAsync();
        await db.Entry(attempt).ReloadAsync(ct);
        if (newlyFinalized && attempt.LoaiBaiLam == ExamCore.Placement)
        {
            try { await ClassifyPlacementAsync(attempt, learnerId, ct); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Placement {AttemptId} was graded but KNN classification failed", id);
            }
        }
        return await ResultAsync(id, learnerId, ct);
    }

    private async Task ClassifyPlacementAsync(KetQuaLamBai attempt, int learnerId, CancellationToken ct)
    {
        var result = await ResultAsync(attempt.MaKetQua, learnerId, ct);
        var classification = knnClassifier.Classify(PlacementFeatures.FromFinalizedResult(result));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockLearnerAsync(learnerId, ct);
        var latestId = await db.KetQuaLamBai.AsNoTracking()
            .Where(x => x.MaHocVien == learnerId && x.LoaiBaiLam == ExamCore.Placement &&
                (x.TrangThai == ExamCore.Submitted || x.TrangThai == ExamCore.Expired))
            .OrderByDescending(x => x.NgayNopBai).ThenByDescending(x => x.MaKetQua)
            .Select(x => x.MaKetQua).FirstAsync(ct);
        if (latestId != attempt.MaKetQua) return;
        var rows = await db.KetQuaPhanLopKNN
            .Where(x => x.MaKetQuaNavigation.MaHocVien == learnerId)
            .OrderBy(x => x.MaPhanLop).Take(2).ToListAsync(ct);
        if (rows.Count > 1)
            throw new ExamProblem("DUPLICATE_KNN_RESULT", "Kết quả phân lớp bị trùng; cần kiểm tra dữ liệu.", 409);
        var current = rows.FirstOrDefault();
        if (current is null)
            db.KetQuaPhanLopKNN.Add(new KetQuaPhanLopKNN {
                MaKetQua = attempt.MaKetQua, GiaiDoanDeXuat = classification.Stage,
                PhienBanMoHinh = classification.ModelVersion, NgayPhanLop = DateTime.UtcNow });
        else
        {
            current.MaKetQua = attempt.MaKetQua;
            current.GiaiDoanDeXuat = classification.Stage;
            current.PhienBanMoHinh = classification.ModelVersion;
            current.NgayPhanLop = DateTime.UtcNow;
            current.DiemMucTieu = null;
        }
        var exactAttempt = await db.KetQuaLamBai.SingleAsync(x => x.MaKetQua == attempt.MaKetQua, ct);
        if (exactAttempt.GiaiDoanLucNop is null)
            exactAttempt.GiaiDoanLucNop = classification.Stage;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        knnDiagnostics.Set(attempt.MaKetQua, classification);
    }

    public async Task<ExamResultDto> ResultAsync(int id, int learnerId, CancellationToken ct)
    {
        var attempt = await OwnedAsync(id, learnerId, ct);
        if (attempt.TrangThai is ExamCore.Active or "BO_DO" && ExamTimer.Remaining(attempt, DateTime.UtcNow) == 0)
            return await FinalizeAsync(id, learnerId, true, ct);
        if (attempt.TrangThai is not (ExamCore.Submitted or ExamCore.Expired))
            throw new ExamProblem("ATTEMPT_NOT_FINALIZED", "Bài thi chưa kết thúc.", 409);
        var questions = await Questions(id, ct);
        var examName = attempt.MaDeThi.HasValue ? await db.DeThi.AsNoTracking()
            .Where(x => x.MaDeThi == attempt.MaDeThi).Select(x => x.TenDe).FirstOrDefaultAsync(ct) : null;
        return grading.Grade(attempt, questions, await Answers(questions, ct))
            with { ExamName = examName ?? GeneratedName(attempt, questions),
                Source = attempt.LoaiBaiLam == ExamCore.Placement ? ExamCore.Placement :
                    IsPartMock(attempt, questions) ? "PART" : ExamCore.Mock,
                Mode = attempt.LoaiBaiLam == ExamCore.Placement ? ExamCore.Placement :
                    attempt.MaDeThi.HasValue ? "FIXED" : IsPartMock(attempt, questions) ? "PART" : "RANDOM",
                ExamId = attempt.MaDeThi,
                Part = IsPartMock(attempt, questions) ? questions[0].MaPartNavigation.SoPart : null };
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

    private static bool IsPartMock(KetQuaLamBai attempt, IReadOnlyList<CauHoiLuotLam> questions) =>
        attempt.LoaiBaiLam == ExamCore.Mock && attempt.MaDeThi is null && questions.Count > 0 &&
        questions.Select(q => q.MaPartNavigation.SoPart).Distinct().Take(2).Count() == 1;

    private static string GeneratedName(KetQuaLamBai attempt, IReadOnlyList<CauHoiLuotLam> questions) =>
        attempt.LoaiBaiLam == ExamCore.Placement ? "Phân lớp đầu vào" :
        IsPartMock(attempt, questions) ? $"Thi thử Part {questions[0].MaPartNavigation.SoPart}" :
        "Đề ngẫu nhiên toàn bài";

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
