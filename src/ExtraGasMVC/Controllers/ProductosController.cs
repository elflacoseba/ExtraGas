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
        // Issue #147 slice 3 item 7: el form usa <select> poblado por
        // ViewBag.UnidadesVenta. El campo UnidadVenta (string) queda por
        // compatibilidad con el modelo pero ya no se bindea desde el form
        // (lo sincroniza el Service en base al FK). UnidadVentaId empieza
        // en null para forzar al operador a elegir (no autoselecciona la
        // primera opción).
        return View(new CreateProductoDto());
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
            // Issue #146.1, .2, .3 + #147 slice 3: errores de validación de
            // negocio que el Service rechaza ANTES de tocar la BD. El mensaje
            // ya viene legible del Service ("Ya existe un producto con el
            // código 'GAS-10'."); lo agregamos a ModelState para que el form
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
            // Issue #147 slice 3 item 7: el FK ahora es la fuente de verdad;
            // el campo string queda por backward-compat con la columna
            // legacy. El Service sincroniza el string en base al FK.
            UnidadVenta = producto.UnidadVenta,
            UnidadVentaId = producto.UnidadVentaId,
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
            // Issue #146.1, .2, .3, .4 + #147 slice 3: validaciones de
            // negocio y race conditions de concurrencia llegan por este
            // canal (el Service traduce DbUpdateConcurrencyException →
            // ValidationException).
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

    // Issue #147 slice 3 item 2: el Delete ahora es un flujo de 2 pasos.
    // GET: muestra el impacto (3 contadores de dependencias) + exige
    // type-to-confirm si Total > 0. POST: valida el confirmCode, llama
    // a DeleteAsync del Service. AdminOnly override del class-level
    // OperadorOrAdmin — desactivar un producto es operación privilegiada
    // (issue #146.6).
    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(ulong id, CancellationToken ct = default)
    {
        var producto = await _productoService.GetByIdAsync(id, ct);
        if (producto is null) return NotFound();

        // Issue #147 slice 3 item 2: el conteo de dependencias alimenta
        // la decisión "confirm simple vs type-to-confirm". Si el producto
        // no existe o está soft-deleted, GetDeleteImpactAsync tira
        // KeyNotFoundException — lo dejamos propagar a 404 vía NotFound()
        // para no confundir al operador con un DTO vacío.
        var impacto = await _productoService.GetDeleteImpactAsync(id, ct);

        // ViewBag: el JS de wwwroot/js/productos-delete.js lee
        // expectedCode para comparar con el input del operador.
        ViewBag.ExpectedCode = producto.Codigo;
        return View(impacto);
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(ulong id, string? confirmCode, CancellationToken ct = default)
    {
        // Issue #147 slice 3 item 2: type-to-confirm. Si el operador tipea
        // el codigo mal, recargamos la vista de impacto con error en lugar
        // de proceder con el delete silencioso. Mismo compare Ordinal que
        // los lookup codes (case-sensitive: la columna es VARCHAR(20) y el
        // DTO lo expone normalizado).
        var producto = await _productoService.GetByIdAsync(id, ct);
        if (producto is null) return NotFound();

        if (string.IsNullOrEmpty(confirmCode) ||
            !string.Equals(confirmCode, producto.Codigo, StringComparison.Ordinal))
        {
            // Re-renderizar la vista de impacto con error y mantener el
            // input del operador en ViewBag para que pueda corregir sin
            // re-tipear.
            var impacto = await _productoService.GetDeleteImpactAsync(id, ct);
            ViewBag.ExpectedCode = producto.Codigo;
            ViewBag.ConfirmError = "Código incorrecto. Tipee el código exacto del producto para confirmar.";
            ViewBag.ConfirmInput = confirmCode;
            return View(impacto);
        }

        // Confirmación válida: proceder con el soft-delete via Service.
        var ok = await _productoService.DeleteAsync(id, GetCurrentUserId(), ct);
        TempData[ok ? "Success" : "Error"] = ok
            ? $"Producto {producto.Codigo} desactivado correctamente."
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
        // Issue #147 slice 3 item 7: el <select> de Create/Edit usa este
        // ViewBag. Réplica del patrón de TiposProducto.
        ViewBag.UnidadesVenta = await _productoService.GetUnidadesVentaAsync(ct);
    }
}
