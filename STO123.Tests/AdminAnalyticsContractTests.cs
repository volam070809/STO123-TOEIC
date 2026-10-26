using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using STO123.Controllers;
using STO123.DTOs.Admin;
using STO123.Models;
using STO123.Services.Admin;
using STO123.Services.Auth;

namespace STO123.Tests;

public sealed class AdminAnalyticsContractTests
{
    [Fact]
    public void Controller_is_management_only_and_exposes_seven_read_endpoints()
    {
        var authorize = Assert.Single(typeof(AdminAnalyticsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(AdminAuthorization.ManagementPolicy, authorize.Policy);
        var methods = typeof(AdminAnalyticsController).GetMethods()
            .Where(method => method.DeclaringType == typeof(AdminAnalyticsController)).ToList();
        Assert.Equal(7, methods.Count);
        Assert.All(methods, method => Assert.NotEmpty(method.GetCustomAttributes(typeof(HttpGetAttribute), true)));
        var templates = methods.SelectMany(method => method.GetCustomAttributes(typeof(HttpGetAttribute), true)
            .Cast<HttpGetAttribute>()).Select(attribute => attribute.Template).ToHashSet();
        Assert.Contains("dashboard", templates);
        Assert.Contains("reports/overview", templates);
        Assert.Contains("reports/learners", templates);
        Assert.Contains("reports/enrollments", templates);
        Assert.Contains("reports/packages-courses", templates);
        Assert.Contains("reports/payments", templates);
        Assert.Contains("reports/toeic-results", templates);
    }

    [Theory]
    [InlineData(100, 120, -16.7)]
    [InlineData(150, 100, 50)]
    [InlineData(0, 0, 0)]
    public void Period_comparison_is_stable(decimal current, decimal previous, double expected)
    {
        Assert.Equal(expected, AdminAnalyticsMath.Compare(current, previous).ChangePercent);
    }

    [Fact]
    public void Period_comparison_does_not_invent_percentage_without_baseline()
    {
        Assert.Null(AdminAnalyticsMath.Compare(10, 0).ChangePercent);
    }

    [Theory]
    [InlineData(7, 10, 70)]
    [InlineData(1, 3, 33.3)]
    public void Part_accuracy_uses_answered_questions(int correct, int answered, double expected)
    {
        Assert.Equal(expected, AdminAnalyticsMath.Accuracy(correct, answered));
        Assert.Null(AdminAnalyticsMath.Accuracy(0, 0));
    }

    [Fact]
    public async Task Invalid_dashboard_range_is_rejected_before_database_access()
    {
        await using var db = EmptyDb();
        var controller = new AdminAnalyticsController(db,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero)));
        var action = await controller.Dashboard(new AdminAnalyticsQuery
        { DateFrom = new DateTime(2025, 1, 1), DateTo = new DateTime(2026, 10, 21) }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(action);
    }

    [Fact]
    public async Task Reversed_report_range_is_rejected_before_database_access()
    {
        await using var db = EmptyDb();
        var controller = new AdminAnalyticsController(db, TimeProvider.System);
        var action = await controller.Payments(new AdminAnalyticsQuery
        { DateFrom = new DateTime(2026, 10, 21), DateTo = new DateTime(2026, 10, 1) }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(action);
    }

    [Fact]
    public void Reports_allow_all_history_beyond_366_days_while_dashboard_default_stays_current_year()
    {
        using var db = EmptyDb();
        var controller = new AdminAnalyticsController(db,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero)));
        var method = typeof(AdminAnalyticsController).GetMethod("Range", BindingFlags.Instance | BindingFlags.NonPublic)!;

        object Invoke(AdminAnalyticsQuery query, bool allWhenMissing) =>
            method.Invoke(controller, [query, 366, allWhenMissing])!;
        static object? Item(object tuple, string name) => tuple.GetType().GetField(name)!.GetValue(tuple);

        var allResult = Invoke(new AdminAnalyticsQuery(), true);
        Assert.Null(Item(allResult, "Item2"));
        var allRange = Item(allResult, "Item1")!;
        Assert.True((bool)allRange.GetType().GetProperty("AllHistory")!.GetValue(allRange)!);
        Assert.Equal(new DateTime(1753, 1, 1), allRange.GetType().GetProperty("From")!.GetValue(allRange));

        var dashboardResult = Invoke(new AdminAnalyticsQuery(), false);
        var dashboardRange = Item(dashboardResult, "Item1")!;
        Assert.False((bool)dashboardRange.GetType().GetProperty("AllHistory")!.GetValue(dashboardRange)!);
        Assert.Equal(new DateTime(2026, 1, 1), dashboardRange.GetType().GetProperty("From")!.GetValue(dashboardRange));

        var excessiveResult = Invoke(new AdminAnalyticsQuery
        {
            DateFrom = new DateTime(2024, 1, 1), DateTo = new DateTime(2025, 1, 1)
        }, true);
        Assert.NotNull(Item(excessiveResult, "Item2"));
    }

