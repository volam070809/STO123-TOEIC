using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace STO123.Services.Ai;

public sealed record AiSource(string Title, string Url);
public sealed record AiGrounding(string Text, IReadOnlyList<AiSource> Sources,
    bool UncheckedPractice = false, int? PracticePart = null, string? ExactAnswer = null,
    string PageContextText = "");
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
        var matches = Features.Select(feature => new { feature,
                score = feature.Aliases.Where(alias => normalized.Contains(Normalize(alias),
                    StringComparison.Ordinal)).Select(alias => Normalize(alias).Length).DefaultIfEmpty().Max() })
            .Where(candidate => candidate.score > 0).OrderByDescending(candidate => candidate.score)
            .Select(candidate => candidate.feature)
            .Take(4).ToArray();
        if (matches.Length == 0) return ("", []);
        return ("Chức năng website đã xác minh:\n" +
            string.Join("\n", matches.Select(feature => $"{feature.Label}: {feature.Workflow}")),
            matches.Select(feature => new AiSource(feature.Label, feature.Link))
                .DistinctBy(source => source.Url).ToArray());
    }

    public static AiWebsiteFeature? ForRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route) || route.Length > 160 || !route.StartsWith('/') ||
            route.Contains('?') || route.Contains('#') || route.Contains("..")) return null;
        var actual = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Features.FirstOrDefault(feature =>
        {
            if (!feature.Roles.Contains("HOC_VIEN") && !feature.Roles.Contains("PUBLIC")) return false;
            var expected = feature.Route.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return expected.Length == actual.Length && expected.Zip(actual).All(pair =>
                pair.First.StartsWith(':') ? int.TryParse(pair.Second, out var id) && id > 0 :
                string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));
        });
    }

    public static string VerifiedCatalog(string question)
    {
        var published = Features.Where(feature => feature.Aliases.Length > 0 &&
            !feature.Route.Contains(':')).ToArray();
        var terms = Regex.Matches(Normalize(question), @"[\p{L}\p{N}]{3,}")
            .Select(match => match.Value).Where(term => term is not
                ("toi" or "ban" or "cach" or "lam" or "sao" or "the" or "nao" or "trang" or "cho"))
            .Distinct().ToArray();
        var detail = published.Select(feature => new { feature,
                score = terms.Count(term => Normalize(feature.Label + " " + feature.Workflow + " " +
                    string.Join(' ', feature.Aliases)).Contains(term, StringComparison.Ordinal)) })
            .OrderByDescending(candidate => candidate.score).Take(6)
            .Select(candidate => $"{candidate.feature.Label}: {candidate.feature.Workflow}");
        return "Các chức năng đã đăng ký: " + string.Join("; ", published.Select(feature =>
            $"{feature.Label} ({feature.Link})")) + ".\nQuy trình liên quan: " + string.Join("; ", detail) +
            ". Danh sách tóm tắt không phải bằng chứng rằng tính năng khác không tồn tại.";
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
