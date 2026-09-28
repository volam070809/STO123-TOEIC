using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Auth;

public sealed class OtpService(ToeicDbContext context) : IOtpService
{
    public XacThucOTP Create(NguoiDung user, string purpose, DateTime nowUtc)
    {
        return new XacThucOTP
        {
            MaNguoiDungNavigation = user,
            MaOTP = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6"),
            LoaiOTP = purpose,
            TrangThai = "CHUA_XAC_THUC",
            NgayTao = nowUtc,
            ThoiGianHetHan = nowUtc.AddMinutes(15)
        };
    }

    public async Task InvalidateUnusedAsync(int userId, string purpose, CancellationToken cancellationToken)
    {
        var unused = await context.XacThucOTP
            .Where(otp => otp.MaNguoiDung == userId && otp.LoaiOTP == purpose && otp.TrangThai == "CHUA_XAC_THUC")
            .ToListAsync(cancellationToken);

        foreach (var otp in unused)
            otp.TrangThai = "HET_HAN";
    }

    public Task<XacThucOTP?> FindNewestValidAsync(
        int userId, string purpose, string code, DateTime nowUtc, CancellationToken cancellationToken)
    {
        return context.XacThucOTP
            .Where(otp => otp.MaNguoiDung == userId
                && otp.LoaiOTP == purpose
                && otp.TrangThai == "CHUA_XAC_THUC"
                && otp.MaOTP == code
                && otp.ThoiGianHetHan > nowUtc)
            .OrderByDescending(otp => otp.NgayTao)
            .ThenByDescending(otp => otp.MaXacThuc)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
