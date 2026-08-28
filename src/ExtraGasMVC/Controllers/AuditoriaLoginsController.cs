using ExtraGasMVC.DTOs;
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

        ViewBag.Busqueda = busqueda;
        ViewBag.Ip = ip;
        ViewBag.SoloFallidos = soloFallidos;
        ViewBag.Pagina = resultado.Pagina;
        ViewBag.Tamanio = resultado.Tamanio;
        ViewBag.Total = resultado.Total;

        return View(resultado.Items);
    }
}
