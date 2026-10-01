using System.Text.Json;

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
    public const string Published = "XUAT_BAN";
    public const string Insufficient = "KHONG_DU_DU_LIEU_TAO_DE";
    public static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    public static int Minutes(DateTime start, DateTime end) =>
        Math.Max(0, (int)Math.Floor((end - start).TotalMinutes));
}

public sealed record ExamDocumentSnapshot(int Order, string Type, string? Text, string? ImagePath);
public sealed record ExamDocuments(int Version, List<ExamDocumentSnapshot> Documents);

public static class ExamDocumentCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Encode(IEnumerable<ExamDocumentSnapshot> documents) =>
        JsonSerializer.Serialize(new ExamDocuments(1, documents.OrderBy(x => x.Order).ToList()), Options);
    public static IReadOnlyList<ExamDocumentSnapshot> Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var root = JsonSerializer.Deserialize<ExamDocuments>(json, Options);
            if (root?.Version != 1 || root.Documents is null || root.Documents.Count is < 1 or > 3)
                throw new JsonException();
            return root.Documents.OrderBy(x => x.Order).ToArray();
        }
        catch (JsonException) { throw new ExamProblem("CONTEXT_UNAVAILABLE", "Không thể tải tài liệu của bài thi.", 409); }
    }
}

public sealed class ExamProblem(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
