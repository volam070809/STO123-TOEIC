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
        if (listeningTotal is < 1 or > 100 || readingTotal is < 1 or > 100 ||
            listeningCorrect < 0 || listeningCorrect > listeningTotal ||
            readingCorrect < 0 || readingCorrect > readingTotal)
            throw new ArgumentOutOfRangeException(nameof(listeningTotal), "Each TOEIC section requires valid answered and total counts.");
        static int Scale(int correct, int total) => Math.Clamp(
            (int)Math.Round((5m + 490m * correct / total) / 5m, 0, MidpointRounding.AwayFromZero) * 5, 5, 495);
        var listening = Scale(listeningCorrect, listeningTotal);
        var reading = Scale(readingCorrect, readingTotal);
        return new(listening, reading, listening + reading, true);
    }
}
