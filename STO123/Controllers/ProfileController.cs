using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Auth;
using STO123.Models;

namespace STO123.Controllers;

[ApiController]
[Authorize(Roles = "HOC_VIEN")]
[Route("api/profile")]
public sealed class ProfileController(ToeicDbContext db) : ControllerBase
{
    private static readonly Regex PhonePattern = new(@"^\+?[0-9]{9,15}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return Unauthorized();

        var name = request.HoTen?.Trim() ?? string.Empty;
        var phone = string.IsNullOrWhiteSpace(request.SoDienThoai) ? null : request.SoDienThoai.Trim();
        if (name.Length is < 1 or > 64)
            return BadRequest(new { message = "Họ tên phải có từ 1 đến 64 ký tự." });
        if (phone is not null && (phone.Length > 16 || !PhonePattern.IsMatch(phone)))
            return BadRequest(new { message = "Số điện thoại phải có 9–15 chữ số, có thể bắt đầu bằng +." });

        var user = await db.NguoiDung.FirstOrDefaultAsync(x => x.MaNguoiDung == userId &&
            x.VaiTro == "HOC_VIEN" && x.TrangThai == "HOAT_DONG", ct);
        if (user is null) return NotFound(new { message = "Không tìm thấy tài khoản học viên đang hoạt động." });

        if (user.HoTen != name || user.SoDienThoai != phone)
        {
            user.HoTen = name;
            user.SoDienThoai = phone!;
            await db.SaveChangesAsync(ct);
        }
        return Ok(new UpdateProfileResponse(user.MaNguoiDung, user.HoTen, user.SoDienThoai));
    }
}
