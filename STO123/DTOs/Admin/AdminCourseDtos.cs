using System.Text.Json.Serialization;

namespace STO123.DTOs.Admin;

public sealed class AdminCourseListQuery
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public byte? Stage { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record AdminCourseListItemDto(
    int CourseId,
    string Name,
    byte Stage,
    int MaxTargetScore,
    decimal? RetailPrice,
    string Status,
    DateTime CreatedAt,
    string? ContentAdminName,
    int UnitCount,
    int LessonCount,
    int StepCount,
    int PackageCount,
    int DirectEnrollmentCount);

public sealed record AdminPagedCoursesDto(
    IReadOnlyList<AdminCourseListItemDto> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record AdminCourseLessonDto(
    int LessonId,
    string Name,
    string? Description,
    int Order,
    int StepCount);

public sealed record AdminCourseUnitDto(
    int UnitId,
    string Name,
    string? Description,
    int Order,
    IReadOnlyList<AdminCourseLessonDto> Lessons);

public sealed record AdminCoursePackageDto(
    int PackageId,
    string Name,
    decimal Price,
    int ValidDays,
    string Status,
    int EnrollmentCount);

public sealed record AdminCourseDetailDto(
    int CourseId,
    string Name,
    byte Stage,
    int MaxTargetScore,
    decimal? RetailPrice,
    string? Description,
    string Status,
    string? CoverImageUrl,
    DateTime CreatedAt,
    int? ContentAdminId,
    string? ContentAdminName,
    int DirectEnrollmentCount,
    IReadOnlyList<AdminCourseUnitDto> Units,
    IReadOnlyList<AdminCoursePackageDto> Packages);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminCourseWriteRequest
{
    public string Name { get; init; } = string.Empty;
    public byte Stage { get; init; }
    public int MaxTargetScore { get; init; }
    public decimal? RetailPrice { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? CoverImageUrl { get; init; }
}

public sealed class AdminCourseImageWriteRequest
{
    public string Name { get; init; } = string.Empty;
    public byte Stage { get; init; }
    public int MaxTargetScore { get; init; }
    public decimal? RetailPrice { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public IFormFile? File { get; init; }

    public AdminCourseWriteRequest ToCourseRequest(string? coverImageUrl = null) => new()
    {
        Name = Name, Stage = Stage, MaxTargetScore = MaxTargetScore, RetailPrice = RetailPrice,
        Description = Description, Status = Status, CoverImageUrl = coverImageUrl
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminChangeCourseStatusRequest
{
    public string Status { get; init; } = string.Empty;
}

public sealed record AdminCourseMutationDto(
    int CourseId,
    string Name,
    byte Stage,
    int MaxTargetScore,
    decimal? RetailPrice,
    string Status);

public sealed record AdminDeleteCourseBlockedDto(
    string Code,
    string Message,
    IReadOnlyList<string> Blockers);
