using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

public class ClientesController : Controller
{
    private readonly IClienteService _clienteService;

    public ClientesController(IClienteService clienteService)
    {
        _clienteService = clienteService;
    }

    public async Task<IActionResult> Index(string? busqueda, bool soloActivos = true, int pagina = 1, int tamanio = 25, CancellationToken ct = default)
    {
        var clientes = await _clienteService.GetAllAsync(ct);
        if (soloActivos) clientes = clientes.Where(c => c.Activo);
        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var q = busqueda.Trim().ToLower();
            clientes = clientes.Where(c =>
                (c.Nombre + " " + c.Apellido).ToLower().Contains(q)
                || (c.Dni ?? string.Empty).Contains(q)
                || (c.CuitCuil ?? string.Empty).Contains(q)
                || c.TelefonoPrincipal.Contains(q));
        }

        var total = clientes.Count();
        var items = clientes
            .OrderBy(c => c.Apellido).ThenBy(c => c.Nombre)
            .Skip((pagina - 1) * tamanio)
            .Take(tamanio)
            .ToList();

        ViewBag.Busqueda = busqueda;
        ViewBag.SoloActivos = soloActivos;
        ViewBag.Pagina = pagina;
        ViewBag.Tamanio = tamanio;
        ViewBag.Total = total;
        return View(items);
    }

    public async Task<IActionResult> Details(ulong id, CancellationToken ct = default)
    {
        var cliente = await _clienteService.GetByIdAsync(id, ct);
        if (cliente is null) return NotFound();
        return View(cliente);
    }

    public IActionResult Create() => View(new Cliente { FechaAlta = DateOnly.FromDateTime(DateTime.UtcNow), Activo = true });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Cliente cliente, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return View(cliente);
        try
        {
            await _clienteService.CreateAsync(cliente, ct);
            TempData["Success"] = $"Cliente {cliente.Nombre} {cliente.Apellido} creado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo crear el cliente: {ex.Message}");
            return View(cliente);
        }
    }

    public async Task<IActionResult> Edit(ulong id, CancellationToken ct = default)
    {
        var cliente = await _clienteService.GetByIdAsync(id, ct);
        if (cliente is null) return NotFound();
        return View(cliente);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ulong id, Cliente cliente, CancellationToken ct = default)
    {
        if (id != cliente.Id) return BadRequest();
        if (!ModelState.IsValid) return View(cliente);
        try
        {
            await _clienteService.UpdateAsync(cliente, ct);
            TempData["Success"] = $"Cliente {cliente.Nombre} {cliente.Apellido} actualizado.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo actualizar el cliente: {ex.Message}");
            return View(cliente);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(ulong id, CancellationToken ct = default)
    {
        var ok = await _clienteService.DeleteAsync(id, ct);
        TempData[ok ? "Success" : "Error"] = ok
            ? "Cliente eliminado correctamente."
            : "No se encontro el cliente.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> CuentasCorrientes(CancellationToken ct = default)
    {
        var clientes = await _clienteService.GetAllAsync(ct);
        ViewBag.Clientes = clientes;
        return View();
    }
}
