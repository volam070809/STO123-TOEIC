using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using STO123.Controllers;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public sealed class PackageAccessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListsRealPackagesAndCoursesWithOnlyValidActiveEnrollment()
    {
        await using var fixture = await PackageDb.CreateAsync();
        var result = Assert.IsType<OkObjectResult>(await fixture.Controller(1).List(CancellationToken.None));
        var packages = Assert.IsAssignableFrom<IEnumerable<PackageDto>>(result.Value).ToList();
        Assert.Equal(2, packages.Count);
        Assert.Equal(120_000m, packages[0].Price);
        Assert.Equal(90, packages[0].ValidDays);
        Assert.Equal(10, Assert.Single(packages[0].Courses).CourseId);
        Assert.True(packages[0].IsPurchased);
        Assert.True(packages[0].IsActive);
        Assert.False(packages[1].IsPurchased);
        Assert.False(packages[1].IsActive);
        Assert.Equal(20, Assert.Single(packages[1].Courses).CourseId);
        Assert.Equal([10], await CourseEntitlement.ActiveCourseIds(fixture.Db, 1, fixture.LocalNow).ToListAsync());
        Assert.Empty(await CourseEntitlement.ActiveCourseIds(fixture.Db, 2, fixture.LocalNow).ToListAsync());
    }

    [Fact]
    public async Task DetailRejectsUnavailablePackageAndInvalidIdentity()
    {
        await using var fixture = await PackageDb.CreateAsync();
        var controller = fixture.Controller(1);
        var detail = Assert.IsType<OkObjectResult>(await controller.Detail(1, CancellationToken.None));
        Assert.Equal(1, Assert.IsType<PackageDto>(detail.Value).PackageId);
        Assert.IsType<NotFoundObjectResult>(await controller.Detail(3, CancellationToken.None));
        Assert.IsType<UnauthorizedResult>(await fixture.Controller(null).List(CancellationToken.None));
        Assert.NotNull(typeof(PackagesController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single(attribute => attribute.Roles == "HOC_VIEN"));
    }

    [Fact]
    public async Task ExpiredEnrollmentKeepsPurchaseHistoryButNotCourseAccess()
    {
        await using var fixture = await PackageDb.CreateAsync();
        var enrollment = await fixture.Db.DangKyGoiHoc.SingleAsync(item => item.MaDangKy == 1);
        enrollment.TrangThai = "HET_HAN";
        await fixture.Db.SaveChangesAsync();
        var package = Assert.IsType<PackageDto>(Assert.IsType<OkObjectResult>(
            await fixture.Controller(1).Detail(1, CancellationToken.None)).Value);
        Assert.True(package.IsPurchased);
        Assert.False(package.IsActive);
        Assert.Empty(await CourseEntitlement.ActiveCourseIds(fixture.Db, 1, fixture.LocalNow).ToListAsync());
    }

    [Fact]
    public async Task ActiveEnrollmentWithoutConfirmedPaymentGrantsNoCourseAccess()
    {
        await using var fixture = await PackageDb.CreateAsync();
        var payment = await fixture.Db.GiaoDichThanhToan.SingleAsync();
        payment.TrangThai = "THAT_BAI";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await CourseEntitlement.ActiveCourseIds(fixture.Db, 1, fixture.LocalNow).ToListAsync());
        var item = Assert.IsType<PackageDto>(Assert.IsType<OkObjectResult>(
            await fixture.Controller(1).Detail(1, CancellationToken.None)).Value);
        Assert.False(item.IsActive);
        Assert.False(item.IsPurchased);
    }

    [Fact]
    public async Task CourseCatalogReportsOnlyCoveredCourseAsActive()
    {
        await using var fixture = await PackageDb.CreateAsync();
        var controller = fixture.CourseController(1);
        var result = Assert.IsType<OkObjectResult>(await controller.Courses(CancellationToken.None));
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var courses = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal("ACTIVE", courses.Single(item => item.GetProperty("courseId").GetInt32() == 10)
            .GetProperty("accessStatus").GetString());
        Assert.Equal("NOT_PURCHASED", courses.Single(item => item.GetProperty("courseId").GetInt32() == 20)
            .GetProperty("accessStatus").GetString());
    }

    [Fact]
    public async Task DirectCourseContentRequestWithoutEntitlementIsForbidden()
    {
        await using var fixture = await PackageDb.CreateAsync();
        var unpaid = await fixture.CourseController(2).CourseDetail(10, CancellationToken.None);
        Assert.Equal(403, Assert.IsType<ObjectResult>(unpaid).StatusCode);
        var expired = await fixture.Db.DangKyGoiHoc.SingleAsync(e => e.MaDangKy == 1);
        expired.NgayKetThuc = fixture.LocalNow.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();
        var result = await fixture.CourseController(1).CourseDetail(10, CancellationToken.None);
        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task PaidLearnerCanOpenEmptyCourse()
    {
        await using var fixture = await PackageDb.CreateAsync();
        var result = Assert.IsType<OkObjectResult>(await fixture.CourseController(1)
            .CourseDetail(10, CancellationToken.None));
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("ACTIVE", document.RootElement.GetProperty("accessStatus").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("contentCount").GetInt32());
        Assert.Empty(document.RootElement.GetProperty("units").EnumerateArray());
    }

    [Fact]
    public async Task PaidComboGrantsExactlyItsLinkedCourses()
    {
        await using var fixture = await PackageDb.CreateAsync();
        fixture.Db.GoiHoc.Add(new GoiHoc { MaGoiHoc = 4, TenGoiHoc = "Combo",
            Gia = 300_000m, SoNgaySuDung = 120, TrangThai = "DANG_MO",
            NgayTao = Now.UtcDateTime });
        await fixture.Db.SaveChangesAsync();
        await fixture.Db.Database.ExecuteSqlRawAsync("INSERT INTO GoiHocKhoaHoc VALUES (4,10),(4,20)");
        fixture.Db.DangKyGoiHoc.Add(new DangKyGoiHoc { MaDangKy = 4, MaNguoiDung = 2,
            MaGoiHoc = 4, GiaDangKy = 300_000m, SoNgaySuDung = 120,
            NgayDangKy = fixture.LocalNow, NgayBatDau = fixture.LocalNow,
            NgayKetThuc = fixture.LocalNow.AddDays(120), TrangThai = "DANG_SU_DUNG" });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.GiaoDichThanhToan.Add(new GiaoDichThanhToan { MaGiaoDich = 4,
            MaDangKy = 4, MaCongThanhToan = 7, SoTien = 300_000m,
            MaGiaoDichCongThanhToan = "combo-confirmed", TrangThai = "THANH_CONG",
            NgayTao = fixture.LocalNow, NgayThanhToan = fixture.LocalNow });
        await fixture.Db.SaveChangesAsync();
        Assert.Equal([10, 20], await CourseEntitlement.ActiveCourseIds(fixture.Db, 2,
            fixture.LocalNow).OrderBy(id => id).ToListAsync());
        Assert.Equal([10], await CourseEntitlement.ActiveCourseIds(fixture.Db, 1,
            fixture.LocalNow).ToListAsync());
    }

    [Fact]
    public async Task CustomEnrollmentAppearsInCatalogAlongsideLegacyOwnership()
    {
        await using var fixture = await PackageDb.CreateAsync();
        var second = await fixture.Db.KhoaHoc.SingleAsync(c => c.MaKhoaHoc == 20);
        second.GiaBanLe = 199_000m;
        fixture.Db.DangKyGoiHoc.Add(new DangKyGoiHoc { MaDangKy = 5, MaNguoiDung = 1,
            MaGoiHoc = null, GiaDangKy = 199_000m, SoNgaySuDung = 30,
            NgayDangKy = fixture.LocalNow, NgayBatDau = fixture.LocalNow,
            NgayKetThuc = fixture.LocalNow.AddDays(30), TrangThai = "DANG_SU_DUNG",
            ChiTietDangKyKhoaHoc = [new() { MaKhoaHoc = 20, DonGia = 199_000m }] });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.GiaoDichThanhToan.Add(new GiaoDichThanhToan { MaGiaoDich = 5,
            MaDangKy = 5, MaCongThanhToan = 7, SoTien = 199_000m,
            MaGiaoDichCongThanhToan = "custom-paid", TrangThai = "THANH_CONG",
            NgayTao = fixture.LocalNow, NgayThanhToan = fixture.LocalNow });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var result = Assert.IsType<OkObjectResult>(await fixture.CourseController(1)
            .Courses(CancellationToken.None));
        using var document = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(result.Value));
        var rows = document.RootElement.EnumerateArray().ToArray();
        Assert.All(rows, row => Assert.Equal("ACTIVE", row.GetProperty("accessStatus").GetString()));
        Assert.Equal(199_000m, rows.Single(row => row.GetProperty("courseId").GetInt32() == 20)
            .GetProperty("retailPrice").GetDecimal());
        Assert.IsType<OkObjectResult>(await fixture.CourseController(1)
            .CourseDetail(20, CancellationToken.None));
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class PackageDb(SqliteConnection connection, ToeicDbContext db) : IAsyncDisposable
    {
        public ToeicDbContext Db { get; } = db;
        public DateTime LocalNow => Now.ToOffset(TimeSpan.FromHours(7)).DateTime;

        public static async Task<PackageDb> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.ExecuteSqlRawAsync("""
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
                CREATE TABLE DangKyGoiHoc (MaDangKy INTEGER PRIMARY KEY, MaNguoiDung INTEGER NOT NULL,
                    MaGoiHoc INTEGER NULL, GiaDangKy TEXT NOT NULL, SoNgaySuDung INTEGER NOT NULL,
                    NgayDangKy TEXT NOT NULL, NgayBatDau TEXT NULL, NgayKetThuc TEXT NULL,
                    TrangThai TEXT NOT NULL);
                CREATE TABLE CongThanhToan (MaCongThanhToan INTEGER PRIMARY KEY,
                    MaCong TEXT NOT NULL, TenCongThanhToan TEXT NOT NULL);
                CREATE TABLE GiaoDichThanhToan (MaGiaoDich INTEGER PRIMARY KEY,
                    MaDangKy INTEGER NOT NULL, MaCongThanhToan INTEGER NOT NULL, SoTien TEXT NOT NULL,
                    MaGiaoDichCongThanhToan TEXT NOT NULL, TrangThai TEXT NOT NULL,
                    NgayTao TEXT NOT NULL, NgayThanhToan TEXT NULL);
                CREATE TABLE KetQuaLamBai (MaKetQua INTEGER PRIMARY KEY, MaHocVien INTEGER NOT NULL);
                CREATE TABLE KetQuaPhanLopKNN (MaPhanLop INTEGER PRIMARY KEY,
                    MaKetQua INTEGER NOT NULL, GiaiDoanDeXuat INTEGER NOT NULL,
                    DiemMucTieu INTEGER NULL, PhienBanMoHinh TEXT NOT NULL,
                    NgayPhanLop TEXT NOT NULL);
                CREATE TABLE UnitKhoaHoc (MaUnit INTEGER PRIMARY KEY, MaKhoaHoc INTEGER NOT NULL,
                    TenUnit TEXT NOT NULL, MoTa TEXT NULL, ThuTu INTEGER NOT NULL);
                CREATE TABLE LessonKhoaHoc (MaLesson INTEGER PRIMARY KEY, MaUnit INTEGER NOT NULL,
                    TenLesson TEXT NOT NULL, MoTa TEXT NULL, ThuTu INTEGER NOT NULL);
                CREATE TABLE BuocLoTrinh (MaBuoc INTEGER PRIMARY KEY, MaLesson INTEGER NOT NULL,
                    TieuDe TEXT NOT NULL, ThuTu INTEGER NOT NULL, MaBaiGiang INTEGER NULL,
                    MaDeThi INTEGER NULL, MaChuDe INTEGER NULL);
                """);
            db.GoiHoc.AddRange(
                new GoiHoc { MaGoiHoc = 1, TenGoiHoc = "Gói A", Gia = 120_000m,
                    SoNgaySuDung = 90, TrangThai = "DANG_MO", NgayTao = Now.UtcDateTime },
                new GoiHoc { MaGoiHoc = 2, TenGoiHoc = "Gói B", Gia = 240_000m,
                    SoNgaySuDung = 180, TrangThai = "DANG_MO", NgayTao = Now.UtcDateTime },
                new GoiHoc { MaGoiHoc = 3, TenGoiHoc = "Đóng", Gia = 10_000m,
                    SoNgaySuDung = 30, TrangThai = "DA_DONG", NgayTao = Now.UtcDateTime });
            db.KhoaHoc.AddRange(
                new KhoaHoc { MaKhoaHoc = 10, TenKhoaHoc = "Khóa A", GiaiDoan = 1,
                    DiemMucTieuToiDa = 450, TrangThai = "DANG_MO", NgayTao = Now.UtcDateTime },
                new KhoaHoc { MaKhoaHoc = 20, TenKhoaHoc = "Khóa B", GiaiDoan = 2,
                    DiemMucTieuToiDa = 650, TrangThai = "DANG_MO", NgayTao = Now.UtcDateTime });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("INSERT INTO GoiHocKhoaHoc VALUES (1,10),(2,20)");
            db.DangKyGoiHoc.AddRange(
                new DangKyGoiHoc { MaDangKy = 1, MaNguoiDung = 1, MaGoiHoc = 1,
                    GiaDangKy = 120_000m, SoNgaySuDung = 90, NgayDangKy = Now.UtcDateTime,
                    NgayBatDau = Now.ToOffset(TimeSpan.FromHours(7)).DateTime.AddDays(-1),
                    NgayKetThuc = Now.ToOffset(TimeSpan.FromHours(7)).DateTime.AddDays(89),
                    TrangThai = "DANG_SU_DUNG" },
                new DangKyGoiHoc { MaDangKy = 2, MaNguoiDung = 1, MaGoiHoc = 2,
                    GiaDangKy = 240_000m, SoNgaySuDung = 180, NgayDangKy = Now.UtcDateTime,
                    TrangThai = "CHO_THANH_TOAN" });
            await db.SaveChangesAsync();
            db.CongThanhToan.Add(new CongThanhToan
            {
                MaCongThanhToan = 7, MaCong = "VNPAY", TenCongThanhToan = "VNPay"
            });
            db.GiaoDichThanhToan.Add(new GiaoDichThanhToan
            {
                MaGiaoDich = 1, MaDangKy = 1, MaCongThanhToan = 7, SoTien = 120_000m,
                MaGiaoDichCongThanhToan = "confirmed", TrangThai = "THANH_CONG",
                NgayTao = Now.UtcDateTime, NgayThanhToan = Now.UtcDateTime
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new PackageDb(connection, db);
        }

        public PackagesController Controller(int? learnerId)
        {
            var claims = learnerId.HasValue ?
                [new Claim(ClaimTypes.NameIdentifier, learnerId.Value.ToString())] : Array.Empty<Claim>();
            return new PackagesController(Db, new Clock())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
                    }
                }
            };
        }

        public PlacementController CourseController(int learnerId) => new(Db, null!, null!, new Clock())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, learnerId.ToString())], "test"))
                }
            }
        };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
