using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace STO123.Services.Auth;

public sealed class EmailService(IConfiguration configuration) : IEmailService
{
    public async Task SendOtpAsync(string toEmail, string purpose, string code, CancellationToken cancellationToken)
    {
        var host = configuration["Smtp:Host"];
        var port = configuration.GetValue<int>("Smtp:Port");
        var fromEmail = configuration["Smtp:FromEmail"];
        var fromName = configuration["Smtp:FromName"] ?? "STO123";
        var username = configuration["Smtp:Username"];
        var password = configuration["Smtp:Password"];
        var useSsl = configuration.GetValue<bool>("Smtp:UseSsl");

        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535 || string.IsNullOrWhiteSpace(fromEmail))
            throw new InvalidOperationException("Smtp:Host, Smtp:Port, and Smtp:FromEmail must be configured.");
        if (string.IsNullOrWhiteSpace(username) != string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Smtp:Username and Smtp:Password must be configured together.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = purpose == "QUEN_MAT_KHAU" ? "STO123 password reset code" : "STO123 email verification code";
        message.Body = new TextPart("plain")
        {
            Text = $"Your STO123 code is {code}. It expires in 15 minutes."
        };

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(host, port, useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, cancellationToken);
        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            await smtp.AuthenticateAsync(username, password, cancellationToken);
        await smtp.SendAsync(message, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);
    }
}
