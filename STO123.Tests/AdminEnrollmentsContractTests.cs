using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using STO123.Controllers;
using STO123.DTOs.Admin;
using STO123.Models;
using STO123.Services.Auth;

namespace STO123.Tests;

public sealed class AdminEnrollmentsContractTests
{
    [Fact]
    public void Controller_requires_management_policy()
    {
        var authorize = Assert.Single(typeof(AdminEnrollmentsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(AdminAuthorization.ManagementPolicy, authorize.Policy);
        Assert.Equal("ADMIN_QUAN_LY", AdminAuthorization.ManagementRole);
    }

    [Fact]
    public void Controller_exposes_read_endpoints_only()
    {
        var methods = typeof(AdminEnrollmentsController).GetMethods()
            .Where(method => method.DeclaringType == typeof(AdminEnrollmentsController)).ToList();
        Assert.Equal(2, methods.Count);
        Assert.All(methods, method => Assert.NotEmpty(method.GetCustomAttributes(typeof(HttpGetAttribute), true)));
    }

    [Fact]
    public void Payment_dto_does_not_expose_gateway_transaction_reference()
    {
        var names = typeof(AdminEnrollmentPaymentDto).GetProperties().Select(property => property.Name).ToList();
        Assert.DoesNotContain("GatewayTransactionId", names);
        Assert.DoesNotContain("MaGiaoDichCongThanhToan", names);
        Assert.Contains("PaymentId", names);
    }

    [Theory]
    [InlineData(0, 20, null, null)]
    [InlineData(1, 101, null, null)]
    [InlineData(1, 20, "UNKNOWN", null)]
    [InlineData(1, 20, null, 0)]
    public async Task List_rejects_invalid_filters(int page, int pageSize, string? status, int? packageId)
    {
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var controller = new AdminEnrollmentsController(db, TimeProvider.System);
        var action = await controller.List(new AdminEnrollmentListQuery
        {
            Page = page, PageSize = pageSize, Status = status, PackageId = packageId
        }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(action);
    }

    [Fact]
    public async Task List_rejects_reversed_date_range_before_querying_database()
    {
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var action = await new AdminEnrollmentsController(db, TimeProvider.System).List(
            new AdminEnrollmentListQuery { DateFrom = new DateTime(2026, 2, 2), DateTo = new DateTime(2026, 2, 1) },
            CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(action);
    }

    [Fact]
    public async Task List_supports_direct_course_purchase_without_package()
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
            CREATE TABLE DangKyGoiHoc (MaDangKy INTEGER PRIMARY KEY, MaNguoiDung INTEGER NOT NULL,
                MaGoiHoc INTEGER NULL, GiaDangKy TEXT NOT NULL, SoNgaySuDung INTEGER NOT NULL,
                NgayDangKy TEXT NOT NULL, NgayBatDau TEXT NULL, NgayKetThuc TEXT NULL, TrangThai TEXT NOT NULL);
            CREATE TABLE GiaoDichThanhToan (MaGiaoDich INTEGER PRIMARY KEY, MaDangKy INTEGER NOT NULL,
                MaCongThanhToan INTEGER NOT NULL, SoTien TEXT NOT NULL, MaGiaoDichCongThanhToan TEXT NOT NULL,
                TrangThai TEXT NOT NULL, NgayTao TEXT NOT NULL, NgayThanhToan TEXT NULL);
            CREATE TABLE CongThanhToan (MaCongThanhToan INTEGER PRIMARY KEY, MaCong TEXT NOT NULL,
                TenCongThanhToan TEXT NOT NULL);
            CREATE TABLE KhoaHoc (MaKhoaHoc INTEGER PRIMARY KEY, TenKhoaHoc TEXT NOT NULL,
                GiaiDoan INTEGER NOT NULL, TrangThai TEXT NOT NULL);
            CREATE TABLE GoiHocKhoaHoc (MaGoiHoc INTEGER NOT NULL, MaKhoaHoc INTEGER NOT NULL,
                PRIMARY KEY (MaGoiHoc, MaKhoaHoc));
            CREATE TABLE ChiTietDangKyKhoaHoc (MaDangKy INTEGER NOT NULL, MaKhoaHoc INTEGER NOT NULL,
                DonGia TEXT NOT NULL, PRIMARY KEY (MaDangKy, MaKhoaHoc));
            INSERT INTO NguoiDung VALUES (1, 'Nguyen An', 'an@example.test', NULL, NULL,
                'HOC_VIEN', 'HOAT_DONG', '2026-01-01', NULL);
            INSERT INTO DangKyGoiHoc VALUES (10, 1, NULL, '250000', 30, '2026-02-01',
                '2026-02-01', '2026-03-03', 'DANG_SU_DUNG');
            INSERT INTO GoiHoc VALUES (2, 'Combo TOEIC');
            INSERT INTO DangKyGoiHoc VALUES (11, 1, 2, '400000', 60, '2025-01-01',
                '2025-01-01', '2025-03-02', 'HET_HAN');
            INSERT INTO CongThanhToan VALUES (1, 'VNPAY', 'VNPay');
            INSERT INTO GiaoDichThanhToan VALUES (20, 10, 1, '250000', 'not-returned-by-api',
                'THANH_CONG', '2026-02-01', '2026-02-01');
            INSERT INTO GiaoDichThanhToan VALUES (21, 11, 1, '400000', 'also-not-returned',
                'THANH_CONG', '2025-01-01', '2025-01-01');
            INSERT INTO KhoaHoc VALUES (30, 'TOEIC 450', 1, 'DANG_MO');
            INSERT INTO ChiTietDangKyKhoaHoc VALUES (10, 30, '250000');
            INSERT INTO GoiHocKhoaHoc VALUES (2, 30);
            """);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero));

        var action = await new AdminEnrollmentsController(db, clock).List(
            new AdminEnrollmentListQuery { Search = "10", Page = 1, PageSize = 20 }, CancellationToken.None);

        var response = Assert.IsType<AdminPagedEnrollmentsDto>(Assert.IsType<OkObjectResult>(action).Value);
        var enrollment = Assert.Single(response.Items);
        Assert.Equal("DIRECT_COURSES", enrollment.PurchaseType);
        Assert.Null(enrollment.PackageId);
        Assert.Equal("SUCCESS", enrollment.PaymentState);
        Assert.Equal("ACTIVE", enrollment.AccessState);
        Assert.Equal(250000m, Assert.Single(enrollment.Courses).UnitPriceSnapshot);
        Assert.Equal(1, response.Summary.DirectCoursePurchases);

        var detailAction = await new AdminEnrollmentsController(db, clock).Detail(10, CancellationToken.None);
        var detail = Assert.IsType<AdminEnrollmentDetailDto>(Assert.IsType<OkObjectResult>(detailAction).Value);
        Assert.True(detail.Entitlement.EnrollmentGrantsAccess);
        Assert.Equal([30], detail.Entitlement.LearnerActiveCourseIds);
        Assert.Equal("VNPAY", Assert.Single(detail.Payments).GatewayCode);
        Assert.Equal("PACKAGE", Assert.Single(detail.History).PurchaseType);

        var packageAction = await new AdminEnrollmentsController(db, clock).List(
            new AdminEnrollmentListQuery { PackageId = 2, Page = 1, PageSize = 20 }, CancellationToken.None);
        var packageResponse = Assert.IsType<AdminPagedEnrollmentsDto>(
            Assert.IsType<OkObjectResult>(packageAction).Value);
        var packageEnrollment = Assert.Single(packageResponse.Items);
        Assert.Equal("PACKAGE", packageEnrollment.PurchaseType);
        Assert.Equal("Combo TOEIC", packageEnrollment.PackageName);
        Assert.Null(Assert.Single(packageEnrollment.Courses).UnitPriceSnapshot);
        Assert.Equal("EXPIRED", packageEnrollment.AccessState);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
