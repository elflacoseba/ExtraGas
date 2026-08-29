using System.ComponentModel.DataAnnotations;

namespace ExtraGasMVC.DTOs;

public class GarrafaDto
{
    public ulong Id { get; set; }
    public string Codigo { get; set; } = null!;
    public byte CapacidadKg { get; set; }
    public ulong? ProveedorId { get; set; }
    public ulong? RecepcionId { get; set; }
    public DateOnly FechaCompra { get; set; }
    public ulong EstadoGarrafaId { get; set; }
    public ulong? ClienteId { get; set; }
    public bool Activo { get; set; }
    public DateTime? FechaUltimoMovimiento { get; set; }
    public string? Observaciones { get; set; }

    /// <summary>
    /// Código canónico del estado actual (ej. <c>FUERA_SERVICIO</c>). Se usa
    /// en la UI para condicionar acciones (editar, cambiar estado) según la
    /// máquina de estados del módulo Garrafas.
    /// </summary>
    public string? EstadoCodigo { get; set; }

    /// <summary>
    /// Nombre legible del estado (ej. "Llena en depósito"). Se renderiza
    /// como badge con <see cref="EstadoColor"/> en las vistas (issue #47).
    /// </summary>
    public string? EstadoNombre { get; set; }

    /// <summary>
    /// Color HEX del estado (catálogo <c>estados_garrafa.color</c>). Se aplica
    /// como background del badge en las vistas (issue #47).
    /// </summary>
    public string? EstadoColor { get; set; }

    /// <summary>
    /// Nombre completo del cliente actual ("Apellido, Nombre"). Null cuando la
    /// garrafa no está asignada a un cliente (issue #47).
    /// </summary>
    public string? ClienteNombre { get; set; }

    /// <summary>
    /// Razón social del proveedor de la garrafa. Null cuando no tiene
    /// proveedor asociado (issue #47).
    /// </summary>
    public string? ProveedorNombre { get; set; }
}

/// <summary>
/// DTO de alta de garrafa. NO incluye <c>Activo</c> (lo setea el Service en
/// <c>true</c>). El flag <c>Activo</c> de Garrafa cumple un rol distinto al
/// de <c>estado_garrafa_id</c>: marca si la garrafa está dada de baja
/// (soft-delete), mientras que el estado describe la situación operacional
/// (LLENA_DEPOSITO, EN_CLIENTE, FUERA_SERVICIO, etc.). Solo cambia vía
/// Delete. Issue #114.
/// </summary>
public class CreateGarrafaDto
{
    [Display(Name = "Código")]
    [Required(ErrorMessage = "El código es obligatorio.")]
    [MaxLength(50, ErrorMessage = "El código no puede superar 50 caracteres.")]
    public string Codigo { get; set; } = null!;

    [Display(Name = "Capacidad (kg)")]
    [Required(ErrorMessage = "La capacidad es obligatoria.")]
    [Range(10, 45, ErrorMessage = "La capacidad debe estar entre 10 y 45 kg.")]
    public byte CapacidadKg { get; set; }

    public ulong? ProveedorId { get; set; }

    public ulong? RecepcionId { get; set; }

    [Display(Name = "Fecha de compra")]
    [Required(ErrorMessage = "La fecha de compra es obligatoria.")]
    public DateOnly FechaCompra { get; set; }

    [Display(Name = "Estado")]
    [Required(ErrorMessage = "El estado de la garrafa es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un estado válido.")]
    public ulong EstadoGarrafaId { get; set; }

    public ulong? ClienteId { get; set; }

    public string? Observaciones { get; set; }
}

/// <summary>
/// DTO de edición de garrafa. NO incluye <c>Activo</c>: es estado y solo
/// cambia vía Delete. El Service lo preserva vía
/// <c>GarrafaEditRules.PreservarFlagsNoEditables</c>. Issue #114.
///
/// <para>El estado operacional de la garrafa (<c>EstadoGarrafaId</c>) sí es
/// editable — se cambia con la acción dedicada "Cambiar estado" del
/// Controller, que valida contra la matriz de transiciones. NO se mezcla
/// con <c>Activo</c>: son dos flags con propósitos distintos.</para>
/// </summary>
public class UpdateGarrafaDto
{
    public ulong Id { get; set; }

    [Display(Name = "Código")]
    [Required(ErrorMessage = "El código es obligatorio.")]
    [MaxLength(50, ErrorMessage = "El código no puede superar 50 caracteres.")]
    public string Codigo { get; set; } = null!;

    [Display(Name = "Capacidad (kg)")]
    [Required(ErrorMessage = "La capacidad es obligatoria.")]
    [Range(10, 45, ErrorMessage = "La capacidad debe estar entre 10 y 45 kg.")]
    public byte CapacidadKg { get; set; }

    public ulong? ProveedorId { get; set; }

    public ulong? RecepcionId { get; set; }

    [Display(Name = "Fecha de compra")]
    [Required(ErrorMessage = "La fecha de compra es obligatoria.")]
    public DateOnly FechaCompra { get; set; }

    [Display(Name = "Estado")]
    [Required(ErrorMessage = "El estado de la garrafa es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un estado válido.")]
    public ulong EstadoGarrafaId { get; set; }

    public ulong? ClienteId { get; set; }

    public string? Observaciones { get; set; }
}

public class CambiarEstadoGarrafaDto
{
    public ulong NuevoEstadoId { get; set; }
    public ulong? ClienteId { get; set; }
    public string? Observaciones { get; set; }

    /// <summary>
    /// Set when the state change is part of a pedido canje. Carries the pedido
    /// id so the resulting <c>movimiento_garrafa</c> row links back to the pedido
    /// for traceability. Ignored on the manual CAMBIO_ESTADO flow.
    /// </summary>
    public ulong? PedidoId { get; set; }

    /// <summary>
    /// Type code for the canje movement (e.g. <c>ENTREGA_CLIENTE</c>,
    /// <c>DEVOLUCION_CLIENTE</c>). When present, the service emits a
    /// non-<c>CAMBIO_ESTADO</c> movement with this type instead of the default
    /// manual type. Ignored on the manual CAMBIO_ESTADO flow.
    /// </summary>
    public string? TipoMovimientoCodigo { get; set; }
}

/// <summary>
/// Bound from a single textarea in the canje modal: the item id plus the
/// trimmed/deduped list of physical garrafa codes the operator entered.
/// </summary>
public class CodigoGarrafaItemDto
{
    public ulong ItemId { get; set; }
    public List<string> Codigos { get; set; } = new();
}
