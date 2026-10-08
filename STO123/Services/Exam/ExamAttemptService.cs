using System.Data;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Exam;
using STO123.Models;
using STO123.Services.Scoring;
using STO123.Services.Knn;

namespace STO123.Services.Exam;

public sealed record PlacementStartResult(int AttemptId, string Status);

public sealed class ExamAttemptService(ToeicDbContext db, PersistedExamTemplate persistedTemplate,
    PersistedExamSnapshotWriter snapshotWriter, IConfiguration configuration, RandomStartProfiler profile,
    ExamGradingService grading, ExamSnapshotQueryService snapshots,
    IToeicScoreCalculator scoring, IKnnClassifier knnClassifier,
    KnnDiagnosticsStore knnDiagnostics,
    ILogger<ExamAttemptService> logger, IHostEnvironment environment)
{
    public Task<int> StartAsync(int learnerId, StartExamRequest request, CancellationToken ct)
    {
        if (request.Source == "FIXED" && request.ExamId is > 0 && request.Part is null)
            return StartPersistedMockAsync(learnerId, request.ExamId.Value, ct);
        throw new ExamProblem("INVALID_SOURCE", "Chỉ có thể bắt đầu đề thi thử đã xuất bản.");
    }
    private async Task<int> StartPersistedMockAsync(int learnerId, int examId, CancellationToken ct)
    {
        using var startProfile = profile.Begin(environment.IsDevelopment());
        var total = Stopwatch.StartNew();
        using var decisionPhase = profile.Phase("startDecision");
        var decisionTime = Stopwatch.StartNew();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        const string decisionSql = """
            SET NOCOUNT ON;
            DECLARE @held int;
            SELECT @held = MaNguoiDung FROM dbo.NguoiDung WITH (UPDLOCK, HOLDLOCK)
            WHERE MaNguoiDung = @learner;
            SELECT e.TrangThai, e.LoaiDe, e.ThoiGianLamBai,
                   a.MaKetQua, a.TrangThai,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.KetQuaLamBai done
                     WHERE done.MaHocVien = @learner AND done.MaDeThi = e.MaDeThi
                       AND done.LoaiBaiLam = 'MOCK' AND done.TrangThai IN ('DA_NOP', 'HET_GIO'))
                     THEN 1 ELSE 0 END AS bit)
            FROM dbo.DeThi e
            OUTER APPLY (SELECT TOP (1) MaKetQua, TrangThai FROM dbo.KetQuaLamBai
                         WHERE MaHocVien = @learner AND MaDeThi = e.MaDeThi AND LoaiBaiLam = 'MOCK'
                         ORDER BY MaKetQua DESC) a
            WHERE e.MaDeThi = @exam;
            """;
        string? examStatus = null, examType = null, attemptStatus = null;
        int? previousId = null, duration = null;
        var completed = false;
        var connection = db.Database.GetDbConnection();
        var openedForDecision = connection.State != ConnectionState.Open;
        if (openedForDecision) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx.GetDbTransaction();
            command.CommandText = decisionSql;
            foreach (var (name, value) in new[] { ("@learner", learnerId), ("@exam", examId) })
            {
                var p = command.CreateParameter(); p.ParameterName = name; p.Value = value; command.Parameters.Add(p);
            }
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                examStatus = reader.GetString(0); examType = reader.GetString(1); duration = reader.GetInt32(2);
                if (!reader.IsDBNull(3)) { previousId = reader.GetInt32(3); attemptStatus = reader.GetString(4); }
                completed = reader.GetBoolean(5);
            }
        }
        finally { if (openedForDecision) await connection.CloseAsync(); }
        decisionTime.Stop();
        profile.CountManualSqlCommand("startDecision/lockedLookup", 2, decisionTime.Elapsed);
        decisionPhase.Dispose();
        if (examStatus != "OPEN" || examType != "DE_THI" || duration != ExamCore.DurationMinutes)
            throw new ExamProblem("EXAM_NOT_OPEN", "Đề thi không mở hoặc không hợp lệ.", 409);
        if (completed) throw new ExamProblem("MOCK_ALREADY_COMPLETED", "Bạn đã hoàn thành đề thi này.", 409);
        if (previousId.HasValue)
        {
            if (attemptStatus is ExamCore.Active or "BO_DO")
            {
                await tx.CommitAsync(ct);
                return previousId.Value;
            }
            throw new ExamProblem("ATTEMPT_STATE_INVALID", "Trạng thái lượt thi không hợp lệ.", 409);
        }
        var templateTime = Stopwatch.StartNew();
        using var templatePhase = profile.Phase("templateRead");
        var plan = await persistedTemplate.LoadAsync(examId, ct);
        templateTime.Stop();
        templatePhase.Dispose();
        if (!ExamGenerationService.ValidPlan(plan) || !ExamGenerationService.ValidGeneratedPlan(plan))
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Cấu trúc đề thi không hợp lệ.", 409);
        var buildTime = Stopwatch.StartNew();
        using var buildPhase = profile.Phase("snapshotBuild");
        var attemptGuid = Guid.NewGuid();
        var global = 0;
        var groupOrder = 0;
        var groups = new List<NhomLuotLam>();
        var questions = new List<(CauHoiLuotLam Question, int? GroupOrder)>(200);
        var partOrders = ExamCore.PartCounts.Keys.ToDictionary(x => x, _ => 0);
        foreach (var unit in plan)
        {
            var partId = unit.Questions[0].MaPart;
            int? selectedGroupOrder = null;
            if (unit.Resource is { } resource)
            {
                selectedGroupOrder = ++groupOrder;
                var group = new NhomLuotLam {
                    MaNguLieuGoc = resource.MaNguLieu,
                    MaPart = partId, ThuTu = selectedGroupOrder.Value,
                    NoiDungNguLieu = resource.NoiDungNguLieu, NoiDungDich = resource.NoiDungDich,
                    DuongDanAudio = resource.DuongDanAudio, DuongDanAnh = resource.DuongDanAnh,
                    TaiLieuJson = unit.Documents.Count > 0 ? ExamDocumentCodec.Encode(unit.Documents.Select(d =>
                        new ExamDocumentSnapshot(d.ThuTu, d.LoaiTaiLieu, d.NoiDung, d.DuongDanAnh))) : null
                };
                groups.Add(group);
            }
            foreach (var source in unit.Questions)
            {
                var question = new CauHoiLuotLam {
                    MaCauHoiGoc = source.MaCauHoi, MaPart = partId,
                    ThuTu = ++global, ThuTuTrongPart = ++partOrders[unit.Part],
                    NoiDung = source.NoiDung, PhuongAnA = source.PhuongAnA,
                    PhuongAnB = source.PhuongAnB, PhuongAnC = source.PhuongAnC,
                    PhuongAnD = source.PhuongAnD, PhuongAnDung = source.PhuongAnDung,
                    GiaiThich = source.GiaiThich
                };
                questions.Add((question, selectedGroupOrder));
            }
        }
        if (global != 200 || partOrders.Any(x => x.Value != ExamCore.PartCounts[x.Key]))
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Cấu trúc đề thi không hợp lệ.", 409);
        buildTime.Stop();
        buildPhase.Dispose();
        var writeTime = Stopwatch.StartNew();
        var created = await snapshotWriter.InsertAsync(learnerId, examId, ExamCore.Mock, attemptGuid,
            ExamCore.DurationMinutes * 60, groups, questions, ct);
        writeTime.Stop();
        profile.AddMeasuredPhase("snapshotWrite", TimeSpan.FromMilliseconds(
            Math.Max(0, writeTime.Elapsed.TotalMilliseconds - created.TimerMilliseconds)));
        profile.AddMeasuredPhase("timerStart", TimeSpan.FromMilliseconds(created.TimerMilliseconds));
        var commitTime = Stopwatch.StartNew();
        using var commitPhase = profile.Phase("commit");
        await tx.CommitAsync(ct);
        commitTime.Stop(); total.Stop();
        commitPhase.Dispose();
        profile.SetInsertedRows(1 + groupOrder + global);
        if (environment.IsDevelopment())
            logger.LogInformation("Persisted Mock Start {ExamId}: StartDecision={DecisionMs}ms TemplateRead={TemplateMs}ms SnapshotBuild={BuildMs}ms SnapshotWrite={WriteMs}ms TimerStart={TimerMs}ms Commit={CommitMs}ms Total={TotalMs}ms SqlCommands={SqlCommands} Rows={Rows} SaveChanges=0",
                examId, decisionTime.ElapsedMilliseconds, templateTime.ElapsedMilliseconds,
                buildTime.ElapsedMilliseconds, Math.Max(0, writeTime.Elapsed.TotalMilliseconds - created.TimerMilliseconds),
                created.TimerMilliseconds, commitTime.ElapsedMilliseconds, total.ElapsedMilliseconds,
                3, 1 + groupOrder + global);
        return created.AttemptId;
    }

    public async Task<PlacementStartResult> StartPlacementAsync(int learnerId, CancellationToken ct)
    {
        var examId = configuration.GetValue<int>("Placement:DefaultExamId");
        if (examId <= 0)
            throw new ExamProblem("PLACEMENT_NOT_CONFIGURED", "Đề kiểm tra đầu vào chưa được cấu hình.", 503);
        using var startProfile = profile.Begin(environment.IsDevelopment(), ExamCore.Placement);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        const string decisionSql = """
            SET NOCOUNT ON;
            SELECT a.MaKetQua, a.TrangThai, a.ThoiGianConLaiGiay, a.BatDauPhienLuc,
                   e.TrangThai, e.LoaiDe, e.ThoiGianLamBai
            FROM dbo.NguoiDung u WITH (UPDLOCK, HOLDLOCK)
            OUTER APPLY (
                SELECT TOP (1) MaKetQua, TrangThai, ThoiGianConLaiGiay, BatDauPhienLuc
                FROM dbo.KetQuaLamBai
                WHERE MaHocVien = u.MaNguoiDung AND LoaiBaiLam = 'PLACEMENT'
                  AND TrangThai IN ('DANG_LAM', 'BO_DO')
                ORDER BY NgayLamBai DESC, MaKetQua DESC
            ) a
            OUTER APPLY (
                SELECT TrangThai, LoaiDe, ThoiGianLamBai FROM dbo.DeThi WHERE MaDeThi = @exam
            ) e
            WHERE u.MaNguoiDung = @learner;
            """;
        KetQuaLamBai? existing = null;
        string? examStatus = null, examType = null;
        int? duration = null;
        using (profile.Phase("startDecision"))
        {
            var connection = db.Database.GetDbConnection();
            var openedHere = connection.State != ConnectionState.Open;
            if (openedHere) await connection.OpenAsync(ct);
            try
            {
                await using var command = connection.CreateCommand();
                command.Transaction = tx.GetDbTransaction();
                command.CommandText = decisionSql;
                var learnerParameter = command.CreateParameter(); learnerParameter.ParameterName = "@learner";
                learnerParameter.Value = learnerId; command.Parameters.Add(learnerParameter);
                var examParameter = command.CreateParameter(); examParameter.ParameterName = "@exam";
                examParameter.Value = examId; command.Parameters.Add(examParameter);
                var watch = Stopwatch.StartNew();
                try
                {
                    await using var reader = await command.ExecuteReaderAsync(ct);
                    if (!await reader.ReadAsync(ct))
                        throw new ExamProblem("ATTEMPT_FORBIDDEN", "Tài khoản học viên không hợp lệ.", 403);
                    if (!reader.IsDBNull(0))
                        existing = new KetQuaLamBai {
                            MaKetQua = reader.GetInt32(0), TrangThai = reader.GetString(1),
                            ThoiGianConLaiGiay = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                            BatDauPhienLuc = reader.IsDBNull(3) ? null : reader.GetDateTime(3)
                        };
                    examStatus = reader.IsDBNull(4) ? null : reader.GetString(4);
                    examType = reader.IsDBNull(5) ? null : reader.GetString(5);
                    duration = reader.IsDBNull(6) ? null : reader.GetInt32(6);
                }
                finally { watch.Stop(); profile.CountManualSqlCommand("startDecision/placementLookup", 2, watch.Elapsed); }
            }
            finally { if (openedHere) await connection.CloseAsync(); }
        }
        switch (PlacementLifecycle.Decide(existing, DateTime.UtcNow))
        {
            case PlacementAction.Resume:
                await tx.CommitAsync(ct);
                return new PlacementStartResult(existing!.MaKetQua, existing.TrangThai);
            case PlacementAction.FinalizeExpired:
                await tx.CommitAsync(ct);
                await tx.DisposeAsync();
                _ = await FinalizeAsync(existing!.MaKetQua, learnerId, true, ct);
                startProfile.Dispose();
                return await StartPlacementAsync(learnerId, ct);
        }
        if (examStatus != "OPEN" || examType != "DE_THI" || duration != ExamCore.DurationMinutes)
            throw new ExamProblem("PLACEMENT_EXAM_UNAVAILABLE", "Đề kiểm tra đầu vào đã cấu hình không hợp lệ hoặc chưa mở.", 409);

        IReadOnlyList<PlannedUnit> plan;
        using (profile.Phase("templateRead")) plan = await persistedTemplate.LoadAsync(examId, ct);
        if (!ExamGenerationService.ValidPlan(plan) || !ExamGenerationService.ValidGeneratedPlan(plan))
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Cấu trúc đề kiểm tra đầu vào không hợp lệ.", 409);
        List<NhomLuotLam> groups;
        List<(CauHoiLuotLam Question, int? GroupOrder)> questions;
        using (profile.Phase("snapshotBuild"))
        {
            groups = [];
            questions = new(200);
            var partOrders = ExamCore.PartCounts.Keys.ToDictionary(part => part, _ => 0);
            foreach (var unit in plan)
            {
                var partId = unit.Questions[0].MaPart;
                int? groupOrder = null;
                if (unit.Resource is { } resource)
                {
                    groupOrder = groups.Count + 1;
                    groups.Add(new NhomLuotLam {
                        MaNguLieuGoc = resource.MaNguLieu, MaPart = partId, ThuTu = groupOrder.Value,
                        NoiDungNguLieu = resource.NoiDungNguLieu, NoiDungDich = resource.NoiDungDich,
                        DuongDanAudio = resource.DuongDanAudio, DuongDanAnh = resource.DuongDanAnh,
                        TaiLieuJson = unit.Documents.Count == 0 ? null : ExamDocumentCodec.Encode(unit.Documents.Select(d =>
                            new ExamDocumentSnapshot(d.ThuTu, d.LoaiTaiLieu, d.NoiDung, d.DuongDanAnh)))
                    });
                }
                foreach (var source in unit.Questions)
                    questions.Add((new CauHoiLuotLam {
                        MaCauHoiGoc = source.MaCauHoi, MaPart = partId,
                        ThuTu = questions.Count + 1, ThuTuTrongPart = ++partOrders[unit.Part],
                        NoiDung = source.NoiDung, PhuongAnA = source.PhuongAnA,
                        PhuongAnB = source.PhuongAnB, PhuongAnC = source.PhuongAnC,
                        PhuongAnD = source.PhuongAnD, PhuongAnDung = source.PhuongAnDung,
                        GiaiThich = source.GiaiThich
                    }, groupOrder));
            }
            if (questions.Count != 200 || partOrders.Any(pair => pair.Value != ExamCore.PartCounts[pair.Key]))
                throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Cấu trúc đề kiểm tra đầu vào không hợp lệ.", 409);
        }
        var writeTime = Stopwatch.StartNew();
        var created = await snapshotWriter.InsertAsync(learnerId, examId, ExamCore.Placement,
            Guid.NewGuid(), ExamCore.DurationMinutes * 60, groups, questions, ct);
        writeTime.Stop();
        profile.AddMeasuredPhase("snapshotWrite", TimeSpan.FromMilliseconds(
            Math.Max(0, writeTime.Elapsed.TotalMilliseconds - created.TimerMilliseconds)));
        profile.AddMeasuredPhase("timerStart", TimeSpan.FromMilliseconds(created.TimerMilliseconds));
        using (profile.Phase("commit")) await tx.CommitAsync(ct);
        profile.SetInsertedRows(1 + groups.Count + questions.Count);
        return new PlacementStartResult(created.AttemptId, ExamCore.Active);
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

    private Task<KetQuaLamBai?> LatestCompletedPlacementAsync(int learnerId, CancellationToken ct) =>
        db.KetQuaLamBai.Where(x => x.MaHocVien == learnerId && x.LoaiBaiLam == ExamCore.Placement &&
            (x.TrangThai == ExamCore.Submitted || x.TrangThai == ExamCore.Expired))
            .OrderByDescending(x => x.NgayNopBai).ThenByDescending(x => x.MaKetQua).FirstOrDefaultAsync(ct);

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
        var snapshot = await snapshots.LoadAsync(id, learnerId, SnapshotView.Active, false, ct);
        var attempt = snapshot.Attempt;
        if (attempt.TrangThai is ExamCore.Active or "BO_DO" && ExamTimer.Remaining(attempt, DateTime.UtcNow) == 0)
        {
            _ = await FinalizeAsync(id, learnerId, true, ct);
            snapshot = await snapshots.LoadAsync(id, learnerId, SnapshotView.Active, false, ct);
            attempt = snapshot.Attempt;
        }
        return ComposeAttempt(snapshot);
    }

    private static ExamAttemptDto ComposeAttempt(ExamSnapshot snapshot)
    {
        var attempt = snapshot.Attempt;
        var questions = snapshot.Questions;
        var groups = snapshot.Groups;
        var answers = snapshot.Answers;
        var id = attempt.MaKetQua;
        ExamQuestionDto Map(CauHoiLuotLam q)
        {
            answers.TryGetValue(q.MaCauHoiLuotLam, out var a);
            return ExamQuestionProjection.BeforeSubmit(q, a);
        }
        var questionsByGroup = questions.Where(q => q.MaNhomLuotLam.HasValue)
            .GroupBy(q => q.MaNhomLuotLam!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var mappedGroups = groups.Select(g => new ExamGroupDto(g.MaNhomLuotLam, g.ThuTu,
            g.MaPartNavigation.SoPart,
            g.MaPartNavigation.SoPart is 1 or 2 or 3 or 4 ? null : g.NoiDungNguLieu,
            !string.IsNullOrWhiteSpace(g.DuongDanAudio), !string.IsNullOrWhiteSpace(g.DuongDanAnh),
            MapDocuments(id, g.MaNhomLuotLam, g.TaiLieuJson),
            questionsByGroup.GetValueOrDefault(g.MaNhomLuotLam, []).Select(Map).ToList())).ToList();
        var partMock = IsPartMock(attempt, questions);
        return new(id, attempt.LoaiBaiLam == ExamCore.Placement ? ExamCore.Placement :
            attempt.MaDeThi.HasValue ? "FIXED" : partMock ? "PART" : "RANDOM", attempt.TrangThai,
            ExamCore.Utc(attempt.NgayLamBai), null,
            questions.Count, mappedGroups, questions.Where(q => q.MaNhomLuotLam == null).Select(Map).ToList())
            { ExamName = snapshot.ExamName ?? GeneratedName(attempt, questions),
              RemainingSeconds = attempt.TrangThai is ExamCore.Active or "BO_DO" ? ExamTimer.Remaining(attempt, DateTime.UtcNow) : null,
              IsPaused = attempt.BatDauPhienLuc is null };
    }

    private static ExamTimerDto TimerState(KetQuaLamBai attempt) => new(attempt.TrangThai,
        attempt.TrangThai is ExamCore.Active or "BO_DO" ? ExamTimer.Remaining(attempt, DateTime.UtcNow) : null,
        attempt.BatDauPhienLuc is null);

    public async Task<ExamTimerDto> PauseAsync(int id, int learnerId, CancellationToken ct)
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
        return TimerState(attempt);
    }

    public async Task<ExamAttemptDto> ResumeAsync(int id, int learnerId, CancellationToken ct)
    {
        var snapshot = await snapshots.LoadAsync(id, learnerId, SnapshotView.Active, true, ct);
        if (snapshot.Attempt.TrangThai == ExamCore.Active &&
            ExamTimer.Remaining(snapshot.Attempt, DateTime.UtcNow) == 0)
        {
            _ = await FinalizeAsync(id, learnerId, true, ct);
            return await GetAsync(id, learnerId, ct);
        }
        return ComposeAttempt(snapshot);
    }

    public async Task<ExamTimerDto> HeartbeatAsync(int id, int learnerId, CancellationToken ct)
    {
        var timer = await snapshots.HeartbeatAsync(id, learnerId, ct);
        if (timer.Status is ExamCore.Active or "BO_DO" && timer.RemainingSeconds == 0)
        {
            var result = await FinalizeAsync(id, learnerId, true, ct);
            return new ExamTimerDto(result.Status, null, true);
        }
        return timer;
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
            .Where(q => q.MaCauHoiLuotLam == questionId && q.MaKetQua == id)
            .Select(q => new { q.PhuongAnD }).FirstOrDefaultAsync(ct);
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
            var questions = await ResultQuestions(id, ct);
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
        var snapshot = await snapshots.LoadAsync(id, learnerId, SnapshotView.Result, false, ct);
        var attempt = snapshot.Attempt;
        if (attempt.TrangThai is ExamCore.Active or "BO_DO" && ExamTimer.Remaining(attempt, DateTime.UtcNow) == 0)
            return await FinalizeAsync(id, learnerId, true, ct);
        if (attempt.TrangThai is not (ExamCore.Submitted or ExamCore.Expired))
            throw new ExamProblem("ATTEMPT_NOT_FINALIZED", "Bài thi chưa kết thúc.", 409);
        return ComposeResult(attempt, snapshot.Questions, snapshot.Answers, snapshot.ExamName);
    }

    private ExamResultDto ComposeResult(KetQuaLamBai attempt,
        IReadOnlyList<CauHoiLuotLam> questions, IReadOnlyDictionary<int, ChiTietKetQua> answers,
        string? examName)
    {
        return grading.Grade(attempt, questions, answers)
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
        var kind = await db.KetQuaLamBai.AsNoTracking()
            .Where(a => a.MaKetQua == id && a.MaHocVien == learnerId)
            .Select(a => a.LoaiBaiLam).FirstOrDefaultAsync(ct);
        if (kind == ExamCore.Mock)
            throw new ExamProblem("MOCK_REVIEW_UNAVAILABLE", "Đề thi thử chỉ hiển thị điểm và thống kê theo Part.", 403);
        var snapshot = await snapshots.LoadAsync(id, learnerId, SnapshotView.Review, false, ct);
        var attempt = snapshot.Attempt;
        if (attempt.TrangThai is ExamCore.Active or "BO_DO" && ExamTimer.Remaining(attempt, DateTime.UtcNow) == 0)
        {
            _ = await FinalizeAsync(id, learnerId, true, ct);
            snapshot = await snapshots.LoadAsync(id, learnerId, SnapshotView.Review, false, ct);
            attempt = snapshot.Attempt;
        }
        if (attempt.TrangThai is not (ExamCore.Submitted or ExamCore.Expired))
            throw new ExamProblem("ATTEMPT_NOT_FINALIZED", "Bài thi chưa kết thúc.", 409);
        var questions = snapshot.Questions;
        var groups = snapshot.Groups;
        var answers = snapshot.Answers;
        var result = ComposeResult(attempt, questions, answers, snapshot.ExamName);
        ReviewQuestionDto Map(CauHoiLuotLam q)
        {
            answers.TryGetValue(q.MaCauHoiLuotLam, out var a);
            var status = string.IsNullOrWhiteSpace(a?.DapAnChon) ? "UNANSWERED" :
                a.DapAnChon.Trim() == q.PhuongAnDung.Trim() ? "CORRECT" : "INCORRECT";
            return new(q.MaCauHoiLuotLam, q.ThuTu, q.MaPartNavigation.SoPart, q.NoiDung,
                q.PhuongAnA, q.PhuongAnB, q.PhuongAnC, q.PhuongAnD, a?.DapAnChon,
                q.PhuongAnDung, status, q.GiaiThich ?? "Chưa có giải thích cho câu hỏi này.", a?.DanhDau ?? false);
        }
        var questionsByGroup = questions.Where(q => q.MaNhomLuotLam.HasValue)
            .GroupBy(q => q.MaNhomLuotLam!.Value).ToDictionary(g => g.Key, g => g.ToList());
        return new(id, groups.Select(g => new ReviewGroupDto(g.MaNhomLuotLam, g.ThuTu,
            g.MaPartNavigation.SoPart, g.NoiDungNguLieu,
            !string.IsNullOrWhiteSpace(g.DuongDanAudio), !string.IsNullOrWhiteSpace(g.DuongDanAnh),
            MapDocuments(id, g.MaNhomLuotLam, g.TaiLieuJson),
            questionsByGroup.GetValueOrDefault(g.MaNhomLuotLam, []).Select(Map).ToList())).ToList(),
            questions.Where(q => q.MaNhomLuotLam == null).Select(Map).ToList())
            { Source = result.Source, Result = result };
    }

    private async Task<List<CauHoiLuotLam>> ResultQuestions(int id, CancellationToken ct)
    {
        var rows = await db.CauHoiLuotLam.AsNoTracking().Where(q => q.MaKetQua == id)
            .OrderBy(q => q.ThuTu).Select(q => new {
                q.MaCauHoiLuotLam, q.MaCauHoiGoc, Part = q.MaPartNavigation.SoPart, q.PhuongAnDung
            }).ToListAsync(ct);
        return rows.Select(q => new CauHoiLuotLam {
            MaCauHoiLuotLam = q.MaCauHoiLuotLam, MaCauHoiGoc = q.MaCauHoiGoc,
            MaPartNavigation = new PartTOEIC { SoPart = q.Part }, PhuongAnDung = q.PhuongAnDung
        }).ToList();
    }

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
