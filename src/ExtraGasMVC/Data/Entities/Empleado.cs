namespace ExtraGasMVC.Data.Entities;

public class Empleado
{
    public ulong Id { get; set; }
    public string Nombre { get; set; } = null!;
    public string Apellido { get; set; } = null!;
    public string? Dni { get; set; }
    public string? Cuil { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? Calle { get; set; }
    public string? Numero { get; set; }
    public string? Piso { get; set; }
    public string? Depto { get; set; }
    public string? Ciudad { get; set; }
    public string? CodigoPostal { get; set; }
    public ulong? ProvinciaId { get; set; }
    public DateOnly? FechaIngreso { get; set; }
    public ulong? UsuarioId { get; set; }
    public bool Activo { get; set; }
    public string? Observaciones { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ulong? CreatedBy { get; set; }
    public ulong? UpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}
