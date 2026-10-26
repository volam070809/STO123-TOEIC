using STO123.DTOs.Admin;

namespace STO123.Services.Admin;

public static class AdminAnalyticsMath
{
    public static AdminComparisonDto Compare(decimal current, decimal previous) => new(current, previous,
        previous == 0 ? current == 0 ? 0 : null : Math.Round((double)((current - previous) / previous * 100m), 1));

    public static double? Accuracy(int correct, int answered) =>
        answered == 0 ? null : Math.Round(correct * 100d / answered, 1);
}
