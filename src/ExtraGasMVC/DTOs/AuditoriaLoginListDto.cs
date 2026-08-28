using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.DTOs;

public class AuditoriaLoginListDto
{
    public ulong Id { get; set; }
    public string UsernameIntentado { get; set; } = null!;
    public ulong? UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public bool Exito { get; set; }
    public string? MotivoFallo { get; set; }
    public string? MotivoFalloLegible { get; set; }
    public string? IpOrigen { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
}
