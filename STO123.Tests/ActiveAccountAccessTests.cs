using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Auth;

namespace STO123.Tests;

public sealed class ActiveAccountAccessTests
{
    [Fact]
    public async Task Status_check_does_not_accept_stale_active_account()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE NguoiDung (MaNguoiDung INTEGER PRIMARY KEY, HoTen TEXT NOT NULL,
                Email TEXT NOT NULL, SoDienThoai TEXT NULL, AnhDaiDien TEXT NULL,
                VaiTro TEXT NOT NULL, TrangThai TEXT NOT NULL, NgayTao TEXT NOT NULL,
                LanDangNhapCuoi TEXT NULL);
            INSERT INTO NguoiDung VALUES (1, 'Admin', 'admin@example.test', NULL, NULL,
                'ADMIN_QUAN_LY', 'HOAT_DONG', '2026-01-01', NULL);
            """);
        var access = new ActiveAccountAccess(db);
        Assert.True(await access.IsActiveAsync(1, "ADMIN_QUAN_LY", CancellationToken.None));

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE NguoiDung SET TrangThai = 'BI_KHOA' WHERE MaNguoiDung = 1");

        Assert.False(await access.IsActiveAsync(1, "ADMIN_QUAN_LY", CancellationToken.None));
    }
}
