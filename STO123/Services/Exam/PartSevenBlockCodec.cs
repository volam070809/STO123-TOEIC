using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace STO123.Services.Exam;

// Version 2 is stored in NguLieuTaiLieu.NoiDung. Existing version 1 rows stay readable.
public static partial class PartSevenBlockCodec
{
    private static readonly HashSet<string> DocumentTypes = ["TEXT", "EMAIL", "LETTER", "INVOICE",
        "RECEIPT", "ARTICLE", "WEB", "NOTICE", "AD", "POLICY", "TABLE", "SCHEDULE",
        "FORM", "SURVEY", "CHAT", "MEMO", "REPORT", "ORDER", "REVIEW", "MENU",
        "AGENDA", "BROCHURE", "POSTER", "CATALOG", "IMAGE"];
    private static readonly HashSet<string> TextKinds = ["HEADING", "PARAGRAPH", "BULLET", "NUMBERED"];

    public static bool IsDocumentType(string? type) => type is not null && DocumentTypes.Contains(type);

    public static bool IsV2(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        try
        {
            using var parsed = JsonDocument.Parse(content);
            return parsed.RootElement.ValueKind == JsonValueKind.Object &&
                parsed.RootElement.TryGetProperty("version", out var version) &&
                version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) && number == 2;
        }
        catch (JsonException) { return false; }
    }

    public static bool IsValid(string? type, string? content)
    {
        if (!IsDocumentType(type) || !IsV2(content) || content!.Length > 150_000) return false;
        try
        {
            using var parsed = JsonDocument.Parse(content);
            var root = parsed.RootElement;
            if (!root.TryGetProperty("blocks", out var blocks) || blocks.ValueKind != JsonValueKind.Array ||
                blocks.GetArrayLength() is < 1 or > 100) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var hasContent = false;
            var hasParagraph = false;
            var hasTableData = false;
            var hasImage = false;
            foreach (var block in blocks.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object ||
                    !block.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.String ||
                    !IdPattern().IsMatch(idValue.GetString()!) || !ids.Add(idValue.GetString()!) ||
                    !block.TryGetProperty("kind", out var kindValue) || kindValue.ValueKind != JsonValueKind.String)
                    return false;
                var kind = kindValue.GetString();
                if (TextKinds.Contains(kind!))
                {
                    if (!String(block, "text", out var text)) return false;
                    hasContent |= !string.IsNullOrWhiteSpace(text);
                    hasParagraph |= kind == "PARAGRAPH" && !string.IsNullOrWhiteSpace(text);
                }
                else if (kind == "META")
                {
                    if (!String(block, "label", out _) || !String(block, "value", out var value)) return false;
                    hasContent |= !string.IsNullOrWhiteSpace(value);
                }
                else if (kind == "TABLE")
                {
                    if (!Table(block)) return false;
                    hasTableData |= block.GetProperty("rows").EnumerateArray().Any(row =>
                        row.EnumerateArray().Any(cell => !string.IsNullOrWhiteSpace(cell.GetString())));
                    hasContent |= hasTableData;
                }
                else if (kind == "IMAGE")
                {
                    if (!String(block, "path", out var path) || !SafeImagePath(path) ||
                        !String(block, "caption", out _) || !String(block, "align", out var align) ||
                        align is not ("left" or "center" or "right") ||
                        !block.TryGetProperty("width", out var width) || width.ValueKind != JsonValueKind.Number ||
                        !width.TryGetInt32(out var size) || size is < 20 or > 100) return false;
                    hasContent = true;
                    hasImage = true;
                }
                else if (kind != "SEPARATOR") return false;
            }
            if (type == "EMAIL") return hasParagraph;
            if (type == "IMAGE") return hasImage;
            if (type is "TABLE" or "INVOICE" or "RECEIPT" or "ORDER" or "SCHEDULE" or "MENU" or "CATALOG")
                return hasTableData && blocks.EnumerateArray().Where(block =>
                    block.GetProperty("kind").GetString() == "TABLE").All(block =>
                    block.GetProperty("headers").EnumerateArray().All(header =>
                        !string.IsNullOrWhiteSpace(header.GetString())));
            return hasContent;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        { return false; }
    }

    private static bool String(JsonElement element, string key, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(key, out var field) || field.ValueKind != JsonValueKind.String) return false;
        value = field.GetString();
        return value is { Length: <= 20_000 };
    }

    private static bool Table(JsonElement block)
    {
        if (!block.TryGetProperty("headers", out var headers) || headers.ValueKind != JsonValueKind.Array ||
            headers.GetArrayLength() is < 1 or > 12 || headers.EnumerateArray().Any(cell =>
                cell.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(cell.GetString()) ||
                cell.GetString()!.Length > 2_000) ||
            !block.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array ||
            rows.GetArrayLength() is < 1 or > 100) return false;
        return rows.EnumerateArray().All(row => row.ValueKind == JsonValueKind.Array &&
            row.GetArrayLength() == headers.GetArrayLength() && row.EnumerateArray().All(cell =>
                cell.ValueKind == JsonValueKind.String && cell.GetString()!.Length <= 2_000));
    }

    private static bool SafeImagePath(string? path) => path is { Length: > 0 and <= 512 } &&
        !path.StartsWith('/') && !path.Contains("..", StringComparison.Ordinal) && !path.Contains('\\') &&
        !path.Any(char.IsControl) && Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp";

    public static IReadOnlyList<string> ImagePaths(string? content)
    {
        if (!IsV2(content)) return [];
        using var parsed = JsonDocument.Parse(content!);
        return parsed.RootElement.GetProperty("blocks").EnumerateArray()
            .Where(block => block.GetProperty("kind").GetString() == "IMAGE")
            .Select(block => block.GetProperty("path").GetString()!).ToArray();
    }

    public static string? ImagePath(string? content, string id)
    {
        if (!IsV2(content) || !IdPattern().IsMatch(id)) return null;
        using var parsed = JsonDocument.Parse(content!);
        foreach (var block in parsed.RootElement.GetProperty("blocks").EnumerateArray())
            if (block.GetProperty("id").GetString() == id && block.GetProperty("kind").GetString() == "IMAGE")
                return block.GetProperty("path").GetString();
        return null;
    }

    public static string ArchiveImages(string content, IReadOnlyDictionary<string, string> mapped)
    {
        var root = JsonNode.Parse(content)!.AsObject();
        foreach (var block in root["blocks"]!.AsArray())
            if (block!["kind"]!.GetValue<string>() == "IMAGE")
                block["path"] = mapped[block["path"]!.GetValue<string>()];
        return root.ToJsonString();
    }

    public static object ToClientContent(string content, int attemptId, int groupId, int order)
    {
        var root = JsonNode.Parse(content)!.AsObject();
        root.Remove("legacyV1");
        foreach (var block in root["blocks"]!.AsArray())
            if (block!["kind"]!.GetValue<string>() == "IMAGE")
            {
                block.AsObject().Remove("path");
                block["imageUrl"] = $"/api/attempts/{attemptId}/groups/{groupId}/documents/{order}/images/{block["id"]!.GetValue<string>()}";
            }
        return root;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
