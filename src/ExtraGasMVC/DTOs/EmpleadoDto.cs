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

    public string? Observaciones { get; set; }
}

/// <summary>
/// DTO de salida para <see cref="Data.Entities.Empleado"/>. Incluye el campo
/// operativo <c>Activo</c> que NO es editable desde ningún formulario: se
/// expone solo para display (Details, Index, listados).
///
/// <para>Issue #114 (replicado en Empleados): <c>Activo</c> solo cambia vía
/// Delete. <c>FechaIngreso</c> sí es editable — es un dato de negocio del
/// empleado (nullable, lo carga el operador al alta y puede corregirlo
/// después), a diferencia de <c>Cliente.FechaAlta</c> que es audit trail.</para>
/// </summary>
public class EmpleadoDto : EmpleadoDtoBase
{
    public ulong Id { get; set; }

    [Display(Name = "Activo")]
    public bool Activo { get; set; }
}

/// <summary>
/// DTO de alta de empleado. NO incluye <c>Activo</c> (lo setea el Service en
/// <c>true</c>). Sin esto el operador podía crear un empleado inactivo desde
/// el formulario — un estado operacional incoherente. Issue #114.
/// </summary>
public class CreateEmpleadoDto : EmpleadoDtoBase { }

/// <summary>
/// DTO de edición de empleado. NO incluye <c>Activo</c>: es estado y solo
/// cambia vía Delete. Editarlo desde el form producía estados zombie
/// (<c>Activo=false</c> con <c>DeletedAt=null</c>). El Service lo preserva
/// vía <c>EmpleadoEditRules.PreservarFlagsNoEditables</c>. Issue #114.
/// <c>FechaIngreso</c> sí es editable (dato de negocio).
/// </summary>
public class UpdateEmpleadoDto : EmpleadoDtoBase
{
    public ulong Id { get; set; }
}

public class ProvinciaDto
{
    public ulong Id { get; set; }
    public string Nombre { get; set; } = null!;
}
