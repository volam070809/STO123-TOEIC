namespace STO123.DTOs.Admin;

public sealed record AdminLearnerActivityDto(
    string Type, string Label, string Status, DateTime OccurredAt, int ReferenceId);

public sealed record AdminLearnerOverviewDto(
    int LearnerId, string FullName, string Email, string? PhoneNumber, string Status,
    DateTime CreatedAt, DateTime? LastLoginAt, int VocabularyTopicsStarted,
    int UniqueRememberedWords, int PracticeAttemptCount, int CompletedMockCount,
    DateTime? LastLearningAt, IReadOnlyList<AdminLearnerActivityDto> RecentActivity);

public sealed record AdminLearnerVocabularyTopicDto(
    int TopicId, string Name, string TopicStatus, int TotalWords, int RememberedWords,
    int LearningWords, decimal? ProgressPercent, string LearningStatus,
    int PracticeAttempts, int CompletedPracticeAttempts, int PracticeCorrect,
    int PracticeIncorrect, decimal? PracticeAccuracy, DateTime? LastActivityAt);

public sealed record AdminLearnerVocabularyAnalyticsDto(
    int UniqueStartedWords, int UniqueRememberedWords, int TopicsStarted,
    IReadOnlyList<AdminLearnerVocabularyTopicDto> Topics);

public sealed record AdminLearnerPracticePartDto(
    int Part, int TotalAttempts, int CompletedAttempts, int Correct, int Incorrect,
    int Unanswered, decimal? Accuracy, DateTime? MostRecentAt);

public sealed record AdminLearnerPracticeAttemptDto(
    int AttemptId, int? Part, DateTime StartedAt, DateTime? CompletedAt, string Status,
    string? Preset, int? Difficulty, int TotalQuestions, int Correct, int Incorrect,
    int Unanswered, decimal? Accuracy);

public sealed record AdminLearnerPracticeAnalyticsDto(
    IReadOnlyList<AdminLearnerPracticePartDto> Parts,
    IReadOnlyList<AdminLearnerPracticeAttemptDto> Items,
    int TotalItems, int Page, int PageSize, int TotalPages);

public sealed record AdminLearnerExamPartDto(
    int Part, int Total, int Correct, int Incorrect, int Unanswered, decimal? Accuracy);

public sealed record AdminLearnerExamAttemptDto(
    int AttemptId, string Type, string Status, string? ExamName, DateTime StartedAt,
    DateTime? CompletedAt, int Total, int Correct, int Incorrect, int Unanswered,
    decimal? Accuracy, int? ListeningScore, int? ReadingScore, int? TotalScore,
    byte? RecommendedStage, int? TargetScore,
    IReadOnlyList<AdminLearnerExamPartDto> Parts);

public sealed record AdminLearnerExamSummaryDto(
    int TotalAttempts, int CompletedAttempts, int Correct, int Incorrect,
    int Unanswered, decimal? Accuracy, int? LatestEstimatedScore);

public sealed record AdminLearnerExamHistoryDto(
    AdminLearnerExamSummaryDto Summary, IReadOnlyList<AdminLearnerExamAttemptDto> Items,
    int TotalItems, int Page, int PageSize, int TotalPages);

public sealed record AdminLearnerExamAnalyticsDto(
    AdminLearnerExamHistoryDto MockTests, AdminLearnerExamHistoryDto PlacementTests);

public sealed record AdminLearnerAccessCourseDto(
    int CourseId, string Name, byte Stage, string CourseStatus, decimal? PriceSnapshot,
    bool HasCurrentAccess, string AccessStatus);

public sealed record AdminLearnerAccessEnrollmentDto(
    int EnrollmentId, string PurchaseType, int? PackageId, string? PackageName,
    decimal RegistrationPrice, int ValidDays, DateTime RegisteredAt, DateTime? StartedAt,
    DateTime? EndsAt, string StoredStatus, string PaymentStatus,
    IReadOnlyList<AdminLearnerAccessCourseDto> Courses);

public sealed record AdminLearnerCourseAnalyticsDto(
    int EnrollmentCount, int AccessibleCourseCount,
    IReadOnlyList<AdminLearnerAccessEnrollmentDto> Enrollments);
