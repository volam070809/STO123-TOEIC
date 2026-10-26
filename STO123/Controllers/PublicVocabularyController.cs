using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Vocabulary;
using STO123.Models;
using STO123.Services.Vocabulary;

namespace STO123.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/public/vocabulary")]
public sealed class PublicVocabularyController : ControllerBase
{
    private readonly ToeicDbContext db;
    private readonly BlobContainerClient container;

    public PublicVocabularyController(ToeicDbContext db, BlobServiceClient blobServiceClient,
        IConfiguration configuration)
    {
        this.db = db;
        var containerName = configuration["AzureBlob:ContainerName"]
            ?? throw new InvalidOperationException("AzureBlob:ContainerName is missing.");
        container = blobServiceClient.GetBlobContainerClient(containerName);
    }

    [HttpGet("topics")]
    public async Task<ActionResult<IReadOnlyList<PublicVocabularyTopicDto>>> Topics(CancellationToken ct)
    {
        var rows = await db.ChuDe.AsNoTracking()
            .Where(topic => topic.TrangThai == "DANG_MO")
            .OrderBy(topic => topic.MaChuDe)
            .Select(topic => new
            {
                topic.MaChuDe,
                topic.TenChuDe,
                topic.MoTa,
                TotalWords = topic.TuVungChuDe.Count
            })
            .ToListAsync(ct);
        return Ok(rows.Select(topic => new PublicVocabularyTopicDto(topic.MaChuDe, topic.TenChuDe,
            topic.MoTa, topic.TotalWords, Math.Min(topic.TotalWords, PublicVocabularyQueries.PreviewLimit)))
            .ToList());
    }

    [HttpGet("topics/{topicId:int}/preview")]
    public async Task<ActionResult<PublicVocabularyPreviewDto>> Preview(int topicId, CancellationToken ct)
    {
        var topic = await db.ChuDe.AsNoTracking()
            .Where(item => item.MaChuDe == topicId && item.TrangThai == "DANG_MO")
            .Select(item => new { item.MaChuDe, item.TenChuDe, item.MoTa,
                TotalWords = item.TuVungChuDe.Count })
            .SingleOrDefaultAsync(ct);
        if (topic is null) return NotFound(new { code = "VOCABULARY_TOPIC_NOT_FOUND" });

        var words = await PublicVocabularyQueries.PreviewLinks(db, topicId)
            .Select(link => new PublicVocabularyWordDto(
                link.MaTuVung,
                link.MaTuVungNavigation.TuVung1,
                link.MaTuVungNavigation.PhienAm,
                link.MaTuVungNavigation.LoaiTu,
                link.MaTuVungNavigation.Nghia,
                link.MaTuVungNavigation.ViDu,
                link.MaTuVungNavigation.DichViDu,
                !string.IsNullOrEmpty(link.MaTuVungNavigation.DuongDanAudio),
                !string.IsNullOrEmpty(link.MaTuVungNavigation.DuongDanAudioViDu)))
            .ToListAsync(ct);

        var summary = new PublicVocabularyTopicDto(topic.MaChuDe, topic.TenChuDe, topic.MoTa,
            topic.TotalWords, words.Count);
        return Ok(new PublicVocabularyPreviewDto(summary, words));
    }

    [HttpGet("topics/{topicId:int}/preview/{wordId:int}/audio")]
    public Task<IActionResult> Audio(int topicId, int wordId, CancellationToken ct) =>
        PreviewAudio(topicId, wordId, false, ct);

    [HttpGet("topics/{topicId:int}/preview/{wordId:int}/audio-example")]
    public Task<IActionResult> ExampleAudio(int topicId, int wordId, CancellationToken ct) =>
        PreviewAudio(topicId, wordId, true, ct);

    private async Task<IActionResult> PreviewAudio(int topicId, int wordId, bool example, CancellationToken ct)
    {
        var previewWordIds = PublicVocabularyQueries.PreviewLinks(db, topicId).Select(link => link.MaTuVung);
        var word = await db.TuVung.AsNoTracking()
            .Where(item => item.MaTuVung == wordId && previewWordIds.Contains(item.MaTuVung))
            .Select(item => new { item.DuongDanAudio, item.DuongDanAudioViDu })
            .SingleOrDefaultAsync(ct);
        var path = example ? word?.DuongDanAudioViDu : word?.DuongDanAudio;
        if (string.IsNullOrWhiteSpace(path)) return NotFound();

        var blob = container.GetBlobClient(path);
        if (!await blob.ExistsAsync(ct)) return NotFound();
        return File(await blob.OpenReadAsync(cancellationToken: ct), "audio/mpeg", enableRangeProcessing: true);
    }
}
