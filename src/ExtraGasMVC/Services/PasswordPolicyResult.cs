namespace ExtraGasMVC.Services;

/// <summary>
/// Resultado de validar una contraseña contra la política configurada.
/// Errors contiene mensajes legibles para mostrar al usuario (uno por regla violada).
/// </summary>
public record PasswordPolicyResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static PasswordPolicyResult Ok() => new(true, Array.Empty<string>());

    public static PasswordPolicyResult Fail(params string[] errors) => new(false, errors);
}
