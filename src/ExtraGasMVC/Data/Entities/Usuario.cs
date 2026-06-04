namespace ExtraGasMVC.Data.Entities;

public class Usuario
{
    public ulong Id { get; set; }
    public string Username { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string? Email { get; set; }
    public ulong RolId { get; set; }
    public bool Activo { get; set; }
    public DateTime? UltimoLogin { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ulong? CreatedBy { get; set; }
    public ulong? UpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    public Rol Rol { get; set; } = null!;
}
