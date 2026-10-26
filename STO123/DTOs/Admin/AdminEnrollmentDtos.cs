namespace STO123.DTOs.Admin;

public sealed class AdminEnrollmentListQuery
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public int? PackageId { get; init; }
    public int? CourseId { get; init; }
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record AdminEnrollmentCourseDto(
    int CourseId, string Name, byte Stage, string Status, decimal? UnitPriceSnapshot);

public sealed record AdminEnrollmentListItemDto(
    int EnrollmentId, int LearnerId, string LearnerName, string LearnerEmail,
    string PurchaseType, int? PackageId, string? PackageName,
    decimal RegistrationPrice, int AccessDays, DateTime RegisteredAt,
    DateTime? StartsAt, DateTime? ExpiresAt, string StoredStatus,
    string PaymentState, string AccessState, int PaymentCount,
    IReadOnlyList<AdminEnrollmentCourseDto> Courses);

public sealed record AdminEnrollmentSummaryDto(
    int Total, int PackagePurchases, int DirectCoursePurchases,
    int CurrentlyActive, int AwaitingPayment);

public sealed record AdminPagedEnrollmentsDto(
    IReadOnlyList<AdminEnrollmentListItemDto> Items, int Page, int PageSize,
    int TotalItems, int TotalPages, AdminEnrollmentSummaryDto Summary);

public sealed record AdminEnrollmentLearnerDto(
    int LearnerId, string FullName, string Email, string? PhoneNumber, string Status);

public sealed record AdminEnrollmentPaymentDto(
    int PaymentId, string GatewayCode, string GatewayName, decimal Amount,
    string Status, DateTime CreatedAt, DateTime? PaidAt);

public sealed record AdminEnrollmentHistoryDto(
    int EnrollmentId, string PurchaseType, int? PackageId, string? PackageName,
    decimal RegistrationPrice, DateTime RegisteredAt, DateTime? ExpiresAt,
    string StoredStatus, string PaymentState, string AccessState);

public sealed record AdminEnrollmentEntitlementDto(
    bool EnrollmentGrantsAccess, string AccessState, DateTime EvaluatedAt,
    IReadOnlyList<int> LearnerActiveCourseIds);

public sealed record AdminEnrollmentDetailDto(
    int EnrollmentId, AdminEnrollmentLearnerDto Learner, string PurchaseType,
    int? PackageId, string? PackageName, decimal RegistrationPrice, int AccessDays,
    DateTime RegisteredAt, DateTime? StartsAt, DateTime? ExpiresAt,
    string StoredStatus, string PaymentState,
    IReadOnlyList<AdminEnrollmentCourseDto> Courses,
    IReadOnlyList<AdminEnrollmentPaymentDto> Payments,
    AdminEnrollmentEntitlementDto Entitlement,
    IReadOnlyList<AdminEnrollmentHistoryDto> History);
