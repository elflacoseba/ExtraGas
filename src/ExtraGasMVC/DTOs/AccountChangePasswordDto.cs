using System.ComponentModel.DataAnnotations;

namespace ExtraGasMVC.DTOs;

public class AccountChangePasswordDto
{
    [Required(ErrorMessage = "La nueva contrasena es obligatoria.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = null!;

    [Required(ErrorMessage = "Debes confirmar la nueva contrasena.")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Las contrasenas no coinciden.")]
    public string ConfirmPassword { get; set; } = null!;
}
