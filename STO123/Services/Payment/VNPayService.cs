using Microsoft.Extensions.Options;
using VNPAY;
using VNPAY.Models;
using VNPAY.Models.Enums;
using STO123.Services.Payment.Models;
using QRCoder;

namespace STO123.Services.Payment;

public class VNPayService : IPaymentGateway
{
    private readonly IVnpayClient _vnpayClient;

    public VNPayService(
        IVnpayClient vnpayClient,
        IOptions<VNPaySettings> options)
    {
        _vnpayClient = vnpayClient;
    }

    public PaymentCreateResult CreatePaymentUrl(
        decimal amount,
        string orderInfo,
        string returnUrl)
    {
        var request = new VnpayPaymentRequest
        {
            Money = (double)amount,
            Description = orderInfo,
            BankCode = BankCode.ANY,
            Language = DisplayLanguage.Vietnamese
        };

        // Tạo payment URL từ VNPAY
        var paymentUrlInfo =
            _vnpayClient.CreatePaymentUrl(request);

        // Lấy vnp_TxnRef
        var uri = new Uri(paymentUrlInfo.Url);

        var query =
            System.Web.HttpUtility.ParseQueryString(uri.Query);

        var transactionRef = query["vnp_TxnRef"];

        if (string.IsNullOrWhiteSpace(transactionRef))
        {
            throw new InvalidOperationException(
                "Không lấy được vnp_TxnRef từ URL VNPay."
            );
        }

        // Tạo QR chứa payment URL
        using var qrGenerator = new QRCodeGenerator();

        using var qrData =
            qrGenerator.CreateQrCode(
                paymentUrlInfo.Url,
                QRCodeGenerator.ECCLevel.Q);

        var pngQrCode = new PngByteQRCode(qrData);

        byte[] qrBytes =
            pngQrCode.GetGraphic(20);

        string qrBase64 =
            Convert.ToBase64String(qrBytes);

        return new PaymentCreateResult
        {
            PaymentUrl = paymentUrlInfo.Url,
            TransactionRef = transactionRef,
            QrCode = $"data:image/png;base64,{qrBase64}"
        };
    }
}