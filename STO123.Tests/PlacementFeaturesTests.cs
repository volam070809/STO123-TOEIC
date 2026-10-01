using STO123.DTOs.Exam;
using STO123.Services.Exam;

namespace STO123.Tests;

public class PlacementFeaturesTests
{
    private static ExamResultDto Result(string status = ExamCore.Submitted) => new(
        1, status, DateTime.UtcNow, DateTime.UtcNow, 100, 5, 495, 500, true,
        new(200, 100, 100, 0, 50), new(100, 50, 50, 0, 50),
        new(100, 50, 50, 0, 50),
        ExamCore.PartCounts.Select(pair => new PartStatsDto(pair.Key,
            new(pair.Value, pair.Value / 2, pair.Value - pair.Value / 2, 0, 0))).ToList())
        { Source = ExamCore.Placement };

    [Fact]
    public void UsesOnlySevenRawPartAccuraciesInOrder()
    {
        var result = Result();
        var actual = PlacementFeatures.FromFinalizedResult(result);
        Assert.Equal(new[] { 50m, 48m, 48.72m, 50m, 50m, 50m, 50m }, actual);
        var differentScore = result with { ListeningScore = 495, ReadingScore = 5, TotalScore = 990 };
        Assert.Equal(actual, PlacementFeatures.FromFinalizedResult(differentScore));
    }

    [Fact]
    public void RejectsUnfinishedOrIncompleteResults()
    {
        Assert.Equal("PLACEMENT_NOT_FINALIZED",
            Assert.Throws<ExamProblem>(() => PlacementFeatures.FromFinalizedResult(Result(ExamCore.Active))).Code);
        var incomplete = Result() with { Parts = Result().Parts.Skip(1).ToList() };
        Assert.Equal("INVALID_PLACEMENT_RESULT",
            Assert.Throws<ExamProblem>(() => PlacementFeatures.FromFinalizedResult(incomplete)).Code);
    }
}
