using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

[Authorize(Policy = "AdminOnly")]
public class AuditoriaLoginsController : Controller
{
    private readonly IAuditoriaLoginService _auditoriaService;

    public AuditoriaLoginsController(IAuditoriaLoginService auditoriaService)
    {
        _auditoriaService = auditoriaService;
    }

    public async Task<IActionResult> Index(
        string? busqueda,
        string? ip,
        bool soloFallidos = false,
        int pagina = 1,
        int tamanio = 50,
        CancellationToken ct = default)
    {
        var resultado = await _auditoriaService.SearchAsync(busqueda, ip, soloFallidos, pagina, tamanio, ct);

        var viewModel = new AuditoriaLoginsIndexViewModel
        {
            Items = resultado.Items,
            Busqueda = busqueda,
            Ip = ip,
            SoloFallidos = soloFallidos,
            Pagina = resultado.Page,
            Tamanio = resultado.PageSize,
            Total = resultado.Total,
        };

        return View(viewModel);
    }
}
