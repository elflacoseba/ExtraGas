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
}
