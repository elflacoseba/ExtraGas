using AutoMapper;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Exceptions;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

[Authorize(Policy = "OperadorOrAdmin")]
public class ClientesController : BaseController
{
    private readonly IClienteService _clienteService;
    private readonly IMapper _mapper;

    public ClientesController(IClienteService clienteService, IMapper mapper)
    {
        _clienteService = clienteService;
        _mapper = mapper;
    }

    private async Task LoadViewBagsAsync(CancellationToken ct = default)
    {
        ViewBag.Provincias = await _clienteService.GetProvinciasAsync(ct);
    }

    public async Task<IActionResult> Index(string? busqueda, bool soloActivos = true, int pagina = 1, int tamanio = 25, CancellationToken ct = default)
    {
        var resultado = await _clienteService.SearchAsync(busqueda, soloActivos, pagina, tamanio, ct);

        ViewBag.Busqueda = busqueda;
        ViewBag.SoloActivos = soloActivos;
        return View(resultado);
    }

    public async Task<IActionResult> Details(ulong id, CancellationToken ct = default)
    {
        var cliente = await _clienteService.GetByIdAsync(id, ct);
        if (cliente is null) return NotFound();

        await LoadViewBagsAsync(ct);
        return View(cliente);
    }

    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        await LoadViewBagsAsync(ct);
        // Issue #114: CreateClienteDto ya no expone FechaAlta ni Activo — los
        // setea el Service en CreateAsync con la fecha del alta y Activo=true.
        return View(new CreateClienteDto());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateClienteDto cliente, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            await LoadViewBagsAsync(ct);
            return View(cliente);
        }
        try
        {
            var currentUserId = GetCurrentUserId();
            await _clienteService.CreateAsync(cliente, currentUserId, ct);
            TempData["Success"] = $"Cliente {cliente.Nombre} {cliente.Apellido} creado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("Dni", ex.Message);
            await LoadViewBagsAsync(ct);
            return View(cliente);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo crear el cliente: {ex.Message}");
            await LoadViewBagsAsync(ct);
            return View(cliente);
        }
    }

    public async Task<IActionResult> Edit(ulong id, CancellationToken ct = default)
    {
        var cliente = await _clienteService.GetByIdAsync(id, ct);
        if (cliente is null) return NotFound();

        await LoadViewBagsAsync(ct);

        // Issue #114: UpdateClienteDto ya no expone Activo ni FechaAlta (son
        // audit trail / estado y solo cambian vía Delete/Restore). Los pasamos
        // por ViewBag para mostrarlos como info read-only en la vista.
        ViewBag.FechaAlta = cliente.FechaAlta;
        ViewBag.Activo = cliente.Activo;

        var updateDto = _mapper.Map<UpdateClienteDto>(cliente);
        return View(updateDto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ulong id, UpdateClienteDto cliente, CancellationToken ct = default)
    {
        if (id != cliente.Id) return BadRequest();
        if (!ModelState.IsValid)
        {
            await LoadViewBagsAsync(ct);
            return View(cliente);
        }
        try
        {
            var currentUserId = GetCurrentUserId();
            await _clienteService.UpdateAsync(cliente, currentUserId, ct);
            TempData["Success"] = $"Cliente {cliente.Nombre} {cliente.Apellido} actualizado.";
            return RedirectToAction(nameof(Index));
        }
        catch (ClienteSoftDeletedException ex)
        {
            // Issue #108: el cliente está soft-deleted. Mostramos el mensaje
            // específico (que viene de la excepción) y redirigimos a Index.
            // Distinct de KeyNotFoundException porque la solución es distinta:
            // restaurar en lugar de crear uno nuevo.
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            // Típicamente: DNI ya registrado (issue #105).
            ModelState.AddModelError("Dni", ex.Message);
            await LoadViewBagsAsync(ct);
            return View(cliente);
        }
        catch (KeyNotFoundException ex)
        {
            // Issue #108: el cliente no existe (o fue purgado). Redirigimos a
            // Index con el mensaje que viene del Service.
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo actualizar el cliente: {ex.Message}");
            await LoadViewBagsAsync(ct);
            return View(cliente);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(ulong id, CancellationToken ct = default)
    {
        var currentUserId = GetCurrentUserId();
        var ok = await _clienteService.DeleteAsync(id, currentUserId, ct);
        TempData[ok ? "Success" : "Error"] = ok
            ? "Cliente eliminado correctamente."
            : "No se encontró el cliente.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(ulong id, CancellationToken ct = default)
    {
        var currentUserId = GetCurrentUserId();
        var ok = await _clienteService.RestoreAsync(id, currentUserId, ct);
        TempData[ok ? "Success" : "Error"] = ok
            ? "Cliente reactivado correctamente."
            : "No se encontró el cliente.";
        return RedirectToAction(nameof(Index));
    }

    // Issue #109: CuentasCorrientes usaba GetAllAsync (trae TODOS los clientes
    // con su dirección completa) y la vista no mostraba saldo ni pedidos
    // pendientes. Si la vista disparaba queries adicionales por cliente era
    // un N+1 garantizado. Ahora delegamos en GetSaldosAsync, que proyecta la
    // vista SQL v_saldo_clientes (cliente + saldo + pedidos pendientes en
    // una sola fila agregada en MySQL).
    public async Task<IActionResult> CuentasCorrientes(CancellationToken ct = default)
    {
        var saldos = await _clienteService.GetSaldosAsync(ct);
        return View(saldos);
    }

    // Issue #111: ruta dedicada a la papelera de clientes soft-deleted. La Index
    // los oculta porque el QueryFilter global los filtra; aca los listamos via
    // IgnoreQueryFilters() y exponemos el boton Restaurar (ya existente en Restore POST).
    public async Task<IActionResult> Papelera(string? busqueda, int pagina = 1, int tamanio = 25, CancellationToken ct = default)
    {
        var resultado = await _clienteService.GetDeletedAsync(busqueda, pagina, tamanio, ct);
        ViewBag.Busqueda = busqueda;
        return View(resultado);
    }
}
