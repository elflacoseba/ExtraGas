using System.Security.Claims;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

[Authorize(Policy = "OperadorOrAdmin")]
public class EmpleadosController : Controller
{
    private readonly IEmpleadoService _empleadoService;

    public EmpleadosController(IEmpleadoService empleadoService)
    {
        _empleadoService = empleadoService;
    }

    public async Task<IActionResult> Index(string? busqueda, bool soloActivos = true, int pagina = 1, int tamanio = 25, CancellationToken ct = default)
    {
        var empleados = await _empleadoService.GetAllAsync(ct);
        if (soloActivos) empleados = empleados.Where(e => e.Activo);
        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var q = busqueda.Trim().ToLower();
            empleados = empleados.Where(e =>
                (e.Nombre + " " + e.Apellido).ToLower().Contains(q)
                || (e.Dni ?? string.Empty).Contains(q)
                || (e.Cuil ?? string.Empty).Contains(q)
                || (e.Telefono ?? string.Empty).Contains(q));
        }

        var total = empleados.Count();
        var items = empleados
            .OrderBy(e => e.Apellido).ThenBy(e => e.Nombre)
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
        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var emailValidator = new System.ComponentModel.DataAnnotations.EmailAddressAttribute();
            if (!emailValidator.IsValid(dto.Email))
                ModelState.AddModelError(nameof(dto.Email), "El formato del email no es válido.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Provincias = await _empleadoService.GetProvinciasAsync(ct);
            return View(dto);
        }
        try
        {
            await _empleadoService.CreateAsync(dto, ct);
            TempData["Success"] = $"Empleado {dto.Nombre} {dto.Apellido} creado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo crear el empleado: {ex.Message}");
            return View(dto);
        }
    }

    public async Task<IActionResult> Edit(ulong id, CancellationToken ct = default)
    {
        var empleado = await _empleadoService.GetByIdAsync(id, ct);
        if (empleado is null) return NotFound();

        ViewBag.Provincias = await _empleadoService.GetProvinciasAsync(ct);

        var updateDto = new UpdateEmpleadoDto
        {
            Id = empleado.Id,
            Nombre = empleado.Nombre,
            Apellido = empleado.Apellido,
            Dni = empleado.Dni,
            Cuil = empleado.Cuil,
            Telefono = empleado.Telefono,
            Email = empleado.Email,
            Calle = empleado.Calle,
            Numero = empleado.Numero,
            Piso = empleado.Piso,
            Depto = empleado.Depto,
            Ciudad = empleado.Ciudad,
            CodigoPostal = empleado.CodigoPostal,
            ProvinciaId = empleado.ProvinciaId,
            FechaIngreso = empleado.FechaIngreso,
            UsuarioId = empleado.UsuarioId,
            Activo = empleado.Activo,
            Observaciones = empleado.Observaciones
        };

        return View(updateDto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ulong id, UpdateEmpleadoDto dto, CancellationToken ct = default)
    {
        if (id != dto.Id) return BadRequest();

        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var emailValidator = new System.ComponentModel.DataAnnotations.EmailAddressAttribute();
            if (!emailValidator.IsValid(dto.Email))
                ModelState.AddModelError(nameof(dto.Email), "El formato del email no es válido.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Provincias = await _empleadoService.GetProvinciasAsync(ct);
            return View(dto);
        }
        try
        {
            await _empleadoService.UpdateAsync(dto, ct);
            TempData["Success"] = $"Empleado {dto.Nombre} {dto.Apellido} actualizado.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo actualizar el empleado: {ex.Message}");
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
}
