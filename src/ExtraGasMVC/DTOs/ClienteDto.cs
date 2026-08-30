using System.ComponentModel.DataAnnotations;

namespace ExtraGasMVC.DTOs;

public class ClienteDtoBase
{
    [Display(Name = "Nombre")]
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(100, ErrorMessage = "El nombre no puede superar {1} caracteres.")]
    public string Nombre { get; set; } = null!;

    [Display(Name = "Apellido")]
    [Required(ErrorMessage = "El apellido es obligatorio.")]
    [StringLength(100, ErrorMessage = "El apellido no puede superar {1} caracteres.")]
    public string Apellido { get; set; } = null!;

    [Display(Name = "Código")]
    [StringLength(20, ErrorMessage = "El código no puede superar {1} caracteres.")]
    public string? Codigo { get; set; }

    [Display(Name = "DNI")]
    [StringLength(15, ErrorMessage = "El DNI no puede superar {1} caracteres.")]
    [RegularExpression("^[0-9 .\\-]*$", ErrorMessage = "El DNI debe ser numérico (se admiten espacios, puntos y guiones como separadores).")]
    public string? Dni { get; set; }

    [Display(Name = "Cuit/Cuil")]
    [StringLength(15, ErrorMessage = "El CUIT/CUIL no puede superar {1} caracteres.")]
    [RegularExpression("^[0-9]{11}$|^[0-9]{2}-[0-9]{8}-[0-9]{1}$", ErrorMessage = "El CUIT/CUIL debe tener 11 dígitos (con o sin guiones).")]
    public string? CuitCuil { get; set; }

    [Display(Name = "Teléfono principal")]
    [Required(ErrorMessage = "El teléfono principal es obligatorio.")]
    [StringLength(25, ErrorMessage = "El teléfono no puede superar {1} caracteres.")]
    [RegularExpression("^[0-9 +()\\-.]*$", ErrorMessage = "El teléfono admite dígitos, espacios y separadores (+, -, paréntesis, puntos).")]
    public string TelefonoPrincipal { get; set; } = null!;

    [Display(Name = "Teléfono secundario")]
    [StringLength(25, ErrorMessage = "El teléfono no puede superar {1} caracteres.")]
    public string? TelefonoSecundario { get; set; }

    [Display(Name = "Email")]
    [EmailAddress(ErrorMessage = "El formato del email no es válido.")]
    [StringLength(150, ErrorMessage = "El email no puede superar {1} caracteres.")]
    public string? Email { get; set; }

    [Display(Name = "Calle")]
    [StringLength(150, ErrorMessage = "La calle no puede superar {1} caracteres.")]
    public string? Calle { get; set; }

    [Display(Name = "Número")]
    [StringLength(10, ErrorMessage = "El número no puede superar {1} caracteres.")]
    public string? Numero { get; set; }

    [Display(Name = "Piso")]
    [StringLength(10, ErrorMessage = "El piso no puede superar {1} caracteres.")]
    public string? Piso { get; set; }

    [Display(Name = "Depto.")]
    [StringLength(10, ErrorMessage = "El departamento no puede superar {1} caracteres.")]
    public string? Depto { get; set; }

    [Display(Name = "Ciudad")]
    [StringLength(100, ErrorMessage = "La ciudad no puede superar {1} caracteres.")]
    public string? Ciudad { get; set; }

    [Display(Name = "Código postal")]
    [StringLength(10, ErrorMessage = "El código postal no puede superar {1} caracteres.")]
    public string? CodigoPostal { get; set; }

    [Display(Name = "Provincia")]
    public ulong? ProvinciaId { get; set; }

    [Display(Name = "Referencias")]
    public string? Referencias { get; set; }

    [Display(Name = "Observaciones")]
    public string? Observaciones { get; set; }
}

/// <summary>
/// DTO de salida para <see cref="Data.Entities.Cliente"/>. Incluye los campos
/// operativos (<c>Activo</c>, <c>FechaAlta</c>) que NO son editables desde
/// ningún formulario: se exponen solo para display (Details, Index, listados).
/// Issue #114: <c>Activo</c> solo cambia vía Delete/Restore; <c>FechaAlta</c>
/// es audit trail del alta y no debe retrocederse.
/// <para>Issue #111: <c>DeletedAt</c> se expone para que la pantalla
/// /Clientes/Papelera muestre la fecha de baja. Es null para clientes
/// activos (la mayoria del tiempo) y no es editable.</para>
/// </summary>
public class ClienteDto : ClienteDtoBase
{
    public ulong Id { get; set; }

    [Display(Name = "Fecha de alta")]
    public DateOnly FechaAlta { get; set; }

    [Display(Name = "Activo")]
    public bool Activo { get; set; }

    [Display(Name = "Fecha de baja")]
    public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// DTO de alta de cliente. NO incluye <c>Activo</c> (lo setea el Service en
/// <c>true</c>) ni <c>FechaAlta</c> (lo setea el Service con la fecha del
/// momento del alta). Sin esto el operador podía crear un cliente inactivo
/// desde el formulario — un estado operacional incoherente. Issue #114.
/// </summary>
public class CreateClienteDto : ClienteDtoBase { }

/// <summary>
/// DTO de edición de cliente. NO incluye <c>Activo</c> ni <c>FechaAlta</c>:
/// ambos son audit trail / estado y solo cambian vía Delete/Restore
/// (<c>Activo</c>) o quedan fijos desde el alta (<c>FechaAlta</c>).
/// Editarlos desde el form producía estados zombie
/// (<c>Activo=false</c> con <c>DeletedAt=null</c>). El Service los preserva
/// vía <c>ClienteEditRules.PreservarFlagsNoEditables</c>. Issue #114.
/// </summary>
public class UpdateClienteDto : ClienteDtoBase
{
    public ulong Id { get; set; }
}
