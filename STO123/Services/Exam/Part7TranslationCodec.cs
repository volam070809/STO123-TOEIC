using System.Text.Json;
using STO123.Models;

namespace STO123.Services.Exam;

// Stored in NguLieu.NoiDungDich. Text without this prefix remains a legacy translation.
public static class Part7TranslationCodec
{
    public const string Prefix = "P7DOCV1:";
    public const string PrefixV2 = "P7DOCV2:";

    public static bool IsValid(string? value, IReadOnlyList<NguLieuTaiLieu> originals)
    {
        if (value?.StartsWith(PrefixV2, StringComparison.Ordinal) == true)
            return IsValidV2(value, originals);
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal)) return true;
        if (originals.Any(d => PartSevenBlockCodec.IsV2(d.NoiDung))) return false;
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
                            field.ValueKind == JsonValueKind.Object && AllStrings(field, "label", "value"))) ||
                    ExamDocumentCodec.IsLayoutType(type) && !SameLayoutShape(originalBody, content, type)) return false;
            }
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException
            or IndexOutOfRangeException or ArgumentOutOfRangeException)
        { return false; }
    }

    private static bool IsValidV2(string value, IReadOnlyList<NguLieuTaiLieu> originals)
    {
        if (value.Length > 150_000) return false;
        try
        {
            using var parsed = JsonDocument.Parse(value[PrefixV2.Length..]);
            var root = parsed.RootElement;
            if (root.GetProperty("version").GetInt32() != 2 ||
                root.GetProperty("kind").GetString() != "PART7_TRANSLATIONS" ||
                root.TryGetProperty("legacyText", out var legacyText) &&
                legacyText.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return false;
            var translated = root.GetProperty("documents");
            if (translated.ValueKind != JsonValueKind.Array || translated.GetArrayLength() != originals.Count)
                return false;
            for (var index = 0; index < originals.Count; index++)
            {
                if (!PartSevenBlockCodec.IsV2(originals[index].NoiDung)) return false;
                using var source = JsonDocument.Parse(originals[index].NoiDung!);
                var sourceBlocks = source.RootElement.GetProperty("blocks");
                var item = translated[index];
                if (item.ValueKind != JsonValueKind.Object ||
                    item.GetProperty("type").GetString() != originals[index].LoaiTaiLieu) return false;
                var translatedBlocks = item.GetProperty("blocks");
                if (translatedBlocks.ValueKind != JsonValueKind.Array ||
                    translatedBlocks.GetArrayLength() != sourceBlocks.GetArrayLength()) return false;
                for (var blockIndex = 0; blockIndex < sourceBlocks.GetArrayLength(); blockIndex++)
                {
                    var original = sourceBlocks[blockIndex];
                    var translatedBlock = translatedBlocks[blockIndex];
                    if (translatedBlock.ValueKind != JsonValueKind.Object ||
                        translatedBlock.GetProperty("id").GetString() != original.GetProperty("id").GetString() ||
                        translatedBlock.GetProperty("kind").GetString() != original.GetProperty("kind").GetString())
                        return false;
                    var kind = original.GetProperty("kind").GetString();
                    if (kind is "HEADING" or "PARAGRAPH" or "BULLET" or "NUMBERED" &&
                        !AllStrings(translatedBlock, "text") ||
                        kind == "META" && !AllStrings(translatedBlock, "label", "value") ||
                        kind == "IMAGE" && !AllStrings(translatedBlock, "caption") ||
                        kind == "TABLE" && (!SameTableShape(original, translatedBlock) ||
                            translatedBlock.GetProperty("headers").EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String) ||
                            translatedBlock.GetProperty("rows").EnumerateArray().Any(row =>
                                row.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String)))) return false;
                }
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

    private static bool SameLayoutShape(JsonElement original, JsonElement translated, string type) =>
        AllStrings(translated, "title", "subtitle", "footer") &&
        SameArrayLength(original, translated, "metadata") &&
        translated.GetProperty("metadata").EnumerateArray().All(item =>
            item.ValueKind == JsonValueKind.Object && AllStrings(item, "label", "value")) &&
        SameArrayLength(original, translated, "paragraphs") &&
        translated.GetProperty("paragraphs").EnumerateArray().All(item => item.ValueKind == JsonValueKind.String) &&
        SameArrayLength(original, translated, "items") &&
        translated.GetProperty("items").EnumerateArray().All(item => item.ValueKind == JsonValueKind.String) &&
        (type is not ("INVOICE" or "SCHEDULE") ||
            SameTableShape(original, translated) &&
            translated.GetProperty("headers").EnumerateArray().All(item => item.ValueKind == JsonValueKind.String) &&
            translated.GetProperty("rows").EnumerateArray().All(row => row.EnumerateArray().All(
                item => item.ValueKind == JsonValueKind.String)));

    private static bool AllStrings(JsonElement value, params string[] keys) =>
        keys.All(key => value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String);

    private static bool OptionalString(JsonElement value, string key) =>
        !value.TryGetProperty(key, out var field) || field.ValueKind == JsonValueKind.String;
}
