using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

[Authorize(Policy = "OperadorOrAdmin")]
public class GarrafasController : Controller
{
    private readonly IGarrafaService _garrafaService;
    private readonly IClienteService _clienteService;

    public GarrafasController(IGarrafaService garrafaService, IClienteService clienteService)
    {
        _garrafaService = garrafaService;
        _clienteService = clienteService;
    }

    public async Task<IActionResult> Index(string? codigo, byte? capacidad, CancellationToken ct = default)
    {
        var garrafas = await _garrafaService.GetAllAsync(ct);
        if (!string.IsNullOrWhiteSpace(codigo))
            garrafas = garrafas.Where(g => g.Codigo.Contains(codigo.Trim(), StringComparison.OrdinalIgnoreCase));
        if (capacidad.HasValue)
            garrafas = garrafas.Where(g => g.CapacidadKg == capacidad.Value);

        ViewBag.Codigo = codigo;
        ViewBag.Capacidad = capacidad;
        return View(garrafas.OrderBy(g => g.Codigo).ToList());
    }

    public async Task<IActionResult> Details(ulong id, CancellationToken ct = default)
    {
        var garrafa = await _garrafaService.GetByIdAsync(id, ct);
        if (garrafa is null) return NotFound();
        return View(garrafa);
    }

    public IActionResult Create() => View(new CreateGarrafaDto { EstadoGarrafaId = 1, Activo = true, FechaCompra = DateOnly.FromDateTime(DateTime.UtcNow) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateGarrafaDto garrafa, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return View(garrafa);
        try
        {
            await _garrafaService.CreateAsync(garrafa, ct);
            TempData["Success"] = $"Garrafa {garrafa.Codigo} creada.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(garrafa);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo crear la garrafa: {ex.Message}");
            return View(garrafa);
        }
    }

    public async Task<IActionResult> Edit(ulong id, CancellationToken ct = default)
    {
        var garrafa = await _garrafaService.GetByIdAsync(id, ct);
        if (garrafa is null) return NotFound();
        ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
        
        var updateDto = new UpdateGarrafaDto
        {
            Id = garrafa.Id,
            Codigo = garrafa.Codigo,
            CapacidadKg = garrafa.CapacidadKg,
            ProveedorId = garrafa.ProveedorId,
            RecepcionId = garrafa.RecepcionId,
            FechaCompra = garrafa.FechaCompra,
            EstadoGarrafaId = garrafa.EstadoGarrafaId,
            ClienteId = garrafa.ClienteId,
            Activo = garrafa.Activo,
            Observaciones = garrafa.Observaciones
        };
        
        return View(updateDto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ulong id, UpdateGarrafaDto garrafa, CancellationToken ct = default)
    {
        if (id != garrafa.Id) return BadRequest();
        if (!ModelState.IsValid)
        {
            ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
            return View(garrafa);
        }
        try
        {
            await _garrafaService.UpdateAsync(garrafa, ct);
            TempData["Success"] = $"Garrafa {garrafa.Codigo} actualizada.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
            return View(garrafa);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo actualizar la garrafa: {ex.Message}");
            ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
            return View(garrafa);
        }
    }

    public async Task<IActionResult> Stock(CancellationToken ct = default)
    {
        var garrafas = await _garrafaService.GetAllAsync(ct);
        var agrupado = garrafas
            .GroupBy(g => new { g.CapacidadKg, g.EstadoGarrafaId })
            .Select(g => new StockGroup
            {
                CapacidadKg = g.Key.CapacidadKg,
                EstadoId = g.Key.EstadoGarrafaId,
                Cantidad = g.Count()
            })
            .OrderBy(s => s.CapacidadKg)
            .ThenBy(s => s.EstadoId)
            .ToList();
        return View(agrupado);
    }

    public async Task<IActionResult> EnClientes(ulong? clienteId, CancellationToken ct = default)
    {
        if (clienteId.HasValue)
        {
            var garrafas = await _garrafaService.GetByClienteAsync(clienteId.Value, ct);
            ViewBag.Cliente = await _clienteService.GetByIdAsync(clienteId.Value, ct);
            return View("EnClientes", garrafas);
        }
        var todas = await _garrafaService.GetAllAsync(ct);
        var enClientes = todas.Where(g => g.ClienteId.HasValue);
        return View(enClientes);
    }

    public async Task<IActionResult> CambiarEstado(ulong id, CancellationToken ct = default)
    {
        var garrafa = await _garrafaService.GetByIdAsync(id, ct);
        if (garrafa is null) return NotFound();

        ViewBag.Estados = await _garrafaService.GetEstadosAsync(ct);
        ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);

        var dto = new CambiarEstadoGarrafaDto
        {
            NuevoEstadoId = garrafa.EstadoGarrafaId,
            ClienteId = garrafa.ClienteId
        };

        ViewBag.Garrafa = garrafa;
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarEstado(ulong id, CambiarEstadoGarrafaDto dto, CancellationToken ct = default)
    {
        var garrafa = await _garrafaService.GetByIdAsync(id, ct);
        if (garrafa is null) return NotFound();

        if (!ModelState.IsValid)
        {
            ViewBag.Estados = await _garrafaService.GetEstadosAsync(ct);
            ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
            ViewBag.Garrafa = garrafa;
            return View(dto);
        }

        try
        {
            var ok = await _garrafaService.CambiarEstadoAsync(id, dto, ct);
            if (!ok) return NotFound();
            TempData["Success"] = $"Estado de la garrafa {garrafa.Codigo} actualizado correctamente.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewBag.Estados = await _garrafaService.GetEstadosAsync(ct);
            ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
            ViewBag.Garrafa = garrafa;
            return View(dto);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo cambiar el estado: {ex.Message}");
            ViewBag.Estados = await _garrafaService.GetEstadosAsync(ct);
            ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
            ViewBag.Garrafa = garrafa;
            return View(dto);
        }
    }

    public async Task<IActionResult> Delete(ulong id, CancellationToken ct = default)
    {
        var garrafa = await _garrafaService.GetByIdAsync(id, ct);
        if (garrafa is null) return NotFound();
        return View(garrafa);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(ulong id, CancellationToken ct = default)
    {
        try
        {
            var ok = await _garrafaService.DeleteAsync(id, ct);
            if (!ok) return NotFound();
            TempData["Success"] = "Garrafa eliminada correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Delete), new { id });
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"No se pudo eliminar la garrafa: {ex.Message}";
            return RedirectToAction(nameof(Delete), new { id });
        }
    }
}
