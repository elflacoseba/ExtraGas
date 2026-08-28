namespace ExtraGasMVC.Data.Entities;

/// <summary>
/// Token de un solo uso para reset de contrasena. Solo se persiste el SHA-256
/// hex del token raw; el raw viaja unicamente por email y nunca se guarda.
/// </summary>
public class PasswordResetToken
{
    public ulong Id { get; set; }
    public ulong UsuarioId { get; set; }
    public string TokenHash { get; set; } = null!;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ulong? CreatedBy { get; set; }
    public ulong? UpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    public Usuario? Usuario { get; set; }
}
