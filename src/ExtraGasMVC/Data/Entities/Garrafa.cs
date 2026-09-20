namespace ExtraGasMVC.Data.Entities;

public class Garrafa
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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ulong? CreatedBy { get; set; }
    public ulong? UpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Token de concurrencia optimista (issue #182 T04). Protege contra
    /// last-write-wins silencioso en Edits y CambiarEstado concurrentes.
    /// La columna BD es <c>version BIGINT UNSIGNED NOT NULL DEFAULT 0</c>;
    /// el service incrementa <c>entity.Version = originalVersion + 1</c>
    /// antes de <c>SaveChangesAsync</c>, y EF Core agrega el valor leido
    /// al <c>WHERE</c> del <c>UPDATE</c>. Si el RowVersion del form (o del
    /// snapshot del service) quedo desactualizado, EF tira
    /// <c>DbUpdateConcurrencyException</c> que el service traduce a
    /// <c>InvalidOperationException</c> con un mensaje legible.
    /// </summary>
    public ulong Version { get; set; }

    public virtual EstadoGarrafa? EstadoGarrafa { get; set; }
    public virtual Cliente? Cliente { get; set; }
    public virtual Proveedor? Proveedor { get; set; }
}
