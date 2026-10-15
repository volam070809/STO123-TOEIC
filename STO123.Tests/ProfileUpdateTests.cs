using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using STO123.Controllers;
using STO123.DTOs.Auth;
using STO123.Models;

namespace STO123.Tests;

public sealed class ProfileUpdateTests
{
    [Fact]
    public async Task UpdatesOnlyTheAuthenticatedLearnersNameAndPhone()
    {
        await using var fixture = await ProfileDb.CreateAsync();
        var controller = fixture.Controller(1);
        var result = await controller.Update(new UpdateProfileRequest
        {
            HoTen = "  Nguyễn An  ", SoDienThoai = " 0912345678 "
        }, CancellationToken.None);

        var response = Assert.IsType<UpdateProfileResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(1, response.MaNguoiDung);
        Assert.Equal("Nguyễn An", response.HoTen);
        Assert.Equal("0912345678", response.SoDienThoai);
        var owner = await fixture.Db.NguoiDung.AsNoTracking().SingleAsync(x => x.MaNguoiDung == 1);
        var other = await fixture.Db.NguoiDung.AsNoTracking().SingleAsync(x => x.MaNguoiDung == 2);
        Assert.Equal("Nguyễn An", owner.HoTen);
        Assert.Equal("0912345678", owner.SoDienThoai);
        Assert.Equal("learner@example.test", owner.Email);
        Assert.Equal("HOC_VIEN", owner.VaiTro);
        Assert.Equal("HOAT_DONG", owner.TrangThai);
        Assert.Equal("Người khác", other.HoTen);
    }

    [Theory]
    [InlineData("   ", "0912345678")]
    [InlineData("Tên hợp lệ", "abc")]
    [InlineData("Tên hợp lệ", "123")]
    public async Task InvalidInputDoesNotWrite(string name, string phone)
    {
        await using var fixture = await ProfileDb.CreateAsync();
        var result = await fixture.Controller(1).Update(new UpdateProfileRequest
        {
            HoTen = name, SoDienThoai = phone
        }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Người học", (await fixture.Db.NguoiDung.AsNoTracking().SingleAsync(x => x.MaNguoiDung == 1)).HoTen);
    }

    [Fact]
    public async Task UnchangedRequestLeavesTheEntityUnmodified()
    {
        await using var fixture = await ProfileDb.CreateAsync();
        var result = await fixture.Controller(1).Update(new UpdateProfileRequest
        {
            HoTen = "Người học", SoDienThoai = "0912345678"
        }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(EntityState.Unchanged, fixture.Db.Entry(
            await fixture.Db.NguoiDung.SingleAsync(x => x.MaNguoiDung == 1)).State);
    }

    [Fact]
    public async Task PhoneCanBeClearedWithoutChangingEmailOrAccountMethod()
    {
        await using var fixture = await ProfileDb.CreateAsync();
        var result = await fixture.Controller(1).Update(new UpdateProfileRequest
        {
            HoTen = "Người học", SoDienThoai = " "
        }, CancellationToken.None);
        var response = Assert.IsType<UpdateProfileResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Null(response.SoDienThoai);
        var saved = await fixture.Db.NguoiDung.AsNoTracking().SingleAsync(x => x.MaNguoiDung == 1);
        Assert.Null(saved.SoDienThoai);
        Assert.Equal("learner@example.test", saved.Email);
    }

    [Fact]
    public async Task MissingIdentityCannotUpdateAnyone()
    {
        await using var fixture = await ProfileDb.CreateAsync();
        var result = await fixture.Controller(null).Update(new UpdateProfileRequest
        {
            HoTen = "Tên khác", SoDienThoai = null
        }, CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal("Người học", (await fixture.Db.NguoiDung.AsNoTracking().SingleAsync(x => x.MaNguoiDung == 1)).HoTen);
    }

    [Fact]
    public async Task InactiveOrNonLearnerAccountCannotUpdate()
    {
        await using var fixture = await ProfileDb.CreateAsync();
        var result = await fixture.Controller(2).Update(new UpdateProfileRequest
        {
            HoTen = "Tên khác", SoDienThoai = null
        }, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Người khác", (await fixture.Db.NguoiDung.AsNoTracking().SingleAsync(x => x.MaNguoiDung == 2)).HoTen);
    }

    [Fact]
    public void RequestRejectsIdentityAndAccountFields()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateProfileRequest>(
            """{"hoTen":"Tên mới","soDienThoai":null,"maNguoiDung":2}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateProfileRequest>(
            """{"hoTen":"Tên mới","soDienThoai":null,"vaiTro":"ADMIN_QUAN_LY"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateProfileRequest>(
            """{"hoTen":"Tên mới","soDienThoai":null,"email":"changed@example.test"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private sealed class ProfileDb : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public ToeicDbContext Db { get; }

        private ProfileDb(SqliteConnection connection, ToeicDbContext db)
        {
            this.connection = connection;
            Db = db;
        }

        public static async Task<ProfileDb> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE NguoiDung (
                    MaNguoiDung INTEGER PRIMARY KEY, HoTen TEXT NOT NULL, Email TEXT NOT NULL,
                    SoDienThoai TEXT NULL, AnhDaiDien TEXT NULL, VaiTro TEXT NOT NULL,
                    TrangThai TEXT NOT NULL, NgayTao TEXT NOT NULL, LanDangNhapCuoi TEXT NULL);
                """);
            db.NguoiDung.AddRange(
                new NguoiDung { MaNguoiDung = 1, HoTen = "Người học", Email = "learner@example.test",
                    SoDienThoai = "0912345678", VaiTro = "HOC_VIEN", TrangThai = "HOAT_DONG", NgayTao = DateTime.UtcNow },
                new NguoiDung { MaNguoiDung = 2, HoTen = "Người khác", Email = "other@example.test",
                    SoDienThoai = null!, VaiTro = "ADMIN_QUAN_LY", TrangThai = "HOAT_DONG", NgayTao = DateTime.UtcNow });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new ProfileDb(connection, db);
        }

        public ProfileController Controller(int? userId)
        {
            var claims = userId is null ? Array.Empty<Claim>() :
                [new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()), new Claim(ClaimTypes.Role, "HOC_VIEN")];
            return new ProfileController(Db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
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
