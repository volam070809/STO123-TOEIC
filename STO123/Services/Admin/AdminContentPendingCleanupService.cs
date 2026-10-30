using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Admin;

/// <summary>Reconciles stale resource reservations without deleting a referenced Blob.</summary>
public sealed class AdminContentPendingCleanupService(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<AdminContentPendingCleanupService> logger) : BackgroundService
{
    private sealed record Pending(long Id, string Path, Guid Lease);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("AdminContent:EnableIdempotency")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Reconcile(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Admin Content reservation reconciliation failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task Reconcile(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ToeicDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<LectureResourceStorage>();
        var pending = new List<Pending>();
        await db.Database.OpenConnectionAsync(ct);
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = """
                SELECT TOP (25) MaYeuCau, DuongDanBlob, LeaseToken
                FROM dbo.AdminContentIdempotency
                WHERE ThaoTac = 'CREATE_LECTURE_RESOURCE' AND TrangThai = 'PENDING'
                  AND LeaseUntilUtc < SYSUTCDATETIME() AND DuongDanBlob IS NOT NULL
                  AND LeaseToken IS NOT NULL
                ORDER BY LeaseUntilUtc, MaYeuCau
                """;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                pending.Add(new(reader.GetInt64(0), LectureResourceStorage.FromReservationPath(reader.GetString(1)), reader.GetGuid(2)));
        }
        foreach (var row in pending)
        {
            var lease = Guid.NewGuid();
            var claimed = await db.Database.ExecuteSqlRawAsync("""
                UPDATE dbo.AdminContentIdempotency
                SET LeaseToken = @p0, LeaseUntilUtc = DATEADD(minute, 30, SYSUTCDATETIME()),
                    CapNhatLucUtc = SYSUTCDATETIME()
                WHERE MaYeuCau = @p1 AND TrangThai = 'PENDING'
                    AND LeaseToken = @p2 AND LeaseUntilUtc < SYSUTCDATETIME()
                """, [lease, row.Id, row.Lease], ct);
            if (claimed != 1) continue;
            if (await db.TaiNguyenBaiGiang.AsNoTracking().AnyAsync(r => r.DuongDanTep == row.Path, ct))
            {
                logger.LogError("Pending reservation {RequestId} references a live resource; Blob retained", row.Id);
                continue;
            }
            try { await storage.DeleteAsync(row.Path, ct); }
            catch (Exception e)
            {
                logger.LogWarning(e, "Stale lecture Blob cleanup failed for {Path}", row.Path);
                continue;
            }
            await db.Database.ExecuteSqlRawAsync("""
                DELETE FROM dbo.AdminContentIdempotency
                WHERE MaYeuCau = @p0 AND TrangThai = 'PENDING' AND LeaseToken = @p1
                """, [row.Id, lease], ct);
        }
    }
}
