namespace STO123.DTOs.Admin;

public sealed class AdminPaymentListQuery
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public string? Gateway { get; init; }
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record AdminPaymentGatewayDto(int GatewayId, string Code, string Name);

public sealed record AdminPaymentListItemDto(
    int PaymentId, string GatewayReference, int EnrollmentId,
    int LearnerId, string LearnerName, string LearnerEmail,
    string PurchaseType, int? PackageId, string? PackageName,
    IReadOnlyList<string> CourseNames,
    decimal RegisteredAmount, decimal PaymentAmount, string Status,
    AdminPaymentGatewayDto Gateway, DateTime CreatedAt, DateTime? PaidAt);

public sealed record AdminPaymentLearnerSummaryItemDto(
    int LearnerId, string LearnerName, string LearnerEmail,
    int TotalTransactions, int SuccessfulTransactions, int FailedTransactions,
    int PendingTransactions, decimal SuccessfulAmount, DateTime LatestTransactionAt);

public sealed record AdminPagedPaymentLearnersDto(
    IReadOnlyList<AdminPaymentLearnerSummaryItemDto> Items, int Page, int PageSize,
    int TotalItems, int TotalPages, AdminPaymentSummaryDto Summary,
    IReadOnlyList<AdminPaymentGatewayDto> Gateways);

public sealed record AdminPaymentSummaryDto(
    int Total, int Pending, int Successful, int Failed, decimal SuccessfulAmount);

public sealed record AdminPagedPaymentsDto(
    IReadOnlyList<AdminPaymentListItemDto> Items, int Page, int PageSize,
    int TotalItems, int TotalPages, AdminPaymentSummaryDto Summary,
    IReadOnlyList<AdminPaymentGatewayDto> Gateways);

public sealed record AdminPaymentLearnerDto(
    int LearnerId, string FullName, string Email, string? PhoneNumber, string Status);

public sealed record AdminPaymentEnrollmentDto(
    int EnrollmentId, string PurchaseType, int? PackageId, string? PackageName,
    decimal RegisteredAmount, int AccessDays, DateTime RegisteredAt,
    DateTime? StartsAt, DateTime? ExpiresAt, string Status);

public sealed record AdminPaymentCourseDto(
    int CourseId, string Name, byte Stage, string Status, decimal? UnitPriceSnapshot);

public sealed record AdminRelatedPaymentDto(
    int PaymentId, decimal Amount, string Status, DateTime CreatedAt, DateTime? PaidAt);

public sealed record AdminPaymentDetailDto(
    int PaymentId, string GatewayReference, decimal Amount, string Status,
    DateTime CreatedAt, DateTime? PaidAt, AdminPaymentGatewayDto Gateway,
    AdminPaymentLearnerDto Learner, AdminPaymentEnrollmentDto Enrollment,
    IReadOnlyList<AdminPaymentCourseDto> Courses,
    IReadOnlyList<AdminRelatedPaymentDto> RelatedPayments);
