using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Payment;

public sealed class PaymentExpiryWorker(IServiceScopeFactory scopes, TimeProvider clock,
    PaymentExpirySignal signal,
    ILogger<PaymentExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int[] learnerIds;
                DateTime? nextPending;
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
                    nextPending = await db.GiaoDichThanhToan.AsNoTracking()
                        .Where(p => p.TrangThai == "CHO_XU_LY")
                        .Select(p => (DateTime?)p.NgayTao).MinAsync(stoppingToken);
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
                if (learnerIds.Length > 0)
                {
                    // A failed learner may still have an overdue payment. Avoid a hot retry loop.
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }
                var delay = nextPending is null ? TimeSpan.FromHours(2) :
                    nextPending.Value.AddMinutes(PaymentExpiry.PendingMinutes) -
                    clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
                if (delay <= TimeSpan.Zero) delay = TimeSpan.FromMinutes(1);
                await signal.WaitAsync(TimeSpan.FromTicks(Math.Min(delay.Ticks,
                    TimeSpan.FromHours(2).Ticks)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not scan pending payments; retrying later");
                try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
