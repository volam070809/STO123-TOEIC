using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using STO123.Models;
using STO123.Services.Exam;
using STO123.Controllers;

namespace STO123.Tests;

public sealed class ExamVersionAzureCopyTests
{
    [Fact]
    public async Task Deleted_source_group_can_be_replaced_from_current_snapshot_without_changing_old_version()
    {
        if (Environment.GetEnvironmentVariable("STO123_RUN_EXAM_AZURE_EDIT_COPY_TEST") != "1") return;
        var connection = Environment.GetEnvironmentVariable("STO123_EXAM_AZURE_COPY_CONNECTION")
            ?? throw new InvalidOperationException("Copy database connection is missing.");
        var key = Guid.NewGuid();
        var blobService = new BlobServiceClient(new Uri("https://ststo123audio.blob.core.windows.net"),
            new DefaultAzureCredential());
        var services = new ServiceCollection();
        services.AddLogging(); services.AddMemoryCache();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ExamMedia:ContainerName"] = "toeic-exam-media" }).Build());
        services.AddSingleton(blobService);
        services.AddDbContext<ToeicDbContext>(o => o.UseSqlServer(connection));
        services.AddScoped<ExamPartLookup>(); services.AddScoped<RandomStartProfiler>();
        services.AddScoped<ExamGenerationService>(); services.AddScoped<ExamVersionStore>();
        using var provider = services.BuildServiceProvider();
        try
        {
            VersionedExam old;
            int publisher;
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ToeicDbContext>();
                var store = scope.ServiceProvider.GetRequiredService<ExamVersionStore>();
                var version = await db.PhienBanDeThi.AsNoTracking().SingleAsync(v =>
                    v.MaDeThi == 1 && v.SoPhienBan == 1);
                old = await store.ReadAsync(version, CancellationToken.None);
                publisher = await db.DeThi.AsNoTracking().Where(e => e.MaDeThi == 1)
                    .Select(e => e.MaNguoiSoan).SingleAsync() ?? 1;
            }
            Assert.Contains(old.Questions, q => q.Id == 32);
            foreach (var id in new[] { 32, 33, 34 })
            {
                using var scope = provider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ToeicDbContext>();
                if (!await db.CauHoi.AnyAsync(q => q.MaCauHoi == id)) continue;
                var controller = new AdminQuestionBankController(db,
                    scope.ServiceProvider.GetRequiredService<ExamGenerationService>());
                var result = await controller.Delete(id, CancellationToken.None);
                Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
            }
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ToeicDbContext>();
                var store = scope.ServiceProvider.GetRequiredService<ExamVersionStore>();
                var selected = old.Questions.OrderBy(q => q.Order).Select(q => q.Id)
                    .SelectMany(id => id == 32 ? new[] { 274, 275, 276 } :
                        id is 33 or 34 ? Array.Empty<int>() : new[] { id }).ToArray();
                var beforeAttempts = await db.KetQuaLamBai.CountAsync();
                var v2 = await store.PublishAsync(1, 1, key, publisher, CancellationToken.None, selected);
                var snapshot = await store.ReadAsync(v2, CancellationToken.None);
                Assert.Equal(200, snapshot.Questions.Count);
                Assert.DoesNotContain(snapshot.Questions, q => q.Id is 32 or 33 or 34);
                Assert.Contains(snapshot.Questions, q => q.Id == 274);
                Assert.Equal(200, old.Questions.Count);
                Assert.Equal(beforeAttempts, await db.KetQuaLamBai.CountAsync());
                Assert.Equal("OPEN", await db.DeThi.AsNoTracking().Where(e => e.MaDeThi == 1)
                    .Select(e => e.TrangThai).SingleAsync());
                Assert.Equal(v2.MaPhienBanDeThi, await db.DeThi.AsNoTracking()
                    .Where(e => e.MaDeThi == 1).Select(e => e.MaPhienBanHienHanh).SingleAsync());
            }
        }
        finally
        {
            var container = blobService.GetBlobContainerClient("toeic-exam-media");
            await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None,
                prefix: $"exam-snapshots/1/v2/{key:N}-", cancellationToken: CancellationToken.None))
                await container.GetBlobClient(blob.Name).DeleteIfExistsAsync();
        }
    }

    [Fact]
    public async Task Published_version_is_immutable_and_retry_keeps_one_sql_row()
    {
        if (Environment.GetEnvironmentVariable("STO123_RUN_EXAM_AZURE_COPY_TEST") != "1") return;
        var connection = Environment.GetEnvironmentVariable("STO123_EXAM_AZURE_COPY_CONNECTION")
            ?? throw new InvalidOperationException("Copy database connection is missing.");
        var retainPublishedVersion = Environment.GetEnvironmentVariable("STO123_EXAM_AZURE_PERSIST_BASELINE") == "1";
        var key = Guid.NewGuid();
        var blobService = new BlobServiceClient(new Uri("https://ststo123audio.blob.core.windows.net"),
            new DefaultAzureCredential());
        var container = blobService.GetBlobContainerClient("toeic-exam-media");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ExamMedia:ContainerName"] = "toeic-exam-media" }).Build());
        services.AddSingleton(blobService);
        services.AddDbContext<ToeicDbContext>(o => o.UseSqlServer(connection));
        services.AddScoped<ExamPartLookup>();
        services.AddScoped<RandomStartProfiler>();
        services.AddScoped<ExamGenerationService>();
        services.AddScoped<ExamVersionStore>();
        using var provider = services.BuildServiceProvider();
        try
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ToeicDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<ExamVersionStore>();
            var before = await db.DeThi.AsNoTracking().Where(e => e.MaDeThi == 1)
                .Select(e => e.MaPhienBanHienHanh).SingleAsync();
            var publisher = await db.DeThi.AsNoTracking().Where(e => e.MaDeThi == 1)
                .Select(e => e.MaNguoiSoan).SingleAsync() ?? 1;
            var first = await store.PublishAsync(1, before, key, publisher, CancellationToken.None);
            var retry = await store.PublishAsync(1, before, key, publisher, CancellationToken.None);
            Assert.Equal(first.MaPhienBanDeThi, retry.MaPhienBanDeThi);
            Assert.Equal(1, await db.PhienBanDeThi.CountAsync(v => v.MaDeThi == 1 && v.KhoaYeuCau == key));
            var snapshot = await store.ReadAsync(first, CancellationToken.None);
            Assert.Equal(200, snapshot.Questions.Count);
            Assert.Equal(7, snapshot.Questions.Select(q => q.Part).Distinct().Count());
            Assert.All(snapshot.Groups.Where(g => g.Audio is not null),
                g => Assert.StartsWith("exam-snapshots/1/", g.Audio));
            Assert.Equal(first.MaPhienBanDeThi, await db.DeThi.AsNoTracking()
                .Where(e => e.MaDeThi == 1).Select(e => e.MaPhienBanHienHanh).SingleAsync());
        }
        finally
        {
            if (!retainPublishedVersion)
            {
                // This integration test only writes to a disposable SQL copy. Remove its exact
                // newly uploaded Blob prefix after the test, preserving every legacy file.
                var prefix = $"exam-snapshots/1/v1/{key:N}-";
                await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None,
                    prefix: prefix, cancellationToken: CancellationToken.None))
                    await container.GetBlobClient(blob.Name).DeleteIfExistsAsync();
            }
        }
    }
}
