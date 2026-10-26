namespace STO123.DTOs.Admin;

public sealed class AdminAnalyticsQuery
{
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
}

public sealed record AdminDateRangeDto(DateTime From, DateTime To, int Days);
public sealed record AdminTrendPointDto(DateTime Date, int Count, decimal Amount = 0,
    double? Average = null, int AverageSamples = 0);
public sealed record AdminBreakdownDto(string Code, int Count, decimal Amount = 0, double? Average = null);
public sealed record AdminComparisonDto(decimal Current, decimal Previous, double? ChangePercent);
public sealed record AdminPerformanceDto(int Id, string Name, string Type, int Count, decimal Revenue);
public sealed record AdminRecentEnrollmentDto(int EnrollmentId, int LearnerId, string LearnerName,
    string PurchaseType, string? OfferingName, decimal Price, string Status, DateTime RegisteredAt);
public sealed record AdminRecentPaymentDto(int PaymentId, int EnrollmentId, string LearnerName,
    decimal Amount, string Status, string Gateway, DateTime CreatedAt);

public sealed record AdminDashboardDto(AdminDateRangeDto Range,
    int NewLearners, int Enrollments,
    decimal SuccessfulRevenue, int PendingPayments, int FailedPayments,
    IReadOnlyList<AdminTrendPointDto> LearnerGrowth,
    IReadOnlyList<AdminTrendPointDto> RevenueTrend,
    IReadOnlyList<AdminRecentEnrollmentDto> RecentEnrollments,
    IReadOnlyList<AdminRecentPaymentDto> RecentPayments,
    IReadOnlyList<AdminPerformanceDto> OfferingPerformance);

public sealed record AdminOverviewReportDto(AdminDateRangeDto Range,
    AdminComparisonDto NewLearners, AdminComparisonDto Enrollments,
    AdminComparisonDto SuccessfulRevenue, AdminComparisonDto LearningAttempts,
    int TotalLearners, int ActiveLearners, int CurrentlyValidEnrollments,
    IReadOnlyList<AdminTrendPointDto> LearnerTrend,
    IReadOnlyList<AdminTrendPointDto> EnrollmentTrend,
    IReadOnlyList<AdminTrendPointDto> RevenueTrend);

public sealed record AdminLearnersReportDto(AdminDateRangeDto Range,
    int TotalLearners, int NewLearners, int LearnersWithActivity, int LearningAttempts,
    int CompletedAttempts, IReadOnlyList<AdminBreakdownDto> AccountStatuses,
    IReadOnlyList<AdminTrendPointDto> LearnerTrend,
    IReadOnlyList<AdminTrendPointDto> ActivityTrend);

public sealed record AdminEnrollmentsReportDto(AdminDateRangeDto Range,
    int Total, int PackagePurchases, int DirectCoursePurchases,
    int Pending, int ActiveStored, int ExpiredStored, int CurrentlyValid,
    decimal RegisteredValue, IReadOnlyList<AdminTrendPointDto> Trend);

public sealed record AdminOfferingsReportDto(AdminDateRangeDto Range,
    int OpenPackages, int ClosedPackages, int OpenCourses, int ClosedCourses,
    IReadOnlyList<AdminPerformanceDto> Packages,
    IReadOnlyList<AdminPerformanceDto> Courses);

public sealed record AdminPaymentsReportDto(AdminDateRangeDto Range,
    int Total, int Pending, int Successful, int Failed,
    decimal SuccessfulRevenue, decimal AverageSuccessfulAmount,
    IReadOnlyList<AdminBreakdownDto> Gateways,
    IReadOnlyList<AdminTrendPointDto> Trend);

public sealed record AdminPartPerformanceDto(int Part, int Questions, int Answered, int Correct, double? Accuracy);
public sealed record AdminAttemptTypeDto(string Type, int Started, int Completed,
    double? AverageListening, double? AverageReading, double? AverageTotal);
public sealed record AdminToeicResultsReportDto(AdminDateRangeDto Range,
    int TotalAttempts, int CompletedAttempts, int ParticipatingLearners, int PlacementClassifications,
    int ProgressSteps, int CompletedProgressSteps, double? AverageProgress,
    IReadOnlyList<AdminAttemptTypeDto> AttemptTypes,
    IReadOnlyList<AdminPartPerformanceDto> Parts,
    IReadOnlyList<AdminTrendPointDto> Trend);
