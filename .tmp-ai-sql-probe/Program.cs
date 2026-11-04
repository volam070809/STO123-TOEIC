using Azure.Storage.Blobs;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using STO123.Models;
using STO123.Services;
using STO123.Services.Admin;
using STO123.Services.Ai;

var connectionString = Environment.GetEnvironmentVariable("STO123_SQL_PROBE");
if (string.IsNullOrWhiteSpace(connectionString)) throw new Exception("SQL probe connection missing");
var readOnly = new SqlConnectionStringBuilder(connectionString) { ApplicationIntent = ApplicationIntent.ReadOnly,
    ApplicationName = "STO123 AI read-only verification" }.ConnectionString;
await using var connection = new SqlConnection(readOnly);
await connection.OpenAsync();
static async Task<int> Count(SqlConnection connection, string sql)
{
    await using var command = new SqlCommand(sql, connection) { CommandTimeout = 15 };
    return Convert.ToInt32(await command.ExecuteScalarAsync());
}
var topics = await Count(connection, "SELECT COUNT(*) FROM dbo.ChuDe WHERE TrangThai='DANG_MO'");
var words = await Count(connection, "SELECT COUNT(DISTINCT v.MaTuVung) FROM dbo.TuVung v JOIN dbo.TuVungChuDe l ON l.MaTuVung=v.MaTuVung JOIN dbo.ChuDe c ON c.MaChuDe=l.MaChuDe WHERE c.TrangThai='DANG_MO'");
var courses = await Count(connection, "SELECT COUNT(*) FROM dbo.KhoaHoc WHERE TrangThai='DANG_MO'");
await connection.CloseAsync();

var options = new DbContextOptionsBuilder<ToeicDbContext>().UseSqlServer(readOnly).Options;
await using var db = new ToeicDbContext(options);
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
    ["LectureResources:ContainerName"] = "unused", ["ConnectionStrings:ToeicDb"] = readOnly
}).Build();
var storage = new LectureResourceStorage(
    new BlobServiceClient(new Uri("https://isolated.invalid.blob.core.windows.net")), config);
var pdf = new AiPdfTextService(db, storage, new MemoryCache(new MemoryCacheOptions()),
    TimeProvider.System, new ContentRevisionStore(config, TimeProvider.System),
    NullLogger<AiPdfTextService>.Instance);
var knowledge = new AiKnowledgeService(db, TimeProvider.System,
    new AiLearningContextService(db, TimeProvider.System), pdf);
var actualTopics = await knowledge.ResolveAsync(1, "Có bao nhiêu chủ đề từ vựng?", null, default);
var actualCourses = await knowledge.ResolveAsync(1, "Có bao nhiêu khóa học đang mở?", null, default);
if (actualTopics.ExactAnswer != $"Hiện có {topics} chủ đề từ vựng đang mở cho học viên.")
    throw new Exception("Topic count mismatch");
if (actualCourses.ExactAnswer != $"Hiện có {courses} khóa học đang mở trên STO123.")
    throw new Exception("Course count mismatch");
if (!actualTopics.Text.Contains($"{words} từ vựng riêng biệt"))
    throw new Exception("Word count mismatch");
Console.WriteLine($"SQL_COMPARE_PASS visible_topics={topics} visible_words={words} open_courses={courses}");
var geminiKey = Environment.GetEnvironmentVariable("STO123_GEMINI_PROBE");
if (!string.IsNullOrWhiteSpace(geminiKey))
{
    var aiConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["Gemini:ApiKey"] = geminiKey, ["Gemini:Model"] = "gemini-3.5-flash-lite"
    }).Build();
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
    var planner = new AiToeicService(client, aiConfig);
    var started = System.Diagnostics.Stopwatch.StartNew();
    var plan = await planner.PlanAsync("Tôi yếu Part 7, trong khóa đã mua có bài nào giúp cải thiện?",
        null, "Khóa học", default);
    Console.WriteLine(plan is null ? "GEMINI_PLAN_UNAVAILABLE" :
        $"GEMINI_PLAN domains={string.Join(',', plan.Domains.Order())} terms={string.Join(',', plan.Terms)} latency_ms={started.ElapsedMilliseconds}");
}
