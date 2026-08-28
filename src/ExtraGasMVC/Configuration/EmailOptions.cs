namespace ExtraGasMVC.Configuration;

/// <summary>
/// Opciones para el envio de emails transaccionales (SMTP via MailKit).
/// Bound desde la seccion "Email" de appsettings.
///
/// IMPORTANTE: el binding se hace en el arranque del proceso con IOptions&lt;&gt;.
/// Cambios en appsettings.json requieren reinicio de la aplicacion.
/// Si se necesita hot-reload, migrar a IOptionsMonitor&lt;&gt;.
///
/// En produccion las credenciales (Username/Password) NO van en appsettings.json:
/// se setean con <c>dotnet user-secrets set "Email:Username" "..." --project src/ExtraGasMVC</c>
/// y su equivalente para <c>Email:Password</c>.
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Host SMTP (ej. "smtp.gmail.com" en prod, "localhost" en dev con MailHog).
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Puerto SMTP. 587 para STARTTLS, 465 para SSL implicito, 1025 para MailHog dev.
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// Si true, usa STARTTLS (<c>SecureSocketOptions.StartTlsWhenAvailable</c>) sobre el puerto 587.
    /// Si false, conecto en plano (util para MailHog local).
    /// </summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>
    /// Direccion del remitente (ej. "noreply@extragas.com").
    /// </summary>
    public string FromAddress { get; set; } = "noreply@example.com";

    /// <summary>
    /// Nombre a mostrar del remitente (ej. "ExtraGas").
    /// </summary>
    public string FromDisplayName { get; set; } = "ExtraGas";

    /// <summary>
    /// Usuario SMTP (opcional: MailHog en dev no requiere auth).
    /// En prod se setea por user-secrets.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password SMTP (opcional: MailHog en dev no requiere auth).
    /// En prod se setea por user-secrets.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// URL base para construir links en emails (ej. "https://extragas.example.com").
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5000";
}
