namespace ExtraGasMVC.DTOs;

public class GarrafaDto
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
}

public class CreateGarrafaDto
{
    public string Codigo { get; set; } = null!;
    public byte CapacidadKg { get; set; }
    public ulong? ProveedorId { get; set; }
    public ulong? RecepcionId { get; set; }
    public DateOnly FechaCompra { get; set; }
    public ulong EstadoGarrafaId { get; set; }
    public ulong? ClienteId { get; set; }
    public bool Activo { get; set; }
    public string? Observaciones { get; set; }
}

public class UpdateGarrafaDto
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
    public string? Observaciones { get; set; }
}

public class CambiarEstadoGarrafaDto
{
    public ulong NuevoEstadoId { get; set; }
    public ulong? ClienteId { get; set; }
    public string? Observaciones { get; set; }
}
