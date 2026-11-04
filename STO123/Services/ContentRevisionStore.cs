using System.Text;
using Microsoft.Data.SqlClient;

namespace STO123.Services;

public sealed record ContentRevisionSnapshot(long Version, IReadOnlyList<string> ChangedDomains);

public sealed class ContentRevisionStore(IConfiguration config, TimeProvider clock)
{
    private static readonly (string Table, string Domain)[] Tracked =
    [
        ("KhoaHoc", "COURSES"), ("GoiHoc", "COURSES"), ("GoiHocKhoaHoc", "COURSES"),
        ("DangKyGoiHoc", "ACCESS"), ("ChiTietDangKyKhoaHoc", "ACCESS"),
        ("UnitKhoaHoc", "CURRICULUM"), ("LessonKhoaHoc", "CURRICULUM"), ("BuocLoTrinh", "CURRICULUM"),
        ("DanhMucBaiGiang", "LECTURES"), ("BaiGiang", "LECTURES"), ("TaiNguyenBaiGiang", "LECTURES"),
        ("ChuDe", "VOCABULARY"), ("TuVung", "VOCABULARY"), ("TuVungChuDe", "VOCABULARY"),
        ("CauHoi", "PRACTICE"), ("NguLieu", "PRACTICE"), ("NguLieuTaiLieu", "PRACTICE"),
        ("NhomCauHoi", "PRACTICE"),
        ("BaiTapKhoaHoc", "EXERCISES"), ("PhienBanBaiTapKhoaHoc", "EXERCISES"),
        ("CauHoiBaiTapKhoaHoc", "EXERCISES"),
        ("DeThi", "EXAMS"), ("PhienBanDeThi", "EXAMS"), ("CauHoiDeThi", "EXAMS")
    ];
    private static readonly string[] Domains = Tracked.Select(x => x.Domain).Distinct().ToArray();
    private static readonly string ChangeQuery = BuildChangeQuery();
    private readonly SemaphoreSlim gate = new(1, 1);
    private ContentRevisionSnapshot? cached;
    private long? cachedSince;
    private DateTimeOffset expiresAt;

    public async Task<ContentRevisionSnapshot> ReadAsync(long? since, bool fresh, CancellationToken ct)
    {
        if (!fresh && cached is { } current && cachedSince == since && clock.GetUtcNow() < expiresAt)
            return current;
        await gate.WaitAsync(ct);
        try
        {
            if (!fresh && cached is { } again && cachedSince == since && clock.GetUtcNow() < expiresAt)
                return again;
            await using var connection = new SqlConnection(config.GetConnectionString("ToeicDb"));
            await connection.OpenAsync(ct);
            await using var versionCommand = new SqlCommand(
                "SELECT CHANGE_TRACKING_CURRENT_VERSION(), " +
                "(SELECT MAX(min_valid_version) FROM sys.change_tracking_tables)", connection);
            await using var reader = await versionCommand.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.IsDBNull(0))
                throw new InvalidOperationException("Content change tracking is not enabled.");
            var version = reader.GetInt64(0);
            var minValid = reader.IsDBNull(1) ? version : reader.GetInt64(1);
            await reader.CloseAsync();

            IReadOnlyList<string> changed;
            if (since is null || since < minValid || since > version)
                changed = Domains;
            else if (since == version)
                changed = [];
            else
            {
                await using var changes = new SqlCommand(ChangeQuery, connection);
                changes.Parameters.AddWithValue("@last", since.Value);
                changes.Parameters.AddWithValue("@current", version);
                await using var rows = await changes.ExecuteReaderAsync(ct);
                var found = new List<string>();
                while (await rows.ReadAsync(ct)) found.Add(rows.GetString(0));
                changed = found;
            }
            cached = new ContentRevisionSnapshot(version, changed);
            cachedSince = since;
            expiresAt = clock.GetUtcNow().AddSeconds(3);
            return cached;
        }
        finally { gate.Release(); }
    }

    private static string BuildChangeQuery()
    {
        // Identifiers are a fixed whitelist in source, never client input.
        // A single UNION across 24 CHANGETABLE calls can take longer to compile than
        // the learner poll timeout on a full database. Simple sequential statements
        // keep each Change Tracking lookup cheap and skip a domain once found.
        var query = new StringBuilder("DECLARE @found TABLE (Mien varchar(20) PRIMARY KEY); ");
        foreach (var (table, domain) in Tracked)
        {
            query.Append("IF NOT EXISTS (SELECT 1 FROM @found WHERE Mien = '").Append(domain)
                .Append("') AND EXISTS (SELECT 1 FROM ")
                .Append("CHANGETABLE(CHANGES dbo.[").Append(table)
                .Append("], @last) AS CT WHERE CT.SYS_CHANGE_VERSION <= @current) ")
                .Append("INSERT INTO @found (Mien) VALUES ('").Append(domain).Append("'); ");
        }
        return query.Append("SELECT Mien FROM @found;").ToString();
    }
}
