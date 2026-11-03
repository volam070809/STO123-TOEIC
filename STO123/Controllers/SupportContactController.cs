using Azure;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using STO123.Services.Admin;

namespace STO123.Controllers;

[ApiController]
public sealed class SupportContactController(SupportContactStore store) : ControllerBase
{
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("api/contact-support")]
    public async Task<IActionResult> Public(CancellationToken ct)
    {
        try
        {
            var settings = await store.ReadAsync(ct);
            return Ok(new { zaloUrl = settings.ZaloEnabled ? settings.ZaloUrl : null,
                messengerUrl = settings.MessengerEnabled ? settings.MessengerUrl : null });
        }
        catch (Exception error) when (error is RequestFailedException or InvalidDataException or JsonException)
        {
            return StatusCode(503, new { message = "Chưa tải được liên hệ hỗ trợ. Vui lòng thử lại." });
        }
    }

    [Authorize(Roles = "ADMIN_QUAN_LY")]
    [HttpGet("api/admin/contact-support")]
    public async Task<IActionResult> Admin(CancellationToken ct)
    {
        try { return Ok(await store.ReadAsync(ct)); }
        catch (Exception error) when (error is RequestFailedException or InvalidDataException or JsonException)
        { return StatusCode(503, new { message = "Không đọc được cấu hình liên hệ từ Azure Blob." }); }
    }

    [Authorize(Roles = "ADMIN_QUAN_LY")]
    [HttpPut("api/admin/contact-support")]
    public async Task<IActionResult> Save([FromBody] SaveSupportContactRequest input, CancellationToken ct)
    {
        if (SupportContactStore.ValidationError(input) is { } message)
            return BadRequest(new { message });
        try { return Ok(await store.SaveAsync(input, ct)); }
        catch (RequestFailedException error) when (error.Status is 409 or 412)
        { return Conflict(new { message = "Cấu hình đã thay đổi ở nơi khác. Tải lại trước khi lưu." }); }
        catch (Exception error) when (error is RequestFailedException or IOException or InvalidDataException or JsonException)
        { return StatusCode(503, new { message = "Chưa xác minh được cấu hình đã lưu. Tải lại để kiểm tra trước khi thử lại." }); }
    }
}
