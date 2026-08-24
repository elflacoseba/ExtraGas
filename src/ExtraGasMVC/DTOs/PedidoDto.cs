using System.ComponentModel.DataAnnotations;

namespace ExtraGasMVC.DTOs;

public class PedidoDto
{
    public ulong Id { get; set; }
    public string? Numero { get; set; }
    public DateTime Fecha { get; set; }
    public DateTime? FechaEntrega { get; set; }
    public ulong ClienteId { get; set; }
    public string? ClienteNombre { get; set; }
    public ulong EmpleadoId { get; set; }
    public string? EmpleadoNombre { get; set; }
    public ulong EstadoPedidoId { get; set; }
    public string? EstadoCodigo { get; set; }
    public string? EstadoNombre { get; set; }
    public string? EstadoColor { get; set; }
    public ulong CanalVentaId { get; set; }
    public string? CanalNombre { get; set; }
    public ulong? MedioContactoId { get; set; }
    public string? MedioContactoNombre { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal Total { get; set; }
    public decimal MontoPagado { get; set; }
    public decimal Saldo { get; set; }
    public string? Observaciones { get; set; }
    public string? MotivoCancelacion { get; set; }
    public string? DireccionEntrega { get; set; }
    public List<PedidoItemDto> Items { get; set; } = new();
}

public class CreatePedidoDto
{
    [Display(Name = "Fecha")]
    [Required(ErrorMessage = "La fecha es obligatoria.")]
    public DateTime Fecha { get; set; }

    [Display(Name = "Fecha de entrega")]
    public DateTime? FechaEntrega { get; set; }

    [Display(Name = "Cliente")]
    [Required(ErrorMessage = "El cliente es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un cliente válido.")]
    public ulong ClienteId { get; set; }

    [Display(Name = "Empleado")]
    [Required(ErrorMessage = "El empleado es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un empleado válido.")]
    public ulong EmpleadoId { get; set; }

    [Display(Name = "Canal de venta")]
    [Required(ErrorMessage = "El canal de venta es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un canal de venta válido.")]
    public ulong CanalVentaId { get; set; }

    [Display(Name = "Medio de contacto")]
    public ulong? MedioContactoId { get; set; }

    [Display(Name = "Dirección de entrega")]
    [StringLength(255, ErrorMessage = "La dirección no puede superar {1} caracteres.")]
    public string? DireccionEntrega { get; set; }

    [Display(Name = "Observaciones")]
    [StringLength(2000, ErrorMessage = "Las observaciones no pueden superar {1} caracteres.")]
    public string? Observaciones { get; set; }
}

public class UpdatePedidoDto
{
    public ulong Id { get; set; }

    [Display(Name = "Fecha")]
    [Required(ErrorMessage = "La fecha es obligatoria.")]
    public DateTime Fecha { get; set; }

    [Display(Name = "Fecha de entrega")]
    public DateTime? FechaEntrega { get; set; }

    [Display(Name = "Cliente")]
    [Required(ErrorMessage = "El cliente es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un cliente válido.")]
    public ulong ClienteId { get; set; }

    [Display(Name = "Empleado")]
    [Required(ErrorMessage = "El empleado es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un empleado válido.")]
    public ulong EmpleadoId { get; set; }

    [Display(Name = "Canal de venta")]
    [Required(ErrorMessage = "El canal de venta es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un canal de venta válido.")]
    public ulong CanalVentaId { get; set; }

    [Display(Name = "Medio de contacto")]
    public ulong? MedioContactoId { get; set; }

    [Display(Name = "Descuento (%)")]
    [Range(0, 100, ErrorMessage = "El descuento debe estar entre 0 y 100.")]
    public decimal Descuento { get; set; }

    [Display(Name = "Dirección de entrega")]
    [StringLength(255, ErrorMessage = "La dirección no puede superar {1} caracteres.")]
    public string? DireccionEntrega { get; set; }

    [Display(Name = "Observaciones")]
    [StringLength(2000, ErrorMessage = "Las observaciones no pueden superar {1} caracteres.")]
    public string? Observaciones { get; set; }
}

public class PedidoItemDto
{
    public ulong Id { get; set; }
    public ulong PedidoId { get; set; }
    public ulong ProductoId { get; set; }
    public string? ProductoNombre { get; set; }
    public string? ProductoCodigo { get; set; }
    public string TipoLinea { get; set; } = null!;
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal { get; set; }
    public string? Observaciones { get; set; }

    /// <summary>
    /// Copiado de <c>Producto.ManejaGarrafaIndividual</c>. Cuando es
    /// <c>true</c> y el item es ENTREGA/DEVOLUCION, la app exige un código
    /// físico por unidad en el canje (issue #44).
    /// </summary>
    public bool ManejaGarrafaIndividual { get; set; }

    /// <summary>
    /// Copiado de <c>Producto.CapacidadKg</c> para que la UI de canje
    /// muestre la capacidad sin un join extra (issue #44).
    /// </summary>
    public decimal? CapacidadKg { get; set; }
}

public class CreatePedidoItemDto
{
    public ulong PedidoId { get; set; }

    [Display(Name = "Producto")]
    [Required(ErrorMessage = "El producto es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un producto válido.")]
    public ulong ProductoId { get; set; }

    [Display(Name = "Tipo de línea")]
    [Required(ErrorMessage = "El tipo de línea es obligatorio.")]
    public string TipoLinea { get; set; } = "VENTA";

    [Display(Name = "Cantidad")]
    [Required(ErrorMessage = "La cantidad es obligatoria.")]
    [Range(0.01, 99999999.99, ErrorMessage = "La cantidad debe ser mayor a 0.")]
    public decimal Cantidad { get; set; }

    [Display(Name = "Observaciones")]
    [StringLength(255, ErrorMessage = "Las observaciones no pueden superar {1} caracteres.")]
    public string? Observaciones { get; set; }
}

public class UpdatePedidoItemDto
{
    public ulong Id { get; set; }

    [Display(Name = "Producto")]
    [Required(ErrorMessage = "El producto es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un producto válido.")]
    public ulong ProductoId { get; set; }

    [Display(Name = "Tipo de línea")]
    [Required(ErrorMessage = "El tipo de línea es obligatorio.")]
    public string TipoLinea { get; set; } = "VENTA";

    [Display(Name = "Cantidad")]
    [Required(ErrorMessage = "La cantidad es obligatoria.")]
    [Range(0.01, 99999999.99, ErrorMessage = "La cantidad debe ser mayor a 0.")]
    public decimal Cantidad { get; set; }

    [Display(Name = "Precio unitario")]
    [Required(ErrorMessage = "El precio unitario es obligatorio.")]
    [Range(0, 9999999999.99, ErrorMessage = "El precio unitario debe ser mayor o igual a 0.")]
    public decimal PrecioUnitario { get; set; }

    [Display(Name = "Observaciones")]
    [StringLength(255, ErrorMessage = "Las observaciones no pueden superar {1} caracteres.")]
    public string? Observaciones { get; set; }
}

public class EstadoPedidoDto
{
    public ulong Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public string? Color { get; set; }
    public bool EsFinal { get; set; }
}

public class CanalVentaDto
{
    public ulong Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
}

public class MedioContactoPedidoDto
{
    public ulong Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
}

public class PedidoViewModel
{
    public PedidoDto Pedido { get; set; } = null!;
    public List<PedidoItemDto> Items { get; set; } = new();
    public decimal SubtotalCalculado { get; set; }
    public decimal TotalCalculado { get; set; }
}
