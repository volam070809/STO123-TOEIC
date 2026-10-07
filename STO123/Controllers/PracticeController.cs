using System.Security.Claims;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Practice;
using STO123.Models;
using STO123.Services.Practice;

namespace STO123.Controllers;

[Authorize(Roles = "HOC_VIEN")]
[ApiController]
[Route("api/practice")]
public sealed class PracticeController(
    PracticeService practiceService,
    ToeicDbContext db,
    BlobServiceClient blobs,
    IConfiguration configuration) : ControllerBase
{
    // =========================================================
    // START
    // =========================================================

    [HttpPost("start")]
    public async Task<IActionResult> Start(
        [FromBody] StartPracticeRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(userId, out var maHocVien))
        {
            return Unauthorized(new
            {
                code = "USER_NOT_FOUND",
                message = "Không xác định được học viên."
            });
        }

        try
        {
            var result = await practiceService.StartAsync(
                maHocVien,
                request,
                ct);

            return Ok(result);
        }
        catch (PracticeProblem ex)
        {
            return StatusCode(ex.StatusCode, new
            {
                code = ex.Code,
                message = ex.Message
            });
        }
    }

    // =========================================================
    // ANSWER
    // =========================================================

    [HttpPost("{maKetQua:int}/answer")]
    public async Task<IActionResult> Answer(
        int maKetQua,
        [FromBody] SubmitPracticeAnswerRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(userId, out var maHocVien))
        {
            return Unauthorized(new
            {
                code = "USER_NOT_FOUND",
                message = "Không xác định được học viên."
            });
        }

        try
        {
            var result = await practiceService.AnswerAsync(
                maHocVien,
                maKetQua,
                request,
                ct);

            return Ok(result);
        }
        catch (PracticeProblem ex)
        {
            return StatusCode(ex.StatusCode, new
            {
                code = ex.Code,
                message = ex.Message
            });
        }
    }

    // =========================================================
    // SUBMIT
    // =========================================================

    [HttpPost("{maKetQua:int}/submit")]
    public async Task<IActionResult> Submit(
        int maKetQua,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(userId, out var maHocVien))
        {
            return Unauthorized(new
            {
                code = "USER_NOT_FOUND",
                message = "Không xác định được học viên."
            });
        }

        try
        {
            var result = await practiceService.SubmitAsync(
                maHocVien,
                maKetQua,
                ct);

            return Ok(result);
        }
        catch (PracticeProblem ex)
        {
            return StatusCode(ex.StatusCode, new
            {
                code = ex.Code,
                message = ex.Message
            });
        }
    }

    // =========================================================
    // RESULT
    // =========================================================

    [HttpGet("{maKetQua:int}/result")]
    public async Task<IActionResult> Result(
        int maKetQua,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(userId, out var maHocVien))
        {
            return Unauthorized(new
            {
                code = "USER_NOT_FOUND",
                message = "Không xác định được học viên."
            });
        }

        try
        {
            var result = await practiceService.GetResultAsync(
                maHocVien,
                maKetQua,
                ct);

            return Ok(result);
        }
        catch (PracticeProblem ex)
        {
            return StatusCode(ex.StatusCode, new
            {
                code = ex.Code,
                message = ex.Message
            });
        }
    }

    // =========================================================
    // HISTORY
    // =========================================================

    [HttpGet("history")]
    public async Task<IActionResult> History(
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(userId, out var maHocVien))
        {
            return Unauthorized(new
            {
                code = "USER_NOT_FOUND",
                message = "Không xác định được học viên."
            });
        }

        try
        {
            var result = await practiceService.GetHistoryAsync(
                maHocVien,
                ct);

            return Ok(result);
        }
        catch (PracticeProblem ex)
        {
            return StatusCode(ex.StatusCode, new
            {
                code = ex.Code,
                message = ex.Message
            });
        }
    }

    // =========================================================
    // BOOKMARK
    // =========================================================

    [HttpPost("{maKetQua:int}/bookmark")]
    public async Task<IActionResult> Bookmark(
        int maKetQua,
        [FromBody] BookmarkPracticeQuestionRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(userId, out var maHocVien))
        {
            return Unauthorized(new
            {
                code = "USER_NOT_FOUND",
                message = "Không xác định được học viên."
            });
        }

        try
        {
            var result = await practiceService.BookmarkAsync(
                maHocVien,
                maKetQua,
                request,
                ct);

            return Ok(result);
        }
        catch (PracticeProblem ex)
        {
            return StatusCode(ex.StatusCode, new
            {
                code = ex.Code,
                message = ex.Message
            });
        }
    }

    // =========================================================
    // AUDIO / IMAGE
    // =========================================================

    [HttpGet(
        "{maKetQua:int}/groups/{maNhomLuotLam:int}/{kind:regex(audio|image)}")]
    public async Task<IActionResult> Media(
        int maKetQua,
        int maNhomLuotLam,
        string kind,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(userId, out var maHocVien))
        {
            return Unauthorized(new
            {
                code = "USER_NOT_FOUND",
                message = "Không xác định được học viên."
            });
        }

        try
        {
            // =====================================================
            // 1. KIỂM TRA PHIÊN LUYỆN TẬP
            // =====================================================

            var ketQua = await db.KetQuaLamBai
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.MaKetQua == maKetQua &&
                        x.MaHocVien == maHocVien &&
                        x.LoaiBaiLam == "PRACTICE",
                    ct);

            if (ketQua is null)
            {
                throw new PracticeProblem(
                    "PRACTICE_NOT_FOUND",
                    "Không tìm thấy phiên luyện tập.",
                    404);
            }

            // =====================================================
            // 2. LẤY NHÓM CÂU HỎI
            // =====================================================

            var group = await db.NhomLuotLam
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.MaKetQua == maKetQua &&
                        x.MaNhomLuotLam == maNhomLuotLam,
                    ct);

            if (group is null)
            {
                throw new PracticeProblem(
                    "MEDIA_NOT_AVAILABLE",
                    "Không tìm thấy nhóm câu hỏi.",
                    404);
            }

            // =====================================================
            // 3. LẤY PATH AUDIO / IMAGE
            // =====================================================

            var path = kind == "audio"
                ? group.DuongDanAudio
                : group.DuongDanAnh;

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new PracticeProblem(
                    "MEDIA_NOT_AVAILABLE",
                    "Không tìm thấy tệp media.",
                    404);
            }

            // =====================================================
            // 4. LẤY CONTAINER AZURE BLOB
            // =====================================================

            var containerName =
                configuration["ExamMedia:ContainerName"]
                ?? configuration["AzureBlob:ContainerName"];

            if (string.IsNullOrWhiteSpace(containerName))
            {
                throw new PracticeProblem(
                    "MEDIA_NOT_AVAILABLE",
                    "Media chưa được cấu hình.",
                    503);
            }

            // =====================================================
            // 5. LẤY BLOB
            // =====================================================

            var container =
                blobs.GetBlobContainerClient(containerName);

            var blob =
                container.GetBlobClient(path);

            var properties =
                await blob.GetPropertiesAsync(
                    cancellationToken: ct);

            // =====================================================
            // 6. TRẢ FILE
            // =====================================================

            return File(
                await blob.OpenReadAsync(
                    cancellationToken: ct),
                MediaType(
                    path,
                    properties.Value.ContentType),
                enableRangeProcessing: true);
        }
        catch (PracticeProblem ex)
        {
            return StatusCode(
                ex.StatusCode,
                new
                {
                    code = ex.Code,
                    message = ex.Message
                });
        }
        catch (RequestFailedException ex)
            when (ex.Status == 404)
        {
            return NotFound(new
            {
                code = "MEDIA_NOT_AVAILABLE",
                message = "Không tìm thấy tệp media."
            });
        }
    }

    // =========================================================
    // MEDIA TYPE
    // =========================================================

    private static string MediaType(
        string path,
        string? storedType)
    {
        if (!string.IsNullOrWhiteSpace(storedType) &&
            storedType != "application/octet-stream")
        {
            return storedType;
        }

        return Path.GetExtension(path)
            .ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",

            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",

            _ => "application/octet-stream"
        };
    }
}