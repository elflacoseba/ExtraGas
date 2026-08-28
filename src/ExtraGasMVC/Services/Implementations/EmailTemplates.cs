namespace ExtraGasMVC.Services.Implementations;

/// <summary>
/// Plantillas de email en espanol para el flujo de recuperacion de password.
/// Cada metodo devuelve el cuerpo plain-text listo para enviar.
/// </summary>
public static class EmailTemplates
{
    /// <summary>
    /// Asunto para el email de restablecimiento.
    /// </summary>
    public const string ResetLinkSubject = "Restablecé tu contraseña — ExtraGas";

    /// <summary>
    /// Asunto para el email de notificacion de cambio exitoso.
    /// </summary>
    public const string PasswordChangedSubject = "Tu contraseña fue cambiada — ExtraGas";

    /// <summary>
    /// Email con el link de restablecimiento de contrasena (valido 1 hora).
    /// </summary>
    /// <param name="recipientName">Nombre a mostrar (ej. "Juan").</param>
    /// <param name="resetUrl">URL completa con el token raw (ej. "https://app/Account/ResetPassword?token=...").</param>
    public static string ResetLink(string recipientName, string resetUrl)
    {
        return $@"Hola {recipientName},

Recibimos un pedido para restablecer la contrasena de tu cuenta en ExtraGas.

Para elegir una nueva contrasena hace clic en el siguiente enlace (caduca en 1 hora):

{resetUrl}

Si no lo solicitaste vos, ignora este mensaje.

— Equipo ExtraGas";
    }

    /// <summary>
    /// Email de notificacion de cambio de contrasena exitoso.
    /// NO contiene link de reset; advierte contactar al admin si no fue el usuario.
    /// </summary>
    /// <param name="recipientName">Nombre a mostrar (ej. "Juan").</param>
    public static string PasswordChanged(string recipientName)
    {
        return $@"Hola {recipientName},

Te confirmamos que la contrasena de tu cuenta en ExtraGas fue cambiada exitosamente.

Si no realizaste este cambio, contacta inmediatamente al administrador.

— Equipo ExtraGas";
    }
}
