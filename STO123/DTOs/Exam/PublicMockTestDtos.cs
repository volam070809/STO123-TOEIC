namespace STO123.DTOs.Exam;

public sealed record PublicMockTestDto(
    int ExamId,
    string ExamCode,
    string ExamName,
    int Duration,
    int QuestionCount);
