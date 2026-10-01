using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public class CourseRecommendationTests
{
    [Fact]
    public void RecommendsFromKnnStageThroughFirstCourseCoveringTarget()
    {
        var courses = new[] {
            new KhoaHoc { GiaiDoan = 1, DiemMucTieuToiDa = 350, TrangThai = "DANG_MO" },
            new KhoaHoc { GiaiDoan = 2, DiemMucTieuToiDa = 650, TrangThai = "DANG_MO" },
            new KhoaHoc { GiaiDoan = 3, DiemMucTieuToiDa = 990, TrangThai = "DANG_MO" }
        };
        Assert.Equal(new byte[] { 2, 3 }, CourseRecommendation.RecommendedStages(courses, 2, 800).Order());
        Assert.Equal(new byte[] { 2 }, CourseRecommendation.RecommendedStages(courses, 2, 600));
        Assert.Empty(CourseRecommendation.RecommendedStages(courses, null, 600));
        courses[1].TrangThai = "DA_DONG";
        Assert.Equal(new byte[] { 3 }, CourseRecommendation.RecommendedStages(courses, 2, 800));
    }
}
