namespace STO123.DTOs.Courses;

public sealed record PublicCourseSummaryDto(
    int CourseId,
    string Name,
    byte Stage,
    int MaxTargetScore,
    string? Description,
    decimal? RetailPrice,
    int AccessDays,
    string? CoverImageUrl);

public sealed record PublicCourseDetailDto(
    int CourseId,
    string Name,
    byte Stage,
    int MaxTargetScore,
    string? Description,
    decimal? RetailPrice,
    int AccessDays,
    string? CoverImageUrl);
