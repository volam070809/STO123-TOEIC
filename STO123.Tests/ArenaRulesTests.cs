using System.Text.Json;
using STO123.Models;
using STO123.Services.Arena;
using STO123.Services.Exam;

namespace STO123.Tests;

public sealed class ArenaRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ArenaQuestionSnapshot[] Questions = [
        new(1, 5, null, "Question one", "A", "B", "C", "D", "B", "Because B", null, null, null, []),
        new(2, 5, null, "Question two", "A", "B", "C", "D", "C", "Because C", null, null, null, [])
    ];
    private static ArenaMatch Match() => new() { Id = Guid.NewGuid(), OwnerId = 1,
        OpponentId = 2, Part = 5, Status = "ACTIVE", DeadlineUtc = Now.AddSeconds(35) };
    private static List<ArenaSubmission> Answers(ArenaMatch match) =>
        JsonSerializer.Deserialize<List<ArenaSubmission>>(match.AnswersJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public void FirstAnswerLocksAndDoesNotRevealCorrectness()
    {
        var match = Match();
        ArenaRules.Submit(match, Questions, 1, 0, "B", Now);
        Assert.Equal("ACTIVE", match.Status);
        Assert.Null(Answers(match)[0].Correct);
        Assert.Equal("ANSWER_LOCKED", Assert.Throws<ArenaProblem>(() =>
            ArenaRules.Submit(match, Questions, 1, 0, "A", Now)).Code);
    }

    [Fact]
    public void BothAnswersRevealIndividualResultsThenAdvanceTogether()
    {
        var match = Match();
        ArenaRules.Submit(match, Questions, 1, 0, "B", Now);
        ArenaRules.Submit(match, Questions, 2, 0, "A", Now.AddSeconds(1));
        Assert.Equal("REVEAL", match.Status);
        Assert.True(Answers(match).Single(x => x.PlayerId == 1).Correct);
        Assert.False(Answers(match).Single(x => x.PlayerId == 2).Correct);
        Assert.Equal(Now.AddSeconds(7), match.RevealUntilUtc);
        ArenaRules.Advance(match, Questions.Length, Now.AddSeconds(7), 35);
        Assert.Equal("ACTIVE", match.Status);
        Assert.Equal(1, match.QuestionIndex);
        ArenaRules.Submit(match, Questions, 1, 1, "C", Now.AddSeconds(8));
        ArenaRules.Submit(match, Questions, 2, 1, "C", Now.AddSeconds(9));
        ArenaRules.Advance(match, Questions.Length, Now.AddSeconds(15), 35);
        Assert.Equal("COMPLETED", match.Status);
        Assert.Equal(2, Answers(match).Count(x => x.Correct == true && x.PlayerId == 1));
    }

    [Fact]
    public void DeadlineScoresMissingOpponentAsIncorrect()
    {
        var match = Match();
        ArenaRules.Submit(match, Questions, 1, 0, "B", Now);
        ArenaRules.Reveal(match, Questions[0], Answers(match), Now.AddSeconds(35));
        var results = Answers(match);
        Assert.True(results.Single(x => x.PlayerId == 1).Correct);
        Assert.False(results.Single(x => x.PlayerId == 2).Correct);
        Assert.Null(results.Single(x => x.PlayerId == 2).Choice);
    }

    [Fact]
    public void ExpiredAndInvalidAnswersNeverMutateMatch()
    {
        var match = Match();
        Assert.Equal("INVALID_CHOICE", Assert.Throws<ArenaProblem>(() =>
            ArenaRules.Submit(match, Questions, 1, 0, "E", Now)).Code);
        Assert.Equal("QUESTION_EXPIRED", Assert.Throws<ArenaProblem>(() =>
            ArenaRules.Submit(match, Questions, 1, 0, "B", Now.AddSeconds(35))).Code);
        Assert.Empty(Answers(match));
    }

    [Fact]
    public void ThreeUnansweredQuestionsForfeitWithoutCompletedEvidence()
    {
        var match = Match();
        match.Status = "REVEAL";
        match.QuestionIndex = 2;
        var answers = Enumerable.Range(0, 3).SelectMany(index => new[] {
            new ArenaSubmission(index, 1, "A", false, Now),
            new ArenaSubmission(index, 2, null, false, Now)
        }).ToArray();
        Assert.True(ArenaRules.ResolveAbandonment(match, answers, Now));
        Assert.Equal("ABANDONED", match.Status);
        Assert.Equal(2, match.ForfeitById);
        Assert.NotNull(match.CompletedUtc);
    }

    [Fact]
    public void ASingleMissedQuestionDoesNotEndMatch()
    {
        var match = Match();
        match.Status = "REVEAL";
        match.QuestionIndex = 2;
        var answers = Enumerable.Range(0, 3).SelectMany(index => new[] {
            new ArenaSubmission(index, 1, "A", false, Now),
            new ArenaSubmission(index, 2, index == 1 ? null : "B", false, Now)
        }).ToArray();
        Assert.False(ArenaRules.ResolveAbandonment(match, answers, Now));
        Assert.Equal("REVEAL", match.Status);
    }
}
