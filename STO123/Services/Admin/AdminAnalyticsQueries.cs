using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Admin;

public sealed class AdminPerformanceRow
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public int Count { get; init; }
    public decimal Revenue { get; init; }
}

public sealed class AdminDashboardPaymentRow
{
    public DateTime Date { get; init; }
    public int Successful { get; init; }
    public int Pending { get; init; }
    public int Failed { get; init; }
    public decimal Revenue { get; init; }
}

public static class AdminAnalyticsQueries
{
    public static IQueryable<AdminDashboardPaymentRow> DashboardPayments(
        ToeicDbContext db, DateTime from, DateTime toExclusive) =>
        db.GiaoDichThanhToan.AsNoTracking()
            .Where(payment =>
                (payment.TrangThai == "THANH_CONG" && payment.NgayThanhToan.HasValue
                    ? payment.NgayThanhToan.Value : payment.NgayTao) >= from &&
                (payment.TrangThai == "THANH_CONG" && payment.NgayThanhToan.HasValue
                    ? payment.NgayThanhToan.Value : payment.NgayTao) < toExclusive)
            .GroupBy(payment => (payment.TrangThai == "THANH_CONG" && payment.NgayThanhToan.HasValue
                ? payment.NgayThanhToan.Value : payment.NgayTao).Date)
            .Select(group => new AdminDashboardPaymentRow
            {
                Date = group.Key,
                Successful = group.Count(payment => payment.TrangThai == "THANH_CONG"),
                Pending = group.Count(payment => payment.TrangThai == "CHO_XU_LY"),
                Failed = group.Count(payment => payment.TrangThai == "THAT_BAI"),
                Revenue = group.Where(payment => payment.TrangThai == "THANH_CONG")
                    .Sum(payment => (decimal?)payment.SoTien) ?? 0m
            });

    public static IQueryable<AdminPerformanceRow> PackagePerformance(
        ToeicDbContext db, DateTime from, DateTime toExclusive)
    {
        // Sum directly from payment rows so each successful transaction is counted exactly once.
        // The correlated scalar aggregate avoids SelectMany over a grouped navigation collection,
        // which the SQL Server provider cannot translate.
        return db.DangKyGoiHoc.AsNoTracking()
            .Where(enrollment => enrollment.MaGoiHoc != null &&
                enrollment.NgayDangKy >= from && enrollment.NgayDangKy < toExclusive)
            .GroupBy(enrollment => new
            {
                Id = enrollment.MaGoiHoc!.Value,
                enrollment.MaGoiHocNavigation.TenGoiHoc
            })
            .Select(group => new AdminPerformanceRow
            {
                Id = group.Key.Id,
                Name = group.Key.TenGoiHoc,
                Type = "PACKAGE",
                Count = group.Count(),
                Revenue = db.GiaoDichThanhToan.AsNoTracking()
                    .Where(payment => payment.TrangThai == "THANH_CONG" &&
                        payment.MaDangKyNavigation.MaGoiHoc == group.Key.Id &&
                        payment.MaDangKyNavigation.NgayDangKy >= from &&
                        payment.MaDangKyNavigation.NgayDangKy < toExclusive)
                    .Sum(payment => (decimal?)payment.SoTien) ?? 0m
            });
    }

    public static IQueryable<AdminPerformanceRow> CoursePerformance(
        ToeicDbContext db, DateTime from, DateTime toExclusive) =>
        db.KhoaHoc.AsNoTracking().Select(course => new AdminPerformanceRow
        {
            Id = course.MaKhoaHoc,
            Name = course.TenKhoaHoc,
            Type = "COURSE",
            Count = course.ChiTietDangKyKhoaHoc.Count(item => item.MaDangKyNavigation.MaGoiHoc == null &&
                item.MaDangKyNavigation.NgayDangKy >= from &&
                item.MaDangKyNavigation.NgayDangKy < toExclusive) +
            course.MaGoiHoc.SelectMany(package => package.DangKyGoiHoc).Count(enrollment =>
                enrollment.NgayDangKy >= from && enrollment.NgayDangKy < toExclusive),
            Revenue = course.ChiTietDangKyKhoaHoc.Where(item => item.MaDangKyNavigation.MaGoiHoc == null &&
                item.MaDangKyNavigation.NgayDangKy >= from &&
                item.MaDangKyNavigation.NgayDangKy < toExclusive &&
                item.MaDangKyNavigation.GiaoDichThanhToan.Any(payment => payment.TrangThai == "THANH_CONG"))
                .Sum(item => item.DonGia)
        });
}
