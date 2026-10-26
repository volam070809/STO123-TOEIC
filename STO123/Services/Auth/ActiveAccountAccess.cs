using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Auth;

public sealed class ActiveAccountAccess(ToeicDbContext db)
{
    public Task<bool> IsActiveAsync(int userId, string role, CancellationToken cancellationToken) =>
        db.NguoiDung.AsNoTracking().AnyAsync(user =>
            user.MaNguoiDung == userId && user.VaiTro == role && user.TrangThai == "HOAT_DONG",
            cancellationToken);
}
