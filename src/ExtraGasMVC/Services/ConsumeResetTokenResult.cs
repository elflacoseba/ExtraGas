namespace ExtraGasMVC.Services;

/// <summary>
/// Desenlace posible al intentar consumir un token de reset de contrasena.
/// El controller mapea cada valor a un mensaje para el usuario.
/// </summary>
public enum ConsumeResetTokenOutcome
{
    Success,
    InvalidToken,
    ExpiredToken,
    AlreadyUsed,
    WeakPassword,
    UnknownError
}

/// <summary>
/// Resultado de <c>ConsumePasswordResetTokenAsync</c>.
/// </summary>
/// <param name="Outcome">Desenlace de la operacion.</param>
/// <param name="ErrorMessage">
/// Mensaje legible para el usuario cuando el desenlace no es
/// <see cref="ConsumeResetTokenOutcome.Success"/>. Null en caso de exito.
/// </param>
public record ConsumeResetTokenResult(ConsumeResetTokenOutcome Outcome, string? ErrorMessage = null);
