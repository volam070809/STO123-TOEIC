using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using STO123.Models;

namespace STO123.Services.Exam;

public static class PracticeQuestionBank
{
    public const string Discriminator = "LUYEN_TAP";
    public const string AttemptType = "PRACTICE";
    public const string Short = "SHORT";
    public const string Medium = "MEDIUM";
    public const string Long = "LONG";
    public static readonly IReadOnlyList<string> Presets = [Short, Medium, Long];
    private static readonly IReadOnlyDictionary<int, int[]> Targets = new Dictionary<int, int[]> {
        [1] = [5, 10, 15], [2] = [10, 15, 20], [3] = [2, 3, 5],
        [4] = [2, 3, 5], [5] = [10, 20, 30], [6] = [1, 2, 3], [7] = [1, 2, 3]
    };
    public static readonly IReadOnlySet<int> GroupedParts = new HashSet<int> { 3, 4, 6, 7 };
    public static bool TryTarget(int part, string? preset, out int count)
    {
        count = 0;
        var index = preset switch { Short => 0, Medium => 1, Long => 2, _ => -1 };
        if (index < 0 || !Targets.TryGetValue(part, out var targets)) return false;
        count = targets[index];
        return true;
    }

    // SQL Server ROUND uses commercial rounding: positive midpoint values round up.
    public static int GroupDifficulty(IEnumerable<int> questionDifficulties) =>
        (int)Math.Round(questionDifficulties.Average(value => (decimal)value), 0,
            MidpointRounding.AwayFromZero);
}

public static class PracticeHistoryMetrics
{
    // Unanswered occurrences remain in the denominator, as in Correct / Total.
    public static decimal Accuracy(int correct, int total) => total == 0 ? 0 :
        Math.Round(100m * correct / total, 1);
}

public sealed class PracticePartService(ToeicDbContext db)
{
    private sealed record Unit(int? ResourceId, int Part, int Difficulty, List<CauHoi> Questions,
        NguLieu? Resource, IReadOnlyList<NguLieuTaiLieu> Documents);
    private sealed record SourceRow(CauHoi Question, int Part, int? ResourceId, int? Order);

    public async Task<object> AvailabilityAsync(CancellationToken ct)
    {
        var units = await LoadUnitsAsync(null, null, ct, includeSnapshotText: false);
        return new { parts = Enumerable.Range(1, 7).Select(part => new {
            part, difficulties = Enumerable.Range(1, 3).Select(difficulty => new {
                difficulty, unitType = PracticeQuestionBank.GroupedParts.Contains(part) ? "GROUP" : "QUESTION",
                validUnitCount = units.Count(x => x.Part == part && x.Difficulty == difficulty),
                presets = PracticeQuestionBank.Presets.Select(preset => {
                    PracticeQuestionBank.TryTarget(part, preset, out var targetUnitCount);
                    return new { preset, targetUnitCount,
                        available = units.Count(x => x.Part == part && x.Difficulty == difficulty) >= targetUnitCount };
                }).ToArray()
            })
        }) };
    }

