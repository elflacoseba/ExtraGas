using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services;

namespace ExtraGasMVC.Services.Interfaces;


public interface IUsuarioService
{
    Task<UsuarioDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<SearchResultDto<UsuarioDto>> SearchAsync(
        string? busqueda, ulong? rolId, bool soloActivos,
        int pagina, int tamanio, CancellationToken ct = default);
    Task<List<RolDto>> GetRolesAsync(CancellationToken ct = default);
    Task<List<EmpleadoSinUsuarioDto>> GetEmpleadosSinUsuarioAsync(CancellationToken ct = default);
    Task<UsuarioDto?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<UsuarioDto> CreateAsync(CreateUsuarioDto dto, ulong? createdBy, CancellationToken ct = default);
    Task<UsuarioDto> UpdateAsync(UpdateUsuarioDto dto, ulong? updatedBy, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
    Task<bool> ChangePasswordAsync(ulong id, string currentPassword, string newPassword, CancellationToken ct = default);

    /// <summary>
    /// Cambia la password de un usuario sin pedir la actual. Usado cuando el
    /// usuario esta forzado a cambiar su password (debe_cambiar_password = true).
    /// </summary>
    Task ChangePasswordWithoutCurrentAsync(ulong id, string newPassword, CancellationToken ct = default);

    /// <summary>
    /// Genera una password temporal aleatoria, la hashea, setea
    /// debe_cambiar_password = true y la devuelve (se muestra una sola vez).
    /// </summary>
    Task<string> ResetPasswordAsync(ulong id, ulong? updatedBy, CancellationToken ct = default);

    Task<LoginResult> ValidateAndLoadForAuthAsync(string username, string password, CancellationToken ct = default);

    /// <summary>
    /// Solicita un reset de contrasena para el usuario con el email indicado.
    /// Emite un token de un solo uso con vigencia limitada, persiste su hash
    /// SHA-256 y envia el token raw por email. Si el email no esta registrado
    /// (o el usuario esta inactivo) retorna en silencio, sin enviar email, para
    /// no permitir enumeracion de cuentas. ipAddress/userAgent son de auditoria.
    /// </summary>
    Task RequestPasswordResetAsync(string email, string? ipAddress, string? userAgent, CancellationToken ct = default);

    /// <summary>
    /// Valida un token de reset raw, lo marca como consumido si es valido y
    /// actualiza el hash BCrypt de la contrasena del usuario.
    /// Ante exito envia un email de notificacion de cambio.
    /// </summary>
    /// <returns>Un <see cref="ConsumeResetTokenResult"/> que describe el desenlace.</returns>
    Task<ConsumeResetTokenResult> ConsumePasswordResetTokenAsync(string rawToken, string newPassword, CancellationToken ct = default);
}
