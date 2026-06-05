using System.Security.Claims;
using AutoMapper;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

[Authorize(Policy = "OperadorOrAdmin")]
public class EmpleadosController : Controller
{
    private readonly IEmpleadoService _empleadoService;
    private readonly IMapper _mapper;

    public EmpleadosController(IEmpleadoService empleadoService, IMapper mapper)
    {
        _empleadoService = empleadoService;
        _mapper = mapper;
    }

    public async Task<IActionResult> Index(string? busqueda, bool soloActivos = true, int pagina = 1, int tamanio = 25, CancellationToken ct = default)
    {
        var resultado = await _empleadoService.SearchAsync(busqueda, soloActivos, pagina, tamanio, ct);

        ViewBag.Busqueda = busqueda;
        ViewBag.SoloActivos = soloActivos;
        ViewBag.Pagina = resultado.Pagina;
        ViewBag.Tamanio = resultado.Tamanio;
        ViewBag.Total = resultado.Total;

        return View(resultado.Items);
    }

    public async Task<IActionResult> Details(ulong id, CancellationToken ct = default)
    {
        var empleado = await _empleadoService.GetByIdAsync(id, ct);
        if (empleado is null) return NotFound();

        ViewBag.Provincias = await _empleadoService.GetProvinciasAsync(ct);
        return View(empleado);
    }

    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        ViewBag.Provincias = await _empleadoService.GetProvinciasAsync(ct);
        return View(new CreateEmpleadoDto { FechaIngreso = DateOnly.FromDateTime(DateTime.UtcNow), Activo = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateEmpleadoDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Provincias = await _empleadoService.GetProvinciasAsync(ct);
            return View(dto);
        }
        try
        {
            var currentUserId = GetCurrentUserId();
            await _empleadoService.CreateAsync(dto, currentUserId, ct);
            TempData["Success"] = $"Empleado {dto.Nombre} {dto.Apellido} creado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewBag.Provincias = await _empleadoService.GetProvinciasAsync(ct);
            return View(dto);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo crear el empleado: {ex.Message}");
            ViewBag.Provincias = await _empleadoService.GetProvinciasAsync(ct);
            return View(dto);
        }
    }

    public async Task<IActionResult> Edit(ulong id, CancellationToken ct = default)
    {
        var empleado = await _empleadoService.GetByIdAsync(id, ct);
        if (empleado is null) return NotFound();

        ViewBag.Provincias = await _empleadoService.GetProvinciasAsync(ct);

        var updateDto = _mapper.Map<UpdateEmpleadoDto>(empleado);
        return View(updateDto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ulong id, UpdateEmpleadoDto dto, CancellationToken ct = default)
    {
        if (id != dto.Id) return BadRequest();

        if (!ModelState.IsValid)
        {
            ViewBag.Provincias = await _empleadoService.GetProvinciasAsync(ct);
            return View(dto);
        }
        try
        {
            var currentUserId = GetCurrentUserId();
            await _empleadoService.UpdateAsync(dto, currentUserId, ct);
            TempData["Success"] = $"Empleado {dto.Nombre} {dto.Apellido} actualizado.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewBag.Provincias = await _empleadoService.GetProvinciasAsync(ct);
            return View(dto);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo actualizar el empleado: {ex.Message}");
            ViewBag.Provincias = await _empleadoService.GetProvinciasAsync(ct);
            return View(dto);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(ulong id, CancellationToken ct = default)
    {
        var ok = await _empleadoService.DeleteAsync(id, ct);
        TempData[ok ? "Success" : "Error"] = ok
            ? "Empleado eliminado correctamente."
            : "No se encontro el empleado.";
        return RedirectToAction(nameof(Index));
    }

    private ulong GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim is not null && ulong.TryParse(claim.Value, out var id) ? id : 0;
    }
}
