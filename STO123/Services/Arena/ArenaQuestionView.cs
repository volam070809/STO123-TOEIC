using STO123.Services.Exam;

namespace STO123.Services.Arena;

/// <summary>Only printed material is sent while a listening question is active.</summary>
public sealed record ArenaDocumentView(int Order, string Type, string? Content, string? ImageUrl);

public sealed record ArenaQuestionView(int Number, int Part, int? GroupId, string? Content,
    string? A, string? B, string? C, string? D, string? Context,
    string? AudioUrl, string? ImageUrl, IReadOnlyList<ArenaDocumentView> Documents,
    string? Correct, string? Explanation, IReadOnlyList<string> OptionLetters);

public static class ArenaQuestionPresenter
{
    public static ArenaQuestionView Present(ArenaQuestionSnapshot question, Guid matchId,
        int index, bool reveal)
    {
        var listeningChoices = question.Part is 1 or 2;
        var listening = question.Part <= 4;
        IEnumerable<ArenaDocumentSnapshot> visibleDocuments = listeningChoices ? [] : listening ?
            question.Documents.Where(d => d.Type == "IMAGE") : question.Documents;
        var documents = visibleDocuments
            .Select(d => new ArenaDocumentView(d.Order, d.Type, d.Content,
                d.ImagePath is null ? null :
                    $"/api/arena/matches/{matchId}/questions/{index}/documents/{d.Order}/image"))
            .ToArray();
        var audioUrl = question.AudioPath is null ? null : question.GroupId is { } groupId ?
            $"/api/arena/matches/{matchId}/groups/{groupId}/audio" :
            $"/api/arena/matches/{matchId}/questions/{index}/audio";
        return new ArenaQuestionView(index + 1, question.Part, question.GroupId,
            listeningChoices && !reveal ? null : question.Content,
            listeningChoices && !reveal ? null : question.A,
            listeningChoices && !reveal ? null : question.B,
            listeningChoices && !reveal ? null : question.C,
            listeningChoices && !reveal ? null : question.D,
            listening ? null : question.Context,
            audioUrl,
            question.ImagePath is null ? null :
                $"/api/arena/matches/{matchId}/questions/{index}/image",
            documents, reveal ? question.Correct : null,
            reveal ? question.Explanation : null,
            question.D is null ? ["A", "B", "C"] : ["A", "B", "C", "D"]);
    }
}
