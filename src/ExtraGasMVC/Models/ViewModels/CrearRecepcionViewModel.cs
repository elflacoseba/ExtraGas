using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Models.ViewModels;

/// <summary>
/// Wrapper for the recepciones creation screen. Composes the create DTO with
/// the lookup lists the form needs: proveedores activos (dropdown proveedor)
/// y productos activos (dropdown producto, base para el flag
/// <c>maneja_garrafa_individual</c>).
/// </summary>
public class CrearRecepcionViewModel
{
    public CrearRecepcionDto Recepcion { get; set; } = new();
    public IEnumerable<ProveedorDto> Proveedores { get; set; } = Array.Empty<ProveedorDto>();
    public IEnumerable<ProductoDto> Productos { get; set; } = Array.Empty<ProductoDto>();
}