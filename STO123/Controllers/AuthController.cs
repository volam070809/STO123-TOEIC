using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using STO123.DTOs.Auth;
using STO123.Services.Auth;
using STO123.Models;
using Microsoft.EntityFrameworkCore;

namespace STO123.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService, IGoogleAuthService googleAuthService,
    IJwtTokenService jwtTokens, ToeicDbContext db) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken) =>
        Respond(await authService.RegisterAsync(request, cancellationToken));

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken cancellationToken) =>
        Respond(await authService.VerifyEmailAsync(request, cancellationToken));

    [HttpPost("resend-otp")]
    public async Task<IActionResult> ResendOtp(EmailRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.ResendOtpAsync(request, cancellationToken));

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken) =>
        Respond(await authService.LoginAsync(request, cancellationToken));

    [HttpPost("google")]
    public async Task<IActionResult> Google(GoogleLoginRequest request, CancellationToken cancellationToken) =>
        Respond(await googleAuthService.AuthenticateAsync(request.IdToken, cancellationToken));

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) =>
        TryGetUserId(out var userId)
            ? Respond(await authService.MeAsync(userId, cancellationToken))
            : Unauthorized();

    [Authorize]
    [HttpPost("renew")]
    public async Task<IActionResult> Renew(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var user = await db.NguoiDung.AsNoTracking().FirstOrDefaultAsync(x => x.MaNguoiDung == userId, cancellationToken);
        if (user is null || user.TrangThai != "HOAT_DONG") return Unauthorized();
        var (token, expiresAtUtc) = jwtTokens.Create(user);
        return Ok(new LoginResponse(token, expiresAtUtc));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.ForgotPasswordAsync(request, cancellationToken));

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken) =>
        Respond(await authService.ResetPasswordAsync(request, cancellationToken));

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken) =>
        TryGetUserId(out var userId)
            ? Respond(await authService.ChangePasswordAsync(userId, request, cancellationToken))
            : Unauthorized();

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult Respond<T>(AuthResult<T> result) =>
        StatusCode(result.StatusCode, result.Error is null ? result.Value : new MessageResponse(result.Error));
}
