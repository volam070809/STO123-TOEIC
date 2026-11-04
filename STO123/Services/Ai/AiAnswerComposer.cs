using System.Text.RegularExpressions;

namespace STO123.Services.Ai;

public static partial class AiAnswerComposer
{
    public static string Compose(string modelAnswer, IReadOnlyList<AiSource> sources)
    {
        // Navigation is supplied only by verified backend sources, never by model-generated URLs.
        var answer = MarkdownLink().Replace(modelAnswer, match => match.Groups[1].Value);
        answer = ExternalUrl().Replace(answer, "");
        answer = InternalRoute().Replace(answer, "").Trim();
        var links = sources.Where(source => Allowed(source.Url))
            .DistinctBy(source => source.Url).Take(5)
            .Select(source => $"- [{CleanTitle(source.Title)}]({source.Url})").ToArray();
        return links.Length == 0 ? answer : answer + "\n\nNguồn và trang liên quan trên STO123:\n" +
            string.Join("\n", links);
    }

    public static bool Allowed(string route) => AiWebsiteGuide.RegisteredLink(route) ||
        Regex.IsMatch(route, @"^/vocabulary\?topic=[1-9]\d*$|^/courses\?paymentId=[1-9]\d*$|^/courses/[1-9]\d*(?:/steps/[1-9]\d*)?$");

    private static string CleanTitle(string title)
    {
        var cleaned = title.Replace('[', ' ').Replace(']', ' ')
            .Replace('(', ' ').Replace(')', ' ').Replace('\n', ' ').Trim();
        return cleaned[..Math.Min(100, cleaned.Length)];
    }

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();
    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalUrl();
    [GeneratedRegex(@"(?<!\w)/(?:courses|vocabulary|practice|mock-test|placement|profile|register|login)(?:/[^\s,.!?]*)?(?:\?[^\s,.!?]*)?")]
    private static partial Regex InternalRoute();
}
