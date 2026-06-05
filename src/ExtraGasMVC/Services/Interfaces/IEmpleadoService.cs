using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Services.Interfaces;

public interface IEmpleadoService
{
    Task<EmpleadoDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<IEnumerable<EmpleadoDto>> GetAllAsync(CancellationToken ct = default);
    Task<EmpleadoDto> CreateAsync(CreateEmpleadoDto dto, CancellationToken ct = default);
    Task<EmpleadoDto> UpdateAsync(UpdateEmpleadoDto dto, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);
    Task<List<Provincia>> GetProvinciasAsync(CancellationToken ct = default);
}
