using System.Security.Claims;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Admin;

namespace STO123.Controllers;

public sealed record VideoStartRequest(string FileName, long Size, int? ResourceId);

[ApiController]
[Authorize(Roles = "ADMIN_NOI_DUNG")]
[Route("api/admin/content/lectures/{lectureId:int}/video-upload")]
public sealed class AdminLectureVideoUploadController(ToeicDbContext db, LectureResourceStorage storage,
    CourseBlobSynchronizer sync, IConfiguration config, ILogger<AdminLectureVideoUploadController> logger) : ControllerBase
{
    private const int BlockBytes = 4 * 1024 * 1024;
    private sealed record Ticket(int LectureId, int AdminId, int? ResourceId, string FileName, string Path, long Size, long Expires, string Key, Guid Lease);
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
    private byte[] Secret => Encoding.UTF8.GetBytes(config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is missing."));
    private string Sign(Ticket ticket)
    {
        var body = Encode(JsonSerializer.SerializeToUtf8Bytes(ticket));
        return body + "." + Encode(HMACSHA256.HashData(Secret, Encoding.UTF8.GetBytes(body)));
    }
    private Ticket? Read(string? token, int lectureId)
    {
        try
        {
            var parts = token?.Split('.'); if (parts is not { Length: 2 }) return null;
            var expected = HMACSHA256.HashData(Secret, Encoding.UTF8.GetBytes(parts[0]));
            if (!CryptographicOperations.FixedTimeEquals(expected, Decode(parts[1]))) return null;
            var ticket = JsonSerializer.Deserialize<Ticket>(Decode(parts[0]));
            return ticket?.LectureId == lectureId && ticket.AdminId == AdminId && ticket.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                && LectureResourceStorage.SafeFileName(ticket.FileName)
                && LectureResourceStorage.Owned(ticket.Path)
                && ticket.Path.EndsWith("/" + ticket.FileName, StringComparison.Ordinal) ? ticket : null;
        }
        catch (Exception) { return null; }
    }
    private int AdminId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private bool CanEdit(BaiGiang row) => User.IsInRole("ADMIN_QUAN_LY") || row.MaAdminNoiDung == AdminId;
    private async Task<BaiGiang?> Lecture(int id, CancellationToken ct) => await db.BaiGiang.FirstOrDefaultAsync(x => x.MaBaiGiang == id, ct);
    private async Task<IActionResult?> Check(int id, int? resourceId, CancellationToken ct)
    {
        var lecture = await Lecture(id, ct); if (lecture is null) return NotFound(); if (!CanEdit(lecture)) return Forbid();
        if (resourceId is { } rid)
        {
            if (!await db.TaiNguyenBaiGiang.AnyAsync(x => x.MaTaiNguyen == rid && x.MaBaiGiang == id, ct)) return NotFound();
            if (await db.BuocLoTrinh.AnyAsync(x => x.MaBaiGiang == id, ct))
                return Conflict(new { message = "Hãy tải tài nguyên mới rồi lưu trữ tệp cũ để giữ lịch sử học." });
        }
        else if (await db.BuocLoTrinh.CountAsync(x => x.MaBaiGiang == id, ct) > 1)
            return Conflict(new { message = "Bài giảng dùng trong nhiều hoạt động. Hãy tạo bản sao riêng trước khi tải video." });
        return null;
    }
    [HttpPost("start")]
    public async Task<IActionResult> Start(VideoStartRequest request, CancellationToken ct)
    {
        if (!LectureResourceStorage.SafeFileName(request.FileName))
            return BadRequest(new { message = "Tên video quá dài hoặc chứa ký tự không hợp lệ (tối đa 128 ký tự)." });
        if (!request.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || request.Size is < 24 || request.Size > storage.MaxVideoBytes)
            return BadRequest(new { message = "Chỉ nhận video MP4 tối đa 950 MB." });
        var invalid = await Check(RouteId(), request.ResourceId, ct); if (invalid is not null) return invalid;
        var path = storage.NewPath(request.FileName);
        var ticket = new Ticket(RouteId(), AdminId, request.ResourceId, request.FileName, path, request.Size,
            DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds(), "video-" + Guid.NewGuid().ToString("N"), Guid.NewGuid());
        var fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(path + "|" + request.Size + "|" + request.FileName));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.AdminContentIdempotency
                (MaAdmin,ThaoTac,KhoaYeuCau,DauVanTay,TrangThai,DuongDanBlob,LeaseToken,LeaseUntilUtc)
            VALUES ({AdminId},'CREATE_LECTURE_RESOURCE',{ticket.Key},{fingerprint},'PENDING',{path},{ticket.Lease},DATEADD(hour,2,SYSUTCDATETIME()))
            """, ct);
        return Ok(new { token = Sign(ticket), blockBytes = BlockBytes, blocks = (request.Size + BlockBytes - 1) / BlockBytes });
    }
    private int RouteId() => int.Parse(RouteData.Values["lectureId"]!.ToString()!);
    private static string BlockId(int index) => Convert.ToBase64String(Encoding.ASCII.GetBytes(index.ToString("D8")));
    private Task<string?> SavedResponse(Ticket ticket, CancellationToken ct) => db.Database.SqlQuery<string>($"""
        SELECT NoiDungPhanHoi AS Value FROM dbo.AdminContentIdempotency
        WHERE MaAdmin={ticket.AdminId} AND ThaoTac='CREATE_LECTURE_RESOURCE'
          AND KhoaYeuCau={ticket.Key} AND TrangThai='COMPLETED'
        """).FirstOrDefaultAsync(ct);
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromHeader(Name = "X-Upload-Token")] string token, CancellationToken ct)
    {
        var ticket = Read(token, RouteId()); if (ticket is null) return Unauthorized();
        var invalid = await Check(ticket.LectureId, ticket.ResourceId, ct); if (invalid is not null) return invalid;
        var saved = await SavedResponse(ticket, ct);
        if (saved is not null) return Ok(new { state = "saved", resource = JsonSerializer.Deserialize<JsonElement>(saved) });
        return Ok(new { state = (await storage.Container.GetBlobClient(ticket.Path).ExistsAsync(ct)).Value ? "uploaded" : "pending" });
    }
    [HttpPut("blocks/{index:int}")]
    [RequestSizeLimit(BlockBytes + 1024)]
    public async Task<IActionResult> Block(int index, [FromHeader(Name = "X-Upload-Token")] string token, CancellationToken ct)
    {
        var ticket = Read(token, RouteId()); if (ticket is null) return Unauthorized();
        var count = (int)((ticket.Size + BlockBytes - 1) / BlockBytes);
        var length = index == count - 1 ? ticket.Size - (long)index * BlockBytes : BlockBytes;
        if (index < 0 || index >= count || Request.ContentLength is { } suppliedLength && suppliedLength != length)
            return BadRequest(new { message = "Khối video không đúng kích thước." });
        var invalid = await Check(ticket.LectureId, ticket.ResourceId, ct); if (invalid is not null) return invalid;
        await using var buffer = new MemoryStream((int)length);
        await Request.Body.CopyToAsync(buffer, ct);
        if (buffer.Length != length) return BadRequest(new { message = "Khối video tải chưa đủ dữ liệu." });
        buffer.Position = 0;
        await storage.Container.GetBlockBlobClient(ticket.Path).StageBlockAsync(BlockId(index), buffer, cancellationToken: ct);
        return NoContent();
    }
    [HttpPost("finish")]
    public async Task<IActionResult> Finish([FromHeader(Name = "X-Upload-Token")] string token, CancellationToken ct)
    {
        var ticket = Read(token, RouteId()); if (ticket is null) return Unauthorized();
        var invalid = await Check(ticket.LectureId, ticket.ResourceId, ct); if (invalid is not null) return invalid;
        var alreadySaved = await SavedResponse(ticket, ct);
        if (alreadySaved is not null) return Content(alreadySaved, "application/json");
        var blob = storage.Container.GetBlockBlobClient(ticket.Path);
        var count = (int)((ticket.Size + BlockBytes - 1) / BlockBytes);
        try
        {
            if (!(await blob.ExistsAsync(ct)).Value)
                await blob.CommitBlockListAsync(Enumerable.Range(0, count).Select(BlockId), new CommitBlockListOptions {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "video/mp4" }
                }, ct);
            var properties = await blob.GetPropertiesAsync(cancellationToken: ct);
            if (properties.Value.ContentLength != ticket.Size) return BadRequest(new { message = "Dung lượng Blob không khớp tệp đã chọn." });
            await using var video = await blob.OpenReadAsync(cancellationToken: ct);
            if (!LectureResourceStorage.TryValidate("VIDEO", ticket.FileName, "video/mp4", video, out _, out _))
            { await blob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None); return BadRequest(new { message = "Video MP4 không hợp lệ." }); }
        }
        catch (RequestFailedException e) when (e.Status is 400 or 404 or 409)
        { return Conflict(new { message = "Thiếu khối video hoặc Blob chưa hoàn tất. Hãy thử tải lại." }); }
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existing = await db.TaiNguyenBaiGiang.FirstOrDefaultAsync(x => x.DuongDanTep == ticket.Path, ct);
        if (existing is not null) return Ok(new { id = existing.MaTaiNguyen, name = existing.TenTaiNguyen, kind = existing.LoaiTaiNguyen, order = existing.ThuTu });
        var row = ticket.ResourceId is { } rid ? await db.TaiNguyenBaiGiang.FirstOrDefaultAsync(x => x.MaTaiNguyen == rid && x.MaBaiGiang == ticket.LectureId, ct) : null;
        var old = row?.DuongDanTep;
        if (row is null)
        {
            var order = (await db.TaiNguyenBaiGiang.Where(x => x.MaBaiGiang == ticket.LectureId).MaxAsync(x => (int?)x.ThuTu, ct) ?? 0) + 1;
            row = new TaiNguyenBaiGiang { MaBaiGiang = ticket.LectureId, ThuTu = order };
            db.TaiNguyenBaiGiang.Add(row);
        }
        row.TenTaiNguyen = ticket.FileName; row.LoaiTaiNguyen = "VIDEO"; row.DuongDanTep = ticket.Path;
        try {
            await db.SaveChangesAsync(ct);
            var response = JsonSerializer.Serialize(new { id = row.MaTaiNguyen, name = row.TenTaiNguyen, kind = row.LoaiTaiNguyen, order = row.ThuTu });
            var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.AdminContentIdempotency SET TrangThai='COMPLETED', MaPhanHoi=200,
                    NoiDungPhanHoi={response}, LeaseToken=NULL, LeaseUntilUtc=NULL, CapNhatLucUtc=SYSUTCDATETIME()
                WHERE MaAdmin={ticket.AdminId} AND ThaoTac='CREATE_LECTURE_RESOURCE'
                    AND KhoaYeuCau={ticket.Key} AND TrangThai='PENDING' AND LeaseToken={ticket.Lease}
                """, ct);
            if (updated != 1) throw new InvalidOperationException("Video upload reservation was lost.");
            await tx.CommitAsync(ct);
        }
        catch (Exception e) { logger.LogError(e, "Video Blob {Path} saved but SQL result is uncertain", ticket.Path);
            return StatusCode(503, new { message = "Video đã lên Blob nhưng chưa xác nhận được SQL. Tải lại danh sách trước khi thử lại." }); }
        if (old is not null && old != ticket.Path && !await db.TaiNguyenBaiGiang.AnyAsync(x => x.DuongDanTep == old, ct))
            try { await storage.DeleteAsync(old, CancellationToken.None); } catch (Exception e) { logger.LogWarning(e, "Old video Blob cleanup pending: {Path}", old); }
        var courses = await db.BuocLoTrinh.AsNoTracking().Where(x => x.MaBaiGiang == ticket.LectureId)
            .Select(x => x.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc).Distinct().ToListAsync(ct);
        foreach (var course in courses)
            try { await sync.MarkCourse(course, ct); await sync.ReleaseCourse(course, ct); }
            catch (Exception e) { logger.LogWarning(e, "Video course sync pending: {Course}", course); }
        return Ok(new { id = row.MaTaiNguyen, name = row.TenTaiNguyen, kind = row.LoaiTaiNguyen, order = row.ThuTu });
    }
}
