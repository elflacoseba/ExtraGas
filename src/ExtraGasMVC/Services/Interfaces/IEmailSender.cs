namespace ExtraGasMVC.Services.Interfaces;

/// <summary>
/// Abstraccion para envio de emails transaccionales.
/// Permite reemplazar el transporte real (MailKit/SMTP) en tests.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Envia un email. La implementacion debe loggear y tragar excepciones SMTP
    /// (fire-and-forget design): un fallo de transporte NO debe propagarse al caller.
    /// </summary>
    /// <param name="to">Direccion del destinatario.</param>
    /// <param name="subject">Asunto del mensaje.</param>
    /// <param name="htmlBody">Cuerpo HTML (alternativa enriquecida).</param>
    /// <param name="textBody">Cuerpo plain-text (fallback y version principal para password-recovery).</param>
    /// <param name="ct">Token de cancelacion.</param>
    Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken ct = default);
}
