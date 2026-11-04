using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace STO123.Services.Ai;

public sealed record AiSource(string Title, string Url);
public sealed record AiGrounding(string Text, IReadOnlyList<AiSource> Sources,
    bool UncheckedPractice = false, int? PracticePart = null);
public sealed record AiWebsiteFeature(string Route, string Label, string[] Roles,
    string Workflow, string[] Aliases, string Link);

// The frontend imports the same JSON embedded in the backend assembly. A route-registry
// regression test checks every learner-facing React route before deployment.
public static class AiWebsiteGuide
{
    public static IReadOnlyList<AiWebsiteFeature> Features { get; } = Load();

    public static (string Text, IReadOnlyList<AiSource> Sources) Resolve(string question)
    {
        var normalized = Normalize(question);
        var matches = Features.Where(feature => feature.Aliases.Any(alias =>
                normalized.Contains(Normalize(alias), StringComparison.Ordinal)))
            .Take(4).ToArray();
        if (matches.Length == 0) return ("", []);
        return ("Chức năng website đã xác minh:\n" +
            string.Join("\n", matches.Select(feature => $"{feature.Label}: {feature.Workflow}")),
            matches.Select(feature => new AiSource(feature.Label, feature.Link))
                .DistinctBy(source => source.Url).ToArray());
    }

    public static bool RegisteredLink(string link) => Features.Any(feature =>
        feature.Link == link && !feature.Link.Contains(':'));

    public static string Normalize(string value)
    {
        var source = value.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(source.Length);
        foreach (var c in source)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(c));
        return Regex.Replace(builder.ToString(), @"\s+", " ");
    }

    private static IReadOnlyList<AiWebsiteFeature> Load()
    {
        using var stream = typeof(AiWebsiteGuide).Assembly
            .GetManifestResourceStream("STO123.WebsiteFeatures.json") ??
            throw new InvalidOperationException("STO123 website feature registry is missing.");
        var features = JsonSerializer.Deserialize<AiWebsiteFeature[]>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        if (features.Length == 0 || features.GroupBy(x => x.Route).Any(g => g.Count() != 1) ||
            features.Any(x => string.IsNullOrWhiteSpace(x.Label) ||
                string.IsNullOrWhiteSpace(x.Workflow) || !x.Route.StartsWith('/') ||
                x.Route.StartsWith("/admin", StringComparison.Ordinal) ||
                x.Route.StartsWith("/api", StringComparison.Ordinal) ||
                x.Roles.Length == 0 || x.Roles.Any(role => role is not ("PUBLIC" or "HOC_VIEN"))))
            throw new InvalidOperationException("STO123 website feature registry is invalid.");
        var routes = features.Select(x => x.Route).ToHashSet(StringComparer.Ordinal);
        if (features.Any(x => !routes.Contains(x.Link) || x.Link.Contains(':')))
            throw new InvalidOperationException("STO123 website feature registry contains an unknown link.");
        return features;
    }
}
