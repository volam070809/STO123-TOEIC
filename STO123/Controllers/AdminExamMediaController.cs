using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Admin;
using STO123.Services.Exam;

namespace STO123.Controllers;

[Authorize(Roles = "ADMIN_NOI_DUNG")]
[ApiController]
[Route("api/admin/content/media")]
public sealed class AdminExamMediaController(ToeicDbContext db, BlobServiceClient blobs,
    IConfiguration configuration) : ControllerBase
{
    private BlobContainerClient Container => blobs.GetBlobContainerClient(
        configuration["ExamMedia:ContainerName"] ?? "toeic-exam-media");
    private static bool SafePath(string? path) => path is { Length: > 0 and <= 512 } &&
        !path.StartsWith('/') && !path.Contains("..", StringComparison.Ordinal) &&
        !path.Contains('\\') && !path.Any(char.IsControl);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? prefix,
        [FromQuery] string? continuationToken, [FromQuery] int pageSize = 30,
        CancellationToken ct = default, [FromQuery] string bank = "DE_THI")
    {
        if (pageSize is < 1 or > 100 || prefix is { Length: > 150 } ||
            bank is not ("DE_THI" or "LUYEN_TAP"))
            return BadRequest(new { code = "INVALID_FILTER", message = "Bộ lọc không hợp lệ." });
        var container = bank == "DE_THI" ? Container : blobs.GetBlobContainerClient("practice-question");
        var page = await container.GetBlobsAsync(BlobTraits.None, BlobStates.None,
            prefix: prefix, cancellationToken: ct).AsPages(continuationToken, pageSize)
            .FirstAsync(ct);
        return Ok(new { container = container.Name, items = page.Values.Select(x => new {
            path = x.Name, size = x.Properties.ContentLength,
            contentType = x.Properties.ContentType, lastModified = x.Properties.LastModified
        }), next = page.ContinuationToken });
    }

    [HttpPost("upload")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> Upload(IFormFile file, [FromQuery] string bank = "DE_THI",
        CancellationToken ct = default)
    {
        if (bank is not ("DE_THI" or "LUYEN_TAP"))
            return BadRequest(new { code = "INVALID_BANK", message = "Ngân hàng không hợp lệ." });
        if (file is null || file.Length is < 12 or > 20_000_000)
            return BadRequest(new { code = "INVALID_FILE", message = "Chọn MP3 hoặc ảnh tối đa 20 MB." });
        var kind = Path.GetExtension(file.FileName).ToLowerInvariant() == ".mp3" ? "AUDIO" : "HINH_ANH";
        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        memory.Position = 0;
        if (!LectureResourceStorage.TryValidate(kind, file.FileName, file.ContentType,
            memory, out var extension, out var contentType))
            return BadRequest(new { code = "INVALID_FILE", message = "Tệp không đúng định dạng MP3 hoặc ảnh JPG, PNG, WebP." });
        var bytes = memory.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var path = $"question-media/{Guid.NewGuid():N}{extension}";
        var container = bank == "DE_THI" ? Container : blobs.GetBlobContainerClient("practice-question");
        var blob = container.GetBlobClient(path);
        await blob.UploadAsync(new BinaryData(bytes), new BlobUploadOptions {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        }, ct);
        var actual = (await blob.DownloadContentAsync(ct)).Value.Content.ToArray();
        if (Convert.ToHexString(SHA256.HashData(actual)).ToLowerInvariant() != hash)
        {
            await blob.DeleteIfExistsAsync(cancellationToken: ct);
            return StatusCode(503, new { code = "UPLOAD_VERIFY_FAILED",
                message = "Không xác minh được tệp vừa tải lên. Vui lòng thử lại." });
        }
        return Ok(new { path, contentType, size = bytes.Length, container = container.Name });
    }

    [HttpGet("file")]
    public async Task<IActionResult> File([FromQuery] string path, CancellationToken ct,
        [FromQuery] string bank = "DE_THI")
    {
        if (!SafePath(path) || bank is not ("DE_THI" or "LUYEN_TAP"))
            return BadRequest(new { code = "INVALID_PATH" });
        var blob = (bank == "DE_THI" ? Container : blobs.GetBlobContainerClient("practice-question"))
            .GetBlobClient(path);
        try
        {
            var properties = await blob.GetPropertiesAsync(cancellationToken: ct);
            return base.File(await blob.OpenReadAsync(cancellationToken: ct),
                properties.Value.ContentType ?? "application/octet-stream", enableRangeProcessing: true);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        { return NotFound(new { code = "MEDIA_NOT_FOUND", message = "Không tìm thấy tệp." }); }
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string path, CancellationToken ct)
    {
        if (!SafePath(path)) return BadRequest(new { code = "INVALID_PATH" });
        if (path.StartsWith("exam-snapshots/", StringComparison.Ordinal))
            return Conflict(new { code = "IMMUTABLE_MEDIA",
                message = "Media lịch sử của phiên bản đề không thể xóa từ thư viện nguồn." });
        await using var mediaLease = await ExamMediaPathLease.AcquireAsync(
            db.Database.GetConnectionString()!, Container.Name, path, ct);
        var blob = Container.GetBlobClient(path);
        if (!(await blob.ExistsAsync(ct)).Value)
        {
            var referenced = await db.NguLieu.AsNoTracking().AnyAsync(x =>
                x.DuongDanAudio == path || x.DuongDanAnh == path, ct) ||
                await db.NguLieuTaiLieu.AsNoTracking().AnyAsync(x => x.DuongDanAnh == path, ct) ||
                await db.NhomLuotLam.AsNoTracking().AnyAsync(x =>
                    x.DuongDanAudio == path || x.DuongDanAnh == path ||
                    x.TaiLieuJson != null && x.TaiLieuJson.Contains(path), ct);
            if (referenced) return Conflict(new { code = "MEDIA_MISSING_BUT_REFERENCED",
                message = "File không còn trên Blob nhưng SQL vẫn tham chiếu. Cần kiểm tra và khôi phục file trước khi xóa." });
            return Ok(new { deleted = true, path, alreadyAbsent = true });
        }
        var resources = await db.NguLieu.AsNoTracking().Where(x =>
            x.DuongDanAudio == path || x.DuongDanAnh == path).Select(x => x.MaNguLieu).ToArrayAsync(ct);
        var documentResources = await db.NguLieuTaiLieu.AsNoTracking()
            .Where(x => x.DuongDanAnh == path).Select(x => x.MaNguLieu).ToArrayAsync(ct);
        var resourceIds = resources.Concat(documentResources).Distinct().ToArray();
        if (resourceIds.Length > 0)
        {
            var members = await db.NhomCauHoi.AsNoTracking().Where(m => resourceIds.Contains(m.MaNguLieu))
                .Select(m => new { m.MaCauHoi, m.MaCauHoiNavigation.LoaiCauHoi })
                .ToArrayAsync(ct);
            if (members.Any(m => m.LoaiCauHoi == "LUYEN_TAP"))
                return Conflict(new { code = "PRACTICE_MEDIA_IN_USE",
                    message = "Media còn được luyện Part sử dụng. Chưa thể xóa an toàn khi lượt luyện có thể bắt đầu đồng thời." });
            var memberIds = members.Select(m => m.MaCauHoi).ToHashSet();
            var compact = await db.KetQuaLamBai.AsNoTracking().Where(a =>
                a.MaPhienBanDeThi == null && a.DuLieuBaiLamJson != null &&
                (a.LoaiBaiLam == ExamCore.Mock || a.LoaiBaiLam == ExamCore.Placement))
                .Select(a => new { a.MaKetQua, a.DuLieuBaiLamJson }).ToArrayAsync(ct);
            var blocked = compact.Where(a => CompactExamStore.Decode(a.DuLieuBaiLamJson)
                .QuestionIds.Any(memberIds.Contains)).Select(a => a.MaKetQua).ToArray();
            if (blocked.Length > 0)
                return Conflict(new { code = "LEGACY_ATTEMPT_DEPENDENCY",
                    message = "Lượt thi compact cũ còn đọc media nguồn. Chưa thể xóa tệp an toàn.", attemptIds = blocked });
            var unversioned = await db.CauHoiDeThi.AsNoTracking()
                .Where(l => memberIds.Contains(l.MaCauHoi) && l.MaDeThiNavigation.MaPhienBanHienHanh == null)
                .Select(l => l.MaDeThi).Distinct().ToArrayAsync(ct);
            if (unversioned.Length > 0)
                return Conflict(new { code = "EXAM_SNAPSHOT_REQUIRED",
                    message = "Cần xuất bản snapshot cho đề đang dùng media trước khi xóa.", examIds = unversioned });
        }
        var legacyRows = await db.NhomLuotLam.AsNoTracking().Where(x =>
            x.DuongDanAudio == path || x.DuongDanAnh == path ||
            x.TaiLieuJson != null && x.TaiLieuJson.Contains(path)).ToArrayAsync(ct);
        var legacyAttemptIds = legacyRows.Select(x => x.MaKetQua).ToArray();
        if (legacyAttemptIds.Length > 0 && await db.KetQuaLamBai.AsNoTracking()
            .AnyAsync(a => legacyAttemptIds.Contains(a.MaKetQua) &&
                a.LoaiBaiLam == PracticeQuestionBank.AttemptType, ct))
            return Conflict(new { code = "PRACTICE_HISTORY_MEDIA",
                message = "Đường dẫn này còn trong lịch sử luyện Part; không thể xác định an toàn tệp thuộc thư viện đề thi." });
        if (legacyRows.Any(x => x.TaiLieuJson?.Contains(path, StringComparison.Ordinal) == true &&
            !ExamDocumentCodec.Decode(x.TaiLieuJson).Any(d => d.ImagePath == path)))
            return Conflict(new { code = "LEGACY_DOCUMENT_UNREADABLE",
                message = "Có tài liệu lịch sử chứa đường dẫn media nhưng không giải mã được. Chưa thể xóa an toàn." });
        var needsHistory = legacyRows.Any(x => x.DuongDanAudio == path || x.DuongDanAnh == path ||
            ExamDocumentCodec.Decode(x.TaiLieuJson).Any(d => d.ImagePath == path));
        string? historyPath = null;
        if (needsHistory)
        {
            var content = (await blob.DownloadContentAsync(ct)).Value.Content.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(content));
            historyPath = $"exam-history-media/{Guid.NewGuid():N}{Path.GetExtension(path).ToLowerInvariant()}";
            var copy = Container.GetBlobClient(historyPath);
            var props = await blob.GetPropertiesAsync(cancellationToken: ct);
            await copy.UploadAsync(new BinaryData(content), new BlobUploadOptions {
                HttpHeaders = new BlobHttpHeaders { ContentType = props.Value.ContentType },
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
            }, ct);
            if (Convert.ToHexString(SHA256.HashData(
                (await copy.DownloadContentAsync(ct)).Value.Content.ToArray())) != hash)
                return StatusCode(503, new { code = "HISTORY_MEDIA_VERIFY_FAILED",
                    message = "Không xác minh được bản media lịch sử. File nguồn chưa bị xóa." });
        }
        await using (var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct))
        {
            var source = await db.NguLieu.Where(x => x.DuongDanAudio == path || x.DuongDanAnh == path)
                .ToListAsync(ct);
            var documents = await db.NguLieuTaiLieu.Where(x => x.DuongDanAnh == path).ToListAsync(ct);
            var history = await db.NhomLuotLam.Where(x => x.DuongDanAudio == path ||
                x.DuongDanAnh == path || x.TaiLieuJson != null && x.TaiLieuJson.Contains(path))
                .ToListAsync(ct);
            var historyIds = history.Select(x => x.MaKetQua).ToArray();
            if (historyIds.Length > 0 && await db.KetQuaLamBai.AsNoTracking()
                .AnyAsync(a => historyIds.Contains(a.MaKetQua) &&
                    a.LoaiBaiLam == PracticeQuestionBank.AttemptType, ct))
                return Conflict(new { code = "PRACTICE_HISTORY_MEDIA",
                    message = "Đường dẫn này vừa được sử dụng trong lượt luyện Part. Vui lòng thử lại sau." });
            if (history.Count > 0 && historyPath is null && history.Any(x =>
                x.DuongDanAudio == path || x.DuongDanAnh == path ||
                ExamDocumentCodec.Decode(x.TaiLieuJson).Any(d => d.ImagePath == path)))
                return Conflict(new { code = "HISTORY_CHANGED",
                    message = "Lịch sử đã thay đổi trong lúc xóa media. Vui lòng thử lại." });
            foreach (var item in source)
            {
                if (item.DuongDanAudio == path) item.DuongDanAudio = null;
                if (item.DuongDanAnh == path) item.DuongDanAnh = null;
            }
            foreach (var item in documents) item.DuongDanAnh = null;
            foreach (var item in history)
            {
                if (item.DuongDanAudio == path) item.DuongDanAudio = historyPath;
                if (item.DuongDanAnh == path) item.DuongDanAnh = historyPath;
                var docs = ExamDocumentCodec.Decode(item.TaiLieuJson);
                if (docs.Any(d => d.ImagePath == path))
                    item.TaiLieuJson = ExamDocumentCodec.Encode(docs.Select(d =>
                        d.ImagePath == path ? d with { ImagePath = historyPath } : d));
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        // If Blob deletion fails after SQL commit, the request fails and can be retried with
        // the same source path. The archived historical path is already committed.
        var deleted = await blob.DeleteIfExistsAsync(cancellationToken: ct);
        if (!deleted.Value && (await blob.ExistsAsync(ct)).Value)
            return StatusCode(503, new { code = "SOURCE_DELETE_UNCONFIRMED",
                message = "Không xác minh được việc xóa file nguồn. Vui lòng thử lại." });
        return Ok(new { deleted = true, path, historyPath });
    }
}
