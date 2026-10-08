using Azure.Storage.Blobs;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using STO123.Models;

namespace STO123.Services.Exam;

public sealed record PlannedUnit(int Part, NguLieu? Resource, IReadOnlyList<CauHoi> Questions,
    IReadOnlyList<NguLieuTaiLieu> Documents);

public sealed class ExamGenerationService(ToeicDbContext db, IConfiguration configuration,
    BlobServiceClient blobs, ExamPartLookup partLookup, RandomStartProfiler profile, IMemoryCache mediaCache)
{
    private static ExamProblem Invalid() => new("INVALID_EXAM_STRUCTURE", "Cấu trúc đề thi không hợp lệ.", 409);

    public Task<IReadOnlyList<PlannedUnit>> BuildAsync(int? fixedExamId, CancellationToken ct,
        int? selectedPart = null) => BuildCoreAsync(fixedExamId, ct, selectedPart, false);

    public Task<IReadOnlyList<PlannedUnit>> BuildRandomFullAsync(CancellationToken ct)
        => BuildCoreAsync(null, ct, null, true);

    public Task<IReadOnlyList<PlannedUnit>> BuildAdminCandidatesAsync(CancellationToken ct)
        => BuildCoreAsync(null, ct, null, false, true);

    public Task<IReadOnlyList<PlannedUnit>> ValidateForPublishAsync(int examId, CancellationToken ct)
        => BuildCoreAsync(examId, ct, null, false, false, true);

    private async Task<IReadOnlyList<PlannedUnit>> BuildCoreAsync(int? fixedExamId, CancellationToken ct,
        int? selectedPart, bool randomFull, bool candidateOnly = false, bool allowDraft = false)
    {
        IReadOnlyDictionary<int, int> parts;
        using (profile.Phase("lookup")) parts = (await partLookup.GetAsync(ct)).NumberById;
        if (parts.Values.Distinct().Count() != 7 || !Enumerable.Range(1, 7).All(parts.Values.Contains)) throw Invalid();
        List<CauHoiDeThi>? links = null;
        if (fixedExamId.HasValue)
        {
            var exam = await db.DeThi.AsNoTracking().FirstOrDefaultAsync(x => x.MaDeThi == fixedExamId, ct);
            if (exam is null || exam.LoaiDe != "DE_THI" ||
                (exam.TrangThai != "OPEN" && !(allowDraft && exam.TrangThai == "CLOSE")) ||
                exam.ThoiGianLamBai != ExamCore.DurationMinutes) throw Invalid();
            links = await db.CauHoiDeThi.AsNoTracking().Where(x => x.MaDeThi == fixedExamId)
                .OrderBy(x => x.ThuTu).ToListAsync(ct);
        }
        var questionQuery = db.CauHoi.AsNoTracking().Where(q => q.TrangThai == ExamCore.Published);
        if (selectedPart.HasValue)
        {
            var partId = parts.Single(p => p.Value == selectedPart.Value).Key;
            questionQuery = questionQuery.Where(q => q.MaPart == partId);
        }
        if (links is not null)
        {
            var linkedIds = links.Select(x => x.MaCauHoi).ToArray();
            questionQuery = questionQuery.Where(q => linkedIds.Contains(q.MaCauHoi));
        }
        List<CauHoi> questions;
        using (profile.Phase("candidates"))
            questions = randomFull
                ? await questionQuery.Select(q => new CauHoi {
                    MaCauHoi = q.MaCauHoi, MaPart = q.MaPart, DoKho = q.DoKho,
                    TrangThai = q.TrangThai, PhuongAnA = q.PhuongAnA, PhuongAnB = q.PhuongAnB,
                    PhuongAnC = q.PhuongAnC, PhuongAnD = q.PhuongAnD,
                    PhuongAnDung = q.PhuongAnDung
                }).ToListAsync(ct)
                : await questionQuery.ToListAsync(ct);
        var questionIds = questions.Select(q => q.MaCauHoi).ToArray();
        int[] resourceIds;
        List<NhomCauHoi> memberships;
        using (profile.Phase("memberships"))
        {
            resourceIds = await db.NhomCauHoi.AsNoTracking()
                .Where(m => questionIds.Contains(m.MaCauHoi)).Select(m => m.MaNguLieu).Distinct().ToArrayAsync(ct);
            // Include every member of a selected resource so incomplete or mixed-Part groups remain invalid.
            memberships = await db.NhomCauHoi.AsNoTracking()
                .Where(m => resourceIds.Contains(m.MaNguLieu)).ToListAsync(ct);
        }
        Dictionary<int, NguLieu> resources;
        List<NguLieuTaiLieu> documents;
        using (profile.Phase("resources"))
        {
            resources = await db.NguLieu.AsNoTracking().Where(r => resourceIds.Contains(r.MaNguLieu))
                .ToDictionaryAsync(r => r.MaNguLieu, ct);
            documents = await db.NguLieuTaiLieu.AsNoTracking().Where(d => resourceIds.Contains(d.MaNguLieu))
                .OrderBy(d => d.ThuTu).ToListAsync(ct);
        }
        using var eligibilityPhase = profile.Phase("eligibility");
        var byPart = questions.GroupBy(q => parts.GetValueOrDefault(q.MaPart, 0)).ToDictionary(g => g.Key, g => g.ToList());
        var questionsById = questions.ToDictionary(q => q.MaCauHoi);
        var docsByResource = documents.GroupBy(d => d.MaNguLieu).ToDictionary(g => g.Key, g => g.ToList());
        var memberByQuestion = memberships.ToDictionary(m => m.MaCauHoi);
        var allByResource = memberships.GroupBy(m => m.MaNguLieu).ToDictionary(g => g.Key, g => g.ToList());
        var units = new Dictionary<int, List<PlannedUnit>>();
        foreach (var part in ExamCore.PartCounts.Keys)
        {
            units[part] = [];
            if (!byPart.TryGetValue(part, out var candidates)) continue;
            if (part == 5)
            {
                foreach (var q in candidates.Where(q => !memberByQuestion.ContainsKey(q.MaCauHoi) && ExamSourceValidator.ValidQuestion(q, part)))
                    units[part].Add(new(part, null, [q], []));
                continue;
            }
            if (part is 1 or 2)
            {
                foreach (var q in candidates)
                {
                    if (!ExamSourceValidator.ValidQuestion(q, part) || !memberByQuestion.TryGetValue(q.MaCauHoi, out var member) ||
                        !resources.TryGetValue(member.MaNguLieu, out var resource) ||
                        !ExamSourceValidator.ValidMedia(resource, part) ||
                        !allByResource.TryGetValue(member.MaNguLieu, out var group) ||
                        !ExamSourceValidator.ValidGroup(part, group)) continue;
                    var docs = docsByResource.GetValueOrDefault(resource.MaNguLieu, []);
                    if (!ExamSourceValidator.ValidDocuments(part, docs)) continue;
                    units[part].Add(new(part, resource, [q], docs));
                }
                continue;
            }
            foreach (var group in allByResource.Values)
            {
                if (!resources.TryGetValue(group[0].MaNguLieu, out var resource) ||
                    !ExamSourceValidator.ValidMedia(resource, part) || !ExamSourceValidator.ValidGroup(part, group)) continue;
                // A group containing unpublished, missing, or mixed-Part questions is not a valid unit.
                var groupQuestions = group.Where(m => questionsById.TryGetValue(m.MaCauHoi, out var q) &&
                    parts.GetValueOrDefault(q.MaPart, 0) == part)
                    .ToDictionary(m => m.MaCauHoi, m => questionsById[m.MaCauHoi]);
                if (groupQuestions.Count != group.Count) continue;
                var ordered = group.OrderBy(m => m.ThuTu).Select(m => groupQuestions[m.MaCauHoi]).ToList();
                if (ordered.Any(q => !ExamSourceValidator.ValidQuestion(q, part))) continue;
                var docs = docsByResource.GetValueOrDefault(resource.MaNguLieu, []);
                if (!ExamSourceValidator.ValidDocuments(part, docs)) continue;
                units[part].Add(new(part, resource, ordered, docs));
            }
        }
        eligibilityPhase.Dispose();
        if (randomFull)
        {
            var checkedPaths = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
            while (true)
            {
                IReadOnlyList<PlannedUnit> selected;
                using (profile.Phase("planner")) selected = RandomExamPlanner.Select(units, Random.Shared);
                HashSet<string> available;
                using (profile.Phase("media")) available = await AvailableMediaAsync(selected, ct, checkedPaths, 24, true);
                var missing = selected.SelectMany(MediaPaths).Where(path => !available.Contains(path))
                    .ToHashSet(StringComparer.Ordinal);
                if (missing.Count == 0)
                {
                    var ids = selected.SelectMany(unit => unit.Questions).Select(q => q.MaCauHoi).ToArray();
                    Dictionary<int, CauHoi> complete;
                    using (profile.Phase("selectedQuestions"))
                        complete = await db.CauHoi.AsNoTracking()
                            .Where(q => ids.Contains(q.MaCauHoi) && q.TrangThai == ExamCore.Published)
                            .ToDictionaryAsync(q => q.MaCauHoi, ct);
                    if (complete.Count != 200 || selected.Any(unit => unit.Questions.Any(q =>
                        !complete.TryGetValue(q.MaCauHoi, out var full) || full.MaPart != q.MaPart ||
                        !ExamSourceValidator.ValidQuestion(full, unit.Part)))) throw Invalid();
                    return selected.Select(unit => unit with {
                        Questions = unit.Questions.Select(q => complete[q.MaCauHoi]).ToArray()
                    }).ToArray();
                }
                foreach (var part in ExamCore.PartCounts.Keys)
                    units[part] = units[part].Where(unit => !MediaPaths(unit).Any(missing.Contains)).ToList();
            }
        }
        using (profile.Phase("media"))
        {
            var availableMedia = await AvailableMediaAsync(units
                .Where(pair => selectedPart is null || pair.Key == selectedPart)
                .SelectMany(pair => pair.Value), ct);
            foreach (var part in ExamCore.PartCounts.Keys)
                units[part] = units[part].Where(u => MediaPaths(u).All(availableMedia.Contains)).ToList();
        }

        if (links is not null)
        {
            return FixedExamPlan.Select(links, units.Values.SelectMany(x => x));
        }

        if (candidateOnly) return units.OrderBy(x => x.Key).SelectMany(x => x.Value).ToArray();

        using (profile.Phase("planner")) return RandomExamPlanner.Select(units, Random.Shared, selectedPart);
    }

    private static IEnumerable<string> MediaPaths(PlannedUnit unit) =>
        new[] { unit.Resource?.DuongDanAudio, unit.Resource?.DuongDanAnh }
            .Concat(unit.Documents.Select(d => d.DuongDanAnh))
            .Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!);

    private async Task<HashSet<string>> AvailableMediaAsync(IEnumerable<PlannedUnit> units, CancellationToken ct,
        ConcurrentDictionary<string, bool>? known = null, int maxConcurrent = 8, bool useSharedCache = false)
    {
        var paths = units.SelectMany(MediaPaths).Distinct(StringComparer.Ordinal).ToArray();
        known ??= new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        if (paths.Length == 0) return [];
        var containerName = configuration["ExamMedia:ContainerName"] ?? configuration["AzureBlob:ContainerName"];
        if (string.IsNullOrWhiteSpace(containerName))
            throw new ExamProblem("MEDIA_NOT_AVAILABLE", "Media bài thi chưa được cấu hình.", 503);
        var cachePrefix = $"random-full-media:{containerName.Trim().ToLowerInvariant()}:";
        var container = blobs.GetBlobContainerClient(containerName);
        using var gate = new SemaphoreSlim(maxConcurrent);
        try
        {
            await Task.WhenAll(paths.Where(path => !known.ContainsKey(path)).Select(async path =>
            {
                // Preserve Blob's case-sensitive name while keeping a canonical cache key.
                var cacheKey = cachePrefix + Uri.EscapeDataString(path);
                if (useSharedCache && mediaCache.TryGetValue<bool>(cacheKey, out var cached))
                {
                    known[path] = cached;
                    profile.CountBlobCacheHit();
                    return;
                }
                await gate.WaitAsync(ct);
                try
                {
                    profile.CountBlobCheck();
                    var exists = (await container.GetBlobClient(path).ExistsAsync(ct)).Value;
                    known[path] = exists;
                    if (useSharedCache)
                        mediaCache.Set(cacheKey, exists, exists ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(20));
                }
                finally { gate.Release(); }
            }));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new ExamProblem("MEDIA_NOT_AVAILABLE", "Không thể kiểm tra media của đề thi.", 503); }
        return paths.Where(path => known.TryGetValue(path, out var exists) && exists)
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static bool ValidPlan(IEnumerable<PlannedUnit> units) =>
        units.SelectMany(x => x.Questions.Select(_ => x.Part)).GroupBy(x => x).All(g =>
            ExamCore.PartCounts.TryGetValue(g.Key, out var count) && g.Count() == count) &&
        units.Sum(x => x.Questions.Count) == 200 &&
        units.Select(x => x.Part).SequenceEqual(units.Select(x => x.Part).OrderBy(x => x));

    public static bool ValidGeneratedPlan(IEnumerable<PlannedUnit> source, int? selectedPart = null)
    {
        var units = source.ToList();
        var questions = units.SelectMany(unit => unit.Questions).ToList();
        return ExamCore.PartCounts.All(pair =>
            (selectedPart is null || selectedPart == pair.Key)
                ? units.Where(unit => unit.Part == pair.Key).Sum(unit => unit.Questions.Count) is var count &&
                  (selectedPart is null ? count == pair.Value : count > 0 && count <= pair.Value)
                : units.All(unit => unit.Part != pair.Key)) &&
            units.All(unit => RandomExamPlanner.ValidUnitSize(unit.Part, unit.Questions.Count)) &&
            questions.Select(q => q.MaCauHoi).Distinct().Count() == questions.Count &&
            units.Where(unit => unit.Resource is not null).Select(unit => unit.Resource!.MaNguLieu).Distinct().Count() ==
            units.Count(unit => unit.Resource is not null) &&
            units.Select(unit => unit.Part).SequenceEqual(units.Select(unit => unit.Part).OrderBy(part => part));
    }

}
