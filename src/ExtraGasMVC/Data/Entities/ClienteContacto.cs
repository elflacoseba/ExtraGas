namespace ExtraGasMVC.Data.Entities;

public class ClienteContacto
{
    public ulong Id { get; set; }
    public ulong ClienteId { get; set; }
    public ulong TipoContactoId { get; set; }
    public string Valor { get; set; } = null!;
    public bool EsPrincipal { get; set; }
    public string? Observaciones { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
