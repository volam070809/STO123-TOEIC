using System.Text.Json.Serialization;

namespace STO123.DTOs.Admin;

public sealed class AdminPackageListQuery
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public int? CourseId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record AdminPackageListItemDto(
    int PackageId, string Name, decimal Price, int ValidDays, string Status, DateTime CreatedAt,
    int CourseCount, int EnrollmentCount, int ActiveEnrollmentCount, int SuccessfulPaymentCount,
    IReadOnlyList<string> CourseNames);

public sealed record AdminPagedPackagesDto(
    IReadOnlyList<AdminPackageListItemDto> Items, int Page, int PageSize, int TotalItems, int TotalPages);

public sealed record AdminPackageCourseDto(int CourseId, string Name, byte Stage, string Status, decimal? RetailPrice);

public sealed record AdminPackageEnrollmentStatsDto(
    int Total, int Pending, int Active, int Expired, int SuccessfulPaymentCount, decimal SuccessfulPaymentAmount);

public sealed record AdminPackageDetailDto(
    int PackageId, string Name, decimal Price, int ValidDays, string? Description, string Status,
    DateTime CreatedAt, IReadOnlyList<AdminPackageCourseDto> Courses, AdminPackageEnrollmentStatsDto Enrollments);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminPackageWriteRequest
{
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int ValidDays { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public IReadOnlyList<int>? CourseIds { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminChangePackageStatusRequest
{
    public string Status { get; init; } = string.Empty;
}

public sealed record AdminPackageMutationDto(
    int PackageId, string Name, decimal Price, int ValidDays, string Status, IReadOnlyList<int> CourseIds);

public sealed record AdminPackageConflictDto(string Code, string Message, IReadOnlyList<string> Blockers);
