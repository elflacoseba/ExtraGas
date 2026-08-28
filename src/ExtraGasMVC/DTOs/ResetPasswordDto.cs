using System.ComponentModel.DataAnnotations;

namespace ExtraGasMVC.DTOs;

/// <summary>
/// Datos del formulario <c>/Account/ResetPassword</c>.
/// El token viaja en un campo oculto y solo se valida en el POST.
/// </summary>
public class ResetPasswordDto
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [DataType(DataType.Password)]
    [Display(Name = "Nueva contraseña")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirmá la contraseña.")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Las contraseñas no coinciden.")]
    [Display(Name = "Confirmar contraseña")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
