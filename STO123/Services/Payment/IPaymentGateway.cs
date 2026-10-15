namespace STO123.Services.Payment;

public interface IPaymentGateway
{
    string CreateCheckoutUrl(string transactionRef, decimal amount, string clientIp, DateTimeOffset createdAt);
}
