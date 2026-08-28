using ExtraGasMVC.Services;

namespace ExtraGasMVC.Services.Interfaces;

public interface IPasswordPolicyService
{
    /// <summary>
    /// Valida una contraseña contra la política configurada.
    /// Devuelve PasswordPolicyResult.Ok() si cumple todas las reglas.
    /// </summary>
    PasswordPolicyResult Validate(string? password);
}
