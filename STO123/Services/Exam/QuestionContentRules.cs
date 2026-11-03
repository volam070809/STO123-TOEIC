using System.Text.RegularExpressions;

namespace STO123.Services.Exam;

public static class QuestionContentRules
{
    public static string? InvalidField(int part, string? text, string? explanation, string? translation)
    {
        if (part is 3 or 4 or 5 or 7 && string.IsNullOrWhiteSpace(text))
            return "Nội dung câu hỏi";
        if (part == 5 && string.IsNullOrWhiteSpace(explanation))
            return "Giải thích";
        if (part == 6 && string.IsNullOrWhiteSpace(translation))
            return "Bản dịch câu hỏi";
        return null;
    }

    public static string FormatPart6(IReadOnlyList<string?> translations) =>
        string.Join("\n", translations.Select((value, index) => $"[{index + 1}] {value?.Trim()}"));

    public static string? Part6Translation(string? value, int order)
    {
        if (order is < 1 or > 4 || string.IsNullOrWhiteSpace(value)) return null;
        var lines = value.Replace("\r", "").Split('\n');
        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"^\s*\[(\d)\]\s*(.*)$");
            if (match.Success && int.Parse(match.Groups[1].Value) == order)
                return match.Groups[2].Value.Trim();
        }
        return null;
    }

    public static bool ValidPart6(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var lines = value.Replace("\r", "").Split('\n');
        if (lines.Length != 4) return false;
        return lines.Select((line, index) => Regex.Match(line, $@"^\s*\[{index + 1}\]\s*(\S.*)$"))
            .All(match => match.Success);
    }

    public static string? ReplacePart6(string? value, int order, string? translation)
    {
        if (!ValidPart6(value)) return null;
        var parts = Enumerable.Range(1, 4).Select(i =>
            i == order ? translation : Part6Translation(value, i)).ToArray();
        return parts.Any(string.IsNullOrWhiteSpace) ? null : FormatPart6(parts);
    }
}
