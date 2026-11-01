using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Payment;

public sealed class PaymentExpiryWorker(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<PaymentExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int[] learnerIds;
                using (var scope = scopes.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ToeicDbContext>();
                    var cutoff = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime
                        .AddMinutes(-PaymentExpiry.PendingMinutes);
                    learnerIds = await db.GiaoDichThanhToan.AsNoTracking()
                        .Where(p => p.TrangThai == "CHO_XU_LY" && p.NgayTao < cutoff)
                        .Select(p => p.MaDangKyNavigation.MaNguoiDung).Distinct()
                        .OrderBy(id => id).Take(200)
                        .ToArrayAsync(stoppingToken);
                }
                foreach (var learnerId in learnerIds)
                {
                    using var scope = scopes.CreateScope();
                    try
                    {
                        await scope.ServiceProvider.GetRequiredService<PaymentCreationService>()
                            .ExpirePendingAsync(learnerId, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Could not expire payment for learner {LearnerId}; retrying later", learnerId);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not scan pending payments; retrying later");
            }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
