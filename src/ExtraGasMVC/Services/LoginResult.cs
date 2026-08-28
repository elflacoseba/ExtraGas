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

/// <summary>
/// Resultado de un intento de login. <see cref="AttemptedUserId"/> lleva el id
/// del usuario que fue encontrado en la BD (aunque después el login falle por
/// inactivo, eliminado, lockout o password incorrecta) para que la auditoría
/// pueda vincular el intento al usuario real. Es null solo cuando el username
/// no existe en la tabla.
/// </summary>
public record LoginResult(UsuarioDto? User, LoginFailureReason FailureReason, ulong? AttemptedUserId = null)
{
    public bool Success => User is not null;

    public static LoginResult Ok(UsuarioDto user) => new(user, LoginFailureReason.None, user.Id);

    public static LoginResult Fail(ulong? attemptedUserId, LoginFailureReason reason)
        => new(null, reason, attemptedUserId);
}
