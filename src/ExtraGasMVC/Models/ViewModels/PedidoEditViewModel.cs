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

    /// <summary>
    /// Subset of <see cref="Items"/> que requieren tracking físico de garrafas
    /// (ENTREGA/DEVOLUCION con <c>Producto.ManejaGarrafaIndividual == true</c>).
    /// Cuando está vacío, el modal de canje no se muestra al confirmar (issue #44).
    /// </summary>
    public List<PedidoItemGarrafaVm> ItemsGarrafaCanje { get; set; } = new();
}

// Snapshot para el modal de canje en la vista Edit. Cada item GARRAFA
// con ENTREGA/DEVOLUCION produce una entrada que la UI rinde como un
// textarea con su etiqueta y cantidad esperada.
public class PedidoItemGarrafaVm
{
    public ulong ItemId { get; set; }
    public string ProductoNombre { get; set; } = string.Empty;
    public decimal? CapacidadKg { get; set; }
    public string TipoLinea { get; set; } = string.Empty;
    public int CantidadEsperada { get; set; }
}
