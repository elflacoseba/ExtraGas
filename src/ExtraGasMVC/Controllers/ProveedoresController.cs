using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

public class ProveedoresController : Controller
{
    private readonly IProveedorService _proveedorService;

    public ProveedoresController(IProveedorService proveedorService)
    {
        _proveedorService = proveedorService;
    }

    public async Task<IActionResult> Index(string? busqueda, bool soloActivos = true, CancellationToken ct = default)
    {
        var proveedores = await _proveedorService.GetAllAsync(ct);
        if (soloActivos) proveedores = proveedores.Where(p => p.Activo);
        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var q = busqueda.Trim().ToLower();
            proveedores = proveedores.Where(p =>
                p.RazonSocial.ToLower().Contains(q)
                || p.Cuit.Contains(q)
                || (p.NombreFantasia ?? string.Empty).ToLower().Contains(q));
        }
        ViewBag.Busqueda = busqueda;
        ViewBag.SoloActivos = soloActivos;
        return View(proveedores.OrderBy(p => p.RazonSocial).ToList());
    }

    public async Task<IActionResult> Details(ulong id, CancellationToken ct = default)
    {
        var proveedor = await _proveedorService.GetByIdAsync(id, ct);
        if (proveedor is null) return NotFound();
        return View(proveedor);
    }

    public IActionResult Create() => View(new Proveedor { Activo = true });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Proveedor proveedor, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return View(proveedor);
        try
        {
            await _proveedorService.CreateAsync(proveedor, ct);
            TempData["Success"] = $"Proveedor {proveedor.RazonSocial} creado.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo crear el proveedor: {ex.Message}");
            return View(proveedor);
        }
    }

    public async Task<IActionResult> Edit(ulong id, CancellationToken ct = default)
    {
        var proveedor = await _proveedorService.GetByIdAsync(id, ct);
        if (proveedor is null) return NotFound();
        return View(proveedor);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ulong id, Proveedor proveedor, CancellationToken ct = default)
    {
        if (id != proveedor.Id) return BadRequest();
        if (!ModelState.IsValid) return View(proveedor);
        try
        {
            await _proveedorService.UpdateAsync(proveedor, ct);
            TempData["Success"] = $"Proveedor {proveedor.RazonSocial} actualizado.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo actualizar el proveedor: {ex.Message}");
            return View(proveedor);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(ulong id, CancellationToken ct = default)
    {
        var ok = await _proveedorService.DeleteAsync(id, ct);
        TempData[ok ? "Success" : "Error"] = ok ? "Proveedor eliminado." : "No se encontro el proveedor.";
        return RedirectToAction(nameof(Index));
    }
}
