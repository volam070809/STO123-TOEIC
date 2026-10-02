using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Exam;

public sealed record PlannedUnit(int Part, NguLieu? Resource, IReadOnlyList<CauHoi> Questions,
    IReadOnlyList<NguLieuTaiLieu> Documents);

public sealed class ExamGenerationService(ToeicDbContext db, IConfiguration configuration, BlobServiceClient blobs)
{
    private static ExamProblem Invalid() => new("INVALID_EXAM_STRUCTURE", "Cấu trúc đề thi không hợp lệ.", 409);

    public async Task<IReadOnlyList<PlannedUnit>> BuildAsync(int? fixedExamId, CancellationToken ct,
        int? selectedPart = null)
    {
        var parts = await db.PartTOEIC.AsNoTracking().ToDictionaryAsync(p => p.MaPart, p => p.SoPart, ct);
        if (parts.Values.Distinct().Count() != 7 || !Enumerable.Range(1, 7).All(parts.Values.Contains)) throw Invalid();
        var questions = await db.CauHoi.AsNoTracking().Where(q => q.TrangThai == ExamCore.Published).ToListAsync(ct);
        var memberships = await db.NhomCauHoi.AsNoTracking().ToListAsync(ct);
        var resources = await db.NguLieu.AsNoTracking().ToDictionaryAsync(r => r.MaNguLieu, ct);
        var documents = await db.NguLieuTaiLieu.AsNoTracking().OrderBy(d => d.ThuTu).ToListAsync(ct);
        var byPart = questions.GroupBy(q => parts.GetValueOrDefault(q.MaPart, 0)).ToDictionary(g => g.Key, g => g.ToList());
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
                    var docs = documents.Where(d => d.MaNguLieu == resource.MaNguLieu).ToList();
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
                var ids = group.Select(m => m.MaCauHoi).ToHashSet();
                var groupQuestions = candidates.Where(q => ids.Contains(q.MaCauHoi)).ToDictionary(q => q.MaCauHoi);
                if (groupQuestions.Count != group.Count || group.Any(m => !groupQuestions.ContainsKey(m.MaCauHoi))) continue;
                var ordered = group.OrderBy(m => m.ThuTu).Select(m => groupQuestions[m.MaCauHoi]).ToList();
                if (ordered.Any(q => !ExamSourceValidator.ValidQuestion(q, part))) continue;
                var docs = documents.Where(d => d.MaNguLieu == resource.MaNguLieu).ToList();
                if (!ExamSourceValidator.ValidDocuments(part, docs)) continue;
                units[part].Add(new(part, resource, ordered, docs));
            }
        }
        var availableMedia = await AvailableMediaAsync(units
            .Where(pair => selectedPart is null || pair.Key == selectedPart)
            .SelectMany(pair => pair.Value), ct);
        foreach (var part in ExamCore.PartCounts.Keys)
            units[part] = units[part].Where(u => MediaPaths(u).All(availableMedia.Contains)).ToList();

        if (fixedExamId.HasValue)
        {
            var exam = await db.DeThi.AsNoTracking().FirstOrDefaultAsync(x => x.MaDeThi == fixedExamId, ct);
            if (exam is null || exam.LoaiDe != "DE_THI" || exam.TrangThai != "OPEN" || exam.ThoiGianLamBai != ExamCore.DurationMinutes) throw Invalid();
            var links = await db.CauHoiDeThi.AsNoTracking().Where(x => x.MaDeThi == fixedExamId)
                .OrderBy(x => x.ThuTu).ToListAsync(ct);
            return FixedExamPlan.Select(links, units.Values.SelectMany(x => x));
        }

        return RandomExamPlanner.Select(units, Random.Shared, selectedPart);
    }

    private static IEnumerable<string> MediaPaths(PlannedUnit unit) =>
        new[] { unit.Resource?.DuongDanAudio, unit.Resource?.DuongDanAnh }
            .Concat(unit.Documents.Select(d => d.DuongDanAnh))
            .Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!);

    private async Task<HashSet<string>> AvailableMediaAsync(IEnumerable<PlannedUnit> units, CancellationToken ct)
    {
        var paths = units.SelectMany(MediaPaths).Distinct(StringComparer.Ordinal).ToArray();
        var available = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        if (paths.Length == 0) return [];
        var containerName = configuration["ExamMedia:ContainerName"] ?? configuration["AzureBlob:ContainerName"];
        if (string.IsNullOrWhiteSpace(containerName))
            throw new ExamProblem("MEDIA_NOT_AVAILABLE", "Media bài thi chưa được cấu hình.", 503);
        var container = blobs.GetBlobContainerClient(containerName);
        using var gate = new SemaphoreSlim(8);
        try
        {
            await Task.WhenAll(paths.Select(async path =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    if ((await container.GetBlobClient(path).ExistsAsync(ct)).Value)
                        available.TryAdd(path, 0);
                }
                finally { gate.Release(); }
            }));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new ExamProblem("MEDIA_NOT_AVAILABLE", "Không thể kiểm tra media của đề thi.", 503); }
        return available.Keys.ToHashSet(StringComparer.Ordinal);
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
                ? units.Where(unit => unit.Part == pair.Key).Sum(unit => unit.Questions.Count) is var count && count > 0 && count <= pair.Value
                : units.All(unit => unit.Part != pair.Key)) &&
            units.All(unit => RandomExamPlanner.ValidUnitSize(unit.Part, unit.Questions.Count)) &&
            questions.Select(q => q.MaCauHoi).Distinct().Count() == questions.Count &&
            units.Where(unit => unit.Resource is not null).Select(unit => unit.Resource!.MaNguLieu).Distinct().Count() ==
            units.Count(unit => unit.Resource is not null) &&
            units.Select(unit => unit.Part).SequenceEqual(units.Select(unit => unit.Part).OrderBy(part => part));
    }

}
