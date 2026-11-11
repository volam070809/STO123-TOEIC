namespace STO123.Services.Payment;

public sealed class PaymentExpirySignal
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
