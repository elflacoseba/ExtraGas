using ExtraGasMVC.Configuration;
using ExtraGasMVC.Services.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ExtraGasMVC.Services.Implementations;

/// <summary>
/// Implementacion de <see cref="IEmailSender"/> usando MailKit + MimeKit.
/// Cualquier fallo de transporte (connect/auth/send) se loggea y se traga:
/// un fallo SMTP NO debe propagarse al caller (fire-and-forget design).
/// </summary>
public class MailKitEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(
        IOptions<EmailOptions> options,
        ILogger<MailKitEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken ct = default)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromDisplayName, _options.FromAddress));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            // Por ahora usamos texto plano (textBody) como cuerpo principal;
            // htmlBody queda disponible para una version futura con BodyBuilder.TextPart/HtmlPart.
            var bodyText = textBody ?? htmlBody;
            message.Body = new TextPart("plain")
            {
                Text = bodyText
            };

            using var client = new SmtpClient();

            var secureOptions = _options.UseSsl
                ? SecureSocketOptions.StartTlsWhenAvailable
                : SecureSocketOptions.None;

            await client.ConnectAsync(_options.Host, _options.Port, secureOptions, ct);

            if (!string.IsNullOrEmpty(_options.Username) && _options.Password is not null)
            {
                await client.AuthenticateAsync(_options.Username, _options.Password, ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(quit: true, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Recipient}", to);
        }
    }
}
