using STO123.Models;

namespace STO123.Services.Auth;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAtUtc) Create(NguoiDung user);
}
