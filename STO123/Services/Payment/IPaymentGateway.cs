using STO123.Services.Payment.Models;

namespace STO123.Services.Payment;

public interface IPaymentGateway
{
    PaymentCreateResult CreatePaymentUrl(
        decimal amount,
        string orderInfo,
        string returnUrl);
}