using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Exam;

public sealed class PracticeExpiryWorker(IServiceScopeFactory scopes, ILogger<PracticeExpiryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ToeicDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);
                await db.Database.ExecuteSqlRawAsync("""
                    SELECT MaKetQua INTO #ExpiredPractice
                    FROM dbo.KetQuaLamBai AS k WITH (UPDLOCK, HOLDLOCK)
                    WHERE k.LoaiBaiLam = 'PRACTICE' AND k.TrangThai = 'DANG_LAM'
                        AND k.NgayLamBai < DATEADD(day, -90, SYSUTCDATETIME())
                        AND NOT EXISTS (SELECT 1 FROM dbo.TienDoBuocLoTrinh p WHERE p.MaKetQua = k.MaKetQua)
                        AND NOT EXISTS (SELECT 1 FROM dbo.KetQuaPhanLopKNN x WHERE x.MaKetQua = k.MaKetQua);
                    DELETE d FROM dbo.ChiTietKetQua d
                    JOIN dbo.CauHoiLuotLam q ON q.MaCauHoiLuotLam = d.MaCauHoiLuotLam
                    JOIN #ExpiredPractice e ON e.MaKetQua = q.MaKetQua;
                    DELETE q FROM dbo.CauHoiLuotLam q JOIN #ExpiredPractice e ON e.MaKetQua = q.MaKetQua;
                    DELETE n FROM dbo.NhomLuotLam n JOIN #ExpiredPractice e ON e.MaKetQua = n.MaKetQua;
                    DELETE k FROM dbo.KetQuaLamBai k JOIN #ExpiredPractice e ON e.MaKetQua = k.MaKetQua;
                    """, stoppingToken);
                await transaction.CommitAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not clean up expired active Part practice sessions; retrying later");
            }
            try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
