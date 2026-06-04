using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Services.Interfaces;

public interface IUsuarioService
{
    Task<UsuarioDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<IEnumerable<UsuarioDto>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<UsuarioDto>> GetActivosAsync(CancellationToken ct = default);
    Task<UsuarioDto?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<UsuarioDto> CreateAsync(CreateUsuarioDto dto, ulong createdBy, CancellationToken ct = default);
    Task<UsuarioDto> UpdateAsync(UpdateUsuarioDto dto, ulong updatedBy, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
    Task<bool> ChangePasswordAsync(ulong id, string currentPassword, string newPassword, CancellationToken ct = default);
    Task<bool> ValidateLoginAsync(string username, string password, CancellationToken ct = default);
    Task<UsuarioDto?> GetByUsernameForAuthAsync(string username, CancellationToken ct = default);
}
