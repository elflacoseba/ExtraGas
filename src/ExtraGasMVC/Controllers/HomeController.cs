using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ExtraGasMVC.Models;
using ExtraGasMVC.Services.Interfaces;

namespace ExtraGasMVC.Controllers;

public class HomeController : Controller
{
    private readonly IClienteService _clienteService;
    private readonly IPedidoService _pedidoService;
    private readonly IProductoService _productoService;

    public HomeController(
        IClienteService clienteService,
        IPedidoService pedidoService,
        IProductoService productoService)
    {
        _clienteService = clienteService;
        _pedidoService = pedidoService;
        _productoService = productoService;
    }

    public async Task<IActionResult> Index()
    {
        var clientes = await _clienteService.GetActivosAsync();
        var pedidos = await _pedidoService.GetAllAsync();
        var productos = await _productoService.GetActivosAsync();

        ViewBag.TotalClientes = clientes.Count();
        ViewBag.TotalPedidos = pedidos.Count();
        ViewBag.TotalProductos = productos.Count();

        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
