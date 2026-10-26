namespace STO123.DTOs.Admin;

using System.Text.Json.Serialization;

public sealed class AdminLearnerListQuery
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record AdminLearnerListItemDto(
    int LearnerId,
    string FullName,
    string Email,
    string? PhoneNumber,
    string Status,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    int EnrollmentCount,
    int ActiveEnrollmentCount,
    int AttemptCount,
    DateTime? LastLearningAt);

public sealed record AdminPagedLearnersDto(
    IReadOnlyList<AdminLearnerListItemDto> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record AdminLearnerCourseDto(int CourseId, string Name, byte Stage, string Status);

public sealed record AdminLearnerEnrollmentDto(
    int EnrollmentId,
    int? PackageId,
    string? PackageName,
    decimal RegistrationPrice,
    int ValidDays,
    DateTime RegisteredAt,
    DateTime? StartedAt,
    DateTime? EndsAt,
    string Status,
    IReadOnlyList<AdminLearnerCourseDto> Courses,
    int PaymentCount,
    decimal SuccessfulPaymentAmount);

public sealed record AdminLearnerAttemptDto(
    int ResultId,
    string AttemptType,
    string Status,
    string? ExamName,
    int? ListeningScore,
    int? ReadingScore,
    int? TotalScore,
    DateTime StartedAt,
    DateTime? SubmittedAt);

public sealed record AdminLearnerLearningSummaryDto(
    int TotalAttempts,
    int CompletedAttempts,
    int ProgressStepCount,
    int CompletedProgressStepCount,
    double? AverageProgressPercent,
    DateTime? LastLearningAt,
    IReadOnlyList<AdminLearnerAttemptDto> RecentAttempts);

public sealed record AdminLearnerDetailDto(
    int LearnerId,
    string FullName,
    string Email,
    string? PhoneNumber,
    string Status,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    IReadOnlyList<AdminLearnerEnrollmentDto> Enrollments,
    AdminLearnerLearningSummaryDto Learning);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminCreateLearnerRequest
{
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public string Password { get; init; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminUpdateLearnerRequest
{
    public string FullName { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminChangeLearnerStatusRequest
{
    public string Status { get; init; } = string.Empty;
}

public sealed record AdminLearnerAccountDto(
    int LearnerId,
    string FullName,
    string Email,
    string? PhoneNumber,
    string Status);

public sealed record AdminDeleteLearnerBlockedDto(
    string Code,
    string Message,
    IReadOnlyList<string> Blockers);
