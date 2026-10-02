using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public class CourseRecommendationTests
{
    private static readonly KhoaHoc[] Courses = [
        new() { GiaiDoan = 1, TrangThai = "DANG_MO" },
        new() { GiaiDoan = 2, TrangThai = "DANG_MO" },
        new() { GiaiDoan = 3, TrangThai = "DANG_MO" }
    ];

    [Theory]
    [InlineData(1, 400, new byte[] { 1 })]
    [InlineData(1, 650, new byte[] { 1, 2 })]
    [InlineData(1, 850, new byte[] { 1, 2, 3 })]
    [InlineData(2, 650, new byte[] { 2 })]
    [InlineData(2, 850, new byte[] { 2, 3 })]
    [InlineData(3, 850, new byte[] { 3 })]
    [InlineData(2, 400, new byte[] { 2 })]
    public void CombinesCurrentStageAndConfirmedTarget(byte current, int target, byte[] expected) =>
        Assert.Equal(expected, CourseRecommendation.RecommendedStages(Courses, current, target));

    [Fact]
    public void NoTargetMeansNoRecommendation()
    {
        Assert.Empty(CourseRecommendation.RecommendedStages(Courses, 2, null));
        Assert.Empty(CourseRecommendation.RecommendedStages(Courses, null, 850));
        Assert.Equal((byte)1, CourseRecommendation.TargetStage(449));
        Assert.Equal((byte)2, CourseRecommendation.TargetStage(450));
        Assert.Equal((byte)3, CourseRecommendation.TargetStage(700));
    }
}
