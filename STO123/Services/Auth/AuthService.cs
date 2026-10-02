using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Auth;
using STO123.Models;

namespace STO123.Services.Auth;

public sealed class AuthService(
    ToeicDbContext context,
    IPasswordHasher<NguoiDung> passwordHasher,
    IOtpService otpService,
    IJwtTokenService jwtTokenService,
    IEmailService emailService,
    AvatarStorage avatars,
    ILogger<AuthService> logger) : IAuthService
{
    private const string InvalidOtp = "invalid or expired OTP";
    private const string VerifyPurpose = "XAC_THUC_EMAIL";
    private const string ResetPurpose = "QUEN_MAT_KHAU";
    private static readonly MessageResponse ResendMessage = new("If the account needs verification, a code has been sent.");
    private static readonly MessageResponse ForgotMessage = new("If an email account exists, a password reset code has been sent.");

    public async Task<AuthResult<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var name = (request.HoTen ?? string.Empty).Trim();
        var phone = request.SoDienThoai?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
            return AuthResult<RegisterResponse>.Failure(400, "Name and email are required.");
        if (name.Length > 64)
            return AuthResult<RegisterResponse>.Failure(400, "Name must be at most 64 characters.");
        if (email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
            return AuthResult<RegisterResponse>.Failure(400, "A valid email address is required.");
        if (phone?.Length > 16)
            return AuthResult<RegisterResponse>.Failure(400, "Phone number must be at most 16 characters.");
        if (PasswordRules.Validate(request.Password) is { } passwordError)
            return AuthResult<RegisterResponse>.Failure(400, passwordError);

        if (await context.NguoiDung.AnyAsync(user => user.Email.Trim().ToLower() == email, cancellationToken))
            return AuthResult<RegisterResponse>.Failure(409, "Email is already registered.");

        var nowUtc = DateTime.UtcNow;
        var user = new NguoiDung
        {
            HoTen = name,
            Email = email,
            SoDienThoai = string.IsNullOrWhiteSpace(phone) ? null! : phone,
            VaiTro = "HOC_VIEN",
            TrangThai = "CHO_XAC_THUC",
            NgayTao = nowUtc
        };
        var credential = new XacThucDangNhap
        {
            MaNguoiDungNavigation = user,
            LoaiXacThuc = "EMAIL",
            MatKhauMaHoa = passwordHasher.HashPassword(user, request.Password),
            TaoLuc = nowUtc
        };
        var otp = otpService.Create(user, VerifyPurpose, nowUtc);

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            context.XacThucDangNhap.Add(credential);
            context.XacThucOTP.Add(otp);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            return AuthResult<RegisterResponse>.Failure(409, "Email is already registered.");
        }

        try
        {
            await emailService.SendOtpAsync(user.Email, VerifyPurpose, otp.MaOTP, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Registration OTP email could not be sent.");
            return AuthResult<RegisterResponse>.Failure(500, "Account created, but the verification email could not be sent. Use resend-otp to try again.");
        }

        return AuthResult<RegisterResponse>.Success(201,
            new RegisterResponse(user.MaNguoiDung, user.HoTen, user.Email, user.VaiTro, user.TrangThai));
    }

    public async Task<AuthResult<MessageResponse>> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var user = await context.NguoiDung.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null)
            return AuthResult<MessageResponse>.Failure(404, "Account not found.");

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var otp = await otpService.FindNewestValidAsync(user.MaNguoiDung, VerifyPurpose, request.Otp, DateTime.UtcNow, cancellationToken);
        if (otp is null)
            return AuthResult<MessageResponse>.Failure(400, InvalidOtp);

        otp.TrangThai = "DA_XAC_THUC";
        user.TrangThai = "HOAT_DONG";
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AuthResult<MessageResponse>.Success(200, new MessageResponse("Email verified."));
    }

    public async Task<MessageResponse> ResendOtpAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var user = await context.NguoiDung.FirstOrDefaultAsync(u => u.Email == email && u.TrangThai == "CHO_XAC_THUC", cancellationToken);
        if (user is null)
            return ResendMessage;

        var otp = await ReplaceOtpAsync(user, VerifyPurpose, cancellationToken);
        if (otp is not null)
            await SendWithoutDisclosureAsync(user.Email, VerifyPurpose, otp.MaOTP, cancellationToken);
        return ResendMessage;
    }

    public async Task<AuthResult<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var user = await context.NguoiDung.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null)
            return AuthResult<LoginResponse>.Failure(401, "invalid email or password");

        var credential = await context.XacThucDangNhap.FirstOrDefaultAsync(
            c => c.MaNguoiDung == user.MaNguoiDung && c.LoaiXacThuc == "EMAIL", cancellationToken);
        if (credential?.MatKhauMaHoa is not { } hash ||
            passwordHasher.VerifyHashedPassword(user, hash, request.Password) == PasswordVerificationResult.Failed)
            return AuthResult<LoginResponse>.Failure(401, "invalid email or password");

        if (user.TrangThai != "HOAT_DONG")
            return AuthResult<LoginResponse>.Failure(403, "email not verified");

        var (token, expiresAtUtc) = jwtTokenService.Create(user);
        user.LanDangNhapCuoi = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return AuthResult<LoginResponse>.Success(200, new LoginResponse(token, expiresAtUtc));
    }

    public async Task<AuthResult<MeResponse>> MeAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await context.NguoiDung.AsNoTracking()
            .Where(u => u.MaNguoiDung == userId)
            .Select(u => new { u.MaNguoiDung, u.HoTen, u.Email, u.SoDienThoai, u.AnhDaiDien,
                u.VaiTro, u.TrangThai,
                HasPassword = u.XacThucDangNhap != null && u.XacThucDangNhap.LoaiXacThuc == "EMAIL" })
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null) return AuthResult<MeResponse>.Failure(404, "Account not found.");
        var custom = AvatarStorage.IsCustomReference(user.MaNguoiDung, user.AnhDaiDien);
        var providerUrl = await avatars.ProviderUrlAsync(user.MaNguoiDung, user.AnhDaiDien, cancellationToken);
        return AuthResult<MeResponse>.Success(200,
            new MeResponse(user.MaNguoiDung, user.HoTen, user.Email, user.SoDienThoai, providerUrl,
                user.VaiTro, user.TrangThai, user.HasPassword)
            { HasCustomAvatar = custom, AvatarVersion = custom ? AvatarStorage.Version(user.AnhDaiDien) : null });
    }

    public async Task<MessageResponse> ForgotPasswordAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var user = await context.NguoiDung.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null || !await context.XacThucDangNhap.AnyAsync(
                c => c.MaNguoiDung == user.MaNguoiDung && c.LoaiXacThuc == "EMAIL", cancellationToken))
            return ForgotMessage;

        var otp = await ReplaceOtpAsync(user, ResetPurpose, cancellationToken);
        if (otp is not null)
            await SendWithoutDisclosureAsync(user.Email, ResetPurpose, otp.MaOTP, cancellationToken);
        return ForgotMessage;
    }

    public async Task<AuthResult<MessageResponse>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        if (PasswordRules.Validate(request.NewPassword) is { } passwordError)
            return AuthResult<MessageResponse>.Failure(400, passwordError);

        var email = NormalizeEmail(request.Email);
        var user = await context.NguoiDung.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null)
            return AuthResult<MessageResponse>.Failure(400, InvalidOtp);

        var credential = await context.XacThucDangNhap.FirstOrDefaultAsync(
            c => c.MaNguoiDung == user.MaNguoiDung && c.LoaiXacThuc == "EMAIL", cancellationToken);
        if (credential is null)
            return AuthResult<MessageResponse>.Failure(400, InvalidOtp);

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var otp = await otpService.FindNewestValidAsync(user.MaNguoiDung, ResetPurpose, request.Otp, DateTime.UtcNow, cancellationToken);
        if (otp is null)
            return AuthResult<MessageResponse>.Failure(400, InvalidOtp);

        credential.MatKhauMaHoa = passwordHasher.HashPassword(user, request.NewPassword);
        otp.TrangThai = "DA_XAC_THUC";
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AuthResult<MessageResponse>.Success(200, new MessageResponse("Password reset."));
    }

    public async Task<AuthResult<MessageResponse>> ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await context.NguoiDung.FirstOrDefaultAsync(u => u.MaNguoiDung == userId, cancellationToken);
        if (user is null)
            return AuthResult<MessageResponse>.Failure(401, "Unauthorized.");

        var credential = await context.XacThucDangNhap.FirstOrDefaultAsync(
            c => c.MaNguoiDung == userId && c.LoaiXacThuc == "EMAIL", cancellationToken);
        if (credential?.MatKhauMaHoa is not { } hash ||
            passwordHasher.VerifyHashedPassword(user, hash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            return AuthResult<MessageResponse>.Failure(401, "Incorrect current password.");

        if (PasswordRules.Validate(request.NewPassword) is { } passwordError)
            return AuthResult<MessageResponse>.Failure(400, passwordError);

        credential.MatKhauMaHoa = passwordHasher.HashPassword(user, request.NewPassword);
        await context.SaveChangesAsync(cancellationToken);
        return AuthResult<MessageResponse>.Success(200, new MessageResponse("Password changed."));
    }

    private async Task<XacThucOTP?> ReplaceOtpAsync(NguoiDung user, string purpose, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        // A public endpoint must enforce this cooldown server-side, including after a page refresh.
        var recentlyIssued = await context.XacThucOTP.AsNoTracking().AnyAsync(
            otp => otp.MaNguoiDung == user.MaNguoiDung && otp.LoaiOTP == purpose &&
                otp.NgayTao > nowUtc.AddSeconds(-60), cancellationToken);
        if (recentlyIssued)
            return null;

        await otpService.InvalidateUnusedAsync(user.MaNguoiDung, purpose, cancellationToken);
        var otp = otpService.Create(user, purpose, nowUtc);
        context.XacThucOTP.Add(otp);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return otp;
    }

    private async Task SendWithoutDisclosureAsync(string email, string purpose, string code, CancellationToken cancellationToken)
    {
        try
        {
            await emailService.SendOtpAsync(email, purpose, code, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("OTP email could not be sent.");
        }
    }

    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();
}
