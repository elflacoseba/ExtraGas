namespace ExtraGasMVC.Data.Entities;

public class Provincia
{
    public ulong Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public string Pais { get; set; } = "Argentina";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
