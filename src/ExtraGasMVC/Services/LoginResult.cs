using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Services;

/// <summary>
/// Resultado de un intento de login, usado por IUsuarioService.ValidateAndLoadForAuthAsync.
/// Permite distinguir el motivo de fallo (necesario para mensajes específicos
/// cuando hay lockout y para la auditoría de logins).
/// </summary>
public enum LoginFailureReason
{
    None,
    UserNotFound,
    UserInactive,
    UserDeleted,
    InvalidPassword,
    LockedOut
}

public record LoginResult(UsuarioDto? User, LoginFailureReason FailureReason)
{
    public bool Success => User is not null;

    public static LoginResult Ok(UsuarioDto user) => new(user, LoginFailureReason.None);

    public static LoginResult Fail(LoginFailureReason reason) => new(null, reason);
}
