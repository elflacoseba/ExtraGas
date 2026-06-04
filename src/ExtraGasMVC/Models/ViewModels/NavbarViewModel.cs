namespace ExtraGasMVC.Models.ViewModels;

public class NavbarViewModel
{
    public bool ShowSearch { get; set; } = true;
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? Role { get; set; }
}
