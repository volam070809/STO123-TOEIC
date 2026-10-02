using STO123.DTOs.Exam;

namespace STO123.Services.Exam;

public sealed record PlacementPartAnalysis(int Part, int Correct, int Total,
    decimal Percentage, string Level);
public sealed record PlacementSummary(decimal OverallPercentage, decimal ListeningPercentage,
    decimal ReadingPercentage, int StrongestPart, int WeakestPart);
public sealed record PlacementAnalysisResult(PlacementSummary Summary,
    IReadOnlyList<PlacementPartAnalysis> Parts);

public static class PlacementAnalysis
{
    public static PlacementAnalysisResult From(ExamResultDto result)
    {
        var percentages = PlacementFeatures.FromFinalizedResult(result);
        var parts = result.Parts.OrderBy(x => x.Part).Select(x => new PlacementPartAnalysis(
            x.Part, x.Stats.Correct, x.Stats.Total, percentages[x.Part - 1],
            percentages[x.Part - 1] < 40 ? "NEEDS_IMPROVEMENT" :
                percentages[x.Part - 1] < 65 ? "DEVELOPING" : "GOOD")).ToList();
        var summary = new PlacementSummary(Math.Round(percentages.Average(), 2),
            Math.Round(percentages.Take(4).Average(), 2),
            Math.Round(percentages.Skip(4).Average(), 2),
            parts.OrderByDescending(x => x.Percentage).ThenBy(x => x.Part).First().Part,
            parts.OrderBy(x => x.Percentage).ThenBy(x => x.Part).First().Part);
        return new(summary, parts);
    }
}
