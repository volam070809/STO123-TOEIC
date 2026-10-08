using STO123.Models;

namespace STO123.Services.Exam;

public static class CriteriaExamPlanner
{
    private sealed record Node(Node? Previous, PlannedUnit? Unit);

    public static IReadOnlyList<PlannedUnit> Select(IReadOnlyList<PlannedUnit> candidates,
        IReadOnlyDictionary<int, (int Easy, int Medium, int Hard)> criteria, Random random)
    {
        var result = new List<PlannedUnit>();
        foreach (var (part, count) in ExamCore.PartCounts)
        {
            if (!criteria.TryGetValue(part, out var target) ||
                target.Easy < 0 || target.Medium < 0 || target.Hard < 0 ||
                target.Easy + target.Medium + target.Hard != count)
                throw new ExamProblem("INVALID_GENERATION_CRITERIA", $"Cấu hình Part {part} không đủ {count} câu.", 409);
            var pool = candidates.Where(x => x.Part == part && RandomExamPlanner.ValidUnitSize(part, x.Questions.Count))
                .ToArray();
            random.Shuffle(pool);
            var states = new Dictionary<(int Easy, int Medium, int Hard), Node> { [(0, 0, 0)] = new(null, null) };
            foreach (var unit in pool)
            {
                var easy = unit.Questions.Count(x => x.DoKho == 1);
                var medium = unit.Questions.Count(x => x.DoKho == 2);
                var hard = unit.Questions.Count(x => x.DoKho == 3);
                if (easy + medium + hard != unit.Questions.Count) continue;
                foreach (var (state, previous) in states.ToArray())
                {
                    var next = (state.Easy + easy, state.Medium + medium, state.Hard + hard);
                    if (next.Item1 > target.Easy || next.Item2 > target.Medium || next.Item3 > target.Hard)
                        continue;
                    states.TryAdd(next, new Node(previous, unit));
                }
                if (states.ContainsKey(target)) break;
            }
            if (!states.TryGetValue(target, out var final))
                throw new ExamProblem(ExamCore.Insufficient, $"Không đủ nhóm câu hỏi đúng cấu hình Part {part}.", 409);
            var selected = new List<PlannedUnit>();
            for (var node = final; node.Unit is not null; node = node.Previous!) selected.Add(node.Unit);
            selected.Reverse();
            result.AddRange(selected);
        }
        if (!ExamGenerationService.ValidGeneratedPlan(result) ||
            result.Sum(x => x.Questions.Count) != 200)
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Đề sinh không đủ 200 câu hợp lệ.", 409);
        return result;
    }
}
