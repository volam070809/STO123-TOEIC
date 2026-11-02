using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using STO123.Controllers;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public sealed class AdminExamAzureReadTests
{
    [Fact]
    public async Task Live_admin_exam_queries_read_real_sql_and_snapshot_without_mutation()
    {
        if (Environment.GetEnvironmentVariable("STO123_RUN_EXAM_AZURE_READ_TEST") != "1") return;
        var connection = Environment.GetEnvironmentVariable("STO123_EXAM_AZURE_READ_CONNECTION")
            ?? throw new InvalidOperationException("Azure SQL connection is missing.");
        var services = new ServiceCollection();
        services.AddLogging(); services.AddMemoryCache();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ExamMedia:ContainerName"] = "toeic-exam-media" }).Build());
        services.AddSingleton(new BlobServiceClient(new Uri("https://ststo123audio.blob.core.windows.net"),
            new DefaultAzureCredential()));
        services.AddDbContext<ToeicDbContext>(o => o.UseSqlServer(connection));
        services.AddScoped<RandomStartProfiler>(); services.AddScoped<ExamPartLookup>();
        services.AddScoped<ExamGenerationService>(); services.AddScoped<ExamVersionStore>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ToeicDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<ExamVersionStore>();
        var generation = scope.ServiceProvider.GetRequiredService<ExamGenerationService>();
        var blobs = scope.ServiceProvider.GetRequiredService<BlobServiceClient>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        Assert.IsType<OkObjectResult>(await new AdminExamVersionsController(db, store)
            .Exams(CancellationToken.None));
        Assert.IsType<OkObjectResult>(await new AdminExamVersionsController(db, store)
            .CurrentSourceStatus(1, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await new AdminQuestionBankController(db, generation)
            .Candidates(3, null, 1, 20, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await new AdminQuestionBankController(db, generation)
            .List(5, "DE_THI", null, 1, 20, CancellationToken.None));
        var bankController = new AdminQuestionBankController(db, generation);
        var units = Assert.IsType<OkObjectResult>(await bankController.Units(3, "DE_THI", "33",
            "all", 1, 20, CancellationToken.None));
        using (var unitJson = JsonDocument.Parse(JsonSerializer.Serialize(units.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))))
        {
            var item = Assert.Single(unitJson.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(32, item.GetProperty("groupId").GetInt32());
            Assert.Equal(3, item.GetProperty("questionCount").GetInt32());
        }
        foreach (var (id, groupId, expected) in new[] { (32, 32, new[] { 32, 33, 34 }),
            (274, 96, new[] { 274, 275, 276 }) })
        {
            var detail = Assert.IsType<OkObjectResult>(await bankController.UnitDetail(id, CancellationToken.None));
            using var detailJson = JsonDocument.Parse(JsonSerializer.Serialize(detail.Value,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Assert.Equal(groupId, detailJson.RootElement.GetProperty("groupId").GetInt32());
            Assert.Equal(expected, detailJson.RootElement.GetProperty("questions").EnumerateArray()
                .Select(q => q.GetProperty("id").GetInt32()).ToArray());
            Assert.All(detailJson.RootElement.GetProperty("questions").EnumerateArray(), q =>
                Assert.False(string.IsNullOrWhiteSpace(q.GetProperty("correct").GetString())));
        }
        var sampleIds = await db.CauHoi.AsNoTracking()
            .Where(q => q.MaPartNavigation.SoPart == 5 || q.MaPartNavigation.SoPart == 6 ||
                q.MaPartNavigation.SoPart == 7)
            .GroupBy(q => q.MaPartNavigation.SoPart)
            .Select(g => g.Min(q => q.MaCauHoi)).ToArrayAsync();
        foreach (var id in sampleIds)
            Assert.IsType<OkObjectResult>(await bankController.UnitDetail(id, CancellationToken.None));
        var firstQuestion = await db.CauHoi.AsNoTracking().OrderBy(q => q.MaCauHoi)
            .Select(q => q.MaCauHoi).FirstAsync();
        Assert.IsType<OkObjectResult>(await new AdminQuestionAuthoringController(db, blobs, config, store)
            .Detail(firstQuestion, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await new AdminExamCriteriaController(db)
            .List(CancellationToken.None));
        var currentVersion = await db.PhienBanDeThi.AsNoTracking()
            .SingleAsync(v => v.MaDeThi == 1 && v.MaPhienBanDeThi ==
                db.DeThi.Where(e => e.MaDeThi == 1).Select(e => e.MaPhienBanHienHanh).Single());
        var snapshot = await store.ReadAsync(currentVersion, CancellationToken.None);
        var candidateUnits = await generation.BuildAdminCandidatesAsync(CancellationToken.None);
        var selectedLinks = snapshot.Questions.Select((q, index) => new CauHoiDeThi {
            MaCauHoi = q.Id, ThuTu = index + 1 }).ToArray();
        Assert.Equal(200, FixedExamPlan.Select(selectedLinks, candidateUnits)
            .Sum(unit => unit.Questions.Count));
        Assert.IsType<OkObjectResult>(await new AdminExamSourceMediaController(db, blobs, config, store)
            .List(3, null, 1, 20, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await new AdminExamMediaController(db, blobs, config)
            .List("ETS 2023/test-01/", null, 5, CancellationToken.None));

        var repeatedLearner = await db.KetQuaLamBai.AsNoTracking()
            .Where(a => a.MaDeThi == 1 && a.LoaiBaiLam == ExamCore.Mock &&
                (a.TrangThai == ExamCore.Submitted || a.TrangThai == ExamCore.Expired))
            .GroupBy(a => a.MaHocVien).Where(g => g.Count() > 1)
            .Select(g => (int?)g.Key).FirstOrDefaultAsync();
        if (repeatedLearner is { } learnerId)
        {
            var mock = new MockTestsController(db, null!, config) {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, learnerId.ToString())], "test"))
                } }
            };
            var history = Assert.IsType<OkObjectResult>(await mock.History(1, 10, CancellationToken.None));
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(history.Value,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        }
    }
}
