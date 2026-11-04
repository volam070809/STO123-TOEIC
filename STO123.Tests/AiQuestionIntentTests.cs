using STO123.Services.Ai;

namespace STO123.Tests;

public sealed class AiQuestionIntentTests
{
    [Theory]
    [InlineData("Đáp án câu này là gì?")]
    [InlineData("Which option is correct?")]
    [InlineData("Give me the answer")]
    public void DirectRequestsForUncheckedAnswersAreProtected(string text) =>
        Assert.True(AiQuestionIntent.IsProtectedAnswerRequest(text));

    [Theory]
    [InlineData("Giải thích thì hiện tại hoàn thành")]
    [InlineData("Tôi có thể thanh toán lại ở đâu?")]
    [InlineData("How to view my answers?")]
    public void UnrelatedLearningAndWebsiteQuestionsRemainAvailable(string text) =>
        Assert.False(AiQuestionIntent.IsProtectedAnswerRequest(text));

    [Fact]
    public void OrdinaryQuestionsAvoidPublishedContentQueries()
    {
        Assert.False(AiQuestionIntent.WantsLessonContent("Xin chào"));
        Assert.False(AiQuestionIntent.WantsVocabularyContent("Xin chào"));
        Assert.False(AiQuestionIntent.WantsPracticeExamples("Xin chào"));
        Assert.False(AiQuestionIntent.WantsLessonContent("Explain present perfect"));
        Assert.False(AiQuestionIntent.WantsVocabularyContent("Explain present perfect"));
        Assert.False(AiQuestionIntent.WantsPracticeExamples("Luyện Part 5 ở đâu?"));
        Assert.True(AiQuestionIntent.WantsLessonContent("Find present perfect lesson"));
        Assert.True(AiQuestionIntent.WantsVocabularyContent("Explain acumen"));
        Assert.True(AiQuestionIntent.WantsPracticeExamples("Show practice question Part 7"));
        Assert.True(AiQuestionIntent.IsWebsiteNavigation("Tôi thanh toán lại ở đâu?"));
    }
}
