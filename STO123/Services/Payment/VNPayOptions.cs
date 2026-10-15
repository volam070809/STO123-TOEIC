using System.Text.RegularExpressions;

namespace STO123.Services.Payment;

public sealed class VNPayOptions
{
    public const string SectionName = "VNPay";
    public const string SandboxPaymentUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";

    public string TmnCode { get; set; } = string.Empty;
    public string HashSecret { get; set; } = string.Empty;
    public string PaymentUrl { get; set; } = SandboxPaymentUrl;
    public string ReturnUrl { get; set; } = string.Empty;
    public string FrontendResultUrl { get; set; } = string.Empty;
    public bool CheckoutEnabled { get; set; }

    public static bool IsValid(VNPayOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PaymentUrl) ||
            !string.Equals(options.PaymentUrl.Trim(), SandboxPaymentUrl, StringComparison.Ordinal))
            return false;

        var hasMerchant = !string.IsNullOrWhiteSpace(options.TmnCode) ||
            !string.IsNullOrWhiteSpace(options.HashSecret);
        if ((options.CheckoutEnabled || hasMerchant) &&
            (!Regex.IsMatch(options.TmnCode.Trim(), "^[A-Za-z0-9]{8}$") ||
             string.IsNullOrWhiteSpace(options.HashSecret)))
            return false;

        if (!string.IsNullOrWhiteSpace(options.FrontendResultUrl) &&
            (!Uri.TryCreate(options.FrontendResultUrl.Trim(), UriKind.Absolute, out var resultUri) ||
             (resultUri.Scheme != Uri.UriSchemeHttps &&
              !(resultUri.Scheme == Uri.UriSchemeHttp && resultUri.IsLoopback)) ||
             resultUri.Query.Length != 0 ||
             resultUri.Fragment.Length != 0))
            return false;

        return !options.CheckoutEnabled ||
            (!string.IsNullOrWhiteSpace(options.ReturnUrl) &&
             Uri.TryCreate(options.ReturnUrl.Trim(), UriKind.Absolute, out var uri) &&
             uri.Scheme == Uri.UriSchemeHttps && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
             !string.IsNullOrWhiteSpace(options.FrontendResultUrl));
    }
}
