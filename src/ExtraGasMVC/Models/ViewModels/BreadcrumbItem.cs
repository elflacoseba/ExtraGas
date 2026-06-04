namespace ExtraGasMVC.Models.ViewModels;

public class BreadcrumbItem
{
    public string Label { get; set; } = string.Empty;
    public string? Controller { get; set; }
    public string? Action { get; set; }
}
