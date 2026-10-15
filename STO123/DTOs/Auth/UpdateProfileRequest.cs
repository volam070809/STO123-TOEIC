using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace STO123.DTOs.Auth;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateProfileRequest
{
    [Required]
    [StringLength(64)]
    public string HoTen { get; init; } = string.Empty;

    [StringLength(16)]
    public string? SoDienThoai { get; init; }
}

public sealed record UpdateProfileResponse(int MaNguoiDung, string HoTen, string? SoDienThoai);
