using STO123.Services.SmartLearning;

namespace STO123.Tests;

public sealed class SkillEstimatorTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NewLearnerReceivesProvisionalGuidanceWithoutClaimingWeakness()
    {
        var profile = SkillEstimator.Estimate([], [], Now);
        Assert.Equal(7, profile.Parts.Count);
        Assert.All(profile.Parts, part => {
            Assert.Equal("TAM_THOI", part.Confidence);
            Assert.Equal(0, part.EvidenceQuestions);
            Assert.Equal("CHUA_DU", part.Trend);
        });
        Assert.Equal("/practice/parts?part=1&recommended=1", profile.Recommendations[0].ActionUrl);
    }

    [Fact]
    public void ExamEvidenceOutweighsCasualArenaAndRepeatedQuestions()
    {
        var exam = Enumerable.Range(1, 30).Select(i =>
            new SkillAnswer(5, "MOCK", "mock-1", i, false, Now)).ToArray();
        var arena = Enumerable.Range(1, 30).Select(i =>
            new SkillAnswer(5, "ARENA", "arena-1", 100 + i, true, Now)).ToArray();
        var profile = SkillEstimator.Estimate(exam.Concat(arena), [], Now);
        Assert.True(profile.Parts[4].EstimatedAccuracy < 50);
        var repeated = SkillEstimator.Estimate(exam.Concat(exam.Select(x => x with {
            Session = "mock-2", Correct = true, CompletedAt = Now.AddDays(-1) })), [], Now);
        Assert.True(repeated.Parts[4].EffectiveQuestions < 60 * SkillEstimator.SourceWeights["MOCK"]);
    }

    [Fact]
    public void PracticeOnlyUsesExistingAggregateAndDetectsLatestDecline()
    {
        var aggregate = new PracticeAggregate(2, 3, 36, 26, 12, 4, Now);
        var part = SkillEstimator.Estimate([], [aggregate], Now).Parts[1];
        Assert.Equal("GIAM", part.Trend);
        Assert.Equal(33, part.RecentAccuracy);
    }

    [Fact]
    public void PlacementOnlyStillProducesUsefulProvisionalAssessment()
    {
        var evidence = Enumerable.Range(1, 10).Select(id =>
            new SkillAnswer(1, "PLACEMENT", "placement-1", id, id <= 4, Now));
        var part = SkillEstimator.Estimate(evidence, [], Now).Parts[0];
        Assert.Equal("TAM_THOI", part.Confidence);
        Assert.InRange(part.EstimatedAccuracy, 40, 50);
        Assert.Equal(10, part.Sources.Single().Questions);
    }

    [Fact]
    public void AdditionalCompletedPracticeChangesGuidanceAndTrend()
    {
        var before = SkillEstimator.Estimate([], [new PracticeAggregate(3, 2, 18, 7, 9, 2, Now.AddDays(-7))], Now);
        var after = SkillEstimator.Estimate([], [new PracticeAggregate(3, 3, 27, 15, 9, 8, Now)], Now);
        Assert.True(after.Parts[2].EstimatedAccuracy > before.Parts[2].EstimatedAccuracy);
        Assert.Equal("TANG", after.Parts[2].Trend);
        Assert.Equal("GIAM", before.Parts[2].Trend);
    }

    [Fact]
    public void ArenaAddsSupplementaryQuestionEvidenceWithoutBeingRequired()
    {
        var arena = Enumerable.Range(1, 5).Select(id =>
            new SkillAnswer(7, "ARENA", "arena-1", id, id <= 3, Now));
        var profile = SkillEstimator.Estimate(arena, [], Now);
        Assert.Equal(5, profile.Parts[6].EvidenceQuestions);
        Assert.Equal("TAM_THOI", profile.Parts[6].Confidence);
        Assert.Contains(profile.Parts[6].Sources, source => source.Source == "ARENA");
    }

    [Fact]
    public void GroupedQuestionsHaveLowerEffectiveSampleThanIndependentOnes()
    {
        var independent = Enumerable.Range(1, 6).Select(id =>
            new SkillAnswer(3, "MOCK", "session", id, true, Now));
        var grouped = independent.Select(x => x with { GroupId = x.QuestionId <= 3 ? 1 : 2 });
        var a = SkillEstimator.Estimate(independent, [], Now).Parts[2];
        var b = SkillEstimator.Estimate(grouped, [], Now).Parts[2];
        Assert.True(b.EffectiveQuestions < a.EffectiveQuestions);
        Assert.Equal(a.EvidenceQuestions, b.EvidenceQuestions);
    }
}
