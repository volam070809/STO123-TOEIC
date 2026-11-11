namespace STO123.Services.Arena;

public sealed class ArenaTickWorker(IServiceScopeFactory scopes, ArenaTickSignal signal,
    ILogger<ArenaTickWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var arena = scope.ServiceProvider.GetRequiredService<ArenaService>();
                await arena.TickAsync(stoppingToken);
                var next = await arena.NextDeadlineUtcAsync(stoppingToken);
                if (next is null)
                    await signal.WaitAsync(TimeSpan.FromHours(2), stoppingToken);
                else
                {
                    var delay = next.Value - DateTime.UtcNow;
                    if (delay > TimeSpan.Zero)
                        await signal.WaitAsync(TimeSpan.FromTicks(Math.Min(delay.Ticks,
                            TimeSpan.FromHours(2).Ticks)), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogError(error, "Arena deadline update failed");
                try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
