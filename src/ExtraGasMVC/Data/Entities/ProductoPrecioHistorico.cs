namespace ExtraGasMVC.Data.Entities;

/// <summary>
/// Registro append-only de cambios de precio de un <see cref="Producto"/>.
/// Una fila por cambio real (precio_anterior != precio_nuevo && precio_anterior != 0).
/// Sin soft-delete, sin updated_at: la tabla es inmutable por convención y por
/// diseño (issue #145). El Service <c>ProductoService.UpdateAsync</c> (Slice 3)
/// es el único punto de INSERT.
///
/// Decisión de diseño: la columna se llama <c>MotivoCambioPrecio</c> en C# y
/// <c>motivo_cambio_precio</c> en SQL, siguiendo la convención snake_case del
/// repositorio (AGENTS.md §Convenciones).
/// </summary>
public class ProductoPrecioHistorico
{
    public ulong Id { get; set; }
    public ulong ProductoId { get; set; }
    public decimal PrecioAnterior { get; set; }
    public decimal PrecioNuevo { get; set; }
    public string? MotivoCambioPrecio { get; set; }
    public ulong? ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }

    public Producto? Producto { get; set; }
    public Usuario? ChangedByUsuario { get; set; }
}
