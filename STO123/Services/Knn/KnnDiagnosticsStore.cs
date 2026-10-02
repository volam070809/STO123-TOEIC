using System.Collections.Concurrent;

namespace STO123.Services.Knn;

// Diagnostic votes are not columns in the Database-First schema. Cache only
// results calculated on submission; a process restart does not reclassify.
public sealed class KnnDiagnosticsStore
{
    private readonly ConcurrentDictionary<int, KnnClassificationResult> results = new();
    public void Set(int attemptId, KnnClassificationResult result) => results[attemptId] = result;
    public KnnClassificationResult? Get(int attemptId) =>
        results.TryGetValue(attemptId, out var result) ? result : null;
}
