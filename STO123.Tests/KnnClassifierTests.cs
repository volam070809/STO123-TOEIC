using STO123.Services.Knn;

namespace STO123.Tests;

public class KnnClassifierTests
{
    private static KnnReferenceSample Sample(byte stage, double value) =>
        new(Enumerable.Repeat(value, 7).ToArray(), stage);
    private static decimal[] Vector(decimal value) => Enumerable.Repeat(value, 7).ToArray();

    [Fact]
    public void RuntimeDatasetLoadsAllSamples()
    {
        var path = Path.Combine(AppContext.BaseDirectory,
            "Data/Knn/knn_reference_runtime_v1.csv");
        Assert.Equal(7650, KnnClassifier.Load(path, 7).Count);
    }

    [Fact]
    public void InvalidRowIsRejected()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "P1,P2,P3,P4,P5,P6,P7,GiaiDoan\n101,1,1,1,1,1,1,2\n");
            Assert.Throws<InvalidDataException>(() => KnnClassifier.Load(path, 1));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExactSampleAndMajorityWin()
    {
        Assert.Equal((byte)3, KnnClassifier.Classify(new[] { Sample(3, 50),
            Sample(1, 90), Sample(2, 10) }, Vector(50), 1).Stage);
        var rows = new[] { Sample(3, 50), Sample(2, 51), Sample(2, 52), Sample(2, 53),
            Sample(1, 80), Sample(1, 81), Sample(3, 82) };
        var result = KnnClassifier.Classify(rows, Vector(50));
        Assert.Equal((byte)2, result.Stage);
        Assert.Equal(3, result.WinnerVotes);
        Assert.Equal(2, result.Stage3Votes);
    }

    [Fact]
    public void TiedVotesUseDistanceSumThenNearestNeighbor()
    {
        var sum = new[] { Sample(1, 49), Sample(2, 48), Sample(1, 47), Sample(2, 46) };
        Assert.Equal((byte)1, KnnClassifier.Classify(sum, Vector(50), 4).Stage);
        var nearest = new[] { Sample(1, 49), Sample(2, 51), Sample(2, 48), Sample(1, 52) };
        Assert.Equal((byte)1, KnnClassifier.Classify(nearest, Vector(50), 4).Stage);
    }

    [Fact]
    public void InvalidInputIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            KnnClassifier.Classify(new[] { Sample(1, 50) }, Vector(101), 1));
    }
}
