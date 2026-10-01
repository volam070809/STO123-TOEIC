using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Exam;

public sealed record PlannedUnit(int Part, NguLieu? Resource, IReadOnlyList<CauHoi> Questions,
    IReadOnlyList<NguLieuTaiLieu> Documents);

public sealed class ExamGenerationService(ToeicDbContext db, IConfiguration configuration, BlobServiceClient blobs)
{
    private static ExamProblem Invalid() => new("INVALID_EXAM_STRUCTURE", "Cấu trúc đề thi không hợp lệ.", 409);
    private static ExamProblem Empty() => new(ExamCore.Insufficient, "Không đủ dữ liệu để tạo đề thi.", 409);

    public async Task<IReadOnlyList<PlannedUnit>> BuildAsync(int? fixedExamId, CancellationToken ct)
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
                foreach (var q in candidates.Where(q => !memberByQuestion.ContainsKey(q.MaCauHoi) && ValidOptions(q, part)))
                    units[part].Add(new(part, null, [q], []));
                continue;
            }
            if (part is 1 or 2)
            {
                foreach (var q in candidates)
                {
                    if (!ValidOptions(q, part) || !memberByQuestion.TryGetValue(q.MaCauHoi, out var member) ||
                        !resources.TryGetValue(member.MaNguLieu, out var resource) ||
                        !ValidMedia(resource, part) ||
                        !allByResource.TryGetValue(member.MaNguLieu, out var group) || group.Count != 1) continue;
                    units[part].Add(new(part, resource, [q], []));
                }
                continue;
            }
            foreach (var group in allByResource.Values)
            {
                if (!resources.TryGetValue(group[0].MaNguLieu, out var resource) || !ValidMedia(resource, part)) continue;
                // A group containing unpublished, missing, or mixed-Part questions is not a valid unit.
                var ids = group.Select(m => m.MaCauHoi).ToHashSet();
                var groupQuestions = candidates.Where(q => ids.Contains(q.MaCauHoi)).ToDictionary(q => q.MaCauHoi);
                if (groupQuestions.Count != group.Count || group.Any(m => !groupQuestions.ContainsKey(m.MaCauHoi))) continue;
                var ordered = group.OrderBy(m => m.ThuTu).Select(m => groupQuestions[m.MaCauHoi]).ToList();
                if ((part is 3 or 4 && ordered.Count != 3) || (part == 6 && ordered.Count != 4)) continue;
                if (ordered.Any(q => !ValidOptions(q, part))) continue;
                var docs = documents.Where(d => d.MaNguLieu == resource.MaNguLieu).ToList();
                if (part == 7 && (docs.Count is < 1 or > 3 || docs.Any(d =>
                    string.IsNullOrWhiteSpace(d.NoiDung) && string.IsNullOrWhiteSpace(d.DuongDanAnh)))) continue;
                units[part].Add(new(part, resource, ordered, docs));
            }
        }
        var availableMedia = await AvailableMediaAsync(units.Values.SelectMany(x => x), ct);
        foreach (var part in ExamCore.PartCounts.Keys)
            units[part] = units[part].Where(u => MediaPaths(u).All(availableMedia.Contains)).ToList();

        if (fixedExamId.HasValue)
        {
            var exam = await db.DeThi.AsNoTracking().FirstOrDefaultAsync(x => x.MaDeThi == fixedExamId, ct);
            if (exam is null || exam.LoaiDe != "DE_THI" || exam.TrangThai != "OPEN" || exam.ThoiGianLamBai != ExamCore.DurationMinutes) throw Invalid();
            var links = await db.CauHoiDeThi.AsNoTracking().Where(x => x.MaDeThi == fixedExamId)
                .OrderBy(x => x.ThuTu).ToListAsync(ct);
            if (links.Count != 200 || links.Select(x => x.ThuTu).Where((value, i) => value != i + 1).Any()) throw Invalid();
            var lookup = units.Values.SelectMany(x => x).SelectMany(u => u.Questions.Select(q => (q.MaCauHoi, u)))
                .ToDictionary(x => x.MaCauHoi, x => x.u);
            var result = new List<PlannedUnit>();
            var index = 0;
            while (index < links.Count)
            {
                if (!lookup.TryGetValue(links[index].MaCauHoi, out var unit)) throw Invalid();
                if (index + unit.Questions.Count > links.Count ||
                    !unit.Questions.Select(q => q.MaCauHoi).SequenceEqual(
                        links.Skip(index).Take(unit.Questions.Count).Select(x => x.MaCauHoi))) throw Invalid();
                result.Add(unit);
                index += unit.Questions.Count;
            }
            if (!ValidPlan(result)) throw Invalid();
            return result;
        }

        var repeat = configuration.GetValue<bool>("ExamGeneration:AllowRepeatedQuestions");
        var plan = new List<PlannedUnit>();
        foreach (var (part, target) in ExamCore.PartCounts)
        {
            var pool = units[part].OrderBy(_ => Random.Shared.Next()).ToList();
            if (pool.Count == 0) throw Empty();
            var selected = ExactUnits(pool, target, repeat);
            if (selected is null) throw Empty();
            plan.AddRange(selected);
        }
        if (!ValidPlan(plan)) throw Empty();
        return plan;
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

    private static List<PlannedUnit>? ExactUnits(List<PlannedUnit> pool, int target, bool repeat)
    {
        // Finite unbounded-knapsack DP. Prefer unused units by processing each source once first.
        var best = new List<PlannedUnit>?[target + 1];
        best[0] = [];
        foreach (var unit in pool)
            for (var n = target; n >= unit.Questions.Count; n--)
                if (best[n] is null && best[n - unit.Questions.Count] is { } prior)
                    best[n] = [.. prior, unit];
        if (best[target] is not null || !repeat) return best[target];
        for (var n = 1; n <= target; n++)
            foreach (var unit in pool)
                if (n >= unit.Questions.Count && best[n] is null && best[n - unit.Questions.Count] is { } prior)
                    best[n] = [.. prior, unit];
        return best[target];
    }

    private static bool ValidPlan(IEnumerable<PlannedUnit> units) =>
        units.SelectMany(x => x.Questions.Select(_ => x.Part)).GroupBy(x => x).All(g =>
            ExamCore.PartCounts.TryGetValue(g.Key, out var count) && g.Count() == count) &&
        units.Sum(x => x.Questions.Count) == 200 &&
        units.Select(x => x.Part).SequenceEqual(units.Select(x => x.Part).OrderBy(x => x));

    private static bool ValidMedia(NguLieu resource, int part) => part switch
    {
        1 => !string.IsNullOrWhiteSpace(resource.DuongDanAudio) && !string.IsNullOrWhiteSpace(resource.DuongDanAnh),
        2 or 3 or 4 => !string.IsNullOrWhiteSpace(resource.DuongDanAudio),
        6 => !string.IsNullOrWhiteSpace(resource.NoiDungNguLieu),
        _ => true
    };

    private static bool ValidOptions(CauHoi q, int part)
    {
        if (string.IsNullOrWhiteSpace(q.PhuongAnA) || string.IsNullOrWhiteSpace(q.PhuongAnB) ||
            string.IsNullOrWhiteSpace(q.PhuongAnC)) return false;
        if (part != 2 && string.IsNullOrWhiteSpace(q.PhuongAnD)) return false;
        var answer = q.PhuongAnDung?.Trim();
        return answer is "A" or "B" or "C" || (part != 2 && answer == "D" && !string.IsNullOrWhiteSpace(q.PhuongAnD));
    }
}
