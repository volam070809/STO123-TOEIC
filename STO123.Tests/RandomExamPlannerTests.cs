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
    public void SparseBankFailsBeforeProducingAnIncompleteFullMock()
    {
        var sparse = FullUnits().ToDictionary(pair => pair.Key,
            pair => new List<PlannedUnit> { pair.Value[0] });
        var incomplete = Assert.Throws<ExamProblem>(() => RandomExamPlanner.Select(sparse, new Random(42)));
        Assert.Equal(ExamCore.Insufficient, incomplete.Code);
        Assert.Contains("Part 1", incomplete.Message);
        sparse[7].Clear();
        var error = Assert.Throws<ExamProblem>(() => RandomExamPlanner.Select(sparse, new Random(42)));
        Assert.Equal(ExamCore.Insufficient, error.Code);
    }

    [Fact]
    public void FullBankCanBeSelectedWithoutExtraSourcesAndPicksRandomSubsetWhenAvailable()
    {
        var units = FullUnits();
        var full = RandomExamPlanner.Select(units, new Random(42));
        Assert.Equal(200, full.Sum(unit => unit.Questions.Count));
        Assert.All(ExamCore.PartCounts, pair => Assert.Equal(pair.Value,
            full.Where(unit => unit.Part == pair.Key).Sum(unit => unit.Questions.Count)));
        Assert.Equal(200, full.SelectMany(unit => unit.Questions).Select(q => q.MaCauHoi).Distinct().Count());
        units[1].Add(new(1, null, [new CauHoi { MaCauHoi = 9999 }], []));
        var selections = Enumerable.Range(0, 20).Select(seed =>
            RandomExamPlanner.Select(units, new Random(seed)).SelectMany(u => u.Questions)
                .Select(q => q.MaCauHoi).ToHashSet()).ToList();
        Assert.All(selections, selection => Assert.Equal(200, selection.Count));
        Assert.Contains(selections, selection => selection.Contains(9999));
        Assert.Contains(selections, selection => !selection.Contains(9999));
    }

    [Fact]
    public void PartSevenWithoutExactWholeGroupCombinationFails()
    {
        var units = FullUnits();
        units[7] = Enumerable.Range(0, 11).Select(i => new PlannedUnit(7, null,
            Enumerable.Range(1, 5).Select(n => new CauHoi { MaCauHoi = 1000 + 5 * i + n }).ToList(), []))
            .ToList();
        var error = Assert.Throws<ExamProblem>(() => RandomExamPlanner.Select(units, new Random(7)));
        Assert.Equal(ExamCore.Insufficient, error.Code);
        Assert.Contains("Part 7", error.Message);
    }

    [Fact]
    public void PartSevenFindsExactCombinationOfWholeMixedSizeGroups()
    {
        var units = FullUnits();
        var nextId = 1000;
        units[7] = Enumerable.Range(0, 10).Select(_ => new PlannedUnit(7, null,
            Enumerable.Range(0, 5).Select(_ => new CauHoi { MaCauHoi = ++nextId }).ToList(), []))
            .Concat(Enumerable.Range(0, 2).Select(_ => new PlannedUnit(7, null,
                Enumerable.Range(0, 2).Select(_ => new CauHoi { MaCauHoi = ++nextId }).ToList(), [])))
            .ToList();
        var plan = RandomExamPlanner.Select(units, new Random(7));
        Assert.Equal(200, plan.Sum(unit => unit.Questions.Count));
        Assert.Equal(54, plan.Where(unit => unit.Part == 7).Sum(unit => unit.Questions.Count));
        Assert.Equal(12, plan.Count(unit => unit.Part == 7));
    }

    [Fact]
    public void InvalidPartSevenGroupCannotBeSplitToCompleteFullMock()
    {
        var units = FullUnits();
        var nextId = 1000;
        units[7] = Enumerable.Range(0, 10).Select(_ => new PlannedUnit(7, null,
            Enumerable.Range(0, 5).Select(_ => new CauHoi { MaCauHoi = ++nextId }).ToList(), []))
            .Append(new PlannedUnit(7, null,
                Enumerable.Range(0, 6).Select(_ => new CauHoi { MaCauHoi = ++nextId }).ToList(), []))
            .ToList();

        var error = Assert.Throws<ExamProblem>(() => RandomExamPlanner.Select(units, new Random(7)));
        Assert.Equal(ExamCore.Insufficient, error.Code);
        Assert.Contains("Part 7", error.Message);
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
