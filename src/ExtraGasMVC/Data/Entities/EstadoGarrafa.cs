namespace ExtraGasMVC.Data.Entities;

public class EstadoGarrafa
{
    public ulong Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public string? Descripcion { get; set; }
    public bool EsDisponibleParaVenta { get; set; }
    public bool RequiereCliente { get; set; }
    public string? Color { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
