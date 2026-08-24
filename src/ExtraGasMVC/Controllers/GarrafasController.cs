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
    private const int RecepcionesDropdownCantidad = 100;

    private readonly IGarrafaService _garrafaService;
    private readonly IClienteService _clienteService;
    private readonly IProveedorService _proveedorService;
    private readonly IRecepcionService _recepcionService;

    public GarrafasController(
        IGarrafaService garrafaService,
        IClienteService clienteService,
        IProveedorService proveedorService,
        IRecepcionService recepcionService)
    {
        _garrafaService = garrafaService;
        _clienteService = clienteService;
        _proveedorService = proveedorService;
        _recepcionService = recepcionService;
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

    /// <summary>
    /// Puebla los 4 ViewBags que alimentan los dropdowns de los formularios
    /// Create/Edit de garrafas (issue #48): Clientes, Estados, Proveedores y
    /// Recepciones. Lookups SECUENCIALES: los 4 servicios comparten el mismo
    /// DbContext Scoped dentro del request, y EF no permite operaciones
    /// concurrentes sobre un único DbContext (mismo motivo que en
    /// <c>RecepcionesController.BuildCreateViewModelAsync</c>).
    /// </summary>
    private async Task CargarDropdownsFormularioAsync(CancellationToken ct)
    {
        ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
        ViewBag.Estados = await _garrafaService.GetEstadosAsync(ct);

        var proveedoresResult = await _proveedorService.SearchAsync(
            null, soloActivos: true, pagina: 1, tamanio: 1000, ct);
        ViewBag.Proveedores = proveedoresResult.Items;

        ViewBag.Recepciones = await _recepcionService.GetRecientesAsync(
            RecepcionesDropdownCantidad, ct);
    }

    public async Task<IActionResult> Index(
        string? codigo, byte? capacidad,
        int page = 1, int pageSize = 20,
        string sortBy = "codigo", string sortDir = "asc",
        CancellationToken ct = default)
    {
        // Issue #52: el service pagina y filtra en SQL. ViewBag expone los
        // filtros actuales para que la vista los mantenga en los links de
        // paginación y en el form.
        // Issue #53: sortBy/sortDir viajan del query string al service y se
        // exponen a la vista para los headers clickeables.
        var resultado = await _garrafaService.GetPagedAsync(
            codigo, capacidad, page, pageSize, sortBy, sortDir, ct);

        ViewBag.Codigo = codigo;
        ViewBag.Capacidad = capacidad;
        ViewBag.SortBy = sortBy;
        ViewBag.SortDir = sortDir;
        return View(resultado);
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

    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        await CargarDropdownsFormularioAsync(ct);
        return View(new CreateGarrafaDto
        {
            EstadoGarrafaId = 1,
            Activo = true,
            FechaCompra = DateOnly.FromDateTime(DateTime.UtcNow)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateGarrafaDto garrafa, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            await CargarDropdownsFormularioAsync(ct);
            return View(garrafa);
        }
        try
        {
            await _garrafaService.CreateAsync(garrafa, GetCurrentUserId(), ct);
            TempData["Success"] = $"Garrafa {garrafa.Codigo} creada.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await CargarDropdownsFormularioAsync(ct);
            return View(garrafa);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo crear la garrafa: {ex.Message}");
            await CargarDropdownsFormularioAsync(ct);
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

        await CargarDropdownsFormularioAsync(ct);

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
            await CargarDropdownsFormularioAsync(ct);
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
            await CargarDropdownsFormularioAsync(ct);
            return View(garrafa);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo actualizar la garrafa: {ex.Message}");
            await CargarDropdownsFormularioAsync(ct);
            return View(garrafa);
        }
    }

    public async Task<IActionResult> Stock(CancellationToken ct = default)
    {
        // Issue #51: ahora consulta la vista v_stock_garrafas (nombres y
        // colores de estado incluidos), eliminando el agrupamiento manual
        // en memoria.
        var stock = await _garrafaService.GetStockAsync(ct);
        return View(stock);
    }

    public async Task<IActionResult> EnClientes(ulong? clienteId, CancellationToken ct = default)
    {
        // Issue #51: la vista v_garrafas_en_clientes ya excluye las que no
        // están en estado EN_CLIENTE, aplica soft-delete y calcula días; el
        // Controller ya no necesita pedir el listado completo y filtrar acá.
        var enClientes = await _garrafaService.GetEnClientesAsync(clienteId, ct);

        if (clienteId.HasValue)
            ViewBag.Cliente = await _clienteService.GetByIdAsync(clienteId.Value, ct);

        return View("EnClientes", enClientes);
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
        // Issue #54: confirmación se hace en Index vía SweetAlert2 (helper
        // confirmarAccion), por eso el flujo siempre vuelve a Index. Si la
        // garrafa está en estado bloqueado (EN_CLIENTE / EN_TRANSITO) el
        // service tira InvalidOperationException y mostramos el motivo en
        // TempData para que el usuario sepa por qué no se eliminó.
        try
        {
            var ok = await _garrafaService.DeleteAsync(id, GetCurrentUserId(), ct);
            if (!ok) return NotFound();
            TempData["Success"] = "Garrafa eliminada correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"No se pudo eliminar la garrafa: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }
}
