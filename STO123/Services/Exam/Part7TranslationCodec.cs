using System.Text.Json;
using STO123.Models;

namespace STO123.Services.Exam;

// Stored in NguLieu.NoiDungDich. Text without this prefix remains a legacy translation.
public static class Part7TranslationCodec
{
    public const string Prefix = "P7DOCV1:";

    public static bool IsValid(string? value, IReadOnlyList<NguLieuTaiLieu> originals)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal)) return true;
        if (value.Length > 100_000) return false;
        try
        {
            using var parsed = JsonDocument.Parse(value[Prefix.Length..]);
            var root = parsed.RootElement;
            if (root.GetProperty("version").GetInt32() != 1 ||
                root.GetProperty("kind").GetString() != "PART7_TRANSLATIONS") return false;
            var translated = root.GetProperty("documents");
            if (translated.ValueKind != JsonValueKind.Array || translated.GetArrayLength() != originals.Count)
                return false;
            for (var index = 0; index < originals.Count; index++)
            {
                var item = translated[index];
                if (item.ValueKind == JsonValueKind.Null) continue;
                if (item.ValueKind != JsonValueKind.Object ||
                    item.GetProperty("type").GetString() != originals[index].LoaiTaiLieu) return false;
                var content = item.GetProperty("content");
                var type = originals[index].LoaiTaiLieu;
                if (type is "TEXT" or "IMAGE")
                {
                    if (content.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(content.GetString())) return false;
                    continue;
                }
                if (content.ValueKind != JsonValueKind.Object ||
                    content.GetProperty("version").GetInt32() != 1) return false;
                using var original = JsonDocument.Parse(originals[index].NoiDung!);
                var originalBody = original.RootElement;
                if (type == "EMAIL" && !AllStrings(content, "from", "to", "date", "subject", "body") ||
                    type == "TABLE" && (!OptionalString(content, "title") || !SameTableShape(originalBody, content) ||
                        !content.GetProperty("headers").EnumerateArray().All(x => x.ValueKind == JsonValueKind.String) ||
                        !content.GetProperty("rows").EnumerateArray().All(row =>
                            row.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String))) ||
                    type == "CHAT" && (!SameArrayLength(originalBody, content, "messages") ||
                        !content.GetProperty("messages").EnumerateArray().All(message =>
                            message.ValueKind == JsonValueKind.Object && AllStrings(message, "sender", "time", "text"))) ||
                    type == "FORM" && (!OptionalString(content, "title") ||
                        !SameArrayLength(originalBody, content, "fields") ||
                        !content.GetProperty("fields").EnumerateArray().All(field =>
                            field.ValueKind == JsonValueKind.Object && AllStrings(field, "label", "value")))) return false;
            }
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException
            or IndexOutOfRangeException or ArgumentOutOfRangeException)
        { return false; }
    }

    private static bool SameTableShape(JsonElement original, JsonElement translated)
    {
        var originalHeaders = original.GetProperty("headers");
        var translatedHeaders = translated.GetProperty("headers");
        var originalRows = original.GetProperty("rows");
        var translatedRows = translated.GetProperty("rows");
        if (originalHeaders.GetArrayLength() != translatedHeaders.GetArrayLength() ||
            originalRows.GetArrayLength() != translatedRows.GetArrayLength()) return false;
        for (var row = 0; row < originalRows.GetArrayLength(); row++)
            if (originalRows[row].GetArrayLength() != translatedRows[row].GetArrayLength()) return false;
        return true;
    }

    private static bool SameArrayLength(JsonElement original, JsonElement translated, string key) =>
        original.GetProperty(key).GetArrayLength() == translated.GetProperty(key).GetArrayLength();

    private static bool AllStrings(JsonElement value, params string[] keys) =>
        keys.All(key => value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String);

    private static bool OptionalString(JsonElement value, string key) =>
        !value.TryGetProperty(key, out var field) || field.ValueKind == JsonValueKind.String;
}
