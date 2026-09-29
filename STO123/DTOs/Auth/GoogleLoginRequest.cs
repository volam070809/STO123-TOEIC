using System.ComponentModel.DataAnnotations;

namespace STO123.DTOs.Auth;

public sealed class GoogleLoginRequest
{
    [Required]
    public string IdToken { get; init; } = string.Empty;
}

