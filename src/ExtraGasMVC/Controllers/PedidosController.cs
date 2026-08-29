using ExtraGasMVC.Constants;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ExtraGasMVC.Controllers;

[Authorize(Policy = "OperadorOrAdmin")]
public class PedidosController : BaseController
{
    // CA1869: cache the JsonSerializerOptions instance to avoid allocating
    // a new one on every deserialization call.
    private static readonly System.Text.Json.JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPedidoService _pedidoService;
    private readonly IClienteService _clienteService;
    private readonly IProductoService _productoService;
    private readonly IGarrafaService _garrafaService;

    public PedidosController(
        IPedidoService pedidoService,
        IClienteService clienteService,
        IProductoService productoService,
        IGarrafaService garrafaService)
    {
        _pedidoService = pedidoService;
        _clienteService = clienteService;
        _productoService = productoService;
        _garrafaService = garrafaService;
    }

    public async Task<IActionResult> Index(
        string? numero, ulong? estadoId, DateTime? desde, DateTime? hasta,
        int pagina = 1, int tamanio = 25, CancellationToken ct = default)
    {
        var resultado = await _pedidoService.SearchAsync(
            numero, estadoId > 0 ? estadoId : null, null,
            desde, hasta, pagina, tamanio, ct);

        ViewBag.Numero = numero;
        ViewBag.EstadoId = estadoId;
        ViewBag.Desde = desde;
        ViewBag.Hasta = hasta;
        ViewBag.Pagina = resultado.Pagina;
        ViewBag.Tamanio = resultado.Tamanio;
        ViewBag.Total = resultado.Total;
        ViewBag.Estados = await _pedidoService.GetEstadosPedidoAsync(ct);
        return View(resultado.Items);
    }

