namespace STO123.Services.Payment.Models;

public class PaymentCreateResult
{
    public string PaymentUrl { get; set; } = string.Empty;

    public string TransactionRef { get; set; } = string.Empty;

    public string QrCode { get; set; } = string.Empty;
}