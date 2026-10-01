namespace STO123.Services.Exam;

public static class RandomExamPlanner
{
    private static ExamProblem Insufficient() => new(ExamCore.Insufficient,
        "Không đủ dữ liệu để tạo đề thi ngẫu nhiên hợp lệ.", 409);

    public static IReadOnlyDictionary<byte, int> DifficultyCounts(IEnumerable<PlannedUnit> units) =>
        units.SelectMany(unit => unit.Questions).GroupBy(question => question.DoKho)
            .ToDictionary(group => group.Key, group => group.Count());

    public static IReadOnlyList<PlannedUnit> Select(
        IReadOnlyDictionary<int, List<PlannedUnit>> units,
        Random random)
    {
        var plan = new List<PlannedUnit>();
        var usedQuestions = new HashSet<int>();
        var usedGroups = new HashSet<int>();
        foreach (var (part, target) in ExamCore.PartCounts)
        {
            if (!units.TryGetValue(part, out var candidates) || candidates.Count == 0) throw Insufficient();
            var pool = candidates.ToArray();
            random.Shuffle(pool);
            var unique = new List<PlannedUnit>();
            var seenQuestions = new HashSet<int>();
            var seenGroups = new HashSet<int>();
            foreach (var unit in pool)
            {
                if (unit.Part != part || !ValidUnitSize(part, unit.Questions.Count) || unit.Questions.Count > target ||
                    unit.Questions.Select(q => q.MaCauHoi).Distinct().Count() != unit.Questions.Count ||
                    unit.Questions.Any(q => usedQuestions.Contains(q.MaCauHoi) || seenQuestions.Contains(q.MaCauHoi)) ||
                    (unit.Resource is not null && (usedGroups.Contains(unit.Resource.MaNguLieu) ||
                        seenGroups.Contains(unit.Resource.MaNguLieu)))) continue;
                unique.Add(unit);
                foreach (var question in unit.Questions) seenQuestions.Add(question.MaCauHoi);
                if (unit.Resource is not null) seenGroups.Add(unit.Resource.MaNguLieu);
            }
            var selected = BestUnits(unique, target);
            if (selected is null || selected.Count == 0) throw Insufficient();
            plan.AddRange(selected);
            foreach (var unit in selected)
            {
                foreach (var question in unit.Questions) usedQuestions.Add(question.MaCauHoi);
                if (unit.Resource is not null) usedGroups.Add(unit.Resource.MaNguLieu);
            }
        }
        if (!ExamGenerationService.ValidRandomPlan(plan)) throw Insufficient();
        return plan;
    }

    private static List<PlannedUnit>? BestUnits(IReadOnlyList<PlannedUnit> pool, int target)
    {
        var best = new List<PlannedUnit>?[target + 1];
        best[0] = [];
        foreach (var unit in pool)
            for (var n = target; n >= unit.Questions.Count; n--)
                if (best[n] is null && n >= unit.Questions.Count && best[n - unit.Questions.Count] is { } prior)
                    best[n] = [.. prior, unit];
        return best.LastOrDefault(selection => selection is not null);
    }

    internal static bool ValidUnitSize(int part, int count) => part switch
    {
        1 or 2 or 5 => count == 1,
        3 or 4 => count == 3,
        6 => count == 4,
        7 => count is >= 2 and <= 5,
        _ => false
    };
}