    public async Task<IActionResult> Details(ulong id, CancellationToken ct = default)
    {
        var pedido = await _pedidoService.GetByIdAsync(id, ct);
        if (pedido is null) return NotFound();

        // Issue #44: trazabilidad post-CONFIRMADO. La card se rendera solo
        // cuando el pedido tiene movimientos de canje (CONFIRMADO, etc).
        var movimientosGarrafa = await _garrafaService.GetMovimientosByPedidoAsync(id, ct);
        ViewBag.MovimientosGarrafa = movimientosGarrafa;

        return View(pedido);
    }

    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        var vm = await BuildCreateViewModelAsync(new CreatePedidoDto { Fecha = DateTime.Now }, ct);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreatePedidoDto pedido, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            var vm = await BuildCreateViewModelAsync(pedido, ct);
            return View(vm);
        }
        try
        {
            var userId = GetCurrentUserId();
            var created = await _pedidoService.CreateAsync(pedido, userId, ct);
            TempData["Success"] = $"Pedido {created.Numero} creado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var vm = await BuildCreateViewModelAsync(pedido, ct);
            return View(vm);
        }
        catch (Exception)
        {
            ModelState.AddModelError(string.Empty, "Ocurrió un error al crear el pedido. Intente nuevamente.");
            var vm = await BuildCreateViewModelAsync(pedido, ct);
            return View(vm);
        }
    }

    public async Task<IActionResult> Edit(ulong id, CancellationToken ct = default)
    {
        var pedido = await _pedidoService.GetByIdAsync(id, ct);
        if (pedido is null) return NotFound();

        if (PedidoEstados.EstadosFinales.Contains(pedido.EstadoCodigo ?? ""))
        {
            TempData["Info"] = "El pedido se encuentra en un estado final y no puede editarse.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var updateDto = new UpdatePedidoDto
        {
            Id = pedido.Id,
            Fecha = pedido.Fecha,
            FechaEntrega = pedido.FechaEntrega,
            ClienteId = pedido.ClienteId,
            EmpleadoId = pedido.EmpleadoId,
            CanalVentaId = pedido.CanalVentaId,
            MedioContactoId = pedido.MedioContactoId,
            Descuento = pedido.Descuento,
            DireccionEntrega = pedido.DireccionEntrega,
            Observaciones = pedido.Observaciones
        };

        var vm = await BuildEditViewModelAsync(id, updateDto, ct);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ulong id, UpdatePedidoDto pedido, CancellationToken ct = default)
    {
        if (id != pedido.Id) return BadRequest();

        var pedidoActual = await _pedidoService.GetByIdAsync(id, ct);
        if (pedidoActual is null) return NotFound();

        // The service layer handles state-based validation (final states, partial edit).
        // The controller only handles HTTP-level concerns (ModelState, TempData, redirects).

        if (!ModelState.IsValid)
        {
            var vm = await BuildEditViewModelAsync(id, pedido, ct);
            return View(vm);
        }
        try
        {
            var userId = GetCurrentUserId();
            await _pedidoService.UpdateAsync(pedido, userId, ct);
            TempData["Success"] = "Pedido actualizado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var vm = await BuildEditViewModelAsync(id, pedido, ct);
            return View(vm);
        }
        catch (Exception)
        {
            ModelState.AddModelError(string.Empty, "Ocurrió un error al actualizar el pedido. Intente nuevamente.");
            var vm = await BuildEditViewModelAsync(id, pedido, ct);
            return View(vm);
        }
    }

    public async Task<IActionResult> Delete(ulong id, CancellationToken ct = default)
    {
        var pedido = await _pedidoService.GetByIdAsync(id, ct);
        if (pedido is null) return NotFound();
        return View(pedido);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(ulong id, CancellationToken ct = default)
    {
        try
        {
            var userId = GetCurrentUserId();
            var ok = await _pedidoService.DeleteAsync(id, userId, ct);
            TempData[ok ? "Success" : "Error"] = ok
                ? "Pedido eliminado correctamente."
                : "No se encontró el pedido.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(ulong id, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var ok = await _pedidoService.RestoreAsync(id, userId, ct);
        TempData[ok ? "Success" : "Error"] = ok
            ? "Pedido reactivado correctamente."
            : "No se encontró el pedido.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarEstado(
        ulong id,
        ulong nuevoEstadoId,
        string? motivoCancelacion,
        string? codigosGarrafaJson,
        CancellationToken ct = default)
    {
        try
        {
            var userId = GetCurrentUserId();

            // Issue #44: cuando el destino es CONFIRMADO y el form trae códigos
            // de garrafas serializados, delegamos en RegistrarCanjePedidoAsync
            // para que la transición de estado y los movimientos de garrafa sean
            // atómicos. Si no hay códigos (pedido solo VENTA / carbón / leña),
            // caemos al flujo normal de CambiarEstadoAsync.
            var destino = await ResolveEstadoDestinoAsync(nuevoEstadoId, ct);
            var esConfirmadoConCodigos = destino?.Codigo == PedidoEstados.Confirmado
                                         && !string.IsNullOrWhiteSpace(codigosGarrafaJson);

            return esConfirmadoConCodigos
                ? await ConfirmarConCanjeAsync(id, codigosGarrafaJson!, userId, ct)
                : await CambiarEstadoGenericoAsync(id, nuevoEstadoId, motivoCancelacion, userId, ct);
        }
        catch (InvalidOperationException ex)
        {
            // Incluye: código inexistente, estado incorrecto, cantidad no coincide,
            // re-CONFIRMADO, etc. (issue #44).
            TempData["Error"] = ex.Message;
        }
        catch (Exception)
        {
            TempData["Error"] = "Ocurrió un error al cambiar el estado del pedido. Intente nuevamente.";
        }
        return RedirectToAction(nameof(Edit), new { id });
    }

    /// <summary>
    /// Resuelve el <see cref="EstadoPedidoDto"/> destino a partir del id del form.
    /// Devuelve null si el id no existe en el catálogo (se propaga como
    /// "estado destino no existe" desde el service).
    /// </summary>
    private async Task<EstadoPedidoDto?> ResolveEstadoDestinoAsync(ulong nuevoEstadoId, CancellationToken ct)
    {
        var estados = await _pedidoService.GetEstadosPedidoAsync(ct);
        return estados.FirstOrDefault(e => e.Id == nuevoEstadoId);
    }

    /// <summary>
    /// Branch CONFIRMADO + códigos de garrafas: deserializa el JSON de
    /// códigos y delega en <c>RegistrarCanjePedidoAsync</c> para hacer
    /// atómica la transición de estado y los movimientos de garrafa.
    /// </summary>
    private async Task<IActionResult> ConfirmarConCanjeAsync(
        ulong id, string codigosGarrafaJson, ulong? userId, CancellationToken ct)
    {
        Dictionary<ulong, List<string>> codigosPorItem;
        try
        {
            codigosPorItem = System.Text.Json.JsonSerializer.Deserialize<Dictionary<ulong, List<string>>>(
                codigosGarrafaJson,
                JsonSerializerOptions)
                ?? new Dictionary<ulong, List<string>>();
        }
        catch (System.Text.Json.JsonException)
        {
            TempData["Error"] = "Los códigos de garrafas enviados tienen un formato inválido. Recargue la pantalla e intente nuevamente.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var ok = await _pedidoService.RegistrarCanjePedidoAsync(id, codigosPorItem, userId, ct);

        TempData[ok ? "Success" : "Error"] = ok
            ? "Pedido confirmado y garrafas registradas correctamente."
            : "No se encontró el pedido.";

        return ok
            ? RedirectToAction(nameof(Details), new { id })
            : RedirectToAction(nameof(Edit), new { id });
    }

    /// <summary>
    /// Branch genérico: llama a <c>CambiarEstadoAsync</c> y, si la transición
    /// lleva al pedido a un estado final, redirige a Details en lugar de Edit.
    /// </summary>
    private async Task<IActionResult> CambiarEstadoGenericoAsync(
        ulong id, ulong nuevoEstadoId, string? motivoCancelacion, ulong? userId, CancellationToken ct)
    {
        var ok = await _pedidoService.CambiarEstadoAsync(id, nuevoEstadoId, motivoCancelacion, userId, ct);
        TempData[ok ? "Success" : "Error"] = ok
            ? "Estado del pedido actualizado correctamente."
            : "No se encontró el pedido.";

        if (!ok) return RedirectToAction(nameof(Edit), new { id });

        var pedido = await _pedidoService.GetByIdAsync(id, ct);
        return PedidoEstados.EstadosFinales.Contains(pedido?.EstadoCodigo ?? "")
            ? RedirectToAction(nameof(Details), new { id })
            : RedirectToAction(nameof(Edit), new { id });
    }

    public async Task<IActionResult> Pendientes(CancellationToken ct = default)
    {
        var pedidos = await _pedidoService.GetPendientesAsync(ct);
        return View(pedidos);
    }

    // ---- Items del pedido ----

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(CreatePedidoItemDto item, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Datos de item inválidos.";
            return RedirectToAction(nameof(Edit), new { id = item.PedidoId });
        }
        try
        {
            await _pedidoService.AddItemAsync(item, ct);
            TempData["Success"] = "Item agregado correctamente.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Edit), new { id = item.PedidoId, fragment = "itemsTable" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(ulong itemId, ulong pedidoId, CancellationToken ct = default)
    {
        try
        {
            var ok = await _pedidoService.RemoveItemAsync(itemId, ct);
            TempData[ok ? "Success" : "Error"] = ok
                ? "Item eliminado correctamente."
                : "No se encontró el item.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (Exception)
        {
            TempData["Error"] = "Ocurrió un error al eliminar el item.";
        }
        return RedirectToAction(nameof(Edit), new { id = pedidoId, fragment = "itemsTable" });
    }

    private async Task<PedidoCreateViewModel> BuildCreateViewModelAsync(
        CreatePedidoDto pedido,
        CancellationToken ct = default)
    {
        // Lookups are independent — fan them out in parallel.
        var clientesTask = _clienteService.GetActivosAsync(ct);
        var empleadosTask = _pedidoService.GetEmpleadosActivosAsync(ct);
        var canalesTask = _pedidoService.GetCanalesVentaAsync(ct);
        var mediosTask = _pedidoService.GetMediosContactoAsync(ct);

        await Task.WhenAll(clientesTask, empleadosTask, canalesTask, mediosTask);

        return new PedidoCreateViewModel
        {
            Pedido = pedido,
            Clientes = clientesTask.Result,
            Empleados = empleadosTask.Result,
            Canales = canalesTask.Result,
            MediosContacto = mediosTask.Result
        };
    }

    private async Task<PedidoEditViewModel> BuildEditViewModelAsync(
        ulong id,
        UpdatePedidoDto pedido,
        CancellationToken ct = default)
    {
        // All of these are read-only reads against the pedido + lookups — safe to fan out.
        var pedidoDbTask = _pedidoService.GetByIdAsync(id, ct);
        var clientesTask = _clienteService.GetActivosAsync(ct);
        var empleadosTask = _pedidoService.GetEmpleadosActivosAsync(ct);
        var canalesTask = _pedidoService.GetCanalesVentaAsync(ct);
        var mediosTask = _pedidoService.GetMediosContactoAsync(ct);
        var productosTask = _productoService.GetActivosAsync(ct);
        var transicionesTask = _pedidoService.GetTransicionesDisponiblesAsync(id, ct);

        await Task.WhenAll(
            pedidoDbTask, clientesTask, empleadosTask, canalesTask,
            mediosTask, productosTask, transicionesTask);

        var pedidoDb = pedidoDbTask.Result;
        var estadoCodigo = pedidoDb?.EstadoCodigo ?? "";

        // Issue #44: el modal de canje solo aplica a items GARRAFA-capaces con
        // tipo ENTREGA o DEVOLUCION. El discriminador es ManejaGarrafaIndividual
        // (NO UnidadVenta) — los productos GAS-10/15/45 lo tienen en TRUE.
        var items = pedidoDb?.Items ?? new List<PedidoItemDto>();
        var itemsGarrafaCanje = items
            .Where(i => i.ManejaGarrafaIndividual
                     && (i.TipoLinea == "ENTREGA" || i.TipoLinea == "DEVOLUCION"))
            .Select(i => new PedidoItemGarrafaVm
            {
                ItemId = i.Id,
                ProductoNombre = i.ProductoNombre ?? "Producto",
                CapacidadKg = i.CapacidadKg,
                TipoLinea = i.TipoLinea,
                CantidadEsperada = (int)i.Cantidad
            })
            .ToList();

        return new PedidoEditViewModel
        {
            Pedido = pedido,
            Clientes = clientesTask.Result,
            Empleados = empleadosTask.Result,
            Canales = canalesTask.Result,
            MediosContacto = mediosTask.Result,
            Productos = productosTask.Result,
            Items = items,
            ItemsGarrafaCanje = itemsGarrafaCanje,
            Transiciones = transicionesTask.Result,
            EstadoActual = new PedidoEstadoActualInfo
            {
                Id = pedidoDb?.EstadoPedidoId ?? 0,
                Codigo = pedidoDb?.EstadoCodigo,
                Nombre = pedidoDb?.EstadoNombre,
                Color = pedidoDb?.EstadoColor,
                EsFinal = PedidoEstados.EstadosFinales.Contains(estadoCodigo)
            },
            Subtotal = pedidoDb?.Subtotal ?? 0m,
            Total = pedidoDb?.Total ?? 0m,
            MotivoCancelacion = pedidoDb?.MotivoCancelacion
        };
    }
}