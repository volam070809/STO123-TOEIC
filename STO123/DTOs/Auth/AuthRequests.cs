using System.ComponentModel.DataAnnotations;

namespace STO123.DTOs.Auth;

public sealed class VerifyEmailRequest
{
    [Required]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Otp { get; init; } = string.Empty;
}

public sealed class EmailRequest
{
    [Required]
    public string Email { get; init; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Otp { get; init; } = string.Empty;

    [Required]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required]
    public string NewPassword { get; init; } = string.Empty;
}
