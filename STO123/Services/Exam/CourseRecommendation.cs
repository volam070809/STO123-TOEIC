using STO123.Models;

namespace STO123.Services.Exam;

public static class CourseRecommendation
{
    public static IReadOnlyList<byte> RecommendedStages(IEnumerable<KhoaHoc> courses, byte? startStage,
        int? targetScore)
    {
        if (startStage is not >= 1 or > 3 || targetScore is not >= 10 or > 990)
            return [];
        var targetStage = TargetStage(targetScore.Value);
        var end = Math.Max(startStage.Value, targetStage);
        return courses.Where(c => c.TrangThai == "DANG_MO" &&
            c.GiaiDoan >= startStage.Value && c.GiaiDoan <= end)
            .Select(c => c.GiaiDoan).Distinct().Order().ToArray();
    }

    public static byte TargetStage(int targetScore) => targetScore < 450 ? (byte)1 :
        targetScore < 700 ? (byte)2 : (byte)3;
}
