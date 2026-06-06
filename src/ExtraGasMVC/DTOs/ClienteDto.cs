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
    [RegularExpression("^[0-9]+$", ErrorMessage = "El DNI debe ser numérico.")]
    public string? Dni { get; set; }

    [Display(Name = "Cuit/Cuil")]
    [StringLength(15, ErrorMessage = "El CUIT/CUIL no puede superar {1} caracteres.")]
    [RegularExpression("^[0-9]{11}$|^[0-9]{2}-[0-9]{8}-[0-9]{1}$", ErrorMessage = "El CUIT/CUIL debe tener 11 dígitos (con o sin guiones).")]
    public string? CuitCuil { get; set; }

    [Display(Name = "Teléfono principal")]
    [Required(ErrorMessage = "El teléfono principal es obligatorio.")]
    [StringLength(25, ErrorMessage = "El teléfono no puede superar {1} caracteres.")]
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

    [Display(Name = "Fecha de alta")]
    [Required(ErrorMessage = "La fecha de alta es obligatoria.")]
    public DateOnly FechaAlta { get; set; }

    [Display(Name = "Activo")]
    public bool Activo { get; set; }

    [Display(Name = "Referencias")]
    public string? Referencias { get; set; }

    [Display(Name = "Observaciones")]
    public string? Observaciones { get; set; }
}

public class ClienteDto : ClienteDtoBase
{
    public ulong Id { get; set; }
}

public class CreateClienteDto : ClienteDtoBase { }

public class UpdateClienteDto : ClienteDtoBase
{
    public ulong Id { get; set; }
}
