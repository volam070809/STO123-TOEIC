using STO123.DTOs.Auth;

namespace STO123.Services.Auth;

public interface IAuthService
{
    Task<AuthResult<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResult<MessageResponse>> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken);
    Task<MessageResponse> ResendOtpAsync(EmailRequest request, CancellationToken cancellationToken);
    Task<AuthResult<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthResult<MeResponse>> MeAsync(int userId, CancellationToken cancellationToken);
    Task<MessageResponse> ForgotPasswordAsync(EmailRequest request, CancellationToken cancellationToken);
    Task<AuthResult<MessageResponse>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
    Task<AuthResult<MessageResponse>> ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken cancellationToken);
}
