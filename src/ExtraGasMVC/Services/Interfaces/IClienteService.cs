using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;

namespace ExtraGasMVC.Services.Interfaces;

public interface IClienteService
{
    Task<ClienteDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<IEnumerable<ClienteDto>> GetAllAsync(CancellationToken ct = default);
    Task<ClienteDto?> GetByDniAsync(string dni, CancellationToken ct = default);
    Task<IEnumerable<ClienteDto>> GetActivosAsync(CancellationToken ct = default);
    Task<PagedResult<ClienteDto>> SearchAsync(
        string? busqueda, bool soloActivos,
        int pagina, int tamanio, CancellationToken ct = default);
    Task<ClienteDto> CreateAsync(CreateClienteDto cliente, ulong? createdBy, CancellationToken ct = default);
    Task<ClienteDto> UpdateAsync(UpdateClienteDto cliente, ulong? updatedBy, CancellationToken ct = default);
    Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default);
    Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default);
    Task<List<ProvinciaDto>> GetProvinciasAsync(CancellationToken ct = default);

    /// <summary>
    /// Saldos agregados por cliente para la pantalla CuentasCorrientes.
    /// Lee la vista <c>v_saldo_clientes</c> (una sola query) en lugar de
    /// cargar todos los clientes y resolver saldo/pedidos en la vista (N+1).
    /// Orden: saldo DESC, nombre ASC para que los deudores más grandes
    /// queden arriba. Issue #109.
    /// </summary>
    Task<IEnumerable<VSaldoClienteDto>> GetSaldosAsync(CancellationToken ct = default);
}