    [Fact]
    public void Package_performance_translates_with_sql_server_provider()
    {
        using var db = SqlServerDb();
        var sql = AdminAnalyticsQueries.PackagePerformance(db,
                new DateTime(2026, 10, 1), new DateTime(2026, 11, 1))
            .OrderByDescending(item => item.Revenue).Take(100).ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEFT JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("THANH_CONG", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Course_performance_translates_with_sql_server_provider()
    {
        using var db = SqlServerDb();
        var sql = AdminAnalyticsQueries.CoursePerformance(db,
                new DateTime(2026, 10, 1), new DateTime(2026, 11, 1))
            .Where(item => item.Count > 0).OrderByDescending(item => item.Count).Take(100)
            .ToQueryString();

        Assert.Contains("SELECT TOP", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("THANH_CONG", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_payment_aggregation_translates_with_sql_server_provider()
    {
        using var db = SqlServerDb();
        var sql = AdminAnalyticsQueries.DashboardPayments(db,
                new DateTime(2026, 10, 1), new DateTime(2026, 11, 1))
            .ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("THANH_CONG", sql, StringComparison.Ordinal);
        Assert.Contains("CHO_XU_LY", sql, StringComparison.Ordinal);
        Assert.Contains("NgayThanhToan", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dashboard_applies_one_period_to_kpis_trends_recents_and_performance()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE NguoiDung (MaNguoiDung INTEGER PRIMARY KEY, HoTen TEXT NOT NULL, Email TEXT NOT NULL,
                SoDienThoai TEXT NULL, AnhDaiDien TEXT NULL, VaiTro TEXT NOT NULL, TrangThai TEXT NOT NULL,
                NgayTao TEXT NOT NULL, LanDangNhapCuoi TEXT NULL);
            CREATE TABLE GoiHoc (MaGoiHoc INTEGER PRIMARY KEY, TenGoiHoc TEXT NOT NULL);
            CREATE TABLE KhoaHoc (MaKhoaHoc INTEGER PRIMARY KEY, TenKhoaHoc TEXT NOT NULL,
                GiaiDoan INTEGER NOT NULL, TrangThai TEXT NOT NULL);
            CREATE TABLE DangKyGoiHoc (MaDangKy INTEGER PRIMARY KEY, MaNguoiDung INTEGER NOT NULL,
                MaGoiHoc INTEGER NULL, GiaDangKy TEXT NOT NULL, SoNgaySuDung INTEGER NOT NULL,
                NgayDangKy TEXT NOT NULL, NgayBatDau TEXT NULL, NgayKetThuc TEXT NULL, TrangThai TEXT NOT NULL);
            CREATE TABLE CongThanhToan (MaCongThanhToan INTEGER PRIMARY KEY, MaCong TEXT NOT NULL,
                TenCongThanhToan TEXT NOT NULL);
            CREATE TABLE GiaoDichThanhToan (MaGiaoDich INTEGER PRIMARY KEY, MaDangKy INTEGER NOT NULL,
                MaCongThanhToan INTEGER NOT NULL, SoTien TEXT NOT NULL, MaGiaoDichCongThanhToan TEXT NOT NULL,
                TrangThai TEXT NOT NULL, NgayTao TEXT NOT NULL, NgayThanhToan TEXT NULL);
            CREATE TABLE ChiTietDangKyKhoaHoc (MaDangKy INTEGER NOT NULL, MaKhoaHoc INTEGER NOT NULL,
                DonGia TEXT NOT NULL, PRIMARY KEY (MaDangKy, MaKhoaHoc));
            CREATE TABLE GoiHocKhoaHoc (MaGoiHoc INTEGER NOT NULL, MaKhoaHoc INTEGER NOT NULL,
                PRIMARY KEY (MaGoiHoc, MaKhoaHoc));
            INSERT INTO NguoiDung VALUES
                (1, 'Trong Ky', 'in@example.test', NULL, NULL, 'HOC_VIEN', 'HOAT_DONG', '2026-10-10 00:00:00', NULL),
                (2, 'Ngoai Ky', 'out@example.test', NULL, NULL, 'HOC_VIEN', 'HOAT_DONG', '2026-10-09 00:00:00', NULL);
            INSERT INTO GoiHoc VALUES (1, 'Goi trong ky');
            INSERT INTO KhoaHoc VALUES (1, 'Khoa trong ky', 1, 'DANG_MO');
            INSERT INTO DangKyGoiHoc VALUES
                (10, 1, 1, '100000', 30, '2026-10-11 00:00:00', NULL, NULL, 'CHO_THANH_TOAN'),
                (11, 2, NULL, '200000', 30, '2026-10-09 00:00:00', NULL, NULL, 'CHO_THANH_TOAN');
            INSERT INTO CongThanhToan VALUES (1, 'VNPAY', 'VNPay');
            INSERT INTO GiaoDichThanhToan VALUES
                (20, 10, 1, '100000', 'paid-in-period', 'THANH_CONG', '2026-10-01 00:00:00', '2026-10-12 12:00:00'),
                (21, 10, 1, '100000', 'pending-in-period', 'CHO_XU_LY', '2026-10-13 00:00:00', NULL),
                (22, 11, 1, '200000', 'failed-outside', 'THAT_BAI', '2026-10-09 00:00:00', NULL);
            INSERT INTO GoiHocKhoaHoc VALUES (1, 1);
            INSERT INTO ChiTietDangKyKhoaHoc VALUES (11, 1, '200000');
            """);

        var controller = new AdminAnalyticsController(db,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero)));
        var action = await controller.Dashboard(new AdminAnalyticsQuery
        { DateFrom = new DateTime(2026, 10, 10), DateTo = new DateTime(2026, 10, 17) }, CancellationToken.None);
        var dashboard = Assert.IsType<AdminDashboardDto>(Assert.IsType<OkObjectResult>(action).Value);

        Assert.Equal(1, dashboard.NewLearners);
        Assert.Equal(1, dashboard.Enrollments);
        Assert.Equal(100000m, dashboard.SuccessfulRevenue);
        Assert.Equal(1, dashboard.PendingPayments);
        Assert.Equal(0, dashboard.FailedPayments);
        Assert.Equal(8, dashboard.LearnerGrowth.Count);
        Assert.Equal(1, dashboard.LearnerGrowth.Sum(point => point.Count));
        Assert.Equal(100000m, dashboard.RevenueTrend.Sum(point => point.Amount));
        Assert.Equal(10, Assert.Single(dashboard.RecentEnrollments).EnrollmentId);
        Assert.Equal(21, Assert.Single(dashboard.RecentPayments).PaymentId);
        Assert.All(dashboard.OfferingPerformance, item => Assert.Equal(1, item.Count));

        var defaultAction = await controller.Dashboard(new AdminAnalyticsQuery(), CancellationToken.None);
        var currentYear = Assert.IsType<AdminDashboardDto>(Assert.IsType<OkObjectResult>(defaultAction).Value);
        Assert.Equal(new DateTime(2026, 1, 1), currentYear.Range.From);
        Assert.Equal(new DateTime(2026, 10, 22), currentYear.Range.To);
        Assert.Equal(10, currentYear.LearnerGrowth.Count);

        var leapYearAction = await controller.Dashboard(new AdminAnalyticsQuery
        { DateFrom = new DateTime(2024, 1, 1), DateTo = new DateTime(2024, 12, 31) }, CancellationToken.None);
        var leapYear = Assert.IsType<AdminDashboardDto>(Assert.IsType<OkObjectResult>(leapYearAction).Value);
        Assert.Equal(366, leapYear.Range.Days);
        Assert.Equal(12, leapYear.LearnerGrowth.Count);

        var emptyAction = await controller.Dashboard(new AdminAnalyticsQuery
        { DateFrom = new DateTime(2026, 10, 17), DateTo = new DateTime(2026, 10, 17) }, CancellationToken.None);
        var empty = Assert.IsType<AdminDashboardDto>(Assert.IsType<OkObjectResult>(emptyAction).Value);
        Assert.Equal(0, empty.NewLearners);
        Assert.Equal(0, empty.Enrollments);
        Assert.Empty(empty.RecentEnrollments);
        Assert.Empty(empty.RecentPayments);
        Assert.Empty(empty.OfferingPerformance);
    }

    private static ToeicDbContext EmptyDb() => new(new DbContextOptionsBuilder<ToeicDbContext>()
        .UseSqlite("Data Source=:memory:").Options);

    private static ToeicDbContext SqlServerDb() => new(new DbContextOptionsBuilder<ToeicDbContext>()
        .UseSqlServer("Server=translation-only.invalid;Database=translation;Integrated Security=true;Encrypt=false")
        .Options);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
