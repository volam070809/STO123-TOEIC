using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using STO123.Controllers;
using STO123.DTOs.Admin;
using STO123.Models;
using STO123.Services.Auth;

namespace STO123.Tests;

public sealed class AdminPaymentsContractTests
{
    [Fact]
    public void Controller_is_management_only_and_read_only()
    {
        var authorize = Assert.Single(typeof(AdminPaymentsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(AdminAuthorization.ManagementPolicy, authorize.Policy);
        var methods = typeof(AdminPaymentsController).GetMethods()
            .Where(method => method.DeclaringType == typeof(AdminPaymentsController)).ToList();
        Assert.Equal(4, methods.Count);
        Assert.All(methods, method => Assert.NotEmpty(method.GetCustomAttributes(typeof(HttpGetAttribute), true)));
    }

    [Fact]
    public void Detail_contract_exposes_reference_but_not_callback_secrets()
    {
        var names = typeof(AdminPaymentDetailDto).GetProperties().Select(property => property.Name).ToList();
        Assert.Contains("GatewayReference", names);
        Assert.DoesNotContain("SecureHash", names);
        Assert.DoesNotContain("Signature", names);
        Assert.DoesNotContain("HashSecret", names);
    }

    [Theory]
    [InlineData(0, 20, null)]
    [InlineData(1, 101, null)]
    [InlineData(1, 20, "UNKNOWN")]
    public async Task List_rejects_invalid_filters(int page, int pageSize, string? status)
    {
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var controller = new AdminPaymentsController(db);
        var request = new AdminPaymentListQuery { Page = page, PageSize = pageSize, Status = status };
        Assert.IsType<BadRequestObjectResult>(await controller.List(request, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Learners(request, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.LearnerHistory(1, request, CancellationToken.None));
    }

    [Fact]
    public async Task List_rejects_pre_sql_datetime_year_before_executing_summary_query()
    {
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var action = await new AdminPaymentsController(db).List(new AdminPaymentListQuery
        {
            DateFrom = new DateTime(2, 10, 10),
            DateTo = new DateTime(2026, 10, 17)
        }, CancellationToken.None);
        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Contains("1753", badRequest.Value!.ToString());
    }

    [Fact]
    public async Task List_and_detail_preserve_payment_and_purchase_snapshots()
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
            CREATE TABLE CongThanhToan (MaCongThanhToan INTEGER PRIMARY KEY, MaCong TEXT NOT NULL,
                TenCongThanhToan TEXT NOT NULL);
            CREATE TABLE GiaoDichThanhToan (MaGiaoDich INTEGER PRIMARY KEY, MaDangKy INTEGER NOT NULL,
                MaCongThanhToan INTEGER NOT NULL, SoTien TEXT NOT NULL, MaGiaoDichCongThanhToan TEXT NOT NULL,
                TrangThai TEXT NOT NULL, NgayTao TEXT NOT NULL, NgayThanhToan TEXT NULL);
            CREATE TABLE KhoaHoc (MaKhoaHoc INTEGER PRIMARY KEY, TenKhoaHoc TEXT NOT NULL,
                GiaiDoan INTEGER NOT NULL, TrangThai TEXT NOT NULL);
            CREATE TABLE ChiTietDangKyKhoaHoc (MaDangKy INTEGER NOT NULL, MaKhoaHoc INTEGER NOT NULL,
                DonGia TEXT NOT NULL, PRIMARY KEY (MaDangKy, MaKhoaHoc));
            CREATE TABLE GoiHocKhoaHoc (MaGoiHoc INTEGER NOT NULL, MaKhoaHoc INTEGER NOT NULL,
                PRIMARY KEY (MaGoiHoc, MaKhoaHoc));
            INSERT INTO NguoiDung VALUES (1, 'Nguyen An', 'an@example.test', '0901000001', NULL,
                'HOC_VIEN', 'HOAT_DONG', '2026-01-01', NULL);
            INSERT INTO NguoiDung VALUES (2, 'Nguyen An', 'an.khac@example.test', NULL, NULL,
                'HOC_VIEN', 'HOAT_DONG', '2026-01-02', NULL);
            INSERT INTO GoiHoc VALUES (2, 'Combo TOEIC');
            INSERT INTO DangKyGoiHoc VALUES
                (10, 1, NULL, '250000', 30, '2026-02-01', NULL, NULL, 'CHO_THANH_TOAN'),
                (11, 1, 2, '400000', 60, '2026-01-01', '2026-01-01', '2026-03-02', 'DANG_SU_DUNG'),
                (12, 2, NULL, '100000', 30, '2026-10-01', NULL, NULL, 'CHO_THANH_TOAN');
            INSERT INTO CongThanhToan VALUES (7, 'VNPAY', 'VNPay');
            INSERT INTO GiaoDichThanhToan VALUES
                (20, 10, 7, '250000', 'direct-ref-20', 'CHO_XU_LY', '2026-02-01', NULL),
                (21, 10, 7, '250000', 'direct-ref-21', 'THAT_BAI', '2026-02-02', NULL),
                (22, 11, 7, '400000', 'package-ref-22', 'THANH_CONG', '2025-12-31 00:00:00', '2026-01-01 12:00:00'),
                (23, 12, 7, '100000', 'other-success-23', 'THANH_CONG', '2026-10-01', '2026-10-02'),
                (24, 12, 7, '100000', 'other-pending-24', 'CHO_XU_LY', '2026-10-03', NULL),
                (25, 12, 7, '100000', 'other-cancelled-25', 'HUY', '2026-10-04', NULL);
            INSERT INTO KhoaHoc VALUES (30, 'TOEIC 450', 1, 'DANG_MO');
            INSERT INTO ChiTietDangKyKhoaHoc VALUES (10, 30, '250000'), (12, 30, '100000');
            INSERT INTO GoiHocKhoaHoc VALUES (2, 30);
            """);
        var controller = new AdminPaymentsController(db);

        var listAction = await controller.List(new AdminPaymentListQuery
        { Search = "direct-ref-20", Gateway = "vnpay", Page = 1, PageSize = 20 }, CancellationToken.None);
        var page = Assert.IsType<AdminPagedPaymentsDto>(Assert.IsType<OkObjectResult>(listAction).Value);
        var payment = Assert.Single(page.Items);
        Assert.Equal("DIRECT_COURSES", payment.PurchaseType);
        Assert.Equal("direct-ref-20", payment.GatewayReference);
        Assert.Equal(250000m, payment.RegisteredAmount);
        Assert.Equal(1, page.Summary.Pending);
        Assert.Equal("VNPAY", Assert.Single(page.Gateways).Code);

        var detailAction = await controller.Detail(20, CancellationToken.None);
        var detail = Assert.IsType<AdminPaymentDetailDto>(Assert.IsType<OkObjectResult>(detailAction).Value);
        Assert.Equal("direct-ref-20", detail.GatewayReference);
        Assert.Equal("DIRECT_COURSES", detail.Enrollment.PurchaseType);
        Assert.Equal(250000m, Assert.Single(detail.Courses).UnitPriceSnapshot);
        Assert.Equal(2, detail.RelatedPayments.Count);

        var packageAction = await controller.List(new AdminPaymentListQuery
        { Search = "package-ref-22", Status = "THANH_CONG", Page = 1, PageSize = 20 }, CancellationToken.None);
        var packagePage = Assert.IsType<AdminPagedPaymentsDto>(Assert.IsType<OkObjectResult>(packageAction).Value);
        Assert.Equal("PACKAGE", Assert.Single(packagePage.Items).PurchaseType);
        Assert.Equal(400000m, packagePage.Summary.SuccessfulAmount);
        var paidDateAction = await controller.List(new AdminPaymentListQuery
        {
            Status = "THANH_CONG", DateFrom = new DateTime(2026, 1, 1),
            DateTo = new DateTime(2026, 1, 1), Page = 1, PageSize = 20
        }, CancellationToken.None);
        var paidDatePage = Assert.IsType<AdminPagedPaymentsDto>(
            Assert.IsType<OkObjectResult>(paidDateAction).Value);
        Assert.Equal(22, Assert.Single(paidDatePage.Items).PaymentId);
        var paidLearnersAction = await controller.Learners(new AdminPaymentListQuery
        {
            Status = "THANH_CONG", DateFrom = new DateTime(2026, 1, 1),
            DateTo = new DateTime(2026, 1, 1), Page = 1, PageSize = 20
        }, CancellationToken.None);
        var paidLearners = Assert.IsType<AdminPagedPaymentLearnersDto>(
            Assert.IsType<OkObjectResult>(paidLearnersAction).Value);
        var paidLearner = Assert.Single(paidLearners.Items);
        Assert.Equal(1, paidLearner.LearnerId);
        Assert.Equal(1, paidLearner.TotalTransactions);
        Assert.Equal(400000m, paidLearner.SuccessfulAmount);
        var packageDetailAction = await controller.Detail(22, CancellationToken.None);
        var packageDetail = Assert.IsType<AdminPaymentDetailDto>(
            Assert.IsType<OkObjectResult>(packageDetailAction).Value);
        Assert.Equal("Combo TOEIC", packageDetail.Enrollment.PackageName);
        Assert.Null(Assert.Single(packageDetail.Courses).UnitPriceSnapshot);

        var learnersAction = await controller.Learners(new AdminPaymentListQuery
        { Page = 1, PageSize = 1 }, CancellationToken.None);
        var learners = Assert.IsType<AdminPagedPaymentLearnersDto>(
            Assert.IsType<OkObjectResult>(learnersAction).Value);
        Assert.Equal(2, learners.TotalItems);
        Assert.Equal(2, learners.TotalPages);
        Assert.Equal(6, learners.Summary.Total);
        Assert.Equal(2, learners.Summary.Successful);
        Assert.Equal(2, learners.Summary.Pending);
        Assert.Equal(1, learners.Summary.Failed);
        Assert.Equal(500000m, learners.Summary.SuccessfulAmount);
        var secondLearner = Assert.Single(learners.Items);
        Assert.Equal(2, secondLearner.LearnerId);
        Assert.Equal("Nguyen An", secondLearner.LearnerName);
        Assert.Equal(3, secondLearner.TotalTransactions);
        Assert.Equal(1, secondLearner.SuccessfulTransactions);
        Assert.Equal(0, secondLearner.FailedTransactions);
        Assert.Equal(1, secondLearner.PendingTransactions);
        Assert.Equal(100000m, secondLearner.SuccessfulAmount);

        var sameNameAction = await controller.Learners(new AdminPaymentListQuery
        { Search = "Nguyen An", Page = 1, PageSize = 20 }, CancellationToken.None);
        var sameName = Assert.IsType<AdminPagedPaymentLearnersDto>(
            Assert.IsType<OkObjectResult>(sameNameAction).Value);
        Assert.Equal(2, sameName.TotalItems);
        Assert.Equal(2, sameName.Items.Select(item => item.LearnerId).Distinct().Count());

        var failedAction = await controller.Learners(new AdminPaymentListQuery
        { Status = "THAT_BAI", Page = 1, PageSize = 20 }, CancellationToken.None);
        var failed = Assert.IsType<AdminPagedPaymentLearnersDto>(
            Assert.IsType<OkObjectResult>(failedAction).Value);
        var failedLearner = Assert.Single(failed.Items);
        Assert.Equal(1, failedLearner.LearnerId);
        Assert.Equal(1, failedLearner.TotalTransactions);
        Assert.Equal(0m, failedLearner.SuccessfulAmount);

        var historyAction = await controller.LearnerHistory(2, new AdminPaymentListQuery
        { Page = 1, PageSize = 2 }, CancellationToken.None);
        var history = Assert.IsType<AdminPagedPaymentsDto>(
            Assert.IsType<OkObjectResult>(historyAction).Value);
        Assert.Equal(3, history.TotalItems);
        Assert.Equal(2, history.TotalPages);
        Assert.Equal(2, history.Items.Count);
        Assert.All(history.Items, item => Assert.Equal(2, item.LearnerId));
        Assert.Contains(history.Items, item => item.CourseNames.Contains("TOEIC 450"));

        var emptyAction = await controller.Learners(new AdminPaymentListQuery
        { Search = "nobody@example.test", Page = 1, PageSize = 20 }, CancellationToken.None);
        var empty = Assert.IsType<AdminPagedPaymentLearnersDto>(
            Assert.IsType<OkObjectResult>(emptyAction).Value);
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalItems);
        Assert.Equal(0, empty.Summary.Total);
    }
}
