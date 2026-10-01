using STO123.DTOs.Exam;

namespace STO123.Services.Exam;

public static class PlacementFeatures
{
    // The KNN input is ordered P1..P7 accuracy only. Scores are deliberately
    // absent from this API so no caller can silently use scaled TOEIC points.
    public static IReadOnlyList<decimal> FromFinalizedResult(ExamResultDto result)
    {
        if (result.Source != ExamCore.Placement)
            throw new ExamProblem("INVALID_PLACEMENT_RESULT", "Kết quả không phải bài phân lớp.", 409);
        if (result.Status is not (ExamCore.Submitted or ExamCore.Expired))
            throw new ExamProblem("PLACEMENT_NOT_FINALIZED", "Bài phân lớp chưa kết thúc.", 409);
        if (result.Parts.Count != ExamCore.PartCounts.Count ||
            result.Parts.Select(p => p.Part).Distinct().Count() != ExamCore.PartCounts.Count)
            throw new ExamProblem("INVALID_PLACEMENT_RESULT", "Kết quả từng Part không hợp lệ.", 409);

        var byPart = result.Parts.ToDictionary(p => p.Part, p => p.Stats);
        var percentages = new decimal[7];
        foreach (var (part, expected) in ExamCore.PartCounts)
        {
            if (!byPart.TryGetValue(part, out var stats) || stats.Total != expected ||
                stats.Correct < 0 || stats.Correct > expected ||
                stats.Incorrect < 0 || stats.Unanswered < 0 ||
                stats.Correct + stats.Incorrect + stats.Unanswered != expected)
                throw new ExamProblem("INVALID_PLACEMENT_RESULT", "Kết quả từng Part không hợp lệ.", 409);
            percentages[part - 1] = Math.Round(100m * stats.Correct / expected, 2);
        }
        return percentages;
    }
}
