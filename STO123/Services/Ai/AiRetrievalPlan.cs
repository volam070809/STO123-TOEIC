using System.Text.Json;
using System.Text.RegularExpressions;

namespace STO123.Services.Ai;

public sealed record AiRetrievalPlan(IReadOnlySet<string> Domains, string[] Terms)
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) {
        "TOPICS", "VOCABULARY", "COURSES", "LESSONS", "PROGRESS",
        "PRACTICE_STATS", "EXAM_RESULTS", "PAYMENT", "PROFILE", "WEBSITE"
    };

    public bool Includes(string domain) => Domains.Contains(domain);

    public static AiRetrievalPlan? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var domains = root.GetProperty("domains").EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()!.ToUpperInvariant())
                .Where(Allowed.Contains).Take(6).ToHashSet(StringComparer.Ordinal);
            var terms = root.TryGetProperty("terms", out var termArray) &&
                termArray.ValueKind == JsonValueKind.Array ? termArray.EnumerateArray()
                    .Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString()?.Trim() ?? "")
                    .Where(value => value.Length is >= 2 and <= 40 &&
                        Regex.IsMatch(value, @"^[\p{L}\p{N}\s-]+$"))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray() : [];
            return new(domains, terms);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        { return null; }
    }
}
