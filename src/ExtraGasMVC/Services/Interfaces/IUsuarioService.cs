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
    Task<LoginResult> ValidateAndLoadForAuthAsync(string username, string password, CancellationToken ct = default);
}
