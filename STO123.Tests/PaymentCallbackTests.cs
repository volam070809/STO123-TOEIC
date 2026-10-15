using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using STO123.Controllers;
using STO123.Models;
using STO123.Services.Exam;
using STO123.Services.Payment;

namespace STO123.Tests;

public sealed class PaymentCallbackTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);
    private const string VectorQuery = "vnp_Amount=12000000&vnp_BankCode=NCB&vnp_CardType=ATM&vnp_CurrCode=VND&vnp_OrderInfo=Thanh+toan+goi+A%2BB&vnp_PayDate=20261015100000&vnp_ResponseCode=00&vnp_TmnCode=TEST1234&vnp_TransactionNo=123456&vnp_TransactionStatus=00&vnp_TxnRef=abc123";
    // Independent Python hashlib/urllib.parse vector, not produced by VNPayCallbackVerifier.
    private const string VectorHash = "208ed908ee406c10b8445023e809e0571112eb832211f1681698376240c73ef1db19c8ceaaad8e89f1ab7d224ee4a911e2248663eb1e792c9ab2bf70c0d93517";

    [Fact]
    public void VerifiesIndependentEncodedVectorAndOptionalFields()
    {
        var verifier = new VNPayCallbackVerifier(Options.Create(Settings()));
        var query = QueryHelpers.ParseQuery("?" + VectorQuery + "&vnp_SecureHash=" + VectorHash);
        var result = verifier.Verify(new QueryCollection(query));
        Assert.Equal(VNPayCallbackError.None, result.Error);
        Assert.Equal("abc123", result.Callback!.TransactionRef);
        Assert.Equal(12_000_000L, result.Callback.Amount);

        // Currency is absent from the official IPN example and is optional.
        var withoutCurrency = Fields();
        withoutCurrency.Remove("vnp_CurrCode");
        Assert.Equal(VNPayCallbackError.None, verifier.Verify(Signed(withoutCurrency)).Error);
    }

    [Fact]
    public void RejectsInvalidSignatureMalformedDataAndDuplicateParameters()
    {
        var verifier = new VNPayCallbackVerifier(Options.Create(Settings()));
        var tampered = new QueryCollection(QueryHelpers.ParseQuery("?" +
            VectorQuery.Replace("12000000", "13000000") + "&vnp_SecureHash=" + VectorHash));
        Assert.Equal(VNPayCallbackError.InvalidSignature, verifier.Verify(tampered).Error);

        var duplicate = new QueryCollection(QueryHelpers.ParseQuery("?" + VectorQuery +
            "&vnp_Amount=12000000&vnp_SecureHash=" + VectorHash));
        Assert.Equal(VNPayCallbackError.InvalidRequest, verifier.Verify(duplicate).Error);

        var missingHash = new QueryCollection(QueryHelpers.ParseQuery("?" + VectorQuery));
        Assert.Equal(VNPayCallbackError.InvalidSignature, verifier.Verify(missingHash).Error);

        foreach (var field in new[] { "vnp_TransactionNo", "vnp_TmnCode", "vnp_BankCode", "vnp_OrderInfo" })
        {
            var values = Fields();
            values.Remove(field);
            Assert.Equal(VNPayCallbackError.InvalidRequest, verifier.Verify(Signed(values)).Error);
        }
        var wrongCurrency = Fields();
        wrongCurrency["vnp_CurrCode"] = "USD";
        Assert.Equal(VNPayCallbackError.InvalidRequest, verifier.Verify(Signed(wrongCurrency)).Error);
        var malformedAmount = Fields();
        malformedAmount["vnp_Amount"] = "12.00";
        Assert.Equal(VNPayCallbackError.InvalidRequest, verifier.Verify(Signed(malformedAmount)).Error);
    }

    [Fact]
    public void MissingMerchantSecretCannotValidateIpn()
    {
        var settings = new VNPayOptions { CheckoutEnabled = false };
        Assert.True(VNPayOptions.IsValid(settings));
        Assert.Equal(VNPayCallbackError.InvalidRequest,
            new VNPayCallbackVerifier(Options.Create(settings)).Verify(Signed(Fields())).Error);
    }

    [Fact]
    public async Task VerifiedSuccessActivatesOnceAndDuplicateDoesNotExtendAccess()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var controller = fixture.Controller();
        var first = await controller.Ipn(CancellationToken.None);
        Assert.Equal("00", RspCode(first));
        var payment = await fixture.Db.GiaoDichThanhToan.AsNoTracking().SingleAsync();
        var enrollment = await fixture.Db.DangKyGoiHoc.AsNoTracking().SingleAsync();
        Assert.Equal("THANH_CONG", payment.TrangThai);
        Assert.Equal("DANG_SU_DUNG", enrollment.TrangThai);
        Assert.Equal(Now.ToOffset(TimeSpan.FromHours(7)).DateTime, enrollment.NgayBatDau);
        Assert.Equal(enrollment.NgayBatDau!.Value.AddDays(90), enrollment.NgayKetThuc);

        fixture.Clock.Current = Now.AddDays(2);
        Assert.Equal("02", RspCode(await controller.Ipn(CancellationToken.None)));
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(enrollment.NgayBatDau, (await fixture.Db.DangKyGoiHoc.SingleAsync()).NgayBatDau);
        Assert.Equal(enrollment.NgayKetThuc, (await fixture.Db.DangKyGoiHoc.SingleAsync()).NgayKetThuc);
    }

    [Fact]
    public async Task VerifiedCustomCoursePaymentUnlocksOnlySelectedCoursesForThirtyDays()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        fixture.Db.KhoaHoc.AddRange(Enumerable.Range(1, 3).Select(id => new KhoaHoc
        {
            MaKhoaHoc = id, TenKhoaHoc = $"Course {id}", GiaiDoan = (byte)id,
            DiemMucTieuToiDa = id * 300, GiaBanLe = id * 99_000m,
            TrangThai = "DANG_MO", NgayTao = Now.UtcDateTime
        }));
        fixture.Db.DangKyGoiHoc.Add(new DangKyGoiHoc
        {
            MaDangKy = 2, MaNguoiDung = 1, MaGoiHoc = null, GiaDangKy = 298_000m,
            SoNgaySuDung = 30, NgayDangKy = Now.UtcDateTime,
            TrangThai = "CHO_THANH_TOAN",
            ChiTietDangKyKhoaHoc = [
                new() { MaKhoaHoc = 1, DonGia = 99_000m },
                new() { MaKhoaHoc = 2, DonGia = 199_000m }
            ]
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.GiaoDichThanhToan.Add(new GiaoDichThanhToan
        {
            MaGiaoDich = 2, MaDangKy = 2, MaCongThanhToan = 7,
            SoTien = 298_000m, MaGiaoDichCongThanhToan = "customref",
            TrangThai = "CHO_XU_LY", NgayTao = Now.UtcDateTime
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var service = new PaymentConfirmationService(fixture.Db, fixture.Clock,
            NullLogger<PaymentConfirmationService>.Instance);
        var callback = new VNPayCallback("customref", 29_800_000, "777", "00", "00");
        Assert.Equal(PaymentConfirmationResult.Confirmed,
            await service.ConfirmAsync(callback, CancellationToken.None));
        var enrollment = await fixture.Db.DangKyGoiHoc.AsNoTracking().SingleAsync(e => e.MaDangKy == 2);
        Assert.Equal(enrollment.NgayBatDau!.Value.AddDays(30), enrollment.NgayKetThuc);
        Assert.Equal([1, 2], await CourseEntitlement.ActiveCourseIds(fixture.Db, 1,
            enrollment.NgayBatDau.Value).OrderBy(id => id).ToListAsync());
        var history = Assert.IsType<List<PaymentHistoryDto>>(Assert.IsType<OkObjectResult>(
            await fixture.Controller().History(CancellationToken.None)).Value);
        var purchase = history.Single(item => item.MaGiaoDich == 2);
        Assert.Null(purchase.MaGoiHoc);
        Assert.Equal([1, 2], purchase.CourseIds);
        Assert.Equal(298_000m, purchase.SoTien);
        Assert.Empty(await CourseEntitlement.ActiveCourseIds(fixture.Db, 1,
            enrollment.NgayKetThuc!.Value).ToListAsync());
        fixture.Clock.Current = Now.AddDays(2);
        Assert.Equal(PaymentConfirmationResult.AlreadyProcessed,
            await service.ConfirmAsync(callback, CancellationToken.None));
        Assert.Equal(enrollment.NgayKetThuc,
            (await fixture.Db.DangKyGoiHoc.AsNoTracking().SingleAsync(e => e.MaDangKy == 2)).NgayKetThuc);
    }

    [Fact]
    public async Task DelayedSuccessRecordsMoneyButDoesNotReplaceActiveEntitlement()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var localNow = Now.ToOffset(TimeSpan.FromHours(7)).DateTime;
        fixture.Db.DangKyGoiHoc.Add(new DangKyGoiHoc
        {
            MaNguoiDung = 1, MaGoiHoc = 1, GiaDangKy = 120_000m,
            SoNgaySuDung = 90, NgayDangKy = localNow.AddDays(-1),
            NgayBatDau = localNow.AddDays(-1), NgayKetThuc = localNow.AddDays(89),
            TrangThai = "DANG_SU_DUNG"
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        Assert.Equal("00", RspCode(await fixture.Controller().Ipn(CancellationToken.None)));
        Assert.Equal("THANH_CONG", (await fixture.Db.GiaoDichThanhToan.SingleAsync()).TrangThai);
        var earlier = await fixture.Db.DangKyGoiHoc.SingleAsync(item => item.MaDangKy == 1);
        Assert.Equal("DANG_SU_DUNG", earlier.TrangThai);
        Assert.NotNull(earlier.NgayBatDau);
        Assert.Equal("02", RspCode(await fixture.Controller().Ipn(CancellationToken.None)));
        var status = Assert.IsType<PaymentStatusDto>(Assert.IsType<OkObjectResult>(
            await fixture.Controller().Status(1, CancellationToken.None)).Value);
        Assert.Equal("DANG_SU_DUNG", status.TrangThaiDangKy);
    }

    [Fact]
    public async Task HistoryIsOwnerScopedAndIncludesPackageAndAmount()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var own = Assert.IsType<List<PaymentHistoryDto>>(Assert.IsType<OkObjectResult>(
            await fixture.Controller(userId: 1).History(CancellationToken.None)).Value);
        Assert.Equal(1, Assert.Single(own).MaGiaoDich);
        Assert.Equal(120_000m, own[0].SoTien);
        var other = Assert.IsType<List<PaymentHistoryDto>>(Assert.IsType<OkObjectResult>(
            await fixture.Controller(userId: 2).History(CancellationToken.None)).Value);
        Assert.Empty(other);
    }

    [Fact]
    public async Task ConcurrentDuplicateNotificationsActivateOnlyOnce()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        await using var firstDb = fixture.NewContext();
        await using var secondDb = fixture.NewContext();
        var callback = new VNPayCallback("abc123", 12_000_000, "123456", "00", "00");
        var first = new PaymentConfirmationService(firstDb, fixture.Clock,
            NullLogger<PaymentConfirmationService>.Instance);
        var second = new PaymentConfirmationService(secondDb, fixture.Clock,
            NullLogger<PaymentConfirmationService>.Instance);

        var results = await Task.WhenAll(
            Task.Run(() => first.ConfirmAsync(callback, CancellationToken.None)),
            Task.Run(() => second.ConfirmAsync(callback, CancellationToken.None)));

        Assert.Contains(PaymentConfirmationResult.Confirmed, results);
        Assert.Contains(PaymentConfirmationResult.AlreadyProcessed, results);
        fixture.Db.ChangeTracker.Clear();
        var payment = await fixture.Db.GiaoDichThanhToan.SingleAsync();
        var enrollment = await fixture.Db.DangKyGoiHoc.SingleAsync();
        Assert.Equal("THANH_CONG", payment.TrangThai);
        Assert.Equal(Now.ToOffset(TimeSpan.FromHours(7)).DateTime, enrollment.NgayBatDau);
        Assert.Equal(enrollment.NgayBatDau!.Value.AddDays(90), enrollment.NgayKetThuc);
    }

    [Fact]
    public async Task FailedNotificationDoesNotActivateAndCannotReverseLater()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var failed = Fields();
        failed["vnp_ResponseCode"] = "24";
        failed["vnp_TransactionStatus"] = "02";
        var controller = fixture.Controller(Signed(failed));
        Assert.Equal("00", RspCode(await controller.Ipn(CancellationToken.None)));
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal("THAT_BAI", (await fixture.Db.GiaoDichThanhToan.SingleAsync()).TrangThai);
        Assert.Equal("CHO_THANH_TOAN", (await fixture.Db.DangKyGoiHoc.SingleAsync()).TrangThai);
        Assert.Equal("02", RspCode(await fixture.Controller().Ipn(CancellationToken.None)));
        Assert.Equal("THAT_BAI", (await fixture.Db.GiaoDichThanhToan.SingleAsync()).TrangThai);
    }

    [Fact]
    public async Task SuspectedDeductionRemainsPendingUntilReconciled()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var uncertain = Fields();
        uncertain["vnp_ResponseCode"] = "07";
        uncertain["vnp_TransactionStatus"] = "07";
        Assert.Equal("99", RspCode(await fixture.Controller(Signed(uncertain)).Ipn(CancellationToken.None)));
        Assert.Equal("CHO_XU_LY", (await fixture.Db.GiaoDichThanhToan.SingleAsync()).TrangThai);
        Assert.Equal("CHO_THANH_TOAN", (await fixture.Db.DangKyGoiHoc.SingleAsync()).TrangThai);
        Assert.Equal("00", RspCode(await fixture.Controller().Ipn(CancellationToken.None)));
    }

    [Fact]
    public async Task InconsistentFailureCodesRemainPending()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var uncertain = Fields();
        uncertain["vnp_ResponseCode"] = "24";
        uncertain["vnp_TransactionStatus"] = "01";
        Assert.Equal("99", RspCode(await fixture.Controller(Signed(uncertain)).Ipn(CancellationToken.None)));
        Assert.Equal("CHO_XU_LY", (await fixture.Db.GiaoDichThanhToan.SingleAsync()).TrangThai);
        Assert.Equal("CHO_THANH_TOAN", (await fixture.Db.DangKyGoiHoc.SingleAsync()).TrangThai);
    }

    [Fact]
    public async Task UnknownAmountAndInvalidSignatureNeverWrite()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var unknown = Fields();
        unknown["vnp_TxnRef"] = "other";
        Assert.Equal("01", RspCode(await fixture.Controller(Signed(unknown)).Ipn(CancellationToken.None)));
        var wrongAmount = Fields();
        wrongAmount["vnp_Amount"] = "1";
        Assert.Equal("04", RspCode(await fixture.Controller(Signed(wrongAmount)).Ipn(CancellationToken.None)));
        var bad = new QueryCollection(QueryHelpers.ParseQuery("?" + VectorQuery + "&vnp_SecureHash=" + new string('0', 128)));
        Assert.Equal("97", RspCode(await fixture.Controller(bad).Ipn(CancellationToken.None)));
        Assert.Equal("CHO_XU_LY", (await fixture.Db.GiaoDichThanhToan.SingleAsync()).TrangThai);
    }

    [Fact]
    public async Task DatabaseFailureRollsBackBothUpdatesAndRequestsRetry()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        await fixture.Db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_activation BEFORE UPDATE ON DangKyGoiHoc
            BEGIN SELECT RAISE(ABORT, 'activation rejected'); END;
            """);
        Assert.Equal("99", RspCode(await fixture.Controller().Ipn(CancellationToken.None)));
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal("CHO_XU_LY", (await fixture.Db.GiaoDichThanhToan.SingleAsync()).TrangThai);
        Assert.Equal("CHO_THANH_TOAN", (await fixture.Db.DangKyGoiHoc.SingleAsync()).TrangThai);
    }

    [Fact]
    public async Task ReturnBeforeIpnIsReadOnlyAndStatusRequiresOwner()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var controller = fixture.Controller();
        var returned = await controller.Return(CancellationToken.None);
        Assert.IsType<ContentResult>(returned);
        Assert.Equal("CHO_XU_LY", (await fixture.Db.GiaoDichThanhToan.SingleAsync()).TrangThai);
        Assert.Equal("CHO_THANH_TOAN", (await fixture.Db.DangKyGoiHoc.SingleAsync()).TrangThai);

        var own = Assert.IsType<OkObjectResult>(await fixture.Controller(userId: 1).Status(1, CancellationToken.None));
        Assert.Contains("CHO_XU_LY", own.Value!.ToString());
        Assert.IsType<NotFoundResult>(await fixture.Controller(userId: 2).Status(1, CancellationToken.None));
        Assert.IsType<UnauthorizedResult>(await fixture.Controller(userId: null).Status(1, CancellationToken.None));
        Assert.Equal("00", RspCode(await controller.Ipn(CancellationToken.None)));
        var after = Assert.IsType<OkObjectResult>(await fixture.Controller(userId: 1).Status(1, CancellationToken.None));
        Assert.Contains("THANH_CONG", after.Value!.ToString());
    }

    [Fact]
    public async Task ReturnRedirectUsesOnlyConfiguredTargetAndNumericId()
    {
        await using var fixture = await PaymentDb.CreateAsync();
        var settings = Settings();
        settings.FrontendResultUrl = "https://example.test/payment/result";
        var target = Assert.IsType<RedirectResult>(await fixture.Controller(settings: settings).Return(CancellationToken.None));
        Assert.Equal("https://example.test/payment/result?paymentId=1", target.Url);
        Assert.DoesNotContain("SecureHash", target.Url);

        var invalid = new QueryCollection(QueryHelpers.ParseQuery("?returnUrl=https://evil.test"));
        var other = Assert.IsType<RedirectResult>(await fixture.Controller(invalid, settings: settings)
            .Return(CancellationToken.None));
        Assert.Equal("https://example.test/payment/result", other.Url);
    }

    private static VNPayOptions Settings() => new()
    {
        TmnCode = "TEST1234", HashSecret = "vector-secret",
        PaymentUrl = VNPayOptions.SandboxPaymentUrl, CheckoutEnabled = false
    };

    private static Dictionary<string, string> Fields() => new()
    {
        ["vnp_Amount"] = "12000000", ["vnp_BankCode"] = "NCB",
        ["vnp_CardType"] = "ATM", ["vnp_CurrCode"] = "VND",
        ["vnp_OrderInfo"] = "Thanh toan goi A+B", ["vnp_PayDate"] = "20261015100000",
        ["vnp_ResponseCode"] = "00", ["vnp_TmnCode"] = "TEST1234",
        ["vnp_TransactionNo"] = "123456", ["vnp_TransactionStatus"] = "00",
        ["vnp_TxnRef"] = "abc123"
    };

    private static IQueryCollection Signed(Dictionary<string, string> fields)
    {
        var query = string.Join('&', fields.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{WebUtility.UrlEncode(pair.Key)}={WebUtility.UrlEncode(pair.Value)}"));
        var hash = Convert.ToHexStringLower(HMACSHA512.HashData(
            Encoding.UTF8.GetBytes("vector-secret"), Encoding.UTF8.GetBytes(query)));
        return new QueryCollection(QueryHelpers.ParseQuery("?" + query + "&vnp_SecureHash=" + hash));
    }

    private static string RspCode(IActionResult action)
    {
        var reply = Assert.IsType<ContentResult>(action);
        Assert.Equal(StatusCodes.Status200OK, reply.StatusCode);
        using var data = System.Text.Json.JsonDocument.Parse(reply.Content!);
        return data.RootElement.GetProperty("RspCode").GetString()!;
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Current = Now;
        public override DateTimeOffset GetUtcNow() => Current;
    }

    private sealed class PaymentDb(SqliteConnection connection, ToeicDbContext db) : IAsyncDisposable
    {
        public ToeicDbContext Db { get; } = db;
        public FixedClock Clock { get; } = new();

        public static async Task<PaymentDb> CreateAsync()
        {
            var connection = new SqliteConnection(
                $"Data Source=payment-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=10");
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
                    GiaBanLe TEXT NULL, TrangThai TEXT NOT NULL, MaAdminNoiDung INTEGER NULL,
                    NgayTao TEXT NOT NULL, DuongDanAnhDaiDien TEXT NULL);
                CREATE TABLE GoiHocKhoaHoc (MaGoiHoc INTEGER NOT NULL, MaKhoaHoc INTEGER NOT NULL,
                    PRIMARY KEY (MaGoiHoc, MaKhoaHoc));
                CREATE TABLE ChiTietDangKyKhoaHoc (MaDangKy INTEGER NOT NULL,
                    MaKhoaHoc INTEGER NOT NULL, DonGia TEXT NOT NULL,
                    PRIMARY KEY (MaDangKy, MaKhoaHoc));
                CREATE TABLE CongThanhToan (MaCongThanhToan INTEGER PRIMARY KEY,
                    MaCong TEXT NOT NULL, TenCongThanhToan TEXT NOT NULL);
                CREATE TABLE DangKyGoiHoc (MaDangKy INTEGER PRIMARY KEY, MaNguoiDung INTEGER NOT NULL,
                    MaGoiHoc INTEGER NULL, GiaDangKy TEXT NOT NULL, SoNgaySuDung INTEGER NOT NULL,
                    NgayDangKy TEXT NOT NULL, NgayBatDau TEXT NULL, NgayKetThuc TEXT NULL,
                    TrangThai TEXT NOT NULL CHECK(TrangThai IN ('CHO_THANH_TOAN','DANG_SU_DUNG','HET_HAN')));
                CREATE TABLE GiaoDichThanhToan (MaGiaoDich INTEGER PRIMARY KEY,
                    MaDangKy INTEGER NOT NULL, MaCongThanhToan INTEGER NOT NULL, SoTien TEXT NOT NULL,
                    MaGiaoDichCongThanhToan TEXT NOT NULL,
                    TrangThai TEXT NOT NULL CHECK(TrangThai IN ('CHO_XU_LY','THANH_CONG','THAT_BAI')),
                    NgayTao TEXT NOT NULL, NgayThanhToan TEXT NULL);
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
            db.CongThanhToan.Add(new CongThanhToan
            {
                MaCongThanhToan = 7, MaCong = "VNPAY", TenCongThanhToan = "VNPay"
            });
            db.DangKyGoiHoc.Add(new DangKyGoiHoc
            {
                MaDangKy = 1, MaNguoiDung = 1, MaGoiHoc = 1, GiaDangKy = 120_000m,
                SoNgaySuDung = 90, NgayDangKy = Now.UtcDateTime, TrangThai = "CHO_THANH_TOAN"
            });
            await db.SaveChangesAsync();
            db.GiaoDichThanhToan.Add(new GiaoDichThanhToan
            {
                MaGiaoDich = 1, MaDangKy = 1, MaCongThanhToan = 7,
                SoTien = 120_000m, MaGiaoDichCongThanhToan = "abc123",
                TrangThai = "CHO_XU_LY", NgayTao = Now.UtcDateTime
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new PaymentDb(connection, db);
        }

        public ToeicDbContext NewContext() => new(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite(connection.ConnectionString).Options);

        public PaymentController Controller(IQueryCollection? query = null, int? userId = 1,
            VNPayOptions? settings = null)
        {
            settings ??= Settings();
            var claims = userId is null ? Array.Empty<Claim>() :
                [new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()), new Claim(ClaimTypes.Role, "HOC_VIEN")];
            var service = new PaymentCreationService(Db,
                new VNPayGateway(Options.Create(settings)), Clock);
            return new PaymentController(service,
                new PaymentConfirmationService(Db, Clock, NullLogger<PaymentConfirmationService>.Instance),
                new VNPayCallbackVerifier(Options.Create(settings)), Db, Options.Create(settings),
                NullLogger<PaymentController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
                        Request = { Query = query ?? Signed(Fields()) }
                    }
                }
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
