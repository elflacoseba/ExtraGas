using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Services.Interfaces;

public interface IEmpleadoService
{
    Task<EmpleadoDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<SearchResultDto<EmpleadoDto>> SearchAsync(
        string? busqueda, bool soloActivos,
        int pagina, int tamanio, CancellationToken ct = default);
    Task<EmpleadoDto> CreateAsync(CreateEmpleadoDto dto, ulong createdBy, CancellationToken ct = default);
    Task<EmpleadoDto> UpdateAsync(UpdateEmpleadoDto dto, ulong updatedBy, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
    Task<List<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default);
}
