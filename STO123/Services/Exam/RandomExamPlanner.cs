namespace STO123.Services.Exam;

public static class RandomExamPlanner
{
    private static ExamProblem Insufficient(int part) => new(ExamCore.Insufficient,
        $"Chưa có tổ hợp dữ liệu hợp lệ đạt đúng số câu cho Part {part}.", 409);

    public static IReadOnlyDictionary<byte, int> DifficultyCounts(IEnumerable<PlannedUnit> units) =>
        units.SelectMany(unit => unit.Questions).GroupBy(question => question.DoKho)
            .ToDictionary(group => group.Key, group => group.Count());

    public static IReadOnlyList<PlannedUnit> Select(
        IReadOnlyDictionary<int, List<PlannedUnit>> units,
        Random random, int? selectedPart = null)
    {
        if (selectedPart is not null && !ExamCore.PartCounts.ContainsKey(selectedPart.Value))
            throw new ExamProblem("INVALID_PART", "Part phải từ 1 đến 7.");
        var plan = new List<PlannedUnit>();
        var usedQuestions = new HashSet<int>();
        var usedGroups = new HashSet<int>();
        foreach (var (part, target) in ExamCore.PartCounts)
        {
            if (selectedPart is not null && part != selectedPart) continue;
            if (!units.TryGetValue(part, out var candidates) || candidates.Count == 0) throw Insufficient(part);
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
            var selected = selectedPart is null ? ExactUnits(unique, target) : BestUnits(unique, target);
            if (selected is null || selected.Count == 0 ||
                (selectedPart is null && selected.Sum(unit => unit.Questions.Count) != target))
                throw Insufficient(part);
            plan.AddRange(selected);
            foreach (var unit in selected)
            {
                foreach (var question in unit.Questions) usedQuestions.Add(question.MaCauHoi);
                if (unit.Resource is not null) usedGroups.Add(unit.Resource.MaNguLieu);
            }
        }
        if (!ExamGenerationService.ValidGeneratedPlan(plan, selectedPart))
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Không thể tạo đề thi từ dữ liệu hợp lệ.", 409);
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

    // Keep one predecessor per reachable count instead of copying partial plans for every unit.
    private static List<PlannedUnit>? ExactUnits(IReadOnlyList<PlannedUnit> pool, int target)
    {
        var reachable = new bool[target + 1];
        var previous = new int[target + 1];
        var chosen = new int[target + 1];
        reachable[0] = true;
        for (var index = 0; index < pool.Count && !reachable[target]; index++)
        {
            var size = pool[index].Questions.Count;
            for (var count = target; count >= size; count--)
            {
                if (reachable[count] || !reachable[count - size]) continue;
                reachable[count] = true;
                previous[count] = count - size;
                chosen[count] = index;
            }
        }
        if (!reachable[target]) return null;
        var selected = new List<PlannedUnit>();
        for (var count = target; count > 0; count = previous[count])
            selected.Add(pool[chosen[count]]);
        selected.Reverse();
        return selected;
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
