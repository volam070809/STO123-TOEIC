using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using STO123.Services;

namespace STO123.Tests;

public sealed class ContentRevisionStoreTests
{
    private static readonly string[] Tables =
    [
        "KhoaHoc", "GoiHoc", "GoiHocKhoaHoc", "DangKyGoiHoc", "ChiTietDangKyKhoaHoc",
        "UnitKhoaHoc", "LessonKhoaHoc", "BuocLoTrinh", "DanhMucBaiGiang", "BaiGiang",
        "TaiNguyenBaiGiang", "ChuDe", "TuVung", "TuVungChuDe", "CauHoi", "NguLieu",
        "NguLieuTaiLieu", "NhomCauHoi", "BaiTapKhoaHoc", "PhienBanBaiTapKhoaHoc",
        "CauHoiBaiTapKhoaHoc", "DeThi", "PhienBanDeThi", "CauHoiDeThi"
    ];

    [Fact]
    public async Task ReportsOnlyCommittedChangesInTheAffectedDomain()
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = "STO123_RevisionTest_" + Guid.NewGuid().ToString("N")[..8];
        const string server = @"(localdb)\MSSQLLocalDB";
        var master = $"Server={server};Database=master;Integrated Security=true;TrustServerCertificate=true";
        var database = $"Server={server};Database={name};Integrated Security=true;TrustServerCertificate=true";
        await using (var root = new SqlConnection(master))
        {
            await root.OpenAsync();
            await new SqlCommand($"CREATE DATABASE [{name}]", root).ExecuteNonQueryAsync();
        }
        try
        {
            await using var connection = new SqlConnection(database);
            await connection.OpenAsync();
            await new SqlCommand("ALTER DATABASE CURRENT SET CHANGE_TRACKING = ON " +
                "(CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON)", connection).ExecuteNonQueryAsync();
            foreach (var table in Tables)
            {
                await new SqlCommand($"CREATE TABLE dbo.[{table}](Id int NOT NULL PRIMARY KEY); " +
                    $"ALTER TABLE dbo.[{table}] ENABLE CHANGE_TRACKING", connection).ExecuteNonQueryAsync();
            }
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:ToeicDb"] = database }).Build();
            var store = new ContentRevisionStore(config, TimeProvider.System);
            var first = await store.ReadAsync(null, true, CancellationToken.None);
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await new SqlCommand("INSERT dbo.KhoaHoc(Id) VALUES(1)", connection,
                    (SqlTransaction)transaction).ExecuteNonQueryAsync();
                var uncommitted = await store.ReadAsync(first.Version, true, CancellationToken.None);
                Assert.Empty(uncommitted.ChangedDomains);
                await transaction.CommitAsync();
            }
            var published = await store.ReadAsync(first.Version, true, CancellationToken.None);
            Assert.True(published.Version > first.Version);
            Assert.Contains("COURSES", published.ChangedDomains);
            Assert.DoesNotContain("VOCABULARY", published.ChangedDomains);
            await new SqlCommand("INSERT dbo.ChuDe(Id) VALUES(1)", connection).ExecuteNonQueryAsync();
            var vocabulary = await store.ReadAsync(published.Version, true, CancellationToken.None);
            Assert.True(vocabulary.Version > published.Version);
            Assert.Contains("VOCABULARY", vocabulary.ChangedDomains);
            Assert.DoesNotContain("COURSES", vocabulary.ChangedDomains);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using var root = new SqlConnection(master);
            await root.OpenAsync();
            await new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{name}]", root).ExecuteNonQueryAsync();
        }
    }
}
