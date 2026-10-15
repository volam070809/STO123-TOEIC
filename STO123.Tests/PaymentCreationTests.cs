using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using STO123.Controllers;
using STO123.Models;
using STO123.Services.Payment;

namespace STO123.Tests;

public sealed class PaymentCreationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GatewaySignsThePersistedReferenceAndWholeVndAmount()
    {
        var gateway = new VNPayGateway(Options.Create(Settings(enabled: true)));
        const string reference = "a01b2345c6784cde9876f01234567890";
        var url = gateway.CreateCheckoutUrl(reference, 120_000m, "127.0.0.1", Now);
        var uri = new Uri(url);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);

        Assert.Equal(VNPayOptions.SandboxPaymentUrl, uri.GetLeftPart(UriPartial.Path));
        Assert.Equal(reference, query["vnp_TxnRef"].ToString());
        Assert.Equal("12000000", query["vnp_Amount"].ToString());
        Assert.Equal("VND", query["vnp_CurrCode"].ToString());
        Assert.Equal("20261015100000", query["vnp_CreateDate"].ToString());
        Assert.Equal("20261015101500", query["vnp_ExpireDate"].ToString());
        Assert.Equal("https://example.test/payment/return", query["vnp_ReturnUrl"].ToString());

        var unsigned = uri.Query.TrimStart('?').Split("&vnp_SecureHash=", 2)[0];
        var expectedHash = Convert.ToHexStringLower(HMACSHA512.HashData(
            Encoding.UTF8.GetBytes("test-secret"), Encoding.UTF8.GetBytes(unsigned)));
        Assert.Equal(expectedHash, query["vnp_SecureHash"].ToString());
    }

    [Fact]
    public void LocalFrontendReturnIsAllowedButRemotePlainHttpIsRejected()
    {
        var settings = Settings(enabled: true);
        settings.FrontendResultUrl = "http://localhost:5173/payment/result";
        Assert.True(VNPayOptions.IsValid(settings));
        settings.FrontendResultUrl = "http://example.test/payment/result";
        Assert.False(VNPayOptions.IsValid(settings));
    }

    [Fact]
    public async Task DisabledCheckoutNeverTouchesTheDatabase()
    {
        var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite("Data Source=file:payment-disabled?mode=memory&cache=shared").Options);
        await using (db)
        {
            var service = new PaymentCreationService(db, new VNPayGateway(Options.Create(Settings())),
                new FixedClock());
            var controller = Controller(service, Settings(), 1, db);
            var result = await controller.Create(new CreatePaymentRequest(1), CancellationToken.None);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable,
                Assert.IsType<ObjectResult>(result).StatusCode);
        }
    }

    [Fact]
    public async Task CreatesOnePendingEnrollmentAndPaymentFromDatabasePrice()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var result = await fixture.Controller(1).Create(new CreatePaymentRequest(1), CancellationToken.None);
        var response = Assert.IsType<CreatePaymentResponse>(Assert.IsType<OkObjectResult>(result).Value);
        var enrollment = await fixture.Db.DangKyGoiHoc.SingleAsync();
        var payment = await fixture.Db.GiaoDichThanhToan.SingleAsync();
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(response.CheckoutUrl).Query);

        Assert.Equal(payment.MaGiaoDich, response.MaGiaoDich);
        Assert.Equal(1, enrollment.MaNguoiDung);
        Assert.Equal(1, enrollment.MaGoiHoc);
        Assert.Equal(120_000m, enrollment.GiaDangKy);
        Assert.Equal(90, enrollment.SoNgaySuDung);
        Assert.Equal("CHO_THANH_TOAN", enrollment.TrangThai);
        Assert.Equal(enrollment.MaDangKy, payment.MaDangKy);
        Assert.Equal(120_000m, payment.SoTien);
        Assert.Equal("CHO_XU_LY", payment.TrangThai);
        Assert.Equal(payment.MaGiaoDichCongThanhToan, query["vnp_TxnRef"].ToString());
    }

    [Fact]
    public async Task CustomCoursesQuoteAndCheckoutStoreExactPriceSnapshots()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var controller = fixture.Controller(1);
        var quoteResult = await controller.Quote(new CoursePurchaseRequest([2, 1], 0),
            CancellationToken.None);
        var quote = Assert.IsType<CourseQuote>(Assert.IsType<OkObjectResult>(quoteResult).Value);
        Assert.Equal([1, 2], quote.Courses.Select(c => c.CourseId));
        Assert.Equal([99_000m, 199_000m], quote.Courses.Select(c => c.Price));
        Assert.Equal(298_000m, quote.Total);
        Assert.Equal(30, quote.ValidDays);

        var created = await controller.CreateCourses(new CoursePurchaseRequest([2, 1], quote.Total),
            CancellationToken.None);
        var response = Assert.IsType<CreatePaymentResponse>(Assert.IsType<OkObjectResult>(created).Value);
        var enrollment = await fixture.Db.DangKyGoiHoc.SingleAsync();
        var details = await fixture.Db.ChiTietDangKyKhoaHoc.OrderBy(d => d.MaKhoaHoc).ToListAsync();
        var payment = await fixture.Db.GiaoDichThanhToan.SingleAsync();
        Assert.Null(enrollment.MaGoiHoc);
        Assert.Equal(30, enrollment.SoNgaySuDung);
        Assert.Equal(298_000m, enrollment.GiaDangKy);
        Assert.Equal([1, 2], details.Select(d => d.MaKhoaHoc));
        Assert.Equal([99_000m, 199_000m], details.Select(d => d.DonGia));
        Assert.Equal(298_000m, payment.SoTien);
        Assert.Equal(payment.MaGiaoDich, response.MaGiaoDich);
        Assert.Equal("29800000", Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(
            new Uri(response.CheckoutUrl).Query)["vnp_Amount"].ToString());
    }

    [Fact]
    public async Task CustomCheckoutRejectsDuplicateCoursesAndChangedQuoteWithoutWrites()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var controller = fixture.Controller(1);
        Assert.IsType<BadRequestObjectResult>(await controller.Quote(
            new CoursePurchaseRequest([1, 1], 0), CancellationToken.None));
        Assert.IsType<ConflictObjectResult>(await controller.CreateCourses(
            new CoursePurchaseRequest([1], 1m), CancellationToken.None));
        Assert.Empty(await fixture.Db.DangKyGoiHoc.ToListAsync());
        Assert.Empty(await fixture.Db.ChiTietDangKyKhoaHoc.ToListAsync());
        Assert.Empty(await fixture.Db.GiaoDichThanhToan.ToListAsync());
    }

    [Fact]
    public async Task EveryNonEmptyCourseSelectionHasAdditivePrice()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var controller = fixture.Controller(1);
        var prices = new[] { 99_000m, 199_000m, 299_000m };
        for (var mask = 1; mask < 8; mask++)
        {
            var ids = Enumerable.Range(1, 3).Where(id => (mask & (1 << (id - 1))) != 0).ToArray();
            var result = await controller.Quote(new CoursePurchaseRequest(ids, 0), CancellationToken.None);
            var quote = Assert.IsType<CourseQuote>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(ids, quote.Courses.Select(c => c.CourseId));
            Assert.Equal(ids.Sum(id => prices[id - 1]), quote.Total);
            Assert.Equal(30, quote.ValidDays);
        }
    }

    [Fact]
    public async Task LegacyPendingCourseBlocksOnlyOverlappingCustomSelection()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var controller = fixture.Controller(1);
        Assert.IsType<OkObjectResult>(await controller.Create(new CreatePaymentRequest(1),
            CancellationToken.None));
        Assert.IsType<ConflictObjectResult>(await controller.Quote(
            new CoursePurchaseRequest([1], 0), CancellationToken.None));
        Assert.IsType<OkObjectResult>(await controller.Quote(
            new CoursePurchaseRequest([2, 3], 0), CancellationToken.None));
    }

    [Fact]
    public async Task CustomPaymentInsertFailureRollsBackEnrollmentAndCourseItems()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        await fixture.Db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_custom_payment BEFORE INSERT ON GiaoDichThanhToan
            BEGIN SELECT RAISE(ABORT, 'payment insert rejected'); END;
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Controller(1).CreateCourses(
            new CoursePurchaseRequest([1, 2], 298_000m), CancellationToken.None));
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.DangKyGoiHoc.ToListAsync());
        Assert.Empty(await fixture.Db.ChiTietDangKyKhoaHoc.ToListAsync());
        Assert.Empty(await fixture.Db.GiaoDichThanhToan.ToListAsync());
    }

    [Fact]
    public async Task InvalidLearnerPackageAndExistingEnrollmentDoNotWrite()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        Assert.IsType<UnauthorizedResult>(await fixture.Controller(null)
            .Create(new CreatePaymentRequest(1), CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(await fixture.Controller(1)
            .Create(new CreatePaymentRequest(999), CancellationToken.None));

        fixture.Db.DangKyGoiHoc.Add(new DangKyGoiHoc
        {
            MaNguoiDung = 1, MaGoiHoc = 1, GiaDangKy = 120_000m, SoNgaySuDung = 90,
            NgayDangKy = Now.ToOffset(TimeSpan.FromHours(7)).DateTime.AddDays(-1),
            NgayBatDau = Now.ToOffset(TimeSpan.FromHours(7)).DateTime.AddDays(-1),
            NgayKetThuc = Now.ToOffset(TimeSpan.FromHours(7)).DateTime.AddDays(89),
            TrangThai = "DANG_SU_DUNG"
        });
        await fixture.Db.SaveChangesAsync();
        var activeEnrollment = await fixture.Db.DangKyGoiHoc.SingleAsync();
        fixture.Db.GiaoDichThanhToan.Add(new GiaoDichThanhToan
        {
            MaDangKy = activeEnrollment.MaDangKy, MaCongThanhToan = 1,
            SoTien = 120_000m, MaGiaoDichCongThanhToan = "paid-existing",
            TrangThai = "THANH_CONG", NgayTao = Now.UtcDateTime,
            NgayThanhToan = Now.UtcDateTime
        });
        await fixture.Db.SaveChangesAsync();
        Assert.IsType<ConflictObjectResult>(await fixture.Controller(1)
            .Create(new CreatePaymentRequest(1), CancellationToken.None));
        Assert.Single(await fixture.Db.GiaoDichThanhToan.ToListAsync());
        Assert.Single(await fixture.Db.DangKyGoiHoc.ToListAsync());
    }

    [Fact]
    public async Task GatewayFailureLeavesNoPaymentRecords()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var service = new PaymentCreationService(fixture.Db, new ThrowingGateway(), new FixedClock());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(1, 1,
            "127.0.0.1", CancellationToken.None));
        Assert.Empty(await fixture.Db.DangKyGoiHoc.ToListAsync());
        Assert.Empty(await fixture.Db.GiaoDichThanhToan.ToListAsync());
    }

    [Fact]
    public async Task RepeatedRequestWhileCheckoutIsPendingDoesNotCreateAnotherPayment()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        Assert.IsType<OkObjectResult>(await fixture.Controller(1)
            .Create(new CreatePaymentRequest(1), CancellationToken.None));
        Assert.IsType<ConflictObjectResult>(await fixture.Controller(1)
            .Create(new CreatePaymentRequest(1), CancellationToken.None));
        Assert.Single(await fixture.Db.DangKyGoiHoc.ToListAsync());
        Assert.Single(await fixture.Db.GiaoDichThanhToan.ToListAsync());
    }

    [Fact]
    public async Task PaidCourseDoesNotBlockPurchaseOfDifferentCourse()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var first = Assert.IsType<OkObjectResult>(await fixture.Controller(1)
            .Create(new CreatePaymentRequest(1), CancellationToken.None));
        var firstId = Assert.IsType<CreatePaymentResponse>(first.Value).MaGiaoDich;
        var firstPayment = await fixture.Db.GiaoDichThanhToan.SingleAsync(p => p.MaGiaoDich == firstId);
        var firstEnrollment = await fixture.Db.DangKyGoiHoc.SingleAsync(e => e.MaDangKy == firstPayment.MaDangKy);
        firstPayment.TrangThai = "THANH_CONG";
        firstPayment.NgayThanhToan = Now.ToOffset(TimeSpan.FromHours(7)).DateTime;
        firstEnrollment.TrangThai = "DANG_SU_DUNG";
        firstEnrollment.NgayBatDau = Now.ToOffset(TimeSpan.FromHours(7)).DateTime;
        firstEnrollment.NgayKetThuc = firstEnrollment.NgayBatDau.Value.AddDays(90);
        await fixture.Db.SaveChangesAsync();

        var second = Assert.IsType<OkObjectResult>(await fixture.Controller(1)
            .Create(new CreatePaymentRequest(2), CancellationToken.None));
        var secondId = Assert.IsType<CreatePaymentResponse>(second.Value).MaGiaoDich;
        var secondPayment = await fixture.Db.GiaoDichThanhToan.SingleAsync(p => p.MaGiaoDich == secondId);
        Assert.Equal(2, (await fixture.Db.DangKyGoiHoc.SingleAsync(e => e.MaDangKy ==
            secondPayment.MaDangKy)).MaGoiHoc);
        Assert.Equal(2, await fixture.Db.GiaoDichThanhToan.CountAsync());
    }

    [Fact]
    public async Task OldPendingCheckoutStillBlocksAnotherAttempt()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        Assert.IsType<OkObjectResult>(await fixture.Controller(1)
            .Create(new CreatePaymentRequest(1), CancellationToken.None));
        var enrollment = await fixture.Db.DangKyGoiHoc.SingleAsync();
        enrollment.NgayDangKy = Now.ToOffset(TimeSpan.FromHours(7)).DateTime.AddDays(-2);
        await fixture.Db.SaveChangesAsync();
        Assert.IsType<ConflictObjectResult>(await fixture.Controller(1)
            .Create(new CreatePaymentRequest(1), CancellationToken.None));
        Assert.Single(await fixture.Db.GiaoDichThanhToan.ToListAsync());
    }

    [Fact]
    public async Task FailedPaymentInsertRollsBackTheEnrollment()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        await fixture.Db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_payment BEFORE INSERT ON GiaoDichThanhToan
            BEGIN SELECT RAISE(ABORT, 'payment insert rejected'); END;
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Controller(1)
            .Create(new CreatePaymentRequest(1), CancellationToken.None));
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.DangKyGoiHoc.ToListAsync());
        Assert.Empty(await fixture.Db.GiaoDichThanhToan.ToListAsync());
    }

    private static VNPayOptions Settings(bool enabled = false) => new()
    {
        TmnCode = "TEST1234", HashSecret = "test-secret",
        PaymentUrl = VNPayOptions.SandboxPaymentUrl,
        ReturnUrl = "https://example.test/payment/return",
        FrontendResultUrl = "https://example.test/payment/result",
        CheckoutEnabled = enabled
    };

    private static PaymentController Controller(PaymentCreationService service, VNPayOptions settings,
        int? userId, ToeicDbContext db)
    {
        var claims = userId is null ? Array.Empty<Claim>() :
            [new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()), new Claim(ClaimTypes.Role, "HOC_VIEN")];
        return new PaymentController(service,
            new PaymentConfirmationService(db, new FixedClock(),
                NullLogger<PaymentConfirmationService>.Instance),
            new VNPayCallbackVerifier(Options.Create(settings)), db, Options.Create(settings),
            NullLogger<PaymentController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
                    Connection = { RemoteIpAddress = IPAddress.Loopback }
                }
            }
        };
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ThrowingGateway : IPaymentGateway
    {
        public string CreateCheckoutUrl(string transactionRef, decimal amount, string clientIp,
            DateTimeOffset createdAt) => throw new InvalidOperationException("Gateway unavailable.");
    }

    private sealed class PaymentDb(SqliteConnection connection, ToeicDbContext db) : IAsyncDisposable
    {
        public ToeicDbContext Db { get; } = db;

        public static async Task<PaymentDb> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE NguoiDung (MaNguoiDung INTEGER PRIMARY KEY, HoTen TEXT NOT NULL,
                    Email TEXT NOT NULL, SoDienThoai TEXT NULL, AnhDaiDien TEXT NULL,
                    VaiTro TEXT NOT NULL, TrangThai TEXT NOT NULL, NgayTao TEXT NOT NULL,
                    LanDangNhapCuoi TEXT NULL);
                CREATE TABLE GoiHoc (MaGoiHoc INTEGER PRIMARY KEY, TenGoiHoc TEXT NOT NULL,
                    Gia TEXT NOT NULL, SoNgaySuDung INTEGER NOT NULL, MoTa TEXT NULL,
                    TrangThai TEXT NOT NULL, NgayTao TEXT NOT NULL);
                CREATE TABLE KhoaHoc (MaKhoaHoc INTEGER PRIMARY KEY, TenKhoaHoc TEXT NOT NULL,
                    GiaiDoan INTEGER NOT NULL, DiemMucTieuToiDa INTEGER NOT NULL, MoTa TEXT NULL,
                    GiaBanLe TEXT NULL,
                    TrangThai TEXT NOT NULL, MaAdminNoiDung INTEGER NULL, NgayTao TEXT NOT NULL,
                    DuongDanAnhDaiDien TEXT NULL);
                CREATE TABLE GoiHocKhoaHoc (MaGoiHoc INTEGER NOT NULL, MaKhoaHoc INTEGER NOT NULL,
                    PRIMARY KEY (MaGoiHoc, MaKhoaHoc));
                CREATE TABLE ChiTietDangKyKhoaHoc (MaDangKy INTEGER NOT NULL,
                    MaKhoaHoc INTEGER NOT NULL, DonGia TEXT NOT NULL,
                    PRIMARY KEY (MaDangKy, MaKhoaHoc));
                CREATE TABLE CongThanhToan (MaCongThanhToan INTEGER PRIMARY KEY,
                    MaCong TEXT NOT NULL, TenCongThanhToan TEXT NOT NULL);
                CREATE TABLE DangKyGoiHoc (MaDangKy INTEGER PRIMARY KEY AUTOINCREMENT,
                    MaNguoiDung INTEGER NOT NULL, MaGoiHoc INTEGER NULL,
                    GiaDangKy TEXT NOT NULL, SoNgaySuDung INTEGER NOT NULL,
                    NgayDangKy TEXT NOT NULL, NgayBatDau TEXT NULL, NgayKetThuc TEXT NULL,
                    TrangThai TEXT NOT NULL);
                CREATE TABLE GiaoDichThanhToan (MaGiaoDich INTEGER PRIMARY KEY AUTOINCREMENT,
                    MaDangKy INTEGER NOT NULL, MaCongThanhToan INTEGER NOT NULL,
                    SoTien TEXT NOT NULL, MaGiaoDichCongThanhToan TEXT NOT NULL,
                    TrangThai TEXT NOT NULL, NgayTao TEXT NOT NULL, NgayThanhToan TEXT NULL);
                CREATE UNIQUE INDEX UX_GiaoDichThanhToan_MaCong
                    ON GiaoDichThanhToan(MaCongThanhToan, MaGiaoDichCongThanhToan);
                """);
            db.NguoiDung.Add(new NguoiDung
            {
                MaNguoiDung = 1, HoTen = "Learner", Email = "learner@example.test",
                VaiTro = "HOC_VIEN", TrangThai = "HOAT_DONG", NgayTao = Now.UtcDateTime
            });
            db.GoiHoc.Add(new GoiHoc
            {
                MaGoiHoc = 1, TenGoiHoc = "TOEIC", Gia = 120_000m,
                SoNgaySuDung = 90, TrangThai = "DANG_MO", NgayTao = Now.UtcDateTime
            });
            db.GoiHoc.Add(new GoiHoc
            {
                MaGoiHoc = 2, TenGoiHoc = "TOEIC 2", Gia = 140_000m,
                SoNgaySuDung = 90, TrangThai = "DANG_MO", NgayTao = Now.UtcDateTime
            });
            db.CongThanhToan.Add(new CongThanhToan
            {
                MaCongThanhToan = 1, MaCong = "VNPAY", TenCongThanhToan = "VNPay"
            });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO KhoaHoc (MaKhoaHoc,TenKhoaHoc,GiaiDoan,DiemMucTieuToiDa,TrangThai,NgayTao)
                VALUES (1,'Course',1,450,'DANG_MO','2026-10-15'),
                       (2,'Course 2',2,700,'DANG_MO','2026-10-15'),
                       (3,'Course 3',3,990,'DANG_MO','2026-10-15');
                UPDATE KhoaHoc SET GiaBanLe = '99000.0' WHERE MaKhoaHoc = 1;
                UPDATE KhoaHoc SET GiaBanLe = '199000.0' WHERE MaKhoaHoc = 2;
                UPDATE KhoaHoc SET GiaBanLe = '299000.0' WHERE MaKhoaHoc = 3;
                INSERT INTO GoiHocKhoaHoc VALUES (1,1);
                INSERT INTO GoiHocKhoaHoc VALUES (2,2);
                """);
            db.ChangeTracker.Clear();
            return new PaymentDb(connection, db);
        }

        public PaymentController Controller(int? learnerId)
        {
            var settings = Settings(enabled: true);
            var service = new PaymentCreationService(Db,
                new VNPayGateway(Options.Create(settings)), new FixedClock());
            return PaymentCreationTests.Controller(service, settings, learnerId, Db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
