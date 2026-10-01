using System.Text.Json;
using STO123.Models;

namespace STO123.Services.Exam;

public static class ExamCore
{
    public static readonly IReadOnlyDictionary<int, int> PartCounts = new Dictionary<int, int>
    { [1] = 6, [2] = 25, [3] = 39, [4] = 30, [5] = 30, [6] = 16, [7] = 54 };
    public const int DurationMinutes = 120;
    public const string Active = "DANG_LAM";
    public const string Submitted = "DA_NOP";
    public const string Expired = "HET_GIO";
    public const string Mock = "MOCK";
    public const string Placement = "PLACEMENT";
    public const string Published = "XUAT_BAN";
    public const string Insufficient = "KHONG_DU_DU_LIEU_TAO_DE";
    public static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    public static int Minutes(DateTime start, DateTime end) =>
        Math.Max(0, (int)Math.Floor((end - start).TotalMinutes));
}

public sealed record ExamDocumentSnapshot(int Order, string Type, string? Content, string? ImagePath,
    string? Text = null);
public sealed record ExamDocuments(int Version, List<ExamDocumentSnapshot> Documents);

public static class ExamDocumentCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Types = ["TEXT", "EMAIL", "TABLE", "CHAT", "FORM", "IMAGE"];

    public static bool IsValidSource(NguLieuTaiLieu document) =>
        IsValid(document.LoaiTaiLieu, document.NoiDung, document.DuongDanAnh);

    private static bool IsValid(string? type, string? content, string? imagePath)
    {
        if (type is null || !Types.Contains(type)) return false;
        if (type == "IMAGE") return !string.IsNullOrWhiteSpace(imagePath);
        if (type == "TEXT") return !string.IsNullOrWhiteSpace(content);
        if (string.IsNullOrWhiteSpace(content)) return false;
        try
        {
            using var parsed = JsonDocument.Parse(content);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
                return false;
            return type switch
            {
                "EMAIL" => HasText(root, "body") &&
                    OptionalStrings(root, "to", "from", "date", "subject"),
                "TABLE" => OptionalStrings(root, "title") && ValidTable(root),
                "CHAT" => root.TryGetProperty("messages", out var messages) &&
                    messages.ValueKind == JsonValueKind.Array && messages.GetArrayLength() > 0 &&
                    messages.EnumerateArray().All(message => message.ValueKind == JsonValueKind.Object &&
                        HasText(message, "text") && OptionalStrings(message, "sender", "time")),
                "FORM" => OptionalStrings(root, "title") &&
                    root.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array &&
                    fields.EnumerateArray().All(field => field.ValueKind == JsonValueKind.Object &&
                        HasText(field, "label") && HasString(field, "value")),
                _ => false
            };
        }
        catch (JsonException) { return false; }
    }

    private static bool HasText(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString());

    private static bool HasString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String;

    private static bool OptionalStrings(JsonElement value, params string[] names) =>
        names.All(name => !value.TryGetProperty(name, out var property) ||
            property.ValueKind == JsonValueKind.String);

    private static bool ValidTable(JsonElement root)
    {
        if (!root.TryGetProperty("headers", out var headers) || headers.ValueKind != JsonValueKind.Array ||
            headers.GetArrayLength() == 0 || headers.EnumerateArray().Any(h => h.ValueKind != JsonValueKind.String) ||
            !root.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array) return false;
        return rows.EnumerateArray().All(row => row.ValueKind == JsonValueKind.Array &&
            row.GetArrayLength() == headers.GetArrayLength() &&
            row.EnumerateArray().All(cell => cell.ValueKind == JsonValueKind.String));
    }

    public static string Encode(IEnumerable<ExamDocumentSnapshot> documents) =>
        JsonSerializer.Serialize(new ExamDocuments(1, documents.OrderBy(x => x.Order).ToList()), Options);

    public static IReadOnlyList<ExamDocumentSnapshot> Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var root = JsonSerializer.Deserialize<ExamDocuments>(json, Options);
            if (root?.Version != 1 || root.Documents is null || root.Documents.Count == 0)
                return [Unavailable(1)];
            return root.Documents.Select(d => d is null ? Unavailable(1) :
                IsValid(d.Type, d.Content ?? d.Text, d.ImagePath) ? d with { Content = d.Content ?? d.Text } :
                Unavailable(d.Order)).OrderBy(x => x.Order).ToArray();
        }
        catch (JsonException) { return [Unavailable(1)]; }
    }

    private static ExamDocumentSnapshot Unavailable(int order) => new(order, "UNAVAILABLE", null, null);

    public static object? ToContent(ExamDocumentSnapshot document)
    {
        if (document.Type == "UNAVAILABLE") return null;
        if (document.Type is "TEXT" or "IMAGE") return document.Content;
        try { return JsonDocument.Parse(document.Content!).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }
}

public sealed class ExamProblem(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