    public async Task<object> StartAsync(int learnerId, int part, int difficulty, string? preset, CancellationToken ct)
    {
        if (difficulty is < 1 or > 3 || !PracticeQuestionBank.TryTarget(part, preset, out var target))
            throw new ExamProblem("INVALID_REQUEST", "Part, độ khó hoặc mức luyện tập không hợp lệ.");
        var units = await LoadUnitsAsync(part, difficulty, ct);
        if (units.Count == 0)
            throw new ExamProblem("NO_DATA", "Chưa có câu hỏi hoặc nhóm hợp lệ cho Part và độ khó đã chọn.", 409);
        if (units.Count < target)
            throw new ExamProblem("INSUFFICIENT_CONTENT", $"Chỉ có {units.Count} câu hỏi hoặc nhóm hợp lệ; mức {preset} cần {target}.", 409);

        var sourceQuestionIds = units.SelectMany(x => x.Questions).Select(x => x.MaCauHoi).Distinct().ToArray();
        var past = await (from answer in db.ChiTietKetQua.AsNoTracking()
                          join q in db.CauHoiLuotLam.AsNoTracking() on answer.MaCauHoiLuotLam equals q.MaCauHoiLuotLam
                          join priorAttempt in db.KetQuaLamBai.AsNoTracking() on q.MaKetQua equals priorAttempt.MaKetQua
                          where priorAttempt.MaHocVien == learnerId && priorAttempt.LoaiBaiLam == PracticeQuestionBank.AttemptType &&
                                sourceQuestionIds.Contains(q.MaCauHoiGoc ?? 0)
                          select new { SourceId = q.MaCauHoiGoc!.Value, answer.DapAnChon, q.PhuongAnDung, priorAttempt.NgayLamBai })
            .ToListAsync(ct);
        var history = past.GroupBy(x => x.SourceId).ToDictionary(g => g.Key, g => new {
            Last = g.Max(x => x.NgayLamBai), Wrong = g.Any(x => !string.IsNullOrWhiteSpace(x.DapAnChon) && x.DapAnChon != x.PhuongAnDung)
        });
        var random = Random.Shared;
        units = units.OrderBy(unit => unit.Questions.Any(q => !history.ContainsKey(q.MaCauHoi)) ? 0 : 1)
            .ThenBy(unit => unit.Questions.Any(q => history.TryGetValue(q.MaCauHoi, out var h) && h.Wrong) ? 0 : 1)
            .ThenBy(unit => unit.Questions.Where(q => history.ContainsKey(q.MaCauHoi))
                .Select(q => history[q.MaCauHoi].Last).DefaultIfEmpty(DateTime.MinValue).Max())
            .ThenBy(_ => random.Next()).Take(target).ToList();

        var now = DateTime.UtcNow;
        var attempt = new KetQuaLamBai { MaHocVien = learnerId, MaLuotLam = Guid.NewGuid(),
            LoaiBaiLam = PracticeQuestionBank.AttemptType, TrangThai = ExamCore.Active, NgayLamBai = now };
        var groupByResource = new Dictionary<int, NhomLuotLam>();
        var groupsInOrder = new List<NhomLuotLam>();
        foreach (var unit in units)
        {
            NhomLuotLam? group = null;
            if (unit.Resource is not null)
            {
                if (!groupByResource.TryGetValue(unit.Resource.MaNguLieu, out group))
                {
                    group = new NhomLuotLam { MaNguLieuGoc = unit.Resource.MaNguLieu,
                        MaPart = unit.Questions[0].MaPart, ThuTu = groupsInOrder.Count + 1,
                        NoiDungNguLieu = unit.Resource.NoiDungNguLieu, NoiDungDich = unit.Resource.NoiDungDich,
                        DuongDanAudio = unit.Resource.DuongDanAudio, DuongDanAnh = unit.Resource.DuongDanAnh,
                        TaiLieuJson = unit.Documents.Count == 0 ? null : ExamDocumentCodec.Encode(unit.Documents.Select(d =>
                            new ExamDocumentSnapshot(d.ThuTu, d.LoaiTaiLieu, d.NoiDung, d.DuongDanAnh))) };
                    groupByResource.Add(unit.Resource.MaNguLieu, group); groupsInOrder.Add(group); attempt.NhomLuotLam.Add(group);
                }
            }
            foreach (var q in unit.Questions)
                attempt.CauHoiLuotLam.Add(new CauHoiLuotLam { MaCauHoiGoc = q.MaCauHoi,
                    MaNhomLuotLamNavigation = group, MaPart = q.MaPart, ThuTu = attempt.CauHoiLuotLam.Count + 1,
                    ThuTuTrongPart = attempt.CauHoiLuotLam.Count + 1, NoiDung = q.NoiDung,
                    PhuongAnA = q.PhuongAnA, PhuongAnB = q.PhuongAnB, PhuongAnC = q.PhuongAnC,
                    PhuongAnD = q.PhuongAnD, PhuongAnDung = q.PhuongAnDung, GiaiThich = q.GiaiThich });
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockResourceAsync($"practice-start:{learnerId}", ct);
        var unfinished = await db.KetQuaLamBai.AsNoTracking().AnyAsync(a =>
            a.MaHocVien == learnerId && a.LoaiBaiLam == PracticeQuestionBank.AttemptType &&
            a.TrangThai == ExamCore.Active, ct);
        if (unfinished)
            throw new ExamProblem("PRACTICE_ACTIVE_EXISTS", "Bạn có bài luyện đang làm. Hãy Tiếp tục hoặc hoàn thành bài đó trước khi bắt đầu bài mới.", 409);
        db.KetQuaLamBai.Add(attempt);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Payload(attempt, part, difficulty, preset, target, isReview: false);
    }

    public async Task<object?> ActiveAsync(int learnerId, CancellationToken ct)
    {
        var attempt = await db.KetQuaLamBai.AsNoTracking()
            .Where(a => a.MaHocVien == learnerId && a.LoaiBaiLam == PracticeQuestionBank.AttemptType &&
                a.TrangThai == ExamCore.Active)
            .OrderByDescending(a => a.NgayLamBai).ThenByDescending(a => a.MaKetQua)
            .Include(a => a.NhomLuotLam)
            .Include(a => a.CauHoiLuotLam).ThenInclude(q => q.ChiTietKetQua)
            .Include(a => a.CauHoiLuotLam).ThenInclude(q => q.MaPartNavigation)
            .AsSplitQuery().FirstOrDefaultAsync(ct);
        if (attempt is null) return null;
        var part = attempt.CauHoiLuotLam.FirstOrDefault()?.MaPartNavigation?.SoPart ?? 0;
        return Payload(attempt, part, null, null, null, isReview: false);
    }

    public async Task<object> ActiveListAsync(int learnerId, CancellationToken ct)
    {
        var rows = await db.KetQuaLamBai.AsNoTracking()
            .Where(a => a.MaHocVien == learnerId && a.LoaiBaiLam == PracticeQuestionBank.AttemptType &&
                a.TrangThai == ExamCore.Active)
            .OrderByDescending(a => a.NgayLamBai).ThenByDescending(a => a.MaKetQua)
            .Select(a => new { attemptId = a.MaKetQua, startedAt = a.NgayLamBai,
                totalQuestions = a.CauHoiLuotLam.Count(), groupCount = a.NhomLuotLam.Count(),
                checkedQuestions = a.CauHoiLuotLam.Count(q => q.ChiTietKetQua != null &&
                    q.ChiTietKetQua.DapAnChon != null),
                part = a.CauHoiLuotLam.Select(q => q.MaPartNavigation.SoPart).FirstOrDefault() })
            .ToListAsync(ct);
        return new { items = rows.Select(row => new { row.attemptId,
            startedAt = DateTime.SpecifyKind(row.startedAt, DateTimeKind.Utc), row.totalQuestions,
            row.groupCount, row.checkedQuestions, row.part, preset = (string?)null
        }).ToArray() };
    }

    public async Task<object> ContinueAsync(int learnerId, int attemptId, CancellationToken ct)
    {
        var attempt = await db.KetQuaLamBai.AsNoTracking()
            .Where(a => a.MaKetQua == attemptId && a.MaHocVien == learnerId &&
                a.LoaiBaiLam == PracticeQuestionBank.AttemptType && a.TrangThai == ExamCore.Active)
            .Include(a => a.NhomLuotLam)
            .Include(a => a.CauHoiLuotLam).ThenInclude(q => q.ChiTietKetQua)
            .Include(a => a.CauHoiLuotLam).ThenInclude(q => q.MaPartNavigation)
            .AsSplitQuery().FirstOrDefaultAsync(ct) ??
            throw new ExamProblem("PRACTICE_NOT_FOUND", "Không tìm thấy bài luyện đang làm.", 404);
        var part = attempt.CauHoiLuotLam.FirstOrDefault()?.MaPartNavigation?.SoPart ?? 0;
        var unitCount = PracticeQuestionBank.GroupedParts.Contains(part) ?
            attempt.NhomLuotLam.Count : attempt.CauHoiLuotLam.Count;
        return Payload(attempt, part, null, null, unitCount, isReview: false);
    }

    public async Task<object> CheckAsync(int learnerId, int attemptId, int questionId, string? selected, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAttemptAsync(attemptId, ct);
        var question = await ActiveQuestionAsync(learnerId, attemptId, questionId, ct);
        selected = selected?.Trim().ToUpperInvariant();
        if (selected is not ("A" or "B" or "C" or "D") || (selected == "D" && string.IsNullOrWhiteSpace(question.PhuongAnD)))
            throw new ExamProblem("INVALID_ANSWER", "Hãy chọn một đáp án hợp lệ.");
        var detail = await db.ChiTietKetQua.FindAsync([questionId], ct);
        if (!string.IsNullOrWhiteSpace(detail?.DapAnChon))
            throw new ExamProblem("PRACTICE_ALREADY_CHECKED", "Câu này đã được kiểm tra. Hãy chọn Làm lại câu này nếu muốn thử lại.", 409);
        if (detail is null) { detail = new ChiTietKetQua { MaCauHoiLuotLam = questionId }; db.ChiTietKetQua.Add(detail); }
        detail.DapAnChon = selected;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is SqlException sql && sql.Number is 2601 or 2627)
        {
            throw new ExamProblem("PRACTICE_ALREADY_CHECKED", "Câu này đã được kiểm tra. Hãy tải lại bài luyện.", 409);
        }
        await transaction.CommitAsync(ct);
        return new { questionOccurrenceId = questionId, selectedOption = selected,
            isCorrect = selected == question.PhuongAnDung, correctOption = question.PhuongAnDung,
            explanation = question.GiaiThich, reveal = Reveal(question) };
    }

