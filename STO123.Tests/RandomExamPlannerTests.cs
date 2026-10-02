using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public class RandomExamPlannerTests
{
    private static Dictionary<int, List<PlannedUnit>> FullUnits()
    {
        var result = new Dictionary<int, List<PlannedUnit>>();
        var id = 0;
        foreach (var (part, count) in ExamCore.PartCounts)
        {
            var size = part switch { 3 or 4 or 7 => 3, 6 => 4, _ => 1 };
            result[part] = [];
            for (var offset = 0; offset < count; offset += size)
                result[part].Add(new(part, null, Enumerable.Range(0, size)
                    .Select(n => new CauHoi { MaCauHoi = ++id, DoKho = (byte)(n + 1) }).ToList(), []));
        }
        return result;
    }

    [Fact]
    public void SparseBankUsesEveryPartWithoutRepeatingUnits()
    {
        var sparse = FullUnits().ToDictionary(pair => pair.Key,
            pair => new List<PlannedUnit> { pair.Value[0] });
        var plan = RandomExamPlanner.Select(sparse, new Random(42));
        Assert.Equal(7, plan.Count);
        Assert.Equal(16, plan.Sum(unit => unit.Questions.Count));
        Assert.Equal(7, plan.Select(unit => unit.Part).Distinct().Count());
        Assert.Equal(plan.Sum(unit => unit.Questions.Count),
            plan.SelectMany(unit => unit.Questions).Select(q => q.MaCauHoi).Distinct().Count());
        sparse[7].Clear();
        var error = Assert.Throws<ExamProblem>(() => RandomExamPlanner.Select(sparse, new Random(42)));
        Assert.Equal(ExamCore.Insufficient, error.Code);
    }

    [Fact]
    public void FullBankCanBeSelectedWithoutExtraSourcesAndPicksRandomSubsetWhenAvailable()
    {
        var units = FullUnits();
        Assert.Equal(200, RandomExamPlanner.Select(units, new Random(42)).Sum(unit => unit.Questions.Count));
        units[1].Add(new(1, null, [new CauHoi { MaCauHoi = 9999 }], []));
        var selections = Enumerable.Range(0, 20).Select(seed =>
            RandomExamPlanner.Select(units, new Random(seed)).SelectMany(u => u.Questions)
                .Select(q => q.MaCauHoi).ToHashSet()).ToList();
        Assert.All(selections, selection => Assert.Equal(200, selection.Count));
        Assert.Contains(selections, selection => selection.Contains(9999));
        Assert.Contains(selections, selection => !selection.Contains(9999));
    }

    [Fact]
    public void MixedPartSevenGroupsFindLargestCompleteSubset()
    {
        var units = FullUnits();
        units[7] = Enumerable.Range(0, 11).Select(i => new PlannedUnit(7, null,
            Enumerable.Range(1, 5).Select(n => new CauHoi { MaCauHoi = 1000 + 5 * i + n }).ToList(), []))
            .ToList();
        var plan = RandomExamPlanner.Select(units, new Random(7));
        Assert.Equal(50, plan.Where(unit => unit.Part == 7).Sum(unit => unit.Questions.Count));
        Assert.All(plan.Where(unit => unit.Part == 7), unit => Assert.Equal(5, unit.Questions.Count));
    }

    [Fact]
    public void DifficultyCountsEveryQuestionInMixedGroup()
    {
        var mixed = FullUnits()[3][0];
        Assert.Equal(new Dictionary<byte, int> { [1] = 1, [2] = 1, [3] = 1 },
            RandomExamPlanner.DifficultyCounts([mixed]));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void PartMockUsesOnlyRequestedPartAndCompleteUnits(int part)
    {
        var units = FullUnits();
        var plan = RandomExamPlanner.Select(units, new Random(12), part);
        Assert.All(plan, unit => Assert.Equal(part, unit.Part));
        Assert.True(ExamGenerationService.ValidGeneratedPlan(plan, part));
        Assert.Equal(plan.Sum(unit => unit.Questions.Count),
            plan.SelectMany(unit => unit.Questions).Select(q => q.MaCauHoi).Distinct().Count());
        units[part].Clear();
        var error = Assert.Throws<ExamProblem>(() => RandomExamPlanner.Select(units, new Random(12), part));
        Assert.Contains($"Part {part}", error.Message);
    }

    [Fact]
    public void GroupedPartNeverRepeatsSourceGroup()
    {
        var first = new PlannedUnit(3, new NguLieu { MaNguLieu = 9 },
            Enumerable.Range(1, 3).Select(id => new CauHoi { MaCauHoi = id }).ToList(), []);
        var repeated = new PlannedUnit(3, new NguLieu { MaNguLieu = 9 },
            Enumerable.Range(4, 3).Select(id => new CauHoi { MaCauHoi = id }).ToList(), []);
        var plan = RandomExamPlanner.Select(new Dictionary<int, List<PlannedUnit>> {
            [3] = [first, repeated]
        }, new Random(4), 3);
        Assert.Single(plan);
        Assert.Equal(3, plan[0].Questions.Count);
    }
}
