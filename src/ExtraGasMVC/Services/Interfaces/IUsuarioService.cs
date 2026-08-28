using ExtraGasMVC.DTOs;

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
}
