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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ulong? CreatedBy { get; set; }
    public string? CreadoPor { get; set; }
    public ulong? UpdatedBy { get; set; }
    public string? ActualizadoPor { get; set; }
    public ulong? EmpleadoId { get; set; }
    public string? EmpleadoNombre { get; set; }
}

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
    [StringLength(100, MinimumLength = 6, ErrorMessage = "La contrasena debe tener al menos 6 caracteres.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = null!;

    [Required(ErrorMessage = "El rol es obligatorio.")]
    public ulong RolId { get; set; }

    public bool Activo { get; set; } = true;

    public ulong? EmpleadoId { get; set; }
}

public class UpdateUsuarioDto
{
    public ulong Id { get; set; }

    [EmailAddress(ErrorMessage = "El formato del email no es valido.")]
    [StringLength(150)]
    public string? Email { get; set; }

    [Required(ErrorMessage = "El rol es obligatorio.")]
    public ulong RolId { get; set; }

    public bool Activo { get; set; }
}

public class ChangePasswordDto
{
    public ulong UsuarioId { get; set; }

    [Required(ErrorMessage = "La contrasena actual es obligatoria.")]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = null!;

    [Required(ErrorMessage = "La nueva contrasena es obligatoria.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "La contrasena debe tener al menos 6 caracteres.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = null!;

    [DataType(DataType.Password)]
    [Compare("NewPassword", ErrorMessage = "Las contrasenas no coinciden.")]
    public string ConfirmPassword { get; set; } = null!;
}

public class SearchResultDto<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Pagina { get; set; }
    public int Tamanio { get; set; }
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
