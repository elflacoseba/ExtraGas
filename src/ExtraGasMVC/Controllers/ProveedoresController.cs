using AutoMapper;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

[Authorize(Policy = "OperadorOrAdmin")]
public class ProveedoresController : BaseController
{
    private readonly IProveedorService _proveedorService;
    private readonly IMapper _mapper;

    public ProveedoresController(IProveedorService proveedorService, IMapper mapper)
    {
        _proveedorService = proveedorService;
        _mapper = mapper;
    }

    public async Task<IActionResult> Index(string? busqueda, bool soloActivos = true, int pagina = 1, int tamanio = 25, CancellationToken ct = default)
    {
        var resultado = await _proveedorService.SearchAsync(busqueda, soloActivos, pagina, tamanio, ct);
        ViewBag.Busqueda = busqueda;
        ViewBag.SoloActivos = soloActivos;
        ViewBag.Pagina = resultado.Pagina;
        ViewBag.Tamanio = resultado.Tamanio;
        ViewBag.Total = resultado.Total;
        return View(resultado.Items);
    }

    public async Task<IActionResult> Details(ulong id, CancellationToken ct = default)
    {
        var proveedor = await _proveedorService.GetByIdAsync(id, ct);
        if (proveedor is null) return NotFound();
        await LoadViewBagsAsync(ct);
        return View(proveedor);
    }

    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        await LoadViewBagsAsync(ct);
        // Issue #114: CreateProveedorDto ya no expone Activo — lo setea el
        // Service en true.
        return View(new CreateProveedorDto());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateProveedorDto proveedor, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            await LoadViewBagsAsync(ct);
            return View(proveedor);
        }
        try
        {
            var currentUserId = GetCurrentUserId();
            await _proveedorService.CreateAsync(proveedor, currentUserId, ct);
            TempData["Success"] = $"Proveedor {proveedor.RazonSocial} creado.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("Cuit", ex.Message);
            await LoadViewBagsAsync(ct);
            return View(proveedor);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo crear el proveedor: {ex.Message}");
            await LoadViewBagsAsync(ct);
            return View(proveedor);
        }
    }

    public async Task<IActionResult> Edit(ulong id, CancellationToken ct = default)
    {
        var proveedor = await _proveedorService.GetByIdAsync(id, ct);
        if (proveedor is null) return NotFound();

        // Issue #114: UpdateProveedorDto ya no expone Activo (es estado y solo
        // cambia vía Delete). Lo pasamos por ViewBag para mostrarlo como info
        // read-only en la vista.
        ViewBag.Activo = proveedor.Activo;

        var updateDto = _mapper.Map<UpdateProveedorDto>(proveedor);
        await LoadViewBagsAsync(ct);
        return View(updateDto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ulong id, UpdateProveedorDto proveedor, CancellationToken ct = default)
    {
        if (id != proveedor.Id) return BadRequest();
        if (!ModelState.IsValid)
        {
            await LoadViewBagsAsync(ct);
            return View(proveedor);
        }
        try
        {
            var currentUserId = GetCurrentUserId();
            await _proveedorService.UpdateAsync(id, proveedor, currentUserId, ct);
            TempData["Success"] = $"Proveedor {proveedor.RazonSocial} actualizado.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("Cuit", ex.Message);
            await LoadViewBagsAsync(ct);
            return View(proveedor);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo actualizar el proveedor: {ex.Message}");
            await LoadViewBagsAsync(ct);
            return View(proveedor);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(ulong id, CancellationToken ct = default)
    {
        var currentUserId = GetCurrentUserId();
        var ok = await _proveedorService.DeleteAsync(id, currentUserId, ct);
        TempData[ok ? "Success" : "Error"] = ok ? "Proveedor eliminado." : "No se encontró el proveedor.";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadViewBagsAsync(CancellationToken ct = default)
    {
        ViewBag.Provincias = await _proveedorService.GetProvinciasAsync(ct);
    }
}
