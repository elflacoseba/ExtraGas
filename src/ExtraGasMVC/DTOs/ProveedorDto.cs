using System.ComponentModel.DataAnnotations;

namespace ExtraGasMVC.DTOs;

public class ProveedorDto
{
    public ulong Id { get; set; }
    public string? Codigo { get; set; }
    public string RazonSocial { get; set; } = null!;
    public string? NombreFantasia { get; set; }
    public string Cuit { get; set; } = null!;
    public string? TelefonoPrincipal { get; set; }
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
    public string? ContactoNombre { get; set; }
    public string? ContactoTelefono { get; set; }
    public string? ContactoEmail { get; set; }
    public string? Observaciones { get; set; }
    public bool Activo { get; set; }
}

public class CreateProveedorDto
{
    [Display(Name = "Código")]
    public string? Codigo { get; set; }

    [Display(Name = "Razón social")]
    [Required(ErrorMessage = "La razón social es obligatoria.")]
    [StringLength(200, ErrorMessage = "La razón social no puede superar {1} caracteres.")]
    public string RazonSocial { get; set; } = null!;

    [Display(Name = "Nombre de fantasía")]
    public string? NombreFantasia { get; set; }

    [Display(Name = "CUIT")]
    [Required(ErrorMessage = "El CUIT es obligatorio.")]
    [RegularExpression(@"^[0-9]{11}$", ErrorMessage = "El CUIT debe contener 11 dígitos numéricos.")]
    public string Cuit { get; set; } = null!;

    [Display(Name = "Teléfono principal")]
    public string? TelefonoPrincipal { get; set; }

    [Display(Name = "Teléfono secundario")]
    public string? TelefonoSecundario { get; set; }

    [Display(Name = "Email")]
    [EmailAddress(ErrorMessage = "El formato del email no es válido.")]
    public string? Email { get; set; }

    [Display(Name = "Calle")]
    public string? Calle { get; set; }

    [Display(Name = "Número")]
    public string? Numero { get; set; }

    [Display(Name = "Piso")]
    public string? Piso { get; set; }

    [Display(Name = "Departamento")]
    public string? Depto { get; set; }

    [Display(Name = "Ciudad")]
    public string? Ciudad { get; set; }

    [Display(Name = "Código postal")]
    public string? CodigoPostal { get; set; }

    [Display(Name = "Provincia")]
    public ulong? ProvinciaId { get; set; }

    [Display(Name = "Referencias")]
    public string? Referencias { get; set; }

    [Display(Name = "Nombre de contacto")]
    public string? ContactoNombre { get; set; }

    [Display(Name = "Teléfono de contacto")]
    public string? ContactoTelefono { get; set; }

    [Display(Name = "Email de contacto")]
    [EmailAddress(ErrorMessage = "El formato del email de contacto no es válido.")]
    public string? ContactoEmail { get; set; }

    [Display(Name = "Observaciones")]
    public string? Observaciones { get; set; }

    [Display(Name = "Activo")]
    public bool Activo { get; set; }
}

public class UpdateProveedorDto
{
    public ulong Id { get; set; }

    [Display(Name = "Código")]
    public string? Codigo { get; set; }

    [Display(Name = "Razón social")]
    [Required(ErrorMessage = "La razón social es obligatoria.")]
    [StringLength(200, ErrorMessage = "La razón social no puede superar {1} caracteres.")]
    public string RazonSocial { get; set; } = null!;

    [Display(Name = "Nombre de fantasía")]
    public string? NombreFantasia { get; set; }

    [Display(Name = "CUIT")]
    [Required(ErrorMessage = "El CUIT es obligatorio.")]
    [RegularExpression(@"^[0-9]{11}$", ErrorMessage = "El CUIT debe contener 11 dígitos numéricos.")]
    public string Cuit { get; set; } = null!;

    [Display(Name = "Teléfono principal")]
    public string? TelefonoPrincipal { get; set; }

    [Display(Name = "Teléfono secundario")]
    public string? TelefonoSecundario { get; set; }

    [Display(Name = "Email")]
    [EmailAddress(ErrorMessage = "El formato del email no es válido.")]
    public string? Email { get; set; }

    [Display(Name = "Calle")]
    public string? Calle { get; set; }

    [Display(Name = "Número")]
    public string? Numero { get; set; }

    [Display(Name = "Piso")]
    public string? Piso { get; set; }

    [Display(Name = "Departamento")]
    public string? Depto { get; set; }

    [Display(Name = "Ciudad")]
    public string? Ciudad { get; set; }

    [Display(Name = "Código postal")]
    public string? CodigoPostal { get; set; }

    [Display(Name = "Provincia")]
    public ulong? ProvinciaId { get; set; }

    [Display(Name = "Referencias")]
    public string? Referencias { get; set; }

    [Display(Name = "Nombre de contacto")]
    public string? ContactoNombre { get; set; }

    [Display(Name = "Teléfono de contacto")]
    public string? ContactoTelefono { get; set; }

    [Display(Name = "Email de contacto")]
    [EmailAddress(ErrorMessage = "El formato del email de contacto no es válido.")]
    public string? ContactoEmail { get; set; }

    [Display(Name = "Observaciones")]
    public string? Observaciones { get; set; }

    [Display(Name = "Activo")]
    public bool Activo { get; set; }
}
