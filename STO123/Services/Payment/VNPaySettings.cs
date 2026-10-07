namespace STO123.Services.Payment;

public class VNPaySettings
{
    public string TmnCode { get; set; } = string.Empty;

    public string HashSecret { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string ReturnUrl { get; set; } = string.Empty;
}