using STO123.Services.Exam;

namespace STO123.Tests;

public sealed class PracticeHistoryMetricsTests
{
    [Theory]
    [InlineData(2, 14, "14.3")]
    [InlineData(0, 14, "0")]
    [InlineData(0, 0, "0")]
    [InlineData(9, 9, "100")]
    public void AccuracyUsesAllQuestionOccurrences(int correct, int total, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            PracticeHistoryMetrics.Accuracy(correct, total));
}
