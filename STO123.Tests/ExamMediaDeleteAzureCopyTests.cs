using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using STO123.Controllers;
using STO123.Models;

namespace STO123.Tests;

public sealed class ExamMediaDeleteAzureCopyTests
{
    [Fact]
    public async Task Delete_removes_source_blob_and_keeps_independent_legacy_media()
    {
        if (Environment.GetEnvironmentVariable("STO123_RUN_EXAM_AZURE_MEDIA_COPY_TEST") != "1") return;
        var connection = Environment.GetEnvironmentVariable("STO123_EXAM_AZURE_COPY_CONNECTION")
            ?? throw new InvalidOperationException("SQL copy connection missing.");
        var blobs = new BlobServiceClient(new Uri("https://ststo123audio.blob.core.windows.net"),
            new DefaultAzureCredential());
        var containerName = "sto123-exam-media-check-" + Guid.NewGuid().ToString("N")[..12];
        var testContainer = blobs.GetBlobContainerClient(containerName);
        await testContainer.CreateIfNotExistsAsync();
        const string sourcePath = "test-source.mp3";
        try
        {
            var original = blobs.GetBlobContainerClient("toeic-exam-media")
                .GetBlobClient("ETS 2023/test-01/part1/audio/Test 01_Part 1_1.mp3");
            var bytes = (await original.DownloadContentAsync()).Value.Content;
            await testContainer.GetBlobClient(sourcePath).UploadAsync(bytes);
            await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
                .UseSqlServer(connection).Options);
            var beforeAttempts = await db.KetQuaLamBai.CountAsync();
            await db.Database.ExecuteSqlRawAsync("UPDATE dbo.NguLieu SET DuongDanAudio='test-source.mp3' WHERE MaNguLieu=96");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT dbo.NhomLuotLam(MaKetQua,MaNguLieuGoc,MaPart,ThuTu,NoiDungNguLieu,DuongDanAudio)
                SELECT TOP (1) a.MaKetQua,96,p.MaPart,
                    ISNULL((SELECT MAX(g.ThuTu)+1 FROM dbo.NhomLuotLam g WHERE g.MaKetQua=a.MaKetQua),1),
                    N'Bản kiểm thử độc lập','test-source.mp3'
                FROM dbo.KetQuaLamBai a CROSS JOIN dbo.PartTOEIC p
                WHERE p.SoPart=3 ORDER BY a.MaKetQua;
                """);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["ExamMedia:ContainerName"] = containerName }).Build();
            var result = await new AdminExamMediaController(db, blobs, config)
                .Delete(sourcePath, CancellationToken.None);
            Assert.IsType<OkObjectResult>(result);
            Assert.False((await testContainer.GetBlobClient(sourcePath).ExistsAsync()).Value);
            var historical = await db.NhomLuotLam.AsNoTracking()
                .Where(g => g.NoiDungNguLieu == "Bản kiểm thử độc lập")
                .Select(g => g.DuongDanAudio).SingleAsync();
            Assert.StartsWith("exam-history-media/", historical);
            Assert.True((await testContainer.GetBlobClient(historical).ExistsAsync()).Value);
            Assert.Null(await db.NguLieu.AsNoTracking().Where(g => g.MaNguLieu == 96)
                .Select(g => g.DuongDanAudio).SingleAsync());
            Assert.Equal(beforeAttempts, await db.KetQuaLamBai.CountAsync());
        }
        finally { await testContainer.DeleteIfExistsAsync(); }
    }
}
