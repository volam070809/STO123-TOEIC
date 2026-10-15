using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace STO123.Services.Payment;

// VNPAY.NET 2.1.0 creates its own read-only PaymentId and cannot accept our
// persisted merchant reference. Build the documented v2.1.0 request explicitly
// so the signed vnp_TxnRef is exactly the value later matched by the IPN.
public sealed class VNPayGateway(IOptions<VNPayOptions> options) : IPaymentGateway
{
    public string CreateCheckoutUrl(string transactionRef, decimal amount, string clientIp, DateTimeOffset createdAt)
    {
        var settings = options.Value;
        if (!VNPayOptions.IsValid(settings) || string.IsNullOrWhiteSpace(settings.TmnCode) ||
            string.IsNullOrWhiteSpace(settings.HashSecret) || string.IsNullOrWhiteSpace(settings.ReturnUrl))
            throw new InvalidOperationException("VNPay checkout configuration is incomplete.");
        if (transactionRef.Length is < 1 or > 100 ||
            !transactionRef.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z'))
            throw new ArgumentException("Invalid merchant transaction reference.", nameof(transactionRef));
        if (amount <= 0 || amount != decimal.Truncate(amount) || amount > 9_999_999_999m)
            throw new ArgumentOutOfRangeException(nameof(amount), "VNPay requires a positive whole-VND amount.");
        if (!IPAddress.TryParse(clientIp, out var address))
            throw new ArgumentException("Invalid client IP address.", nameof(clientIp));

        var vietnamTime = createdAt.ToUniversalTime().ToOffset(TimeSpan.FromHours(7));
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Amount"] = (amount * 100m).ToString("0", CultureInfo.InvariantCulture),
            ["vnp_Command"] = "pay",
            ["vnp_CreateDate"] = vietnamTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            ["vnp_CurrCode"] = "VND",
            ["vnp_ExpireDate"] = vietnamTime.AddMinutes(15).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            ["vnp_IpAddr"] = address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString(),
            ["vnp_Locale"] = "vn",
            ["vnp_OrderInfo"] = $"Thanh toan khoa hoc {transactionRef}",
            ["vnp_OrderType"] = "other",
            ["vnp_ReturnUrl"] = settings.ReturnUrl.Trim(),
            ["vnp_TmnCode"] = settings.TmnCode.Trim(),
            ["vnp_TxnRef"] = transactionRef,
            ["vnp_Version"] = "2.1.0"
        };

        var query = string.Join('&', values.Select(pair =>
            $"{WebUtility.UrlEncode(pair.Key)}={WebUtility.UrlEncode(pair.Value)}"));
        var key = Encoding.UTF8.GetBytes(settings.HashSecret.Trim());
        var hash = HMACSHA512.HashData(key, Encoding.UTF8.GetBytes(query));
        return $"{settings.PaymentUrl.Trim()}?{query}&vnp_SecureHash={Convert.ToHexStringLower(hash)}";
    }
}
