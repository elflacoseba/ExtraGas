namespace ExtraGasMVC.DTOs;

public class ClienteDto
{
    public ulong Id { get; set; }
    public string? Codigo { get; set; }
    public string Nombre { get; set; } = null!;
    public string Apellido { get; set; } = null!;
    public string? Dni { get; set; }
    public string? CuitCuil { get; set; }
    public string TelefonoPrincipal { get; set; } = null!;
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
    public string? Observaciones { get; set; }
    public DateOnly FechaAlta { get; set; }
    public bool Activo { get; set; }
}

public class CreateClienteDto
{
    public string? Codigo { get; set; }
    public string Nombre { get; set; } = null!;
    public string Apellido { get; set; } = null!;
    public string? Dni { get; set; }
    public string? CuitCuil { get; set; }
    public string TelefonoPrincipal { get; set; } = null!;
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
    public string? Observaciones { get; set; }
    public DateOnly FechaAlta { get; set; }
    public bool Activo { get; set; }
}

public class UpdateClienteDto
{
    public ulong Id { get; set; }
    public string? Codigo { get; set; }
    public string Nombre { get; set; } = null!;
    public string Apellido { get; set; } = null!;
    public string? Dni { get; set; }
    public string? CuitCuil { get; set; }
    public string TelefonoPrincipal { get; set; } = null!;
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
    public string? Observaciones { get; set; }
    public DateOnly FechaAlta { get; set; }
    public bool Activo { get; set; }
}
