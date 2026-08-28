using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Models.ViewModels;

/// <summary>
/// ViewModel tipado para la pantalla de auditoria de logins. Reemplaza el
/// uso de ViewBag en AuditoriaLoginsController.Index y permite que la vista
/// sea descubrible desde el compilador.
/// </summary>
public class AuditoriaLoginsIndexViewModel
{
    public required IReadOnlyList<AuditoriaLoginListDto> Items { get; init; }

    /// <summary>Filtros aplicados (eco para mantener el estado de los inputs del form).</summary>
    public string? Busqueda { get; init; }
    public string? Ip { get; init; }
    public bool SoloFallidos { get; init; }

    /// <summary>Estado de paginacion.</summary>
    public int Pagina { get; init; } = 1;
    public int Tamanio { get; init; } = 50;
    public int Total { get; init; }
    public int TotalPaginas => Tamanio > 0 ? (int)Math.Ceiling(Total / (double)Tamanio) : 0;
}
