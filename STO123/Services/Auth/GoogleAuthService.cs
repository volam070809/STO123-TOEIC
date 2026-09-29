using System.ComponentModel.DataAnnotations;
using Google.Apis.Auth;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Auth;
using STO123.Models;

namespace STO123.Services.Auth;

public sealed class GoogleAuthService(
    ToeicDbContext context,
    IJwtTokenService jwtTokenService,
    IConfiguration configuration) : IGoogleAuthService
{
    public async Task<AuthResult<LoginResponse>> AuthenticateAsync(string idToken, CancellationToken cancellationToken)
    {
        var clientId = configuration["Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return AuthResult<LoginResponse>.Failure(503, "Google sign-in is not configured.");

        if (string.IsNullOrWhiteSpace(idToken))
            return AuthResult<LoginResponse>.Failure(400, "Google credential is required.");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });
        }
        catch (InvalidJwtException)
        {
            return AuthResult<LoginResponse>.Failure(401, "Invalid Google credential.");
        }
        catch (HttpRequestException)
        {
            return AuthResult<LoginResponse>.Failure(503, "Google sign-in is temporarily unavailable.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var googleId = payload.Subject?.Trim();
        var email = payload.Email?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(googleId) || googleId.Length > 255)
            return AuthResult<LoginResponse>.Failure(401, "Invalid Google identity.");
        if (!payload.EmailVerified || string.IsNullOrWhiteSpace(email) ||
            email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
            return AuthResult<LoginResponse>.Failure(403, "Google email must be verified.");

        var credential = await context.XacThucDangNhap
            .Include(item => item.MaNguoiDungNavigation)
            .FirstOrDefaultAsync(item => item.LoaiXacThuc == "GOOGLE" && item.GoogleId == googleId, cancellationToken);

        if (credential is not null)
        {
            var existingUser = credential.MaNguoiDungNavigation;
            if (existingUser.VaiTro != "HOC_VIEN")
                return AuthResult<LoginResponse>.Failure(403, "Account is not eligible for learner sign-in.");
            if (existingUser.TrangThai != "HOAT_DONG")
                return AuthResult<LoginResponse>.Failure(403, "Account is not active.");

            existingUser.LanDangNhapCuoi = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return IssueToken(existingUser);
        }

        // The current table permits one credential per user and requires EMAIL
        // credentials to have no GoogleId. Existing email accounts cannot be linked safely.
        if (await context.NguoiDung.AnyAsync(user => user.Email.Trim().ToLower() == email, cancellationToken))
            return AuthResult<LoginResponse>.Failure(409, "An account with this email already exists. Sign in with its original method.");

        var nowUtc = DateTime.UtcNow;
        var displayName = string.IsNullOrWhiteSpace(payload.Name) ? email.Split('@')[0] : payload.Name.Trim();
        var userToCreate = new NguoiDung
        {
            HoTen = displayName[..Math.Min(displayName.Length, 64)],
            Email = email,
            VaiTro = "HOC_VIEN",
            TrangThai = "HOAT_DONG",
            NgayTao = nowUtc,
            LanDangNhapCuoi = nowUtc
        };
        if (Uri.TryCreate(payload.Picture, UriKind.Absolute, out var picture) &&
            picture.Scheme == Uri.UriSchemeHttps && payload.Picture.Length <= 512)
            userToCreate.AnhDaiDien = payload.Picture;

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            context.XacThucDangNhap.Add(new XacThucDangNhap
            {
                MaNguoiDungNavigation = userToCreate,
                LoaiXacThuc = "GOOGLE",
                GoogleId = googleId,
                TaoLuc = nowUtc
            });
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            return AuthResult<LoginResponse>.Failure(409, "Google account conflict. Please try signing in again.");
        }

        return IssueToken(userToCreate);
    }

    private AuthResult<LoginResponse> IssueToken(NguoiDung user)
    {
        var (token, expiresAtUtc) = jwtTokenService.Create(user);
        return AuthResult<LoginResponse>.Success(200, new LoginResponse(token, expiresAtUtc));
    }
}


