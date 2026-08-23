using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Models.ViewModels;

// Wrapper for the pedido creation screen.
// Composes the create DTO with the lookups required to populate the <select> inputs.
public class PedidoCreateViewModel
{
    public CreatePedidoDto Pedido { get; set; } = new();
    public IEnumerable<ClienteDto> Clientes { get; set; } = Array.Empty<ClienteDto>();
    public IEnumerable<EmpleadoDto> Empleados { get; set; } = Array.Empty<EmpleadoDto>();
    public IEnumerable<CanalVentaDto> Canales { get; set; } = Array.Empty<CanalVentaDto>();
    public IEnumerable<MedioContactoPedidoDto> MediosContacto { get; set; } = Array.Empty<MedioContactoPedidoDto>();
}
