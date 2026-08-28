namespace ExtraGasMVC.Configuration;

/// <summary>
/// Política configurable de contraseñas. Bound desde "Auth:PasswordPolicy" de appsettings.
///
/// IMPORTANTE: el binding se hace en el arranque del proceso con IOptions&lt;&gt;.
/// Cambios en appsettings.json requieren reinicio de la aplicacion.
/// Si se necesita hot-reload, migrar a IOptionsMonitor&lt;&gt;.
/// </summary>
public class PasswordPolicyOptions
{
    public const string SectionName = "Auth:PasswordPolicy";

    /// <summary>
    /// Longitud mínima de la contraseña. 0 o negativo desactiva la regla.
    /// </summary>
    public int MinLength { get; set; } = 8;

    /// <summary>
    /// Requiere al menos una letra mayúscula (A-Z).
    /// </summary>
    public bool RequireUppercase { get; set; } = true;

    /// <summary>
    /// Requiere al menos una letra minúscula (a-z).
    /// </summary>
    public bool RequireLowercase { get; set; } = true;

    /// <summary>
    /// Requiere al menos un dígito (0-9).
    /// </summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>
    /// Requiere al menos un símbolo no alfanumérico.
    /// </summary>
    public bool RequireSpecialChar { get; set; } = false;

    /// <summary>
    /// Cantidad máxima de caracteres consecutivos repetidos permitidos.
    /// 0 o negativo desactiva la regla. Default 4 evita "aaaa".
    /// </summary>
    public int MaxConsecutiveChars { get; set; } = 4;
}
