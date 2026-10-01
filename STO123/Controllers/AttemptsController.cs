using System.Security.Claims;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Exam;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Controllers;

[Authorize(Roles = "HOC_VIEN")]
[ApiController]
[Route("api/attempts")]
public sealed class AttemptsController(ExamAttemptService attempts, ToeicDbContext db,
    BlobServiceClient blobs, IConfiguration configuration) : ControllerBase
{
    private int LearnerId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id :
        throw new ExamProblem("ATTEMPT_FORBIDDEN", "Phiên đăng nhập không hợp lệ.", 403);

    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(await action()); }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }

    [HttpGet("{id:int}")]
    public Task<IActionResult> Get(int id, CancellationToken ct) => Run(async () => await attempts.GetAsync(id, LearnerId, ct));

    [HttpPut("{id:int}/answers/{questionId:int}")]
    public Task<IActionResult> Save(int id, int questionId, SaveAnswerRequest request, CancellationToken ct) => Run(async () =>
    {
        await attempts.SaveAsync(id, questionId, LearnerId, request, ct);
        return new { saved = true };
    });

    [HttpPut("{id:int}/flags/{questionId:int}")]
    public Task<IActionResult> Flag(int id, int questionId, FlagRequest request, CancellationToken ct) => Run(async () =>
    {
        await attempts.FlagAsync(id, questionId, LearnerId, request.Flagged, ct);
        return new { saved = true };
    });

    [HttpPost("{id:int}/submit")]
    public Task<IActionResult> Submit(int id, CancellationToken ct) => Run(async () => await attempts.FinalizeAsync(id, LearnerId, false, ct));

    [HttpGet("{id:int}/result")]
    public Task<IActionResult> Result(int id, CancellationToken ct) => Run(async () => await attempts.ResultAsync(id, LearnerId, ct));

    [HttpGet("{id:int}/review")]
    public Task<IActionResult> Review(int id, CancellationToken ct) => Run(async () => await attempts.ReviewAsync(id, LearnerId, ct));

    [HttpGet("{id:int}/groups/{groupId:int}/{kind:regex(audio|image)}")]
    public async Task<IActionResult> Media(int id, int groupId, string kind, CancellationToken ct)
    {
        try
        {
            _ = await attempts.OwnedAsync(id, LearnerId, ct);
            var group = await db.NhomLuotLam.AsNoTracking().Include(g => g.MaPartNavigation)
                .FirstOrDefaultAsync(g => g.MaKetQua == id && g.MaNhomLuotLam == groupId, ct);
            if (group is null) throw new ExamProblem("MEDIA_NOT_AVAILABLE", "Không tìm thấy tệp media.", 404);
            var path = kind == "audio" ? group.DuongDanAudio : group.DuongDanAnh;
            if (string.IsNullOrWhiteSpace(path)) throw new ExamProblem("MEDIA_NOT_AVAILABLE", "Không tìm thấy tệp media.", 404);
            var containerName = configuration["ExamMedia:ContainerName"] ?? configuration["AzureBlob:ContainerName"];
            if (string.IsNullOrWhiteSpace(containerName)) throw new ExamProblem("MEDIA_NOT_AVAILABLE", "Media chưa được cấu hình.", 503);
            var blob = blobs.GetBlobContainerClient(containerName).GetBlobClient(path);
            var properties = await blob.GetPropertiesAsync(cancellationToken: ct);
            return File(await blob.OpenReadAsync(cancellationToken: ct), MediaType(path, properties.Value.ContentType), enableRangeProcessing: true);
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
        catch (RequestFailedException e) when (e.Status == 404) { return NotFound(new { code = "MEDIA_NOT_AVAILABLE", message = "Không tìm thấy tệp media." }); }
    }

    [HttpGet("{id:int}/groups/{groupId:int}/documents/{order:int}/image")]
    public async Task<IActionResult> DocumentImage(int id, int groupId, int order, CancellationToken ct)
    {
        try
        {
            _ = await attempts.OwnedAsync(id, LearnerId, ct);
            var group = await db.NhomLuotLam.AsNoTracking().Include(g => g.MaPartNavigation)
                .FirstOrDefaultAsync(g => g.MaKetQua == id && g.MaNhomLuotLam == groupId, ct);
            if (group is null)
                throw new ExamProblem("MEDIA_NOT_AVAILABLE", "Không tìm thấy hình ảnh.", 404);
            var document = ExamDocumentCodec.Decode(group.TaiLieuJson).FirstOrDefault(d => d.Order == order);
            if (string.IsNullOrWhiteSpace(document?.ImagePath))
                throw new ExamProblem("MEDIA_NOT_AVAILABLE", "Không tìm thấy hình ảnh.", 404);
            var containerName = configuration["ExamMedia:ContainerName"] ?? configuration["AzureBlob:ContainerName"];
            if (string.IsNullOrWhiteSpace(containerName)) throw new ExamProblem("MEDIA_NOT_AVAILABLE", "Media chưa được cấu hình.", 503);
            var blob = blobs.GetBlobContainerClient(containerName).GetBlobClient(document.ImagePath);
            var properties = await blob.GetPropertiesAsync(cancellationToken: ct);
            return File(await blob.OpenReadAsync(cancellationToken: ct), MediaType(document.ImagePath, properties.Value.ContentType), enableRangeProcessing: true);
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
        catch (RequestFailedException e) when (e.Status == 404) { return NotFound(new { code = "MEDIA_NOT_AVAILABLE", message = "Không tìm thấy hình ảnh." }); }
    }

    private static string MediaType(string path, string? storedType)
    {
        if (!string.IsNullOrWhiteSpace(storedType) && storedType != "application/octet-stream") return storedType;
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg", ".wav" => "audio/wav", ".ogg" => "audio/ogg",
            ".png" => "image/png", ".webp" => "image/webp", ".gif" => "image/gif",
            ".jpg" or ".jpeg" => "image/jpeg", _ => "application/octet-stream"
        };
    }
}

public sealed record FlagRequest(bool Flagged);
