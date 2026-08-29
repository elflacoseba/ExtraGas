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
        // Issue #114: CreateProductoDto ya no expone Activo — lo setea el
        // Service en true. UnidadVenta queda como default de UI.
        return View(new CreateProductoDto { UnidadVenta = "UNIDAD" });
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
        };

        // Issue #114: UpdateProductoDto ya no expone Activo (es estado y
        // solo cambia vía Delete). Lo pasamos por ViewBag para mostrarlo
        // como info read-only en la vista.
        ViewBag.Activo = producto.Activo;

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
        // Issue #114: antes este Delete implementaba soft-delete a mano vía
        // UpdateAsync con Activo=false — un anti-patrón que dependía de que
        // Activo fuera editable. Con el fix, el soft-delete se delega al
        // Service (DeleteAsync), que setea DeletedAt + Activo=false en una
        // sola operación consistente con el resto de los módulos.
        var ok = await _productoService.DeleteAsync(id, ct);
        TempData[ok ? "Success" : "Error"] = ok
            ? "Producto desactivado correctamente."
            : "No se encontró el producto.";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadViewBagsAsync(CancellationToken ct)
    {
        ViewBag.TiposProducto = await _productoService.GetTiposProductoAsync(ct);
    }
}
