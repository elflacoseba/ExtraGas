using System.ComponentModel.DataAnnotations;

namespace ExtraGasMVC.DTOs;

public class EmpleadoDtoBase
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(100)]
    public string Nombre { get; set; } = null!;

    [Required(ErrorMessage = "El apellido es obligatorio.")]
    [StringLength(100)]
    public string Apellido { get; set; } = null!;

    [StringLength(15)]
    public string? Dni { get; set; }

    [StringLength(15)]
    public string? Cuil { get; set; }

    [StringLength(25)]
    public string? Telefono { get; set; }

    [EmailAddress(ErrorMessage = "El formato del email no es válido.")]
    [StringLength(150)]
    public string? Email { get; set; }

    [StringLength(150)]
    public string? Calle { get; set; }

    [StringLength(10)]
    public string? Numero { get; set; }

    [StringLength(10)]
    public string? Piso { get; set; }

    [StringLength(10)]
    public string? Depto { get; set; }

    [StringLength(100)]
    public string? Ciudad { get; set; }

    [StringLength(10)]
    public string? CodigoPostal { get; set; }

    public ulong? ProvinciaId { get; set; }

    public DateOnly? FechaIngreso { get; set; }

    public ulong? UsuarioId { get; set; }

    public bool Activo { get; set; }

    public string? Observaciones { get; set; }
}

public class EmpleadoDto : EmpleadoDtoBase
{
    public ulong Id { get; set; }
}

public class CreateEmpleadoDto : EmpleadoDtoBase { }

public class UpdateEmpleadoDto : EmpleadoDtoBase
{
    public ulong Id { get; set; }
}

public class ProvinciaDto
{
    public ulong Id { get; set; }
    public string Nombre { get; set; } = null!;
}
