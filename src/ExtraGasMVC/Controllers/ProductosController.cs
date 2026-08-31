using ExtraGasMVC.Constants;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Exceptions;
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

    public async Task<IActionResult> Index(
        string? busqueda,
        bool soloActivos = true,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default)
    {
        // Issue #146.5: paginación server-side. Antes este Controller
        // llamaba GetAllAsync() y filtraba en memoria con LINQ-to-Objects
        // — escaneaba toda la tabla + cargaba la navegación TipoProducto
        // para todas las filas. Con catálogos grandes era un riesgo de
        // performance y consumo de memoria. Ahora el WHERE y el Skip/Take
        // se traducen a SQL; el resultado usa PagedResult<T> que ya
        // existe en el repo (reusado por GarrafaService y otros).
        var resultado = await _productoService.GetPagedAsync(
            busqueda, soloActivos, page, pageSize, ct);

        ViewBag.Busqueda = busqueda;
        ViewBag.SoloActivos = soloActivos;
        return View(resultado);
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
        catch (ValidationException ex)
        {
            // Issue #146.1, .2, .3: errores de validación de negocio que
            // el Service rechaza ANTES de tocar la BD. El mensaje ya viene
            // legible del Service ("Ya existe un producto con el código
            // 'GAS-10'."); lo agregamos a ModelState para que el form
            // muestre el error arriba y el operador entienda qué corregir.
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadViewBagsAsync(ct);
            return View(producto);
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

        // Issue #147 item 4: auditoría visible read-only al pie del form.
        // UpdateProductoDto NO expone los 4 miembros de auditoría — el
        // Service los necesita para escribir (UpdatedAt/UpdatedBy), no
        // para bindear desde el form. Los exponemos via ViewBag desde el
        // ProductoDto que ya cargó GetByIdAsync (que sí los pobló).
        ViewBag.AuditCreatedAt = producto.CreatedAt;
        ViewBag.AuditUpdatedAt = producto.UpdatedAt;
        ViewBag.AuditCreatedBy = producto.CreatedByUserName;
        ViewBag.AuditUpdatedBy = producto.UpdatedByUserName;

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
        catch (ValidationException ex)
        {
            // Issue #146.1, .2, .3, .4: validaciones de negocio y race
            // conditions de concurrencia llegan por este canal (el Service
            // traduce DbUpdateConcurrencyException → ValidationException).
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadViewBagsAsync(ct);
            return View(producto);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo actualizar el producto: {ex.Message}");
            await LoadViewBagsAsync(ct);
            return View(producto);
        }
    }

    // Issue #146.6: AdminOnly override del class-level OperadorOrAdmin.
    // Desactivar un producto es una operación privilegiada — un operador
    // podría borrar del catálogo el GAS-10 por error y dejar la app
    // inutilizable hasta que un DBA reactive (ver issue crítico sobre
    // RestoreAsync). Mismo patrón que el Restore existente (PR #145 Slice
    // 2). Consolida los dos puntos del ABM que requieren rol ADMIN.
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(ulong id, CancellationToken ct = default)
    {
        // Issue #114: antes este Delete implementaba soft-delete a mano vía
        // UpdateAsync con Activo=false — un anti-patrón que dependía de que
        // Activo fuera editable. Con el fix, el soft-delete se delega al
        // Service (DeleteAsync), que setea DeletedAt + Activo=false en una
        // sola operación consistente con el resto de los módulos.
        var ok = await _productoService.DeleteAsync(id, GetCurrentUserId(), ct);
        TempData[ok ? "Success" : "Error"] = ok
            ? "Producto desactivado correctamente."
            : "No se encontró el producto.";
        return RedirectToAction(nameof(Index));
    }

    // Issue #145 Slice 2 + #146.6: AdminOnly override del class-level
    // OperadorOrAdmin. Restaurar un producto es una operación privilegiada
    // — cualquier operador podria revertir un delete accidental y volver a
    // exponer un producto desactivado a propósito.
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(ulong id, CancellationToken ct = default)
    {
        var currentUserId = GetCurrentUserId();
        var ok = await _productoService.RestoreAsync(id, currentUserId, ct);
        TempData[ok ? TempDataKeys.Success : TempDataKeys.Error] = ok
            ? "Producto reactivado correctamente."
            : "No se encontró el producto.";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadViewBagsAsync(CancellationToken ct)
    {
        ViewBag.TiposProducto = await _productoService.GetTiposProductoAsync(ct);
    }
}
