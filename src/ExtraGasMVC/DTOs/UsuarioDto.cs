using System.ComponentModel.DataAnnotations;

namespace ExtraGasMVC.DTOs;

public class UsuarioDto
{
    public ulong Id { get; set; }
    public string Username { get; set; } = null!;
    public string? Email { get; set; }
    public ulong RolId { get; set; }
    public string? RolCodigo { get; set; }
    public string? RolNombre { get; set; }
    public bool Activo { get; set; }
    public DateTime? UltimoLogin { get; set; }
    public bool DebeCambiarPassword { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ulong? CreatedBy { get; set; }
    public string? CreadoPor { get; set; }
    public ulong? UpdatedBy { get; set; }
    public string? ActualizadoPor { get; set; }
    public ulong? EmpleadoId { get; set; }
    public string? EmpleadoNombre { get; set; }
}

/// <summary>
/// DTO de alta de usuario. NO incluye <c>Activo</c> (lo setea el Service en
/// <c>true</c>). Sin esto el operador podía crear un usuario inactivo desde
/// el formulario — un estado operacional incoherente. Issue #114.
/// </summary>
public class CreateUsuarioDto
{
    [Required(ErrorMessage = "El nombre de usuario es obligatorio.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "El usuario debe tener entre 3 y 50 caracteres.")]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Solo letras, numeros, puntos, guiones y guiones bajos.")]
    public string Username { get; set; } = null!;

    [EmailAddress(ErrorMessage = "El formato del email no es valido.")]
    [StringLength(150)]
    public string? Email { get; set; }

    [Required(ErrorMessage = "La contrasena es obligatoria.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = null!;

    [Required(ErrorMessage = "El rol es obligatorio.")]
    public ulong RolId { get; set; }

    public ulong? EmpleadoId { get; set; }
}

/// <summary>
/// DTO de edición de usuario. NO incluye <c>Activo</c>: es estado y solo
/// cambia vía Delete (la regla "no puede desactivarse a sí mismo" del
/// controller queda redundante: el propio Controller de Delete ya bloquea
/// la auto-eliminación). Editarlo desde el form producía estados zombie
/// (<c>Activo=false</c> con <c>DeletedAt=null</c>). El Service lo preserva
/// vía <c>UsuarioEditRules.PreservarFlagsNoEditables</c>. Issue #114.
/// </summary>
public class UpdateUsuarioDto
{
    public ulong Id { get; set; }

    [EmailAddress(ErrorMessage = "El formato del email no es valido.")]
    [StringLength(150)]
    public string? Email { get; set; }

    [Required(ErrorMessage = "El rol es obligatorio.")]
    public ulong RolId { get; set; }
}

public class ChangePasswordDto
{
    public ulong UsuarioId { get; set; }

    [Required(ErrorMessage = "La contrasena actual es obligatoria.")]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = null!;

    [Required(ErrorMessage = "La nueva contrasena es obligatoria.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = null!;

    [DataType(DataType.Password)]
    [Compare("NewPassword", ErrorMessage = "Las contrasenas no coinciden.")]
    public string ConfirmPassword { get; set; } = null!;
}

public class RolDto
{
    public ulong Id { get; set; }
    public string Nombre { get; set; } = null!;
    public string Codigo { get; set; } = null!;
}

public class EmpleadoSinUsuarioDto
{
    public ulong Id { get; set; }
    public string NombreCompleto { get; set; } = null!;
}
