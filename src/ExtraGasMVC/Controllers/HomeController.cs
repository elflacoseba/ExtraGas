using System.Diagnostics;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities.Views;
using ExtraGasMVC.Models;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Controllers;

public class HomeController : Controller
{
    private readonly IClienteService _clienteService;
    private readonly IPedidoService _pedidoService;
    private readonly IProductoService _productoService;
    private readonly ExtraGasDbContext _context;

    public HomeController(
        IClienteService clienteService,
        IPedidoService pedidoService,
        IProductoService productoService,
        ExtraGasDbContext context)
    {
        _clienteService = clienteService;
        _pedidoService = pedidoService;
        _productoService = productoService;
        _context = context;
    }

    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        var clientesActivos = (await _clienteService.GetActivosAsync(ct)).Count();
        var pedidos = await _pedidoService.GetAllAsync(ct);
        var productosActivos = (await _productoService.GetActivosAsync(ct)).Count();
        var garrafas = await _context.Garrafas.AsNoTracking().CountAsync(ct);
        var pedidosPendientes = pedidos.Count(p => p.Saldo > 0);
        var totalCobrado = pedidos.Sum(p => p.MontoPagado);
        var totalSaldo = pedidos.Sum(p => p.Saldo);

        var topProductos = await _context.VProductosMasVendidos
            .AsNoTracking()
            .Where(v => v.Fecha >= DateTime.UtcNow.AddDays(-30))
            .GroupBy(v => new { v.ProductoId, v.ProductoNombre, v.TipoProducto })
            .Select(g => new TopProducto
            {
                Producto = g.Key.ProductoNombre,
                Tipo = g.Key.TipoProducto,
                Cantidad = g.Sum(x => x.CantidadVendida)
            })
            .OrderByDescending(x => x.Cantidad)
            .Take(5)
            .ToListAsync(ct);

        var ultimosPedidos = await _context.VPedidosResumen
            .AsNoTracking()
            .OrderByDescending(p => p.Fecha)
            .Take(5)
            .ToListAsync(ct);

        var model = new DashboardViewModel
        {
            TotalClientesActivos = clientesActivos,
            TotalPedidos = pedidos.Count(),
            PedidosPendientes = pedidosPendientes,
            TotalProductosActivos = productosActivos,
            TotalGarrafas = garrafas,
            TotalCobrado = totalCobrado,
            TotalSaldo = totalSaldo,
            TopProductos = topProductos,
            UltimosPedidos = ultimosPedidos
        };
        return View(model);
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [Route("/Error/404")]
    public IActionResult NotFoundPage() => View("NotFound");

    [Route("/Error/500")]
    public IActionResult ServerError() => View("ServerError");
}
