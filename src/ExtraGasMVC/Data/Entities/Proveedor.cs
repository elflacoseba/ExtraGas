namespace ExtraGasMVC.Data.Entities;

public class Proveedor
{
    public ulong Id { get; set; }
    public string? Codigo { get; set; }
    public string RazonSocial { get; set; } = null!;
    public string? NombreFantasia { get; set; }
    public string Cuit { get; set; } = null!;
    public string? TelefonoPrincipal { get; set; }
    public string? TelefonoSecundario { get; set; }
    public string? Email { get; set; }
    public string? Calle { get; set; }
    public string? Numero { get; set; }
    public string? Piso { get; set; }
    public string? Depto { get; set; }
    public string? Ciudad { get; set; }
    public string? CodigoPostal { get; set; }
    public ulong? ProvinciaId { get; set; }
    public string? Referencias { get; set; }
    public string? ContactoNombre { get; set; }
    public string? ContactoTelefono { get; set; }
    public string? ContactoEmail { get; set; }
    public string? Observaciones { get; set; }
    public bool Activo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ulong? CreatedBy { get; set; }
    public ulong? UpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}
