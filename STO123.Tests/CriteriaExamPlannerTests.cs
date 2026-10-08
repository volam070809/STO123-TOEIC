using STO123.Models;
using STO123.Services.Exam;
using STO123.DTOs.Exam;

namespace STO123.Tests;

public class CriteriaExamPlannerTests
{
    [Theory]
    [InlineData(1, "MOCK-000001")]
    [InlineData(25, "MOCK-000025")]
    [InlineData(123, "MOCK-000123")]
    public void ExamCodeIsDerivedFromPermanentId(int id, string expected) =>
        Assert.Equal(expected, MockExamCode.FromId(id));

    [Fact]
    public void LegacyAttemptHasNoDerivedCode()
    {
        Assert.Null(typeof(DeThi).GetProperty("ExamCode"));
        Assert.Null(MockExamCode.FromId((int?)null));
        var result = new ExamResultDto(1, ExamCore.Submitted, DateTime.UtcNow, DateTime.UtcNow,
            0, 0, 0, 0, true, new RawStatsDto(0, 0, 0, 0, 0),
            new RawStatsDto(0, 0, 0, 0, 0), new RawStatsDto(0, 0, 0, 0, 0), [])
            { Source = ExamCore.Mock, ExamId = null };
        Assert.Null(result.ExamCode);
        Assert.Equal("MOCK-000025", (result with { ExamId = 25 }).ExamCode);
        Assert.Null((result with { Source = ExamCore.Placement, ExamId = 25 }).ExamCode);
    }

    [Fact]
    public void MockResultContractContainsStatisticsWithoutAnswerMaterial()
    {
        var properties = typeof(ExamResultDto).GetProperties().Select(x => x.Name).ToHashSet();
        Assert.Contains("Parts", properties);
        Assert.Contains("TotalScore", properties);
        Assert.Contains("ExamCode", properties);
        Assert.DoesNotContain("CorrectOption", properties);
        Assert.DoesNotContain("Explanation", properties);
        Assert.DoesNotContain("Transcript", properties);
        Assert.DoesNotContain("Questions", properties);
    }

    [Fact]
    public void CriteriaSelectsExactly200WithoutSplittingGroups()
    {
        var units = new List<PlannedUnit>();
        var criteria = new Dictionary<int, (int Easy, int Medium, int Hard)>();
        var id = 0;
        foreach (var (part, count) in ExamCore.PartCounts)
        {
            var size = part switch { 3 or 4 or 7 => 3, 6 => 4, _ => 1 };
            criteria[part] = (count, 0, 0);
            for (var i = 0; i < count / size; i++)
                units.Add(new PlannedUnit(part, part == 5 ? null : new NguLieu { MaNguLieu = ++id },
                    Enumerable.Range(0, size).Select(_ => new CauHoi { MaCauHoi = ++id, DoKho = 1 }).ToArray(), []));
        }
        var selected = CriteriaExamPlanner.Select(units, criteria, new Random(42));
        Assert.Equal(200, selected.Sum(x => x.Questions.Count));
        Assert.All(ExamCore.PartCounts, pair => Assert.Equal(pair.Value,
            selected.Where(x => x.Part == pair.Key).Sum(x => x.Questions.Count)));
        Assert.Equal(units.Count, selected.Count);
    }

    [Fact]
    public void ImpossibleDifficultyDistributionDoesNotYieldPartialExam()
    {
        var candidates = ExamCore.PartCounts.SelectMany(pair =>
            Enumerable.Range(0, pair.Value / (pair.Key switch { 3 or 4 or 7 => 3, 6 => 4, _ => 1 }))
                .Select(i => new PlannedUnit(pair.Key, null,
                    Enumerable.Range(0, pair.Key switch { 3 or 4 or 7 => 3, 6 => 4, _ => 1 })
                        .Select(n => new CauHoi { MaCauHoi = pair.Key * 1000 + i * 10 + n, DoKho = 1 }).ToArray(), [])))
            .ToList();
        var criteria = ExamCore.PartCounts.ToDictionary(x => x.Key, x => (x.Value, 0, 0));
        criteria[7] = (53, 1, 0);
        var error = Assert.Throws<ExamProblem>(() => CriteriaExamPlanner.Select(candidates, criteria, new Random(1)));
        Assert.Equal(ExamCore.Insufficient, error.Code);
    }
}
