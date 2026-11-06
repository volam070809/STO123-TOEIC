using STO123.Services.Arena;
using STO123.Services.Exam;

namespace STO123.Tests;

public sealed class ArenaQuestionPresenterTests
{
    private static ArenaQuestionSnapshot Question(int part, int? groupId = 42) => new(
        17, part, groupId, "Câu hỏi in hoặc lời nói", "Lời nói A", "Lời nói B", "Lời nói C",
        part == 2 ? null : "Lời nói D", "B", "Giải thích", "Lời thoại hoặc bài đọc",
        part <= 4 ? "audio.mp3" : null, part == 1 ? "photo.jpg" : null,
        [new(1, "TEXT", "Ngữ liệu in", null), new(2, "IMAGE", "", "visual.jpg")]);

    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 3)]
    public void ListeningChoicesAreNotSentBeforeReveal(int part, int count)
    {
        var view = ArenaQuestionPresenter.Present(Question(part), Guid.NewGuid(), 0, false);
        Assert.Null(view.Content);
        Assert.Null(view.A);
        Assert.Null(view.B);
        Assert.Null(view.C);
        Assert.Null(view.D);
        Assert.Null(view.Context);
        Assert.Null(view.Correct);
        Assert.Null(view.Explanation);
        Assert.Empty(view.Documents);
        Assert.Equal(count, view.OptionLetters.Count);
        Assert.NotNull(view.AudioUrl);
        if (part == 1) Assert.NotNull(view.ImageUrl);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void GroupedListeningKeepsPrintedChoicesAndVisualButHidesTranscript(int part)
    {
        var view = ArenaQuestionPresenter.Present(Question(part), Guid.NewGuid(), 0, false);
        Assert.NotNull(view.Content);
        Assert.NotNull(view.A);
        Assert.Null(view.Context);
        Assert.Null(view.Correct);
        Assert.Single(view.Documents);
        Assert.Equal("IMAGE", view.Documents[0].Type);
        Assert.Contains("/groups/42/audio", view.AudioUrl);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void ReadingPartsKeepPrintedMaterialAndOptions(int part)
    {
        var view = ArenaQuestionPresenter.Present(Question(part), Guid.NewGuid(), 0, false);
        Assert.Equal("Câu hỏi in hoặc lời nói", view.Content);
        Assert.Equal("Lời nói A", view.A);
        Assert.Equal("Lời thoại hoặc bài đọc", view.Context);
        Assert.Equal(2, view.Documents.Count);
        Assert.Null(view.Correct);
    }

    [Fact]
    public void AnswerIsSentOnlyDuringReveal()
    {
        var before = ArenaQuestionPresenter.Present(Question(2), Guid.NewGuid(), 0, false);
        var after = ArenaQuestionPresenter.Present(Question(2), Guid.NewGuid(), 0, true);
        Assert.Null(before.Correct);
        Assert.Equal("B", after.Correct);
        Assert.Equal("Giải thích", after.Explanation);
    }
}
