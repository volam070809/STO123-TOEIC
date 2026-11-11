namespace STO123.Services.Arena;

// A match write wakes the deadline worker when it is waiting for a later deadline.
public sealed class ArenaTickSignal
{
    private readonly SemaphoreSlim signal = new(0, 1);

    public void Wake()
    {
        try { signal.Release(); }
        catch (SemaphoreFullException) { }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) =>
        signal.WaitAsync(timeout, ct);
}
