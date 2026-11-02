using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using STO123.Controllers;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public sealed class ExamGroupDeleteAzureCopyTests
{
    [Fact]
    public async Task Group_delete_blocks_legacy_dependency_then_deletes_independent_source()
    {
        if (Environment.GetEnvironmentVariable("STO123_RUN_EXAM_AZURE_GROUP_DELETE_COPY_TEST") != "1") return;
        var connection = Environment.GetEnvironmentVariable("STO123_EXAM_AZURE_COPY_CONNECTION")
            ?? throw new InvalidOperationException("Copy database connection is missing.");
        var options = new DbContextOptionsBuilder<ToeicDbContext>().UseSqlServer(connection).Options;
        await using var db = new ToeicDbContext(options);
        var beforeAttempts = await db.KetQuaLamBai.CountAsync();
        var beforeQuestionHistory = await db.CauHoiLuotLam.CountAsync(q =>
            q.MaCauHoiGoc == 32 || q.MaCauHoiGoc == 33 || q.MaCauHoiGoc == 34);
        var beforeGroupHistory = await db.NhomLuotLam.CountAsync(g => g.MaNguLieuGoc == 32);
        var current = await db.PhienBanDeThi.AsNoTracking().SingleAsync(v => v.MaDeThi == 1 && v.SoPhienBan == 1);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ExamMedia:ContainerName"] = "toeic-exam-media" }).Build();
        var store = new ExamVersionStore(db,
            new BlobServiceClient(new Uri("https://ststo123audio.blob.core.windows.net"), new DefaultAzureCredential()),
            config, null!, new MemoryCache(new MemoryCacheOptions()));
        var original = await store.ReadAsync(current, CancellationToken.None);
        Assert.Contains(original.Questions, q => q.Id == 32);
        var controller = new AdminQuestionGroupDeleteController(db);
        var blocked = await controller.Delete(32, CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(blocked);
        Assert.Equal(3, await db.CauHoi.CountAsync(q => new[] { 32, 33, 34 }.Contains(q.MaCauHoi)));
        var result = await controller.Delete(96, CancellationToken.None);
        Assert.True(result is OkObjectResult,
            JsonSerializer.Serialize((result as ObjectResult)?.Value));
        Assert.Equal(beforeAttempts, await db.KetQuaLamBai.CountAsync());
        Assert.Equal(0, await db.CauHoi.CountAsync(q => new[] { 274, 275, 276 }.Contains(q.MaCauHoi)));
        Assert.Equal(0, await db.NguLieu.CountAsync(g => g.MaNguLieu == 96));
        Assert.Equal(0, await db.CauHoiDeThi.CountAsync(l => new[] { 274, 275, 276 }.Contains(l.MaCauHoi)));
        Assert.Equal(beforeQuestionHistory, await db.CauHoiLuotLam.CountAsync(q =>
            q.MaCauHoiGoc == 32 || q.MaCauHoiGoc == 33 || q.MaCauHoiGoc == 34));
        Assert.Equal(beforeGroupHistory, await db.NhomLuotLam.CountAsync(g => g.MaNguLieuGoc == 32));
        Assert.True(beforeQuestionHistory > 0 || beforeGroupHistory > 0);
        Assert.Equal("OPEN", await db.DeThi.AsNoTracking().Where(e => e.MaDeThi == 1)
            .Select(e => e.TrangThai).SingleAsync());
        var retained = await store.ReadAsync(current, CancellationToken.None);
        Assert.Equal(200, retained.Questions.Count);
        Assert.Contains(retained.Questions, q => q.Id == 32);
    }
}
