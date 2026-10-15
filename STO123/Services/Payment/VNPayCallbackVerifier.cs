using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace STO123.Services.Payment;

public enum VNPayCallbackError { None, InvalidRequest, InvalidSignature }

public sealed record VNPayCallback(
    string TransactionRef, long Amount, string TransactionNumber,
    string ResponseCode, string TransactionStatus);

public sealed record VNPayCallbackVerification(VNPayCallbackError Error, VNPayCallback? Callback = null);

public sealed class VNPayCallbackVerifier(IOptions<VNPayOptions> options)
{
    public VNPayCallbackVerification Verify(IQueryCollection query)
    {
        if (string.IsNullOrWhiteSpace(options.Value.TmnCode) ||
            string.IsNullOrWhiteSpace(options.Value.HashSecret))
            return new(VNPayCallbackError.InvalidRequest);
        if (query.Count is < 1 or > 40 ||
            query.Any(item => !item.Key.StartsWith("vnp_", StringComparison.Ordinal) ||
                              item.Value.Count != 1 || item.Key.Length > 64 ||
                              item.Value[0] is null || item.Value[0]!.Length > 2048))
            return new(VNPayCallbackError.InvalidRequest);

        if (!Single(query, "vnp_SecureHash", out var suppliedHash) ||
            suppliedHash.Length != 128 || !IsHex(suppliedHash))
            return new(VNPayCallbackError.InvalidSignature);

        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in query)
        {
            if (item.Key is "vnp_SecureHash" or "vnp_SecureHashType") continue;
            fields.Add(item.Key, item.Value[0]!);
        }
        var canonical = string.Join('&', fields.Select(item =>
            $"{WebUtility.UrlEncode(item.Key)}={WebUtility.UrlEncode(item.Value)}"));
        var expected = HMACSHA512.HashData(
            Encoding.UTF8.GetBytes(options.Value.HashSecret.Trim()), Encoding.UTF8.GetBytes(canonical));
        var actual = Convert.FromHexString(suppliedHash);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            return new(VNPayCallbackError.InvalidSignature);

        if (!Single(query, "vnp_TmnCode", out var merchant) ||
            !string.Equals(merchant, options.Value.TmnCode.Trim(), StringComparison.Ordinal) ||
            !Single(query, "vnp_TxnRef", out var reference) || reference.Length is < 1 or > 100 ||
            !reference.All(IsAsciiLetterOrDigit) ||
            !Single(query, "vnp_Amount", out var amountText) ||
            amountText.Length is < 1 or > 12 || !amountText.All(IsAsciiDigit) ||
            !long.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0 ||
            !Single(query, "vnp_BankCode", out var bankCode) || bankCode.Length is < 3 or > 20 ||
            !bankCode.All(IsAsciiLetterOrDigit) ||
            !Single(query, "vnp_OrderInfo", out var orderInfo) ||
            orderInfo.Length is < 1 or > 255 ||
            !Single(query, "vnp_TransactionNo", out var transactionNo) ||
            transactionNo.Length is < 1 or > 15 || !transactionNo.All(IsAsciiDigit) ||
            !Single(query, "vnp_ResponseCode", out var responseCode) ||
            responseCode.Length != 2 || !responseCode.All(IsAsciiDigit) ||
            !Single(query, "vnp_TransactionStatus", out var transactionStatus) ||
            transactionStatus.Length != 2 || !transactionStatus.All(IsAsciiDigit) ||
            (query.ContainsKey("vnp_CurrCode") &&
             (!Single(query, "vnp_CurrCode", out var currency) || currency != "VND")))
            return new(VNPayCallbackError.InvalidRequest);

        return new(VNPayCallbackError.None,
            new VNPayCallback(reference, amount, transactionNo, responseCode, transactionStatus));
    }

    private static bool Single(IQueryCollection query, string name, out string value)
    {
        value = string.Empty;
        if (!query.TryGetValue(name, out var values) || values.Count != 1 || values[0] is null)
            return false;
        value = values[0]!;
        return true;
    }

    private static bool IsHex(string value) => value.All(ch =>
        ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static bool IsAsciiDigit(char ch) => ch is >= '0' and <= '9';
    private static bool IsAsciiLetterOrDigit(char ch) => IsAsciiDigit(ch) ||
        ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
