using System.Globalization;

namespace STO123.Services.Knn;

public sealed record KnnReferenceSample(double[] Features, byte Stage);
public sealed record KnnClassificationResult(byte Stage, string ModelVersion, int K,
    int Stage1Votes, int Stage2Votes, int Stage3Votes, int WinnerVotes);

public interface IKnnClassifier
{
    KnnClassificationResult Classify(IReadOnlyList<decimal> features);
}

public sealed class KnnClassifier : IKnnClassifier
{
    private readonly Lazy<IReadOnlyList<KnnReferenceSample>> samples;
    private readonly int k;
    private readonly string version;

    public KnnClassifier(IWebHostEnvironment environment, IConfiguration configuration)
    {
        k = configuration.GetValue("Knn:K", 7);
        version = configuration["Knn:ModelVersion"] ?? "KNN-STO123-v1";
        var relativePath = configuration["Knn:DatasetPath"] ?? "Data/Knn/knn_reference_runtime_v1.csv";
        if (Path.IsPathRooted(relativePath) || relativePath.Split('/', '\\').Contains(".."))
            throw new InvalidDataException("KNN dataset path must stay within the content root.");
        var path = Path.Combine(environment.ContentRootPath, relativePath);
        samples = new(() => Load(path, k));
    }

    public static IReadOnlyList<KnnReferenceSample> Load(string path, int k)
    {
        if (k < 1) throw new InvalidDataException("K must be positive.");
        using var reader = new StreamReader(path);
        if (reader.ReadLine()?.TrimStart('\uFEFF') != "P1,P2,P3,P4,P5,P6,P7,GiaiDoan")
            throw new InvalidDataException("Invalid KNN dataset header.");
        var result = new List<KnnReferenceSample>();
        string? line;
        var number = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            number++;
            var cells = line.Split(',');
            if (cells.Length != 8 || !byte.TryParse(cells[7], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var stage) || stage is < 1 or > 3)
                throw new InvalidDataException($"Invalid KNN row {number}.");
            var features = new double[7];
            for (var i = 0; i < 7; i++)
                if (!double.TryParse(cells[i], NumberStyles.Float, CultureInfo.InvariantCulture,
                        out features[i]) || !double.IsFinite(features[i]) || features[i] is < 0 or > 100)
                    throw new InvalidDataException($"Invalid KNN row {number}.");
            result.Add(new(features, stage));
        }
        if (result.Count < k) throw new InvalidDataException("KNN dataset has fewer than K samples.");
        return result;
    }

    public KnnClassificationResult Classify(IReadOnlyList<decimal> features) =>
        Classify(samples.Value, features, k, version);

    public static KnnClassificationResult Classify(IReadOnlyList<KnnReferenceSample> samples,
        IReadOnlyList<decimal> features, int k = 7, string version = "KNN-STO123-v1")
    {
        if (features.Count != 7 || features.Any(x => x is < 0 or > 100))
            throw new ArgumentOutOfRangeException(nameof(features), "Seven percentages in 0..100 are required.");
        if (k < 1 || samples.Count < k) throw new ArgumentOutOfRangeException(nameof(k));
        var nearest = samples.Select((sample, index) => new
            {
                sample.Stage, Index = index,
                Distance = Math.Sqrt(Enumerable.Range(0, 7)
                    .Sum(i => Math.Pow((double)features[i] - sample.Features[i], 2)))
            })
            .OrderBy(x => x.Distance).ThenBy(x => x.Index).Take(k).ToArray();
        var votes = Enumerable.Range(1, 3).Select(stage => new
            {
                Stage = (byte)stage, Count = nearest.Count(x => x.Stage == stage),
                Distance = nearest.Where(x => x.Stage == stage).Sum(x => x.Distance),
                First = Array.FindIndex(nearest, x => x.Stage == stage)
            }).ToArray();
        var winner = votes.OrderByDescending(x => x.Count).ThenBy(x => x.Distance)
            .ThenBy(x => x.First).First();
        return new(winner.Stage, version, k, votes[0].Count, votes[1].Count,
            votes[2].Count, winner.Count);
    }
}
