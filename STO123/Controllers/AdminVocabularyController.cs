using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN_NOI_DUNG")]
[Route("api/admin/vocabulary")]
public sealed class AdminVocabularyController(ToeicDbContext db, BlobServiceClient blobs, IConfiguration config) : ControllerBase
{
    private BlobContainerClient Container => blobs.GetBlobContainerClient(config["AzureBlob:ContainerName"] ?? "vocabulary-audio");
    public sealed record TopicInput(string TenChuDe, string? MoTa, string? TrangThai);
    public sealed record WordInput(string Word, string Nghia, string? PhienAm, string? LoaiTu, string? ViDu, string? DichViDu, int[] TopicIds);
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static readonly HashSet<string> PartsOfSpeech = new(StringComparer.Ordinal) {
        "noun", "verb", "adjective", "adverb", "pronoun", "preposition", "conjunction",
        "interjection", "determiner", "article", "modal verb", "phrasal verb", "phrase", "idiom"
    };
    private static bool ValidPartOfSpeech(string? value, string? previous = null) =>
        Clean(value) is not { } selected || PartsOfSpeech.Contains(selected) || selected == previous;
    private static IActionResult Error(string message) => new BadRequestObjectResult(new { message });
    private static string SafeName(string name) => Regex.Replace(name.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD), "[^a-z0-9]+", "-").Trim('-') is { Length: > 0 } s ? s : "word";
    private async Task<string> Upload(IFormFile file, string path, string mime, CancellationToken ct)
    {
        var blob = Container.GetBlobClient(path);
        await using var stream = file.OpenReadStream();
        await blob.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = mime } }, ct);
        if (!await blob.ExistsAsync(ct)) throw new IOException("Không xác minh được tệp trên Azure Blob.");
        return path;
    }
    private async Task Cleanup(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (await db.ChuDe.AnyAsync(x => x.DuongDanAnhBia == path, ct) ||
            await db.TuVung.AnyAsync(x => x.DuongDanAudio == path || x.DuongDanAudioViDu == path, ct)) return;
        try { await Container.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: ct); }
        catch (Exception) { /* Orphan can be removed by a later maintenance pass; SQL remains authoritative. */ }
    }
    [HttpGet("topics")]
    public async Task<IActionResult> Topics(string? q, int page = 1, CancellationToken ct = default)
    {
        var query = db.ChuDe.AsNoTracking().Where(x => q == null || x.TenChuDe.Contains(q));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.MaChuDe).Skip((Math.Max(1, page) - 1) * 12).Take(12)
            .Select(x => new { x.MaChuDe, x.TenChuDe, x.MoTa, x.TrangThai, x.DuongDanAnhBia, wordCount = x.TuVungChuDe.Count }).ToListAsync(ct);
        return Ok(new { items, total });
    }
    [HttpGet("topic-options")]
    public async Task<IActionResult> TopicOptions(CancellationToken ct) => Ok(await db.ChuDe.AsNoTracking().OrderBy(x => x.TenChuDe).Select(x => new { x.MaChuDe, x.TenChuDe }).ToListAsync(ct));
    [HttpGet("word-options")]
    public async Task<IActionResult> WordOptions(string? q, int topicId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q)) return Ok(Array.Empty<object>());
        return Ok(await db.TuVung.AsNoTracking().Where(x => (x.TuVung1.Contains(q) || x.Nghia.Contains(q)) && !x.TuVungChuDe.Any(t => t.MaChuDe == topicId))
            .OrderBy(x => x.TuVung1).Take(30).Select(x => new { x.MaTuVung, word = x.TuVung1, x.Nghia }).ToListAsync(ct));
    }
    [HttpPost("topics")]
    public async Task<IActionResult> CreateTopic(TopicInput input, CancellationToken ct)
    {
        if (Clean(input.TenChuDe) is not { Length: <= 128 } name || Clean(input.MoTa)?.Length > 256) return Error("Tên hoặc mô tả không hợp lệ.");
        var topic = new ChuDe { TenChuDe = name, MoTa = Clean(input.MoTa), TrangThai = "DANG_MO" };
        db.ChuDe.Add(topic); await db.SaveChangesAsync(ct); return Ok(new { topic.MaChuDe });
    }
    [HttpPut("topics/{id:int}")]
    public async Task<IActionResult> EditTopic(int id, TopicInput input, CancellationToken ct)
    {
        var topic = await db.ChuDe.FindAsync([id], ct); if (topic == null) return NotFound();
        if (Clean(input.TenChuDe) is not { Length: <= 128 } name || Clean(input.MoTa)?.Length > 256 || input.TrangThai is not ("DANG_MO" or "DA_DONG")) return Error("Thông tin chủ đề không hợp lệ.");
        topic.TenChuDe = name; topic.MoTa = Clean(input.MoTa); topic.TrangThai = input.TrangThai;
        await db.SaveChangesAsync(ct); return NoContent();
    }
    [HttpDelete("topics/{id:int}")]
    public async Task<IActionResult> DeleteTopic(int id, CancellationToken ct)
    {
        var topic = await db.ChuDe.FindAsync([id], ct); if (topic == null) return NotFound();
        if (await db.TuVungChuDe.AnyAsync(x => x.MaChuDe == id, ct) || await db.LanLuyenTuVung.AnyAsync(x => x.MaChuDe == id, ct) || await db.BuocLoTrinh.AnyAsync(x => x.MaChuDe == id, ct))
            return Conflict(new { message = "Chủ đề có từ vựng, lịch sử hoặc tham chiếu khóa học. Hãy đóng chủ đề thay vì xóa." });
        var old = topic.DuongDanAnhBia; db.ChuDe.Remove(topic); await db.SaveChangesAsync(ct); await Cleanup(old, ct); return NoContent();
    }
    [HttpPost("topics/{id:int}/cover")]
    public async Task<IActionResult> Cover(int id, IFormFile file, CancellationToken ct)
    {
        var topic = await db.ChuDe.FindAsync([id], ct); if (topic == null) return NotFound();
        if (file == null || file.Length is < 4 or > 5_000_000 || file.ContentType != "image/jpeg" || !file.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) return Error("Chỉ nhận ảnh JPG tối đa 5 MB.");
        await using (var stream = file.OpenReadStream()) { var header = new byte[3]; if (await stream.ReadAsync(header, ct) != 3 || header[0] != 0xff || header[1] != 0xd8 || header[2] != 0xff) return Error("Tệp JPG không hợp lệ."); }
        var path = $"topic-images/topic-{id}/cover-{Guid.NewGuid():N}.jpg";
        await Upload(file, path, "image/jpeg", ct);
        var old = topic.DuongDanAnhBia; topic.DuongDanAnhBia = path;
        try { await db.SaveChangesAsync(ct); } catch { await Container.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: ct); throw; }
        await Cleanup(old, ct); return Ok(new { path });
    }
    [HttpDelete("topics/{id:int}/cover")]
    public async Task<IActionResult> DeleteCover(int id, CancellationToken ct)
    {
        var topic = await db.ChuDe.FindAsync([id], ct); if (topic == null) return NotFound();
        var old = topic.DuongDanAnhBia; topic.DuongDanAnhBia = null; await db.SaveChangesAsync(ct); await Cleanup(old, ct); return NoContent();
    }
    [AllowAnonymous]
    [HttpGet("topics/{id:int}/cover")]
    public async Task<IActionResult> GetCover(int id, CancellationToken ct)
    {
        var path = await db.ChuDe.AsNoTracking().Where(x => x.MaChuDe == id).Select(x => x.DuongDanAnhBia).FirstOrDefaultAsync(ct);
        if (path == null || !await Container.GetBlobClient(path).ExistsAsync(ct)) return NotFound();
        return File(await Container.GetBlobClient(path).OpenReadAsync(cancellationToken: ct), "image/jpeg");
    }
    [HttpGet("topics/{id:int}/words")]
    public async Task<IActionResult> Words(int id, string? q, string? filter, CancellationToken ct)
    {
        var query = db.TuVungChuDe.AsNoTracking().Where(x => x.MaChuDe == id).Select(x => x.MaTuVungNavigation);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => x.TuVung1.Contains(q) || x.Nghia.Contains(q));
        if (filter == "audio") query = query.Where(x => x.DuongDanAudio == null || x.DuongDanAudioViDu == null);
        if (filter == "info") query = query.Where(x => x.PhienAm == null || x.LoaiTu == null || x.ViDu == null || x.DichViDu == null);
        return Ok(await query.OrderBy(x => x.MaTuVung).Select(x => new { x.MaTuVung, word = x.TuVung1, x.Nghia, x.PhienAm, x.LoaiTu, x.ViDu, x.DichViDu, hasAudio = x.DuongDanAudio != null, hasExampleAudio = x.DuongDanAudioViDu != null, topicIds = x.TuVungChuDe.Select(t => t.MaChuDe) }).ToListAsync(ct));
    }
    private async Task<bool> ValidTopics(int[] ids, CancellationToken ct) => ids.Length > 0 && ids.Distinct().Count() == ids.Length && await db.ChuDe.CountAsync(x => ids.Contains(x.MaChuDe), ct) == ids.Length;
    [HttpPost("words")]
    public async Task<IActionResult> CreateWord(WordInput input, CancellationToken ct)
    {
        if (!Valid(input) || !ValidPartOfSpeech(input.LoaiTu) || !await ValidTopics(input.TopicIds, ct)) return Error("Loại từ, thông tin từ vựng hoặc chủ đề không hợp lệ.");
        var word = new TuVung { TuVung1 = input.Word.Trim(), Nghia = input.Nghia.Trim(), PhienAm = Clean(input.PhienAm), LoaiTu = Clean(input.LoaiTu), ViDu = Clean(input.ViDu), DichViDu = Clean(input.DichViDu) };
        foreach (var id in input.TopicIds) word.TuVungChuDe.Add(new TuVungChuDe { MaChuDe = id });
        db.TuVung.Add(word); await db.SaveChangesAsync(ct); return Ok(new { word.MaTuVung });
    }
    private static bool Valid(WordInput x) => Clean(x.Word) is { Length: <= 128 } && Clean(x.Nghia) is { Length: <= 256 } &&
        (Clean(x.PhienAm) is null or { Length: <= 128 }) && (Clean(x.LoaiTu) is null or { Length: <= 32 }) &&
        (Clean(x.ViDu) is null or { Length: <= 512 }) && (Clean(x.DichViDu) is null or { Length: <= 512 }) && x.TopicIds != null;
    [HttpPut("words/{id:int}")]
    public async Task<IActionResult> EditWord(int id, WordInput input, CancellationToken ct)
    {
        var word = await db.TuVung.Include(x => x.TuVungChuDe).FirstOrDefaultAsync(x => x.MaTuVung == id, ct); if (word == null) return NotFound();
        if (!Valid(input) || !ValidPartOfSpeech(input.LoaiTu, word.LoaiTu) || !await ValidTopics(input.TopicIds, ct)) return Error("Loại từ, thông tin từ vựng hoặc chủ đề không hợp lệ.");
        var removed = word.TuVungChuDe.Where(x => !input.TopicIds.Contains(x.MaChuDe)).ToList();
        if (removed.Count > 0 && await db.ChiTietLuyenTuVung.AnyAsync(x => x.MaTuVung == id && removed.Select(r => r.MaChuDe).Contains(x.MaChuDe), ct)) return Conflict(new { message = "Không thể gỡ chủ đề đã có lịch sử luyện tập." });
        word.TuVung1 = input.Word.Trim(); word.Nghia = input.Nghia.Trim(); word.PhienAm = Clean(input.PhienAm); word.LoaiTu = Clean(input.LoaiTu); word.ViDu = Clean(input.ViDu); word.DichViDu = Clean(input.DichViDu);
        db.TuVungChuDe.RemoveRange(removed);
        foreach (var topicId in input.TopicIds.Except(word.TuVungChuDe.Select(x => x.MaChuDe))) db.TuVungChuDe.Add(new TuVungChuDe { MaTuVung = id, MaChuDe = topicId });
        await db.SaveChangesAsync(ct); return NoContent();
    }
    [HttpPost("topics/{topicId:int}/words/{wordId:int}")]
    public async Task<IActionResult> Link(int topicId, int wordId, CancellationToken ct)
    {
        if (!await db.ChuDe.AnyAsync(x => x.MaChuDe == topicId, ct) || !await db.TuVung.AnyAsync(x => x.MaTuVung == wordId, ct)) return NotFound();
        if (!await db.TuVungChuDe.AnyAsync(x => x.MaChuDe == topicId && x.MaTuVung == wordId, ct)) { db.TuVungChuDe.Add(new TuVungChuDe { MaChuDe = topicId, MaTuVung = wordId }); await db.SaveChangesAsync(ct); }
        return NoContent();
    }
    [HttpDelete("topics/{topicId:int}/words/{wordId:int}")]
    public async Task<IActionResult> Unlink(int topicId, int wordId, CancellationToken ct)
    {
        var link = await db.TuVungChuDe.FindAsync([topicId, wordId], ct); if (link == null) return NotFound();
        if (await db.ChiTietLuyenTuVung.AnyAsync(x => x.MaChuDe == topicId && x.MaTuVung == wordId, ct)) return Conflict(new { message = "Từ này đã có lịch sử luyện tập trong chủ đề." });
        db.TuVungChuDe.Remove(link); await db.SaveChangesAsync(ct); return NoContent();
    }
    [HttpPost("words/{id:int}/audio/{kind}")]
    public async Task<IActionResult> SetAudio(int id, string kind, IFormFile file, CancellationToken ct)
    {
        var word = await db.TuVung.FindAsync([id], ct); if (word == null) return NotFound();
        if (kind is not ("word" or "example") || file == null || file.Length is < 4 or > 10_000_000 || !file.FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) return Error("Chỉ nhận MP3 tối đa 10 MB.");
        await using (var stream = file.OpenReadStream()) { var header = new byte[3]; if (await stream.ReadAsync(header, ct) != 3 || !(header[0] == 'I' && header[1] == 'D' && header[2] == '3') && !(header[0] == 0xff && (header[1] & 0xe0) == 0xe0)) return Error("Tệp MP3 không hợp lệ."); }
        var path = kind == "word" ? $"words/{SafeName(word.TuVung1)}-{Guid.NewGuid():N}.mp3" : $"examples/{SafeName(word.TuVung1)}-example-{Guid.NewGuid():N}.mp3";
        await Upload(file, path, "audio/mpeg", ct);
        var old = kind == "word" ? word.DuongDanAudio : word.DuongDanAudioViDu;
        if (kind == "word") word.DuongDanAudio = path; else word.DuongDanAudioViDu = path;
        try { await db.SaveChangesAsync(ct); } catch { await Container.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: ct); throw; }
        await Cleanup(old, ct); return Ok(new { path });
    }
    [HttpDelete("words/{id:int}/audio/{kind}")]
    public async Task<IActionResult> DeleteAudio(int id, string kind, CancellationToken ct)
    {
        var word = await db.TuVung.FindAsync([id], ct); if (word == null) return NotFound(); if (kind is not ("word" or "example")) return Error("Loại âm thanh không hợp lệ.");
        var old = kind == "word" ? word.DuongDanAudio : word.DuongDanAudioViDu;
        if (kind == "word") word.DuongDanAudio = null; else word.DuongDanAudioViDu = null;
        await db.SaveChangesAsync(ct); await Cleanup(old, ct); return NoContent();
    }
    [HttpGet("words/{id:int}/audio/{kind}")]
    public async Task<IActionResult> Audio(int id, string kind, CancellationToken ct)
    {
        if (kind is not ("word" or "example")) return NotFound();
        var word = await db.TuVung.AsNoTracking().FirstOrDefaultAsync(x => x.MaTuVung == id, ct);
        var path = kind == "word" ? word?.DuongDanAudio : word?.DuongDanAudioViDu;
        if (path == null || !await Container.GetBlobClient(path).ExistsAsync(ct)) return NotFound();
        return File(await Container.GetBlobClient(path).OpenReadAsync(cancellationToken: ct), "audio/mpeg", enableRangeProcessing: true);
    }
}
