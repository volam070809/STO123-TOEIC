using STO123.DTOs.Auth;

namespace STO123.Services.Auth;

public interface IGoogleAuthService
{
    Task<AuthResult<LoginResponse>> AuthenticateAsync(string idToken, CancellationToken cancellationToken);
}

