using STO123.Services.Exam;

namespace STO123.Tests;

public sealed class QuestionContentRulesTests
{
    [Theory]
    [InlineData(3, " ", null, null, "Nội dung câu hỏi")]
    [InlineData(4, null, null, null, "Nội dung câu hỏi")]
    [InlineData(5, "Question", " ", null, "Giải thích")]
    [InlineData(6, null, null, " ", "Bản dịch câu hỏi")]
    [InlineData(7, "", null, null, "Nội dung câu hỏi")]
    [InlineData(2, null, null, null, null)]
    [InlineData(1, null, null, null, null)]
    public void Required_fields_match_part_rules(int part, string? text, string? explanation,
        string? translation, string? expected)
    {
        Assert.Equal(expected, QuestionContentRules.InvalidField(part, text, explanation, translation));
    }

    [Fact]
    public void Part_six_translations_keep_each_question_independent()
    {
        var text = QuestionContentRules.FormatPart6(["Dịch câu 1", "Dịch câu 2", "Dịch câu 3", "Dịch câu 4"]);
        Assert.True(QuestionContentRules.ValidPart6(text));
        Assert.Equal("Dịch câu 1", QuestionContentRules.Part6Translation(text, 1));
        var edited = QuestionContentRules.ReplacePart6(text, 3, "Đã sửa câu 3");
        Assert.True(QuestionContentRules.ValidPart6(edited));
        Assert.Equal("Dịch câu 1", QuestionContentRules.Part6Translation(edited, 1));
        Assert.Equal("Đã sửa câu 3", QuestionContentRules.Part6Translation(edited, 3));
        Assert.False(QuestionContentRules.ValidPart6("[1] Có\n[2] Có\n[3] Có\n[4]  "));
        Assert.False(QuestionContentRules.ValidPart6("[1] Có\n[1] Trùng\n[3] Có\n[4] Có"));
    }
}
