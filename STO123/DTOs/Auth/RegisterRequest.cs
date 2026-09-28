using System.ComponentModel.DataAnnotations;

namespace STO123.DTOs.Auth;

public sealed class RegisterRequest
{
    [Required]
    [StringLength(64)]
    public string HoTen { get; init; } = string.Empty;

    [Required]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    [StringLength(16)]
    public string? SoDienThoai { get; init; }
}
