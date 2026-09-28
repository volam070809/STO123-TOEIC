using STO123.Models;

namespace STO123.Services.Auth;

public interface IOtpService
{
    XacThucOTP Create(NguoiDung user, string purpose, DateTime nowUtc);
    Task InvalidateUnusedAsync(int userId, string purpose, CancellationToken cancellationToken);
    Task<XacThucOTP?> FindNewestValidAsync(int userId, string purpose, string code, DateTime nowUtc, CancellationToken cancellationToken);
}
