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

    /// <summary>
    /// Código canónico del estado actual (ej. <c>FUERA_SERVICIO</c>). Se usa
    /// en la UI para condicionar acciones (editar, cambiar estado) según la
    /// máquina de estados del módulo Garrafas.
    /// </summary>
    public string? EstadoCodigo { get; set; }
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
