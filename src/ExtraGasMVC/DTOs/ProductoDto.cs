using System.ComponentModel.DataAnnotations;

namespace ExtraGasMVC.DTOs;

/// <summary>
/// DTO de salida para <see cref="Data.Entities.Producto"/>. Incluye el campo
/// operativo <c>Activo</c> que NO es editable desde ningún formulario: se
/// expone solo para display (Details, Index, listados).
///
/// <para>Issue #114 (replicado en Productos): <c>Activo</c> solo cambia vía
/// Delete. <c>ManejaGarrafaIndividual</c> sí es editable — es config de
/// negocio del producto (define cómo se factura y rastrea).</para>
/// </summary>
public class ProductoDto
{
    public ulong Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public string? Descripcion { get; set; }
    public ulong TipoProductoId { get; set; }
    public string? TipoProductoNombre { get; set; }
    public decimal? CapacidadKg { get; set; }
    public string UnidadVenta { get; set; } = "UNIDAD";
    public decimal PrecioActual { get; set; }
    public bool ManejaGarrafaIndividual { get; set; }
    public bool Activo { get; set; }

    // Issue #147 item 4: auditoría visible en Details/Edit. Los timestamps
    // se mapean por convención desde Producto.CreatedAt/UpdatedAt; los
    // usernames NO se mapean por convención (Producto expone CreatedBy/
    // UpdatedBy como ulong FK, no como string) — el Service los resuelve
    // explícitamente vía LoadAuditUsersAsync + AplicarAudit y los asigna
    // después del Map. El MappingProfile tiene .Ignore() explícito para
    // los dos usernames como defensa en profundidad (regresión #118).
    [Display(Name = "Creado")]
    public DateTime CreatedAt { get; set; }

    [Display(Name = "Última modificación")]
    public DateTime UpdatedAt { get; set; }

    [Display(Name = "Creado por")]
    public string? CreatedByUserName { get; set; }

    [Display(Name = "Modificado por")]
    public string? UpdatedByUserName { get; set; }
}

/// <summary>
/// DTO de alta de producto. NO incluye <c>Activo</c> (lo setea el Service en
/// <c>true</c>). Sin esto el operador podía crear un producto inactivo desde
/// el formulario — un estado operacional incoherente. Issue #114.
/// </summary>
public class CreateProductoDto
{
    [Display(Name = "Código")]
    [Required(ErrorMessage = "El código es obligatorio.")]
    [StringLength(30, ErrorMessage = "El código no puede superar {1} caracteres.")]
    public string Codigo { get; set; } = null!;

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(150, ErrorMessage = "El nombre no puede superar {1} caracteres.")]
    public string Nombre { get; set; } = null!;

    [Display(Name = "Descripción")]
    [StringLength(255, ErrorMessage = "La descripción no puede superar {1} caracteres.")]
    public string? Descripcion { get; set; }

    [Required(ErrorMessage = "El tipo de producto es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un tipo de producto válido.")]
    public ulong TipoProductoId { get; set; }

    [Display(Name = "Capacidad (kg)")]
    [Range(0.01, 9999999999.99, ErrorMessage = "La capacidad debe ser un valor positivo.")]
    public decimal? CapacidadKg { get; set; }

    [Display(Name = "Unidad de venta")]
    [StringLength(20, ErrorMessage = "La unidad de venta no puede superar {1} caracteres.")]
    public string UnidadVenta { get; set; } = "UNIDAD";

    [Display(Name = "Precio actual")]
    [Range(0, 9999999999.99, ErrorMessage = "El precio debe estar entre {1} y {2}.")]
    public decimal PrecioActual { get; set; }

    public bool ManejaGarrafaIndividual { get; set; }
}

/// <summary>
/// DTO de edición de producto. NO incluye <c>Activo</c>: es estado y solo
/// cambia vía Delete. Editarlo desde el form producía estados zombie
/// (<c>Activo=false</c> con <c>DeletedAt=null</c>). El Service lo preserva
/// vía <c>ProductoEditRules.PreservarFlagsNoEditables</c>. Issue #114.
/// <c>ManejaGarrafaIndividual</c> sí es editable (config de negocio).
/// </summary>
public class UpdateProductoDto
{
    public ulong Id { get; set; }

    [Display(Name = "Código")]
    [Required(ErrorMessage = "El código es obligatorio.")]
    [StringLength(30, ErrorMessage = "El código no puede superar {1} caracteres.")]
    public string Codigo { get; set; } = null!;

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(150, ErrorMessage = "El nombre no puede superar {1} caracteres.")]
    public string Nombre { get; set; } = null!;

    [Display(Name = "Descripción")]
    [StringLength(255, ErrorMessage = "La descripción no puede superar {1} caracteres.")]
    public string? Descripcion { get; set; }

    [Required(ErrorMessage = "El tipo de producto es obligatorio.")]
    [Range(1, ulong.MaxValue, ErrorMessage = "Seleccione un tipo de producto válido.")]
    public ulong TipoProductoId { get; set; }

    [Display(Name = "Capacidad (kg)")]
    [Range(0.01, 9999999999.99, ErrorMessage = "La capacidad debe ser un valor positivo.")]
    public decimal? CapacidadKg { get; set; }

    [Display(Name = "Unidad de venta")]
    [StringLength(20, ErrorMessage = "La unidad de venta no puede superar {1} caracteres.")]
    public string UnidadVenta { get; set; } = "UNIDAD";

    [Display(Name = "Precio actual")]
    [Range(0, 9999999999.99, ErrorMessage = "El precio debe estar entre {1} y {2}.")]
    public decimal PrecioActual { get; set; }

    public bool ManejaGarrafaIndividual { get; set; }

    /// <summary>
    /// Motivo del cambio de precio. Solo tiene sentido registrarlo cuando el
    /// precio cambió (el hook de Slice 3 en <c>ProductoService.UpdateAsync</c>
    /// ignora este campo si <c>PrecioActual</c> queda igual). Es metadata de
    /// auditoría — NO se mapea a la entity <c>Producto</c> (MappingProfile
    /// lo ignora explícitamente) y NO se persiste en <c>productos</c>; vive
    /// solo en <c>producto_precios_historico</c>.
    /// Issue #145 Slice 3.
    /// </summary>
    [Display(Name = "Motivo del cambio de precio")]
    [StringLength(255, ErrorMessage = "El motivo no puede superar {1} caracteres.")]
    public string? MotivoCambioPrecio { get; set; }
}
