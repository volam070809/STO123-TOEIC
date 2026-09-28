namespace STO123.Services.Auth;

public interface IEmailService
{
    Task SendOtpAsync(string toEmail, string purpose, string code, CancellationToken cancellationToken);
}
