using STO123.DTOs.Exam;
using STO123.Services.Exam;

namespace STO123.Tests;

public class PlacementAnalysisTests
{
    [Fact]
    public void ComputesSevenPartAveragesAndExtremes()
    {
        var parts = ExamCore.PartCounts.Select(pair => new PartStatsDto(pair.Key,
            new(pair.Value, pair.Key == 1 ? 6 : pair.Key == 2 ? 0 : pair.Value / 2,
                0, pair.Value - (pair.Key == 1 ? 6 : pair.Key == 2 ? 0 : pair.Value / 2), 0))).ToList();
        var result = new ExamResultDto(1, ExamCore.Submitted, DateTime.UtcNow, DateTime.UtcNow,
            10, null, null, null, false, new(200, 0, 0, 200, 0),
            new(100, 0, 0, 100, 0), new(100, 0, 0, 100, 0), parts)
            { Source = ExamCore.Placement };
        var analysis = PlacementAnalysis.From(result);
        Assert.Equal(7, analysis.Parts.Count);
        Assert.Equal(1, analysis.Summary.StrongestPart);
        Assert.Equal(2, analysis.Summary.WeakestPart);
        Assert.Equal("GOOD", analysis.Parts[0].Level);
        Assert.Equal("NEEDS_IMPROVEMENT", analysis.Parts[1].Level);
        Assert.Equal(Math.Round(analysis.Parts.Average(x => x.Percentage), 2),
            analysis.Summary.OverallPercentage);
        Assert.Equal(Math.Round(analysis.Parts.Take(4).Average(x => x.Percentage), 2),
            analysis.Summary.ListeningPercentage);
        Assert.Equal(Math.Round(analysis.Parts.Skip(4).Average(x => x.Percentage), 2),
            analysis.Summary.ReadingPercentage);
    }
}
