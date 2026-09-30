using System.Security.Claims;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Controllers;

[ApiController]
[Route("api/tu-vung")]
public class TuVungController : ControllerBase
{
    private const string Learning = "DANG_HOC";
    private const string Completed = "DA_THUOC";
    private static readonly string[] TrialWords =
    [
        "appointment", "meeting", "schedule", "manager", "department",
        "employee", "deadline", "document", "conference", "colleague"
    ];

    private readonly ToeicDbContext db;
    private readonly BlobContainerClient container;

    public TuVungController(ToeicDbContext db, BlobServiceClient blobServiceClient, IConfiguration configuration)
    {
        this.db = db;
        var containerName = configuration["AzureBlob:ContainerName"]
            ?? throw new InvalidOperationException("AzureBlob:ContainerName is missing.");
        container = blobServiceClient.GetBlobContainerClient(containerName);
    }

    [AllowAnonymous]
    [HttpGet("demo")]
    public async Task<IActionResult> GetDemo(CancellationToken ct)
    {
        var topicId = await db.ChuDe.AsNoTracking()
            .Where(x => x.TenChuDe == "Office Essentials" && x.TrangThai == "DANG_MO")
            .Select(x => (int?)x.MaChuDe).FirstOrDefaultAsync(ct);
        var query = db.TuVung.AsNoTracking().Where(x => TrialWords.Contains(x.TuVung1));
        if (topicId.HasValue)
            query = query.Where(x => x.TuVungChuDe.Any(link => link.MaChuDe == topicId.Value));
        var words = await query.Select(x => new WordDto(x.MaTuVung, x.TuVung1, x.PhienAm, x.LoaiTu,
            x.Nghia, x.ViDu, x.DichViDu, null,
            false, false)).ToListAsync(ct);
        if (words.Count != TrialWords.Length ||
            words.Select(x => x.Word).Distinct(StringComparer.OrdinalIgnoreCase).Count() != TrialWords.Length)
            return StatusCode(503, new { message = "Office Essentials trial is not fully configured." });
        return Ok(new { tenChuDe = "Office Essentials",
            words = TrialWords.Select(name => words.Single(x =>
                string.Equals(x.Word, name, StringComparison.OrdinalIgnoreCase))) });
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await db.TuVung.AsNoTracking().OrderBy(x => x.MaTuVung)
            .Select(x => new WordDto(x.MaTuVung, x.TuVung1, x.PhienAm, x.LoaiTu,
                x.Nghia, x.ViDu, x.DichViDu, null,
                !string.IsNullOrEmpty(x.DuongDanAudio),
                !string.IsNullOrEmpty(x.DuongDanAudioViDu))).ToListAsync(ct));

    [Authorize]
    [HttpGet("topics")]
    public async Task<IActionResult> GetTopics(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var topics = await db.ChuDe.AsNoTracking().Where(x => x.TrangThai == "DANG_MO")
            .OrderBy(x => x.MaChuDe)
            .Select(x => new
            {
                x.MaChuDe, x.TenChuDe, x.MoTa,
                Total = x.TuVungChuDe.Count(),
                Done = x.TuVungChuDe.Count(link => link.MaTuVungNavigation.TienDoTuVung
                    .Any(p => p.MaNguoiDung == userId && p.TrangThai == Completed)),
                LearningCount = x.TuVungChuDe.Count(link => link.MaTuVungNavigation.TienDoTuVung
                    .Any(p => p.MaNguoiDung == userId && p.TrangThai == Learning))
            }).ToListAsync(ct);
        return Ok(topics.Select(x => Summary(x.MaChuDe, x.TenChuDe, x.MoTa,
            x.Total, x.Done, x.LearningCount)));
    }

    [Authorize]
    [HttpGet("topics/{topicId:int}")]
    public async Task<IActionResult> GetTopic(int topicId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var topic = await db.ChuDe.AsNoTracking()
            .Where(x => x.MaChuDe == topicId && x.TrangThai == "DANG_MO")
            .Select(x => new { x.MaChuDe, x.TenChuDe, x.MoTa }).FirstOrDefaultAsync(ct);
        if (topic is null) return NotFound();
        var words = await db.TuVungChuDe.AsNoTracking().Where(x => x.MaChuDe == topicId)
            .OrderBy(x => x.MaTuVung)
            .Select(x => new WordDto(x.MaTuVung, x.MaTuVungNavigation.TuVung1,
                x.MaTuVungNavigation.PhienAm, x.MaTuVungNavigation.LoaiTu,
                x.MaTuVungNavigation.Nghia, x.MaTuVungNavigation.ViDu,
                x.MaTuVungNavigation.DichViDu,
                x.MaTuVungNavigation.TienDoTuVung.Where(p => p.MaNguoiDung == userId)
                    .Select(p => p.TrangThai).FirstOrDefault(),
                !string.IsNullOrEmpty(x.MaTuVungNavigation.DuongDanAudio),
                !string.IsNullOrEmpty(x.MaTuVungNavigation.DuongDanAudioViDu))).ToListAsync(ct);
        var done = words.Count(x => x.TrangThai == Completed);
        var learning = words.Count(x => x.TrangThai == Learning);
        return Ok(new { topic = Summary(topic.MaChuDe, topic.TenChuDe, topic.MoTa,
            words.Count, done, learning), words });
    }

