namespace ExtraGasMVC.DTOs;

public class EmpleadoDto
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
}

public class CreateEmpleadoDto
{
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
}

public class UpdateEmpleadoDto
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
}
