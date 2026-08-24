using ExtraGasMVC.Constants;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

[Authorize(Policy = "OperadorOrAdmin")]
public class GarrafasController : BaseController
{
    private readonly IGarrafaService _garrafaService;
    private readonly IClienteService _clienteService;

    public GarrafasController(IGarrafaService garrafaService, IClienteService clienteService)
    {
        _garrafaService = garrafaService;
        _clienteService = clienteService;
    }

    /// <summary>
    /// Devuelve el código del estado actual de la garrafa leyendo directamente
    /// de la base de datos. Se usa en Edit (GET y POST) para validar contra la
    /// máquina de estados sin depender del valor que el cliente envío en el form.
    /// </summary>
    private async Task<string?> GetEstadoCodigoActualAsync(ulong id, CancellationToken ct)
    {
        var garrafa = await _garrafaService.GetByIdAsync(id, ct);
        return garrafa?.EstadoCodigo;
    }

    /// <summary>
    /// Construye el redirect estándar a Details con un mensaje de error en
    /// TempData para los casos en los que se intenta editar una garrafa en
    /// un estado que lo prohíbe (ver issue #41).
    /// </summary>
    private IActionResult RedirectBloqueadoPorEstado(ulong id)
    {
        TempData["Error"] = "No se puede editar una garrafa dada de baja.";
        return RedirectToAction(nameof(Details), new { id });
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

    public async Task<IActionResult> Historial(ulong id, CancellationToken ct = default)
    {
        var garrafa = await _garrafaService.GetByIdAsync(id, ct);
        if (garrafa is null) return NotFound();

        var movimientos = await _garrafaService.GetHistorialAsync(id, ct);
        ViewBag.Garrafa = garrafa;
        return View(movimientos);
    }

    public IActionResult Create() => View(new CreateGarrafaDto { EstadoGarrafaId = 1, Activo = true, FechaCompra = DateOnly.FromDateTime(DateTime.UtcNow) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateGarrafaDto garrafa, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return View(garrafa);
        try
        {
            await _garrafaService.CreateAsync(garrafa, GetCurrentUserId(), ct);
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

        // Issue #41: una garrafa en FUERA_SERVICIO es inmutable. Si alguien
        // llega por URL directa, lo mandamos a Details con el mismo mensaje
        // de error que vería si la UI tuviera el botón oculto.
        if (garrafa.EstadoCodigo == GarrafaEstados.FueraServicio)
            return RedirectBloqueadoPorEstado(id);

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

        // Issue #41: validar contra el estado actual en BD (no contra el
        // valor que envía el form) para que un POST hand-crafted con un
        // EstadoGarrafaId != FUERA_SERVICIO no sirva para esquivar la regla.
        var estadoCodigo = await GetEstadoCodigoActualAsync(id, ct);
        if (estadoCodigo == null) return NotFound();
        if (estadoCodigo == GarrafaEstados.FueraServicio)
            return RedirectBloqueadoPorEstado(id);

        if (!ModelState.IsValid)
        {
            ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
            return View(garrafa);
        }
        try
        {
            await _garrafaService.UpdateAsync(garrafa, GetCurrentUserId(), ct);
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

        // Issue #40: el dropdown muestra sólo los destinos válidos para el estado
        // actual de la garrafa, según GarrafaTransiciones. La validación real
        // (incluido requests hand-crafted) la hace CambiarEstadoAsync en el service.
        ViewBag.Estados = await _garrafaService.GetTransicionesDisponiblesAsync(id, ct);
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

        // Re-poblar ViewBags antes de cualquier re-render (Valid o error).
        ViewBag.Estados = await _garrafaService.GetTransicionesDisponiblesAsync(id, ct);
        ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
        ViewBag.Garrafa = garrafa;

        if (!ModelState.IsValid)
            return View(dto);

        try
        {
            var currentUserId = GetCurrentUserId();
            var ok = await _garrafaService.CambiarEstadoAsync(id, dto, currentUserId, ct);
            if (!ok) return NotFound();
            TempData["Success"] = $"Estado de la garrafa {garrafa.Codigo} actualizado correctamente.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (InvalidOperationException ex)
        {
            // Incluye: transición inválida, estado destino inexistente,
            // estado destino que requiere cliente, etc.
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(dto);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo cambiar el estado: {ex.Message}");
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
