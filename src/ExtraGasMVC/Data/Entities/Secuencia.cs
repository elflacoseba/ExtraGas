namespace ExtraGasMVC.Data.Entities;

public class Secuencia
{
    public ulong Id { get; set; }
    public string Nombre { get; set; } = null!;
    public string Prefijo { get; set; } = null!;
    public ushort Anio { get; set; }
    public uint UltimoValor { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
