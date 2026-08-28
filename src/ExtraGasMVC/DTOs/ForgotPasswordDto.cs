using System.ComponentModel.DataAnnotations;

namespace ExtraGasMVC.DTOs;

/// <summary>
/// Datos del formulario <c>/Account/ForgotPassword</c>.
/// </summary>
public class ForgotPasswordDto
{
    [Required(ErrorMessage = "El email es obligatorio.")]
    [EmailAddress(ErrorMessage = "El email no tiene un formato válido.")]
    [StringLength(150, ErrorMessage = "El email no puede superar los 150 caracteres.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;
}
