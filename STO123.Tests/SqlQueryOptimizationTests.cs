using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using STO123.Controllers;
using STO123.Models;
using STO123.Services.Arena;

namespace STO123.Tests;

public sealed class SqlQueryOptimizationTests
{
    [Fact]
    public async Task QuestionBankDefaultListPaginatesWholeGroups()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE PartTOEIC (MaPart INTEGER PRIMARY KEY, SoPart INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE CauHoi (MaCauHoi INTEGER PRIMARY KEY, MaPart INTEGER NOT NULL, LoaiCauHoi TEXT NOT NULL, NoiDung TEXT, PhuongAnA TEXT, PhuongAnB TEXT, PhuongAnC TEXT, PhuongAnD TEXT, PhuongAnDung TEXT, GiaiThich TEXT)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE NguLieu (MaNguLieu INTEGER PRIMARY KEY, NoiDungNguLieu TEXT, NoiDungDich TEXT, DuongDanAudio TEXT, DuongDanAnh TEXT)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE NhomCauHoi (MaCauHoi INTEGER PRIMARY KEY, MaNguLieu INTEGER NOT NULL, ThuTu INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE NguLieuTaiLieu (MaTaiLieu INTEGER PRIMARY KEY, MaNguLieu INTEGER NOT NULL, LoaiTaiLieu TEXT, NoiDung TEXT, DuongDanAnh TEXT, ThuTu INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE CauHoiDeThi (MaDeThi INTEGER NOT NULL, MaCauHoi INTEGER NOT NULL, ThuTu INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE DeThi (MaDeThi INTEGER PRIMARY KEY, TenDe TEXT, TrangThai TEXT, MaPhienBanHienHanh INTEGER)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO PartTOEIC VALUES (3,3),(5,5)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO CauHoi VALUES (1,5,'DE_THI','Single','A','B','C','D','A',NULL),(2,3,'DE_THI','First','A','B','C','D','A',NULL),(3,3,'DE_THI','Second','A','B','C','D','A',NULL),(4,3,'DE_THI','Third','A','B','C','D','A',NULL)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO NguLieu VALUES (10,'Group',NULL,NULL,NULL)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO NhomCauHoi VALUES (2,10,1),(3,10,2),(4,10,3)");

        var api = new AdminQuestionBankController(db, null!);
        var first = await api.Units(null, "DE_THI", null, "all", 1, 1, CancellationToken.None);
        var second = await api.Units(null, "DE_THI", null, "all", 2, 1, CancellationToken.None);
        var third = await api.Units(null, "DE_THI", null, "all", 3, 1, CancellationToken.None);
        using var firstJson = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(first).Value));
        using var secondJson = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(second).Value));
        using var thirdJson = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(third).Value));
        Assert.Equal(2, firstJson.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, Assert.Single(firstJson.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("questionCount").GetInt32());
        Assert.Equal(3, Assert.Single(secondJson.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("questionCount").GetInt32());
        Assert.Empty(thirdJson.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ArenaDeadlineQueryReturnsNullWithoutPendingMatches()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE ArenaMatch (Id TEXT PRIMARY KEY, Status TEXT NOT NULL, CreatedUtc TEXT NOT NULL, StartedUtc TEXT, DeadlineUtc TEXT, RevealUntilUtc TEXT)");
        var arena = new ArenaService(db, null!, null!, null!, new ArenaPresence(),
            new ArenaTickSignal(), NullLogger<ArenaService>.Instance);
        Assert.Null(await arena.NextDeadlineUtcAsync(CancellationToken.None));
        await db.Database.ExecuteSqlRawAsync("INSERT INTO ArenaMatch VALUES ('00000000-0000-0000-0000-000000000001','WAITING','2026-11-11 00:00:00',NULL,NULL,NULL)");
        Assert.Equal(new DateTime(2026, 11, 11, 0, 30, 0),
            await arena.NextDeadlineUtcAsync(CancellationToken.None));
    }

    [Fact]
    public async Task VocabularyTopicCountsProgressInSql()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE ChuDe (MaChuDe INTEGER PRIMARY KEY)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE TuVungChuDe (MaChuDe INTEGER NOT NULL, MaTuVung INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE TienDoTuVung (MaNguoiDung INTEGER NOT NULL, MaTuVung INTEGER NOT NULL, TrangThai TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE LanLuyenTuVung (MaLanLuyen INTEGER PRIMARY KEY, MaChuDe INTEGER NOT NULL, MaNguoiDung INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO ChuDe VALUES (1)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO TuVungChuDe VALUES (1,10),(1,11)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO TienDoTuVung VALUES (7,10,'DA_NHO'),(7,11,'DA_NHO'),(8,10,'DANG_HOC'),(9,12,'DA_NHO')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO LanLuyenTuVung VALUES (1,1,7),(2,1,8)");
        var api = new AdminSectionStatisticsController(db, TimeProvider.System);
        var response = Assert.IsType<OkObjectResult>(await api.VocabularyTopic(1, CancellationToken.None));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
        var values = json.RootElement.GetProperty("metrics").EnumerateArray()
            .Select(metric => metric.GetProperty("value").GetInt32()).ToArray();
        Assert.Equal([2, 2, 2, 2, 2], values);
    }
}
