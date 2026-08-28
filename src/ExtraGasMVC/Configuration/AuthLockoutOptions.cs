namespace ExtraGasMVC.Configuration;

/// <summary>
/// Opciones de lockout por intentos fallidos de login.
/// Bound desde la sección "Auth:Lockout" de appsettings.
/// </summary>
public class AuthLockoutOptions
{
    public const string SectionName = "Auth:Lockout";

    /// <summary>
    /// Cantidad máxima de intentos fallidos consecutivos antes de bloquear.
    /// 0 o negativo desactiva el lockout.
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// Duración del bloqueo en minutos tras alcanzar el umbral.
    /// </summary>
    public int LockoutMinutes { get; set; } = 15;
}
