namespace STO123.Services.Admin;

public static class AdminLearnerAnalyticsMath
{
    // Practice and exam scoring use every stored question occurrence as the denominator.
    public static decimal? Accuracy(int correct, int total) => total <= 0 ? null :
        Math.Round(100m * correct / total, 1);

    public static bool IsCompletedExam(string status) => status is "DA_NOP" or "HET_GIO";

    public static string VocabularyStatus(int total, int remembered, int learning) =>
        total > 0 && remembered == total ? "REMEMBERED" :
        remembered > 0 || learning > 0 ? "IN_PROGRESS" : "NOT_STARTED";
}
