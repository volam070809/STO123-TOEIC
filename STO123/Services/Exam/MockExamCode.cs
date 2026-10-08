namespace STO123.Services.Exam;

public static class MockExamCode
{
    public static string FromId(int examId) => $"MOCK-{examId:D6}";
    public static string? FromId(int? examId) => examId.HasValue ? FromId(examId.Value) : null;
}