    [Authorize]
    [HttpGet("topics/{topicId:int}/practice")]
    public async Task<IActionResult> GetPractice(int topicId, CancellationToken ct, [FromQuery] string mode = "all")
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (mode is not ("all" or "review"))
            return BadRequest(new { message = "Mode must be all or review." });

        var topic = await db.ChuDe.AsNoTracking()
            .Where(x => x.MaChuDe == topicId && x.TrangThai == "DANG_MO")
            .Select(x => new { x.MaChuDe, x.TenChuDe }).FirstOrDefaultAsync(ct);
        if (topic is null) return NotFound();

        var topicWords = await db.TuVungChuDe.AsNoTracking()
            .Where(x => x.MaChuDe == topicId).OrderBy(x => x.MaTuVung)
            .Select(x => new
            {
                x.MaTuVung,
                Word = x.MaTuVungNavigation.TuVung1,
                Meaning = x.MaTuVungNavigation.Nghia,
                TrangThai = x.MaTuVungNavigation.TienDoTuVung
                    .Where(p => p.MaNguoiDung == userId)
                    .Select(p => p.TrangThai).FirstOrDefault()
            }).ToListAsync(ct);

        // Review targets are filtered by this learner. Distractors may use any word
        // in the same topic, so even a one-word review set can have four options.
        return Ok(new
        {
            topic,
            words = topicWords.Where(x => mode == "all" || x.TrangThai == null || x.TrangThai == Learning)
                .Select(x => new PracticeWordDto(x.MaTuVung, x.Word, x.Meaning)),
            optionPool = topicWords.Select(x => new PracticeWordDto(x.MaTuVung, x.Word, x.Meaning))
        });
    }

    [Authorize]
    [HttpPut("{id:int}/progress")]
    public async Task<IActionResult> SetProgress(int id, [FromBody] ProgressRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (request.TrangThai is not (Learning or Completed))
            return BadRequest(new { message = "TrangThai must be DANG_HOC or DA_THUOC." });
        if (!await db.TuVung.AnyAsync(x => x.MaTuVung == id &&
            x.TuVungChuDe.Any(link => link.MaChuDeNavigation.TrangThai == "DANG_MO"), ct))
            return NotFound();

        var progress = await db.TienDoTuVung.FindAsync([userId, id], ct);
        if (progress is null)
        {
            progress = new TienDoTuVung { MaNguoiDung = userId, MaTuVung = id,
                TrangThai = request.TrangThai, CapNhatLuc = DateTime.UtcNow };
            db.TienDoTuVung.Add(progress);
        }
        else if (!request.OnlyIfNotStarted || progress.TrangThai != Completed)
        {
            progress.TrangThai = request.TrangThai;
            progress.CapNhatLuc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return Ok(new { maTuVung = id, trangThai = progress.TrangThai, capNhatLuc = progress.CapNhatLuc });
    }

    [Authorize]
    [HttpGet("{id:int}/audio")]
    public Task<IActionResult> GetAudio(int id, CancellationToken ct) => Audio(id, false, ct);

    [Authorize]
    [HttpGet("{id:int}/audio-vi-du")]
    public Task<IActionResult> GetExampleAudio(int id, CancellationToken ct) => Audio(id, true, ct);

    private async Task<IActionResult> Audio(int id, bool example, CancellationToken ct)
    {
        var word = await db.TuVung.AsNoTracking().Where(x => x.MaTuVung == id &&
            x.TuVungChuDe.Any(link => link.MaChuDeNavigation.TrangThai == "DANG_MO"))
            .Select(x => new { x.DuongDanAudio, x.DuongDanAudioViDu }).FirstOrDefaultAsync(ct);
        var path = example ? word?.DuongDanAudioViDu : word?.DuongDanAudio;
        if (string.IsNullOrWhiteSpace(path)) return NotFound();
        var blob = container.GetBlobClient(path);
        if (!await blob.ExistsAsync(ct)) return NotFound();
        return File(await blob.OpenReadAsync(cancellationToken: ct), "audio/mpeg", enableRangeProcessing: true);
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;

    private static TopicDto Summary(int id, string name, string? description, int total, int done, int learning) =>
        new(id, name, description, total, done, learning,
            total == 0 ? 0 : done * 100 / total,
            total > 0 && done == total ? "Hoàn thành" :
                done > 0 ? "Đang học" : "Chưa học");

    public sealed record PracticeWordDto(int MaTuVung, string Word, string Meaning);
    public sealed record ProgressRequest(string TrangThai, bool OnlyIfNotStarted = false);
    public sealed record WordDto(int MaTuVung, string Word, string? Pronunciation,
        string? PartOfSpeech, string Meaning, string? Example, string? ExampleMeaning,
        string? TrangThai, bool HasAudio, bool HasExampleAudio);
    public sealed record TopicDto(int MaChuDe, string TenChuDe, string? MoTa,
        int TotalWords, int CompletedWords, int LearningWords, int ProgressPercent, string Status);
}