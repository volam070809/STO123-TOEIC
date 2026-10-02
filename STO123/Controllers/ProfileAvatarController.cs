using System.Security.Claims;
using Azure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Auth;

namespace STO123.Controllers;

[ApiController]
[Authorize(Roles = "HOC_VIEN")]
[Route("api/profile/avatar")]
public sealed class ProfileAvatarController(ToeicDbContext db, AvatarStorage avatars,
    ILogger<ProfileAvatarController> logger) : ControllerBase
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0
        ? id : 0;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (UserId == 0) return Unauthorized();
        var reference = await db.NguoiDung.AsNoTracking()
            .Where(x => x.MaNguoiDung == UserId && x.VaiTro == "HOC_VIEN" && x.TrangThai == "HOAT_DONG")
            .Select(x => x.AnhDaiDien).FirstOrDefaultAsync(ct);
        if (!AvatarStorage.IsCustomReference(UserId, reference)) return NotFound();
        try
        {
            var image = await avatars.OpenAsync(reference!, ct);
            if (image is null) return NotFound();
            Response.Headers.CacheControl = "private, no-store";
            return File(image.Value.Stream, image.Value.ContentType);
        }
        catch (RequestFailedException)
        {
            return StatusCode(503, new { message = "Không thể tải ảnh đại diện." });
        }
    }

    [HttpPost]
    [RequestSizeLimit(MaxBytes + 128 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (UserId == 0) return Unauthorized();
        if (file is null || file.Length is <= 0 or > MaxBytes)
            return BadRequest(new { message = "Ảnh phải có dung lượng tối đa 2 MB." });
        var user = await OwnLearner(ct);
        if (user is null) return NotFound();
        await using var image = new MemoryStream();
        await file.CopyToAsync(image, ct);
        if (!AvatarFileValidator.TryValidate(file.FileName, file.ContentType, image.GetBuffer().AsSpan(0, (int)image.Length),
            out var extension, out var contentType))
            return BadRequest(new { message = "Chỉ chấp nhận ảnh JPG, PNG hoặc WEBP hợp lệ." });
        image.Position = 0;

        var previous = user.AnhDaiDien;
        string? providerUrl;
        try { providerUrl = await avatars.ProviderUrlAsync(UserId, previous, ct); }
        catch (RequestFailedException) { return StatusCode(503, new { message = "Không thể chuẩn bị ảnh đại diện." }); }

        string uploaded;
        try { uploaded = await avatars.UploadAsync(UserId, image, extension, contentType, providerUrl, ct); }
        catch (RequestFailedException) { return StatusCode(503, new { message = "Không thể lưu ảnh đại diện." }); }
        try
        {
            user.AnhDaiDien = uploaded;
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            try { await avatars.DeleteAsync(uploaded, ct); }
            catch (RequestFailedException error) { logger.LogWarning(error, "Could not clean up failed avatar upload."); }
            throw;
        }
        if (AvatarStorage.IsCustomReference(UserId, previous))
            try { await avatars.DeleteAsync(previous!, ct); }
            catch (RequestFailedException error) { logger.LogWarning(error, "Could not clean up previous avatar."); }
        return Ok(new { hasCustomAvatar = true, avatarVersion = AvatarStorage.Version(uploaded) });
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        if (UserId == 0) return Unauthorized();
        var user = await OwnLearner(ct);
        if (user is null) return NotFound();
        var previous = user.AnhDaiDien;
        if (!AvatarStorage.IsCustomReference(UserId, previous))
            return Ok(new { hasCustomAvatar = false, anhDaiDien = AvatarStorage.IsProviderUrl(previous) ? previous : null });
        string? providerUrl;
        try { providerUrl = await avatars.ProviderUrlAsync(UserId, previous, ct); }
        catch (RequestFailedException) { return StatusCode(503, new { message = "Không thể xóa ảnh đại diện lúc này." }); }
        user.AnhDaiDien = providerUrl!;
        await db.SaveChangesAsync(ct);
        try { await avatars.DeleteAsync(previous!, ct); }
        catch (RequestFailedException error) { logger.LogWarning(error, "Could not delete previous avatar blob."); }
        return Ok(new { hasCustomAvatar = false, anhDaiDien = providerUrl });
    }

    private Task<NguoiDung?> OwnLearner(CancellationToken ct) => db.NguoiDung
        .FirstOrDefaultAsync(x => x.MaNguoiDung == UserId && x.VaiTro == "HOC_VIEN" &&
            x.TrangThai == "HOAT_DONG", ct);
}
