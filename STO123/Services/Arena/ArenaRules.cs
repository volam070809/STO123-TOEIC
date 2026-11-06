using System.Text.Json;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Services.Arena;

/// <summary>Pure match transitions. Both HTTP and SignalR use these rules through ArenaService.</summary>
public static class ArenaRules
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Submit(ArenaMatch row, IReadOnlyList<ArenaQuestionSnapshot> questions,
        int learnerId, int questionIndex, string? choice, DateTime now)
    {
        if (row.Status != "ACTIVE" || row.QuestionIndex != questionIndex)
            throw new ArenaProblem("QUESTION_CLOSED", "Câu này đã kết thúc. Đang đồng bộ kết quả.", 409);
        if (questionIndex < 0 || questionIndex >= questions.Count)
            throw new ArenaProblem("INVALID_QUESTION", "Câu hỏi không hợp lệ.");
        if (row.DeadlineUtc <= now)
            throw new ArenaProblem("QUESTION_EXPIRED", "Đã hết thời gian trả lời câu này.", 409);
        var selected = choice?.Trim().ToUpperInvariant();
        if (selected is not ("A" or "B" or "C" or "D") ||
            selected == "D" && questions[questionIndex].D is null)
            throw new ArenaProblem("INVALID_CHOICE", "Hãy chọn một đáp án hợp lệ.");
        var answers = JsonSerializer.Deserialize<List<ArenaSubmission>>(row.AnswersJson, Json) ?? [];
        if (answers.Any(x => x.QuestionIndex == questionIndex && x.PlayerId == learnerId))
            throw new ArenaProblem("ANSWER_LOCKED", "Đáp án của bạn đã được khóa.", 409);
        answers.Add(new ArenaSubmission(questionIndex, learnerId, selected, null, now));
        if (answers.Count(x => x.QuestionIndex == questionIndex) == 2)
            Reveal(row, questions[questionIndex], answers, now);
        else row.AnswersJson = JsonSerializer.Serialize(answers, Json);
    }

    public static void Reveal(ArenaMatch row, ArenaQuestionSnapshot question,
        List<ArenaSubmission> answers, DateTime now)
    {
        foreach (var playerId in new[] { row.OwnerId, row.OpponentId ?? 0 }.Where(x => x > 0))
        {
            var index = answers.FindIndex(x => x.QuestionIndex == row.QuestionIndex && x.PlayerId == playerId);
            if (index < 0) answers.Add(new ArenaSubmission(row.QuestionIndex, playerId, null, false, now));
            else answers[index] = answers[index] with { Correct = string.Equals(
                answers[index].Choice, question.Correct, StringComparison.OrdinalIgnoreCase) };
        }
        row.AnswersJson = JsonSerializer.Serialize(answers, Json);
        row.Status = "REVEAL";
        row.DeadlineUtc = null;
        row.RevealUntilUtc = now.AddSeconds(6);
    }

    public static void Advance(ArenaMatch row, int count, DateTime now, int questionSeconds)
    {
        if (row.QuestionIndex + 1 >= count)
        { row.Status = "COMPLETED"; row.CompletedUtc = now; row.RevealUntilUtc = null; }
        else { row.QuestionIndex++; row.Status = "ACTIVE"; row.DeadlineUtc = now.AddSeconds(questionSeconds);
            row.RevealUntilUtc = null; }
    }

    /// <summary>Three consecutive unanswered questions indicate an interrupted match.</summary>
    public static bool ResolveAbandonment(ArenaMatch row, IReadOnlyList<ArenaSubmission> answers,
        DateTime now)
    {
        if (row.Status != "REVEAL" || row.QuestionIndex < 2 || row.OpponentId is null) return false;
        var first = row.QuestionIndex - 2;
        var ownerAbsent = Enumerable.Range(first, 3).All(index => answers.Any(a =>
            a.QuestionIndex == index && a.PlayerId == row.OwnerId && a.Choice is null));
        var opponentAbsent = Enumerable.Range(first, 3).All(index => answers.Any(a =>
            a.QuestionIndex == index && a.PlayerId == row.OpponentId && a.Choice is null));
        if (!ownerAbsent && !opponentAbsent) return false;
        row.Status = "ABANDONED";
        row.ForfeitById = ownerAbsent == opponentAbsent ? null :
            ownerAbsent ? row.OwnerId : row.OpponentId;
        row.CompletedUtc = now;
        row.RevealUntilUtc = null;
        return true;
    }
}
