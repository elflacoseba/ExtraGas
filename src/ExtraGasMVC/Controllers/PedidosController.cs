using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

public class PedidosController : Controller
{
    private readonly IPedidoService _pedidoService;
    private readonly IClienteService _clienteService;

    public PedidosController(IPedidoService pedidoService, IClienteService clienteService)
    {
        _pedidoService = pedidoService;
        _clienteService = clienteService;
    }

    public async Task<IActionResult> Index(string? numero, DateTime? desde, DateTime? hasta, int pagina = 1, int tamanio = 25, CancellationToken ct = default)
    {
        var pedidos = await _pedidoService.GetAllAsync(ct);
        if (!string.IsNullOrWhiteSpace(numero))
            pedidos = pedidos.Where(p => (p.Numero ?? string.Empty).Contains(numero.Trim(), StringComparison.OrdinalIgnoreCase));
        if (desde.HasValue) pedidos = pedidos.Where(p => p.Fecha >= desde.Value);
        if (hasta.HasValue) pedidos = pedidos.Where(p => p.Fecha <= hasta.Value.AddDays(1));

        var total = pedidos.Count();
        var items = pedidos
            .Skip((pagina - 1) * tamanio)
            .Take(tamanio)
            .ToList();

        ViewBag.Numero = numero;
        ViewBag.Desde = desde;
        ViewBag.Hasta = hasta;
        ViewBag.Pagina = pagina;
        ViewBag.Tamanio = tamanio;
        ViewBag.Total = total;
        return View(items);
    }

    public async Task<IActionResult> Details(ulong id, CancellationToken ct = default)
    {
        var pedido = await _pedidoService.GetByIdAsync(id, ct);
        if (pedido is null) return NotFound();
        return View(pedido);
    }

    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
        return View(new Pedido
        {
            Fecha = DateTime.UtcNow,
            EstadoPedidoId = 1
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Pedido pedido, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
            return View(pedido);
        }
        try
        {
            var created = await _pedidoService.CreateAsync(pedido, ct);
            TempData["Success"] = $"Pedido {created.Numero} creado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo crear el pedido: {ex.Message}");
            ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
            return View(pedido);
        }
    }

    public async Task<IActionResult> Edit(ulong id, CancellationToken ct = default)
    {
        var pedido = await _pedidoService.GetByIdAsync(id, ct);
        if (pedido is null) return NotFound();
        ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
        return View(pedido);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ulong id, Pedido pedido, CancellationToken ct = default)
    {
        if (id != pedido.Id) return BadRequest();
        if (!ModelState.IsValid)
        {
            ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
            return View(pedido);
        }
        try
        {
            await _pedidoService.UpdateAsync(pedido, ct);
            TempData["Success"] = $"Pedido {pedido.Numero} actualizado.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo actualizar el pedido: {ex.Message}");
            ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
            return View(pedido);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(ulong id, CancellationToken ct = default)
    {
        var ok = await _pedidoService.DeleteAsync(id, ct);
        TempData[ok ? "Success" : "Error"] = ok ? "Pedido eliminado." : "No se encontro el pedido.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Pendientes(CancellationToken ct = default)
    {
        var pedidos = await _pedidoService.GetPendientesAsync(ct);
        return View(pedidos);
    }
}
