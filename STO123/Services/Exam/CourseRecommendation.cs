using STO123.Models;

namespace STO123.Services.Exam;

public static class CourseRecommendation
{
    public static IReadOnlySet<byte> RecommendedStages(IEnumerable<KhoaHoc> courses, byte? startStage, int? target)
    {
        if (startStage is not >= 1 or > 3) return new HashSet<byte>();
        var open = courses.Where(c => c.TrangThai == "DANG_MO" && c.GiaiDoan >= startStage)
            .OrderBy(c => c.GiaiDoan).ToArray();
        var result = new HashSet<byte>();
        foreach (var course in open)
        {
            result.Add(course.GiaiDoan);
            if (target is null || target <= course.DiemMucTieuToiDa) break;
        }
        return result;
    }
}
