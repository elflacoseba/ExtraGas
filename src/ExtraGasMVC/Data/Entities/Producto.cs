namespace ExtraGasMVC.Data.Entities;

public class Producto
{
    public ulong Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public string? Descripcion { get; set; }
    public ulong TipoProductoId { get; set; }
    public decimal? CapacidadKg { get; set; }
    public string UnidadVenta { get; set; } = "UNIDAD";

    /// <summary>
    /// Issue #147 slice 3 item 7: FK a la lookup <c>unidades_venta</c>.
    /// Nullable para la ventana de transición: la columna legacy
    /// <c>unidad_venta</c> (VARCHAR) convive con esta durante el
    /// expand-contract hasta que se haga el DROP COLUMN en una migración
    /// cleanup (ver design.md Open Questions #1 y ADR #12 pattern).
    /// La app prefiere este FK si está populado y cae al VARCHAR como
    /// fallback. Una vez completada la transición, este será NOT NULL.
    /// </summary>
    public ulong? UnidadVentaId { get; set; }

    public decimal PrecioActual { get; set; }
    public bool ManejaGarrafaIndividual { get; set; }
    public bool Activo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ulong? CreatedBy { get; set; }
    public ulong? UpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Token de concurrencia optimista. Issue #146.4: protege contra
    /// last-write-wins silencioso cuando dos operadores editan el mismo
    /// producto a la vez. La columna BD es <c>row_version BINARY(8)</c>
    /// y un trigger BEFORE UPDATE la incrementa automáticamente; EF Core
    /// la agrega al WHERE del UPDATE para detectar conflictos. Si el
    /// RowVersion del form quedó desactualizado, EF tira
    /// <c>DbUpdateConcurrencyException</c> que el Service traduce a
    /// <c>ValidationException</c> con un mensaje legible.
    ///
    /// <para>Nullable en C# aunque la columna BD es NOT NULL con DEFAULT:
    /// cuando EF carga una fila existente el valor viene poblado, pero
    /// cuando crea una nueva aún no existe en BD. La convención EF Core
    /// permite byte[] null en el INSERT y la BD rellena con DEFAULT.</para>
    /// </summary>
    public byte[]? RowVersion { get; set; }

    public virtual TipoProducto? TipoProducto { get; set; }

    /// <summary>
    /// Issue #147 slice 3 item 7: navigation property a la lookup
    /// <see cref="UnidadVenta"/>. Usada por el Service para hidratar
    /// <c>ProductoDto.UnidadVentaNombre</c> vía <c>.Include(p => p.UnidadVentaRef)</c>.
    /// Se llama <c>UnidadVentaRef</c> para evitar colisión con la columna
    /// legacy <c>UnidadVenta</c> (VARCHAR) — C# no permite dos members
    /// con el mismo nombre. La configuración EF lo mapea explícitamente
    /// vía <c>HasOne(p =&gt; p.UnidadVentaRef)</c>.
    /// </summary>
    public virtual UnidadVenta? UnidadVentaRef { get; set; }
}
