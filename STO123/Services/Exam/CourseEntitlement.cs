using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Payment;

namespace STO123.Services.Exam;

public static class CourseEntitlement
{
    public static IQueryable<int> ActiveCourseIds(ToeicDbContext db, int learnerId, DateTime localNow)
    {
        var active = db.DangKyGoiHoc.AsNoTracking()
            .Where(enrollment => enrollment.MaNguoiDung == learnerId &&
                enrollment.TrangThai == "DANG_SU_DUNG" && enrollment.NgayBatDau <= localNow &&
                enrollment.NgayKetThuc > localNow &&
                enrollment.GiaoDichThanhToan.Any(payment => payment.TrangThai == "THANH_CONG"));
        var legacy = active.Where(e => e.MaGoiHoc != null)
            .SelectMany(e => e.MaGoiHocNavigation.MaKhoaHoc.Select(c => c.MaKhoaHoc));
        var custom = active.Where(e => e.MaGoiHoc == null)
            .SelectMany(e => e.ChiTietDangKyKhoaHoc.Select(c => c.MaKhoaHoc));
        return legacy.Concat(custom).Distinct();
    }

    public static IQueryable<int> PendingCourseIds(ToeicDbContext db, int learnerId, DateTime? localNow = null) {
        var cutoff = (localNow ?? DateTime.UtcNow.AddHours(7)).AddMinutes(-PaymentExpiry.PendingMinutes);
        var pending = db.DangKyGoiHoc.AsNoTracking().Where(e => e.MaNguoiDung == learnerId &&
            e.TrangThai == "CHO_THANH_TOAN" && e.GiaoDichThanhToan.Any(p =>
                (p.TrangThai == "CHO_XU_LY" && p.NgayTao >= cutoff) || p.TrangThai == "THANH_CONG"));
        return pending.Where(e => e.MaGoiHoc != null)
            .SelectMany(e => e.MaGoiHocNavigation.MaKhoaHoc.Select(c => c.MaKhoaHoc))
            .Concat(pending.Where(e => e.MaGoiHoc == null)
                .SelectMany(e => e.ChiTietDangKyKhoaHoc.Select(c => c.MaKhoaHoc))).Distinct();
    }
}
