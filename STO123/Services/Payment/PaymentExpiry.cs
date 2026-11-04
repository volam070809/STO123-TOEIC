using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Payment;

public static class PaymentExpiry
{
    // The signed checkout is payable for three minutes. Keep one further minute
    // for a callback already in flight before allowing a new checkout.
    public const int CheckoutMinutes = 3;
    public const int PendingMinutes = CheckoutMinutes + 1;

    public static async Task ExpireAsync(ToeicDbContext db, int learnerId, DateTime localNow, CancellationToken ct)
    {
        var cutoff = localNow.AddMinutes(-PendingMinutes);
        var stale = await db.GiaoDichThanhToan
            .Include(p => p.MaDangKyNavigation)
            .Where(p => p.MaDangKyNavigation.MaNguoiDung == learnerId &&
                p.TrangThai == "CHO_XU_LY" && p.NgayTao < cutoff)
            .ToListAsync(ct);
        foreach (var payment in stale)
        {
            payment.TrangThai = "QUA_HAN";
            if (payment.MaDangKyNavigation.TrangThai == "CHO_THANH_TOAN")
                payment.MaDangKyNavigation.TrangThai = "QUA_HAN";
        }
        if (stale.Count > 0) await db.SaveChangesAsync(ct);
    }
}
