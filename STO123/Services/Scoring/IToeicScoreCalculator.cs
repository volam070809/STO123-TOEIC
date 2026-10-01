namespace STO123.Services.Scoring;

public sealed record ToeicScaledScore(int ListeningScore, int ReadingScore, int TotalScore, bool IsEstimated);

public interface IToeicScoreCalculator
{
    ToeicScaledScore Calculate(int listeningCorrect, int listeningTotal, int readingCorrect, int readingTotal);
}

public sealed class EstimatedLinearToeicScoreCalculator : IToeicScoreCalculator
{
    public ToeicScaledScore Calculate(int listeningCorrect, int listeningTotal, int readingCorrect, int readingTotal)
    {
        if (listeningTotal != 100 || readingTotal != 100 || listeningCorrect is < 0 or > 100 || readingCorrect is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(listeningTotal), "A full TOEIC score requires two valid 100-question sections.");
        static int Scale(int correct) => Math.Clamp(
            (int)Math.Round((5m + correct * 4.9m) / 5m, 0, MidpointRounding.AwayFromZero) * 5, 5, 495);
        var listening = Scale(listeningCorrect);
        var reading = Scale(readingCorrect);
        return new(listening, reading, listening + reading, true);
    }
}