    public async Task<object> SeeAnswerAsync(int learnerId, int attemptId, int questionId, CancellationToken ct)
    {
        var question = await ActiveQuestionAsync(learnerId, attemptId, questionId, ct);
        return new { questionOccurrenceId = questionId, selectedOption = (string?)null,
            isCorrect = (bool?)null, correctOption = question.PhuongAnDung,
            explanation = question.GiaiThich, reveal = Reveal(question) };
    }

    public async Task<object> RedoAsync(int learnerId, int attemptId, int questionId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAttemptAsync(attemptId, ct);
        _ = await ActiveQuestionAsync(learnerId, attemptId, questionId, ct);
        var detail = await db.ChiTietKetQua.FindAsync([questionId], ct);
        if (detail is not null)
        {
            detail.DapAnChon = null;
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return new { questionOccurrenceId = questionId, selectedOption = (string?)null };
    }

    private Task LockAttemptAsync(int attemptId, CancellationToken ct) =>
        LockResourceAsync($"practice-attempt:{attemptId}", ct);

    private async Task LockResourceAsync(string resource, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @lockResult int;
                EXEC @lockResult = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive',
                    @LockOwner='Transaction', @LockTimeout=10000;
                IF @lockResult < 0 THROW 51000, 'Practice attempt is busy.', 1;
                """, ct);
        }
        catch (SqlException e) when (e.Number == 51000)
        {
            throw new ExamProblem("PRACTICE_BUSY", "Bài luyện đang được cập nhật. Vui lòng thử lại.", 409);
        }
    }

    private async Task<CauHoiLuotLam> ActiveQuestionAsync(int learnerId, int attemptId, int questionId, CancellationToken ct) =>
        await db.CauHoiLuotLam.Include(q => q.MaKetQuaNavigation).Include(q => q.MaNhomLuotLamNavigation)
            .FirstOrDefaultAsync(q => q.MaCauHoiLuotLam == questionId && q.MaKetQua == attemptId &&
                q.MaKetQuaNavigation.MaHocVien == learnerId &&
                q.MaKetQuaNavigation.LoaiBaiLam == PracticeQuestionBank.AttemptType &&
                q.MaKetQuaNavigation.TrangThai == ExamCore.Active, ct) ??
        throw new ExamProblem("PRACTICE_QUESTION_NOT_FOUND", "Câu hỏi luyện tập không tồn tại.", 404);

    private static object Reveal(CauHoiLuotLam q) => new {
        q.NoiDung, q.PhuongAnA, q.PhuongAnB, q.PhuongAnC, q.PhuongAnD,
        context = q.MaNhomLuotLamNavigation?.NoiDungNguLieu
    };

    public async Task<object> FinishAsync(int learnerId, int attemptId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAttemptAsync(attemptId, ct);
        var attempt = await db.KetQuaLamBai.Include(a => a.CauHoiLuotLam).ThenInclude(q => q.ChiTietKetQua)
            .FirstOrDefaultAsync(a => a.MaKetQua == attemptId && a.MaHocVien == learnerId &&
                a.LoaiBaiLam == PracticeQuestionBank.AttemptType, ct);
        if (attempt is null) throw new ExamProblem("PRACTICE_NOT_FOUND", "Lượt luyện tập không tồn tại.", 404);
        if (attempt.TrangThai == ExamCore.Active)
        { attempt.TrangThai = ExamCore.Submitted; attempt.NgayNopBai = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
        await transaction.CommitAsync(ct);
        return Summary(attempt);
    }

    public async Task<object> HistoryAsync(int learnerId, int page, int pageSize, int? part, string? sort, CancellationToken ct)
    {
        sort = (sort ?? "NEWEST").Trim().ToUpperInvariant();
        if (page < 1 || pageSize is < 5 or > 10 || part is < 1 or > 7 ||
            sort is not ("NEWEST" or "OLDEST"))
            throw new ExamProblem("INVALID_REQUEST", "Bộ lọc hoặc trang lịch sử không hợp lệ.");
        var completed = db.KetQuaLamBai.AsNoTracking().Where(a => a.MaHocVien == learnerId &&
            a.LoaiBaiLam == PracticeQuestionBank.AttemptType && a.NgayNopBai != null);
        var completedAttempts = await completed.CountAsync(ct);
        var counts = await (from question in db.CauHoiLuotLam.AsNoTracking()
                            join attempt in completed on question.MaKetQua equals attempt.MaKetQua
                            group question by 1 into grouped
                            select new {
                                Total = grouped.Count(),
                                Answered = grouped.Sum(q => q.ChiTietKetQua != null && q.ChiTietKetQua.DapAnChon != null ? 1 : 0),
                                Correct = grouped.Sum(q => q.ChiTietKetQua != null && q.ChiTietKetQua.DapAnChon == q.PhuongAnDung ? 1 : 0)
                            }).FirstOrDefaultAsync(ct);
        var historyQuery = part.HasValue ? completed.Where(a => a.CauHoiLuotLam.Any(q => q.MaPartNavigation.SoPart == part.Value)) : completed;
        var total = part.HasValue ? await historyQuery.CountAsync(ct) : completedAttempts;
        var ordered = sort == "OLDEST" ? historyQuery.OrderBy(a => a.NgayNopBai).ThenBy(a => a.MaKetQua) :
            historyQuery.OrderByDescending(a => a.NgayNopBai).ThenByDescending(a => a.MaKetQua);
        var rows = await ordered
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new { a.MaKetQua, a.NgayLamBai, a.NgayNopBai, a.TrangThai, Total = a.CauHoiLuotLam.Count(),
                Answered = a.CauHoiLuotLam.Count(q => q.ChiTietKetQua != null && q.ChiTietKetQua.DapAnChon != null),
                Correct = a.CauHoiLuotLam.Count(q => q.ChiTietKetQua != null && q.ChiTietKetQua.DapAnChon == q.PhuongAnDung),
                Part = a.CauHoiLuotLam.Select(q => q.MaPartNavigation.SoPart).FirstOrDefault() })
            .ToListAsync(ct);
        return new { items = rows.Select(a => new { attemptId = a.MaKetQua,
            startedAt = DateTime.SpecifyKind(a.NgayLamBai, DateTimeKind.Utc),
            finishedAt = DateTime.SpecifyKind(a.NgayNopBai!.Value, DateTimeKind.Utc),
            status = a.TrangThai, total = a.Total, attempted = a.Answered,
            correct = a.Correct, incorrect = a.Answered - a.Correct, unanswered = a.Total - a.Answered,
            accuracy = PracticeHistoryMetrics.Accuracy(a.Correct, a.Total), part = a.Part }).ToArray(),
            summary = new { totalCompleted = completedAttempts, totalQuestions = counts?.Total ?? 0,
                totalAnswered = counts?.Answered ?? 0, totalCorrect = counts?.Correct ?? 0,
                overallAccuracy = PracticeHistoryMetrics.Accuracy(counts?.Correct ?? 0, counts?.Total ?? 0) },
            total, page, pageSize, part, sort };
    }

    public async Task<object> ReviewAsync(int learnerId, int attemptId, CancellationToken ct)
    {
        var attempt = await db.KetQuaLamBai.AsNoTracking().Where(a => a.MaKetQua == attemptId &&
                a.MaHocVien == learnerId && a.LoaiBaiLam == PracticeQuestionBank.AttemptType && a.NgayNopBai != null)
            .Include(a => a.NhomLuotLam)
            .Include(a => a.CauHoiLuotLam).ThenInclude(q => q.ChiTietKetQua)
            .Include(a => a.CauHoiLuotLam).ThenInclude(q => q.MaPartNavigation)
            .AsSplitQuery().FirstOrDefaultAsync(ct);
        if (attempt is null) throw new ExamProblem("PRACTICE_NOT_FOUND", "Lượt luyện tập đã nộp không tồn tại.", 404);
        return Payload(attempt, attempt.CauHoiLuotLam.FirstOrDefault()?.MaPartNavigation.SoPart ?? 0,
            null, null, null, true);
    }

    private async Task<List<Unit>> LoadUnitsAsync(int? part, int? difficulty, CancellationToken ct,
        bool includeSnapshotText = true)
    {
        var query = db.CauHoi.AsNoTracking().Where(q =>
            q.LoaiCauHoi == PracticeQuestionBank.Discriminator && q.TrangThai == ExamCore.Published);
        if (part.HasValue) query = query.Where(q => q.MaPartNavigation.SoPart == part.Value);
        // A grouped Part must be assembled from every member before its difficulty can be calculated.
        if (difficulty.HasValue && part.HasValue && !PracticeQuestionBank.GroupedParts.Contains(part.Value))
            query = query.Where(q => q.DoKho == difficulty.Value);
        var source = await query.Select(q => new SourceRow(new CauHoi {
            MaCauHoi = q.MaCauHoi, MaPart = q.MaPart, DoKho = q.DoKho,
            LoaiCauHoi = q.LoaiCauHoi, TrangThai = q.TrangThai,
            NoiDung = includeSnapshotText ? q.NoiDung : null,
            PhuongAnA = q.PhuongAnA, PhuongAnB = q.PhuongAnB, PhuongAnC = q.PhuongAnC,
            PhuongAnD = q.PhuongAnD, PhuongAnDung = q.PhuongAnDung,
            GiaiThich = includeSnapshotText ? q.GiaiThich : null
        }, q.MaPartNavigation.SoPart, q.NhomCauHoi == null ? null : q.NhomCauHoi.MaNguLieu,
            q.NhomCauHoi == null ? null : q.NhomCauHoi.ThuTu)).ToListAsync(ct);
        var resourceIds = source.Where(q => q.ResourceId.HasValue).Select(q => q.ResourceId!.Value)
            .Distinct().ToArray();
        if (resourceIds.Length == 0) return FilterDifficulty(BuildUnits(source, [], [], []), difficulty);

        // Each query is bounded by the selected resource IDs. All members are loaded so
        // an unpublished or non-Practice member cannot be silently dropped.
        var members = await db.NhomCauHoi.AsNoTracking()
            .Where(m => resourceIds.Contains(m.MaNguLieu))
            .Select(m => new SourceRow(new CauHoi {
                MaCauHoi = m.MaCauHoiNavigation.MaCauHoi, MaPart = m.MaCauHoiNavigation.MaPart,
                DoKho = m.MaCauHoiNavigation.DoKho, LoaiCauHoi = m.MaCauHoiNavigation.LoaiCauHoi,
                TrangThai = m.MaCauHoiNavigation.TrangThai,
                NoiDung = includeSnapshotText ? m.MaCauHoiNavigation.NoiDung : null,
                PhuongAnA = m.MaCauHoiNavigation.PhuongAnA, PhuongAnB = m.MaCauHoiNavigation.PhuongAnB,
                PhuongAnC = m.MaCauHoiNavigation.PhuongAnC, PhuongAnD = m.MaCauHoiNavigation.PhuongAnD,
                PhuongAnDung = m.MaCauHoiNavigation.PhuongAnDung,
                GiaiThich = includeSnapshotText ? m.MaCauHoiNavigation.GiaiThich : null
            }, m.MaCauHoiNavigation.MaPartNavigation.SoPart, m.MaNguLieu, m.ThuTu))
            .ToListAsync(ct);
        var resources = await db.NguLieu.AsNoTracking().Where(r => resourceIds.Contains(r.MaNguLieu))
            .Select(r => new NguLieu { MaNguLieu = r.MaNguLieu, NoiDungNguLieu = r.NoiDungNguLieu,
                NoiDungDich = includeSnapshotText ? r.NoiDungDich : null,
                DuongDanAudio = r.DuongDanAudio, DuongDanAnh = r.DuongDanAnh })
            .ToListAsync(ct);
        var documents = await db.NguLieuTaiLieu.AsNoTracking().Where(d => resourceIds.Contains(d.MaNguLieu))
            .Select(d => new NguLieuTaiLieu { MaTaiLieu = d.MaTaiLieu, MaNguLieu = d.MaNguLieu,
                ThuTu = d.ThuTu, LoaiTaiLieu = d.LoaiTaiLieu == "VAN_BAN" ? "TEXT" : d.LoaiTaiLieu,
                NoiDung = d.NoiDung,
                DuongDanAnh = d.DuongDanAnh }).ToListAsync(ct);
        return FilterDifficulty(BuildUnits(source, members, resources, documents), difficulty);
    }

    private static List<Unit> FilterDifficulty(List<Unit> units, int? difficulty) =>
        difficulty.HasValue ? units.Where(unit => unit.Difficulty == difficulty.Value).ToList() : units;

    private static List<Unit> BuildUnits(IReadOnlyList<SourceRow> source, IReadOnlyList<SourceRow> members,
        IReadOnlyList<NguLieu> resources, IReadOnlyList<NguLieuTaiLieu> documents)
    {
        var result = new List<Unit>();
        var membersByResource = members.Where(x => x.ResourceId.HasValue)
            .GroupBy(x => x.ResourceId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Order).ToArray());
        var resourcesById = resources.ToDictionary(x => x.MaNguLieu);
        var documentsByResource = documents.GroupBy(x => x.MaNguLieu)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<NguLieuTaiLieu>)g.OrderBy(x => x.ThuTu).ToArray());

        foreach (var row in source.Where(x => !PracticeQuestionBank.GroupedParts.Contains(x.Part)))
        {
            var q = row.Question;
            if (!ExamSourceValidator.ValidQuestion(q, row.Part)) continue;
            if (row.Part == 5)
            {
                if (row.ResourceId is null)
                    result.Add(new Unit(null, row.Part, q.DoKho, [q], null, []));
                continue;
            }
            if (row.ResourceId is not { } id || !resourcesById.TryGetValue(id, out var resource) ||
                !membersByResource.TryGetValue(id, out var group) || group.Length != 1 ||
                group[0].Question.MaCauHoi != q.MaCauHoi || group[0].Order != 1 ||
                !ExamSourceValidator.ValidMedia(resource, row.Part)) continue;
            var docs = documentsByResource.GetValueOrDefault(id, []);
            if (!ExamSourceValidator.ValidDocuments(row.Part, docs)) continue;
            result.Add(new Unit(id, row.Part, q.DoKho, [q], resource, docs));
        }

        foreach (var candidate in source.Where(x => PracticeQuestionBank.GroupedParts.Contains(x.Part) &&
                     x.ResourceId.HasValue).GroupBy(x => x.ResourceId!.Value).Select(g => g.First()))
        {
            var id = candidate.ResourceId!.Value;
            if (!membersByResource.TryGetValue(id, out var group) ||
                !resourcesById.TryGetValue(id, out var resource)) continue;
            var part = candidate.Part;
            var orderedMemberships = group.Select(x => new NhomCauHoi {
                MaCauHoi = x.Question.MaCauHoi, MaNguLieu = id, ThuTu = x.Order ?? 0
            }).ToArray();
            if (!ExamSourceValidator.ValidGroup(part, orderedMemberships) ||
                group.Select(x => x.Question.MaCauHoi).Distinct().Count() != group.Length || group.Any(x =>
                x.Part != part || x.Question.DoKho is < 1 or > 3 ||
                x.Question.LoaiCauHoi != PracticeQuestionBank.Discriminator ||
                x.Question.TrangThai != ExamCore.Published ||
                !ExamSourceValidator.ValidQuestion(x.Question, part)) ||
                !ExamSourceValidator.ValidMedia(resource, part)) continue;
            var docs = documentsByResource.GetValueOrDefault(id, []);
            if (!ExamSourceValidator.ValidDocuments(part, docs)) continue;
            var groupDifficulty = PracticeQuestionBank.GroupDifficulty(group.Select(x => (int)x.Question.DoKho));
            result.Add(new Unit(id, part, groupDifficulty, group.Select(x => x.Question).ToList(), resource, docs));
        }
        return result;
    }

    private static object Payload(KetQuaLamBai attempt, int part, int? difficulty,
        string? preset, int? selectedUnitCount, bool isReview)
    {
        var groups = attempt.NhomLuotLam.OrderBy(g => g.ThuTu).Select(g => new {
            groupId = g.MaNhomLuotLam, g.ThuTu,
            context = isReview || part >= 5 || attempt.CauHoiLuotLam.Any(q =>
                q.MaNhomLuotLam == g.MaNhomLuotLam && !string.IsNullOrWhiteSpace(q.ChiTietKetQua?.DapAnChon))
                ? g.NoiDungNguLieu : null,
            audioUrl = string.IsNullOrWhiteSpace(g.DuongDanAudio) ? null : $"/api/attempts/{attempt.MaKetQua}/groups/{g.MaNhomLuotLam}/audio",
            imageUrl = string.IsNullOrWhiteSpace(g.DuongDanAnh) ? null : $"/api/attempts/{attempt.MaKetQua}/groups/{g.MaNhomLuotLam}/image",
            documents = (isReview || part >= 5 ? ExamDocumentCodec.Decode(g.TaiLieuJson) : []).Select(d => new { d.Order, d.Type, content = ExamDocumentCodec.ToContent(d),
                imageUrl = string.IsNullOrWhiteSpace(d.ImagePath) ? null : $"/api/attempts/{attempt.MaKetQua}/groups/{g.MaNhomLuotLam}/documents/{d.Order}/image" })
        }).ToArray();
        var questions = attempt.CauHoiLuotLam.OrderBy(q => q.ThuTu).Select(q => new {
            questionOccurrenceId = q.MaCauHoiLuotLam, q.ThuTu, part = q.MaPartNavigation?.SoPart ?? part,
            groupId = q.MaNhomLuotLam, noiDung = isReview || part > 2 ? q.NoiDung : null,
            phuongAnA = isReview || part > 2 ? q.PhuongAnA : null,
            phuongAnB = isReview || part > 2 ? q.PhuongAnB : null,
            phuongAnC = isReview || part > 2 ? q.PhuongAnC : null,
            phuongAnD = isReview || part > 2 ? q.PhuongAnD : null,
            isChecked = !string.IsNullOrWhiteSpace(q.ChiTietKetQua?.DapAnChon),
            selectedOption = q.ChiTietKetQua?.DapAnChon,
            correctOption = isReview || !string.IsNullOrWhiteSpace(q.ChiTietKetQua?.DapAnChon) ? q.PhuongAnDung : null,
            isCorrect = q.ChiTietKetQua?.DapAnChon is { } selected ? selected == q.PhuongAnDung : (bool?)null,
            explanation = isReview || !string.IsNullOrWhiteSpace(q.ChiTietKetQua?.DapAnChon) ? q.GiaiThich : null,
            reveal = !isReview && !string.IsNullOrWhiteSpace(q.ChiTietKetQua?.DapAnChon) ? Reveal(q) : null
        }).ToArray();
        return new { attemptId = attempt.MaKetQua, status = attempt.TrangThai, part, difficulty,
            preset, selectedUnitCount = selectedUnitCount ??
                (PracticeQuestionBank.GroupedParts.Contains(part) ? groups.Length : questions.Length),
            actualQuestionCount = questions.Length, actualGroupCount = groups.Length,
            groups, questions, isReview, result = isReview ? Summary(attempt) : null };
    }

    private static object Summary(KetQuaLamBai attempt)
    {
        var total = attempt.CauHoiLuotLam.Count;
        var answered = attempt.CauHoiLuotLam.Count(q => !string.IsNullOrWhiteSpace(q.ChiTietKetQua?.DapAnChon));
        var correct = attempt.CauHoiLuotLam.Count(q => q.ChiTietKetQua?.DapAnChon == q.PhuongAnDung);
        return new { attemptId = attempt.MaKetQua, total, attempted = answered, correct,
            incorrect = answered - correct, unanswered = total - answered,
            accuracy = PracticeHistoryMetrics.Accuracy(correct, total), status = attempt.TrangThai };
    }
}
