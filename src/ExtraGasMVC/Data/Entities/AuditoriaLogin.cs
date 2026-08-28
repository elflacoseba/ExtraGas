namespace ExtraGasMVC.Data.Entities;

/// <summary>
/// Registro de un intento de login (exitoso o fallido).
/// Se inserta una fila por cada intento, exista o no el usuario.
/// </summary>
public class AuditoriaLogin
{
    public ulong Id { get; set; }
    public string UsernameIntentado { get; set; } = null!;
    public ulong? UsuarioId { get; set; }
    public bool Exito { get; set; }
    public string? MotivoFallo { get; set; }
    public string? IpOrigen { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }

    public Usuario? Usuario { get; set; }
}
