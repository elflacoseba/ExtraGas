using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Models.ViewModels;

// Snapshot of the pedido's current state used to render the badge,
// the available transitions and the cancellation reason on the edit view.
public class PedidoEstadoActualInfo
{
    public ulong Id { get; set; }
    public string? Codigo { get; set; }
    public string? Nombre { get; set; }
    public string? Color { get; set; }
    public bool EsFinal { get; set; }
}

// Wrapper for the pedido edit screen.
// Composes the update DTO with lookups, items, totals, current state and transitions.
public class PedidoEditViewModel
{
    public UpdatePedidoDto Pedido { get; set; } = new();
    public IEnumerable<ClienteDto> Clientes { get; set; } = Array.Empty<ClienteDto>();
    public IEnumerable<EmpleadoDto> Empleados { get; set; } = Array.Empty<EmpleadoDto>();
    public IEnumerable<CanalVentaDto> Canales { get; set; } = Array.Empty<CanalVentaDto>();
    public IEnumerable<MedioContactoPedidoDto> MediosContacto { get; set; } = Array.Empty<MedioContactoPedidoDto>();
    public IEnumerable<ProductoDto> Productos { get; set; } = Array.Empty<ProductoDto>();
    public IEnumerable<PedidoItemDto> Items { get; set; } = Array.Empty<PedidoItemDto>();
    public List<EstadoPedidoDto> Transiciones { get; set; } = new();
    public PedidoEstadoActualInfo EstadoActual { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }
    public string? MotivoCancelacion { get; set; }
}
