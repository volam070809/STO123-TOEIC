namespace STO123.Services.Exam;

public sealed record ArenaDocumentSnapshot(int Order, string Type, string? Content, string? ImagePath);

public sealed record ArenaQuestionSnapshot(int QuestionId, int Part, int? GroupId, string? Content,
    string A, string B, string C, string? D, string Correct, string? Explanation,
    string? Context, string? AudioPath, string? ImagePath,
    IReadOnlyList<ArenaDocumentSnapshot> Documents);
