using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

[Authorize(Policy = "OperadorOrAdmin")]
public class ProductosController : BaseController
{
    private readonly IProductoService _productoService;

    public ProductosController(IProductoService productoService)
    {
        _productoService = productoService;
    }

    public async Task<IActionResult> Index(string? busqueda, bool soloActivos = true, CancellationToken ct = default)
    {
        var productos = await _productoService.GetAllAsync(ct);
        if (soloActivos) productos = productos.Where(p => p.Activo);
        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var q = busqueda.Trim();
            productos = productos.Where(p =>
                p.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase)
                || p.Codigo.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (p.Descripcion ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        ViewBag.Busqueda = busqueda;
        ViewBag.SoloActivos = soloActivos;
        return View(productos.OrderBy(p => p.Nombre).ToList());
    }

    public async Task<IActionResult> Details(ulong id, CancellationToken ct = default)
    {
        var producto = await _productoService.GetByIdAsync(id, ct);
        if (producto is null) return NotFound();
        return View(producto);
    }

    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        await LoadViewBagsAsync(ct);
        return View(new CreateProductoDto { Activo = true, UnidadVenta = "UNIDAD" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateProductoDto producto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            await LoadViewBagsAsync(ct);
            return View(producto);
        }
        try
        {
            await _productoService.CreateAsync(producto, GetCurrentUserId(), ct);
            TempData["Success"] = $"Producto {producto.Nombre} creado.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo crear el producto: {ex.Message}");
            await LoadViewBagsAsync(ct);
            return View(producto);
        }
    }

    public async Task<IActionResult> Edit(ulong id, CancellationToken ct = default)
    {
        var producto = await _productoService.GetByIdAsync(id, ct);
        if (producto is null) return NotFound();

        var updateDto = new UpdateProductoDto
        {
            Id = producto.Id,
            Codigo = producto.Codigo,
            Nombre = producto.Nombre,
            Descripcion = producto.Descripcion,
            TipoProductoId = producto.TipoProductoId,
            CapacidadKg = producto.CapacidadKg,
            UnidadVenta = producto.UnidadVenta,
            PrecioActual = producto.PrecioActual,
            ManejaGarrafaIndividual = producto.ManejaGarrafaIndividual,
            Activo = producto.Activo
        };

        await LoadViewBagsAsync(ct);
        return View(updateDto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ulong id, UpdateProductoDto producto, CancellationToken ct = default)
    {
        if (id != producto.Id) return BadRequest();
        if (!ModelState.IsValid)
        {
            await LoadViewBagsAsync(ct);
            return View(producto);
        }
        try
        {
            await _productoService.UpdateAsync(producto, GetCurrentUserId(), ct);
            TempData["Success"] = $"Producto {producto.Nombre} actualizado.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo actualizar el producto: {ex.Message}");
            await LoadViewBagsAsync(ct);
            return View(producto);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(ulong id, CancellationToken ct = default)
    {
        var producto = await _productoService.GetByIdAsync(id, ct);
        if (producto is null)
        {
            TempData["Error"] = "No se encontró el producto.";
            return RedirectToAction(nameof(Index));
        }
        
        var updateDto = new UpdateProductoDto
        {
            Id = producto.Id,
            Codigo = producto.Codigo,
            Nombre = producto.Nombre,
            Descripcion = producto.Descripcion,
            TipoProductoId = producto.TipoProductoId,
            CapacidadKg = producto.CapacidadKg,
            UnidadVenta = producto.UnidadVenta,
            PrecioActual = producto.PrecioActual,
            ManejaGarrafaIndividual = producto.ManejaGarrafaIndividual,
            Activo = false
        };
        
        await _productoService.UpdateAsync(updateDto, GetCurrentUserId(), ct);
        TempData["Success"] = "Producto desactivado.";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadViewBagsAsync(CancellationToken ct)
    {
        ViewBag.TiposProducto = await _productoService.GetTiposProductoAsync(ct);
    }
}
