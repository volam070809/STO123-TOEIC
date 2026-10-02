using STO123.Services.Exam;

namespace STO123.DTOs.Exam;

public sealed record StartExamRequest(string Source, int? ExamId, int? Part = null);
public sealed record SaveAnswerRequest(string? SelectedOption, bool? Flagged);
public sealed record FixedMockDto(int ExamId, string ExamName, int Duration, string Status);
public sealed record ExamQuestionDto(int AttemptQuestionId, int Order, int PartOrder, int Part,
    string? Text, string? A, string? B, string? C, string? D, string? SelectedOption, bool Flagged);
public sealed record ExamDocumentDto(int Order, string Type, object? Content, bool HasImage, string? ImageUrl);
public sealed record ExamGroupDto(int GroupId, int Order, int Part, string? Context, bool HasAudio,
    bool HasImage, IReadOnlyList<ExamDocumentDto> Documents, IReadOnlyList<ExamQuestionDto> Questions);
public sealed record ExamAttemptDto(int AttemptId, string Source, string Status, DateTime StartedAt,
    DateTime? ExpiresAt, int TotalQuestions, IReadOnlyList<ExamGroupDto> Groups,
    IReadOnlyList<ExamQuestionDto> IndependentQuestions)
{
    public string? ExamName { get; init; }
    public int? RemainingSeconds { get; init; }
    public bool IsPaused { get; init; }
}
public sealed record ExamHistoryDto(int AttemptId, string Status, DateTime StartedAt, DateTime? ExpiresAt,
    int? TotalScore);
public sealed record FixedMockHistoryDto(int ExamId, string ExamName, string ExamStatus,
    int CompletedAttempts, int? LatestScore, decimal? AverageScore, int? BestScore,
    DateTime? LatestCompletedAt, int? ActiveAttemptId);
public sealed record MockStatisticsDto(decimal? AverageBetweenFixedExams, int? HighestMockScore,
    int? LatestMockScore);
public sealed record MockHistoryDto(IReadOnlyList<FixedMockHistoryDto> FixedExams,
    IReadOnlyList<ExamHistoryDto> RandomAttempts, MockStatisticsDto Statistics)
{
    public IReadOnlyList<MockAttemptSummaryDto> Attempts { get; init; } = [];
}
public sealed record MockAttemptSummaryDto(int AttemptId, string Name, string Status,
    DateTime StartedAt, DateTime? ExpiresAt, int TotalQuestions, int Answered,
    int? Part, int? TotalScore, int? Correct, decimal? Percentage, string Mode,
    int? ExamId = null)
{
    public int? RemainingSeconds { get; init; }
    public bool IsPaused { get; init; }
}
public sealed record RawStatsDto(int Total, int Correct, int Incorrect, int Unanswered, decimal Percentage);
public sealed record PartStatsDto(int Part, RawStatsDto Stats);
public sealed record ExamResultDto(int AttemptId, string Status, DateTime StartedAt, DateTime? FinishedAt,
    int TimeUsedSeconds, int? ListeningScore, int? ReadingScore, int? TotalScore, bool IsEstimated,
    RawStatsDto Overall, RawStatsDto Listening, RawStatsDto Reading, IReadOnlyList<PartStatsDto> Parts)
{
    public string? ExamName { get; init; }
    public string Source { get; init; } = ExamCore.Mock;
}
public sealed record ReviewQuestionDto(int AttemptQuestionId, int Order, int Part, string? Text,
    string A, string B, string C, string? D, string? SelectedOption, string CorrectOption,
    string Status, string Explanation, bool Flagged);
public sealed record ReviewGroupDto(int GroupId, int Order, int Part, string? Context, bool HasAudio,
    bool HasImage, IReadOnlyList<ExamDocumentDto> Documents, IReadOnlyList<ReviewQuestionDto> Questions);
public sealed record ExamReviewDto(int AttemptId, IReadOnlyList<ReviewGroupDto> Groups,
    IReadOnlyList<ReviewQuestionDto> IndependentQuestions)
{
    public string Source { get; init; } = ExamCore.Mock;
}
