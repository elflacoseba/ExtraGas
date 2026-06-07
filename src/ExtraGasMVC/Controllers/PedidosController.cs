using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ExtraGasMVC.Controllers;

[Authorize(Policy = "OperadorOrAdmin")]
public class PedidosController : BaseController
{
    private readonly IPedidoService _pedidoService;
    private readonly IClienteService _clienteService;
    private readonly IProductoService _productoService;

    public PedidosController(
        IPedidoService pedidoService,
        IClienteService clienteService,
        IProductoService productoService)
    {
        _pedidoService = pedidoService;
        _clienteService = clienteService;
        _productoService = productoService;
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
        return View(pedido);
    }

    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        await CargarViewBagLookups(ct);
        return View(new CreatePedidoDto
        {
            Fecha = DateTime.Now
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreatePedidoDto pedido, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            await CargarViewBagLookups(ct);
            return View(pedido);
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
            await CargarViewBagLookups(ct);
            return View(pedido);
        }
        catch (Exception)
        {
            ModelState.AddModelError(string.Empty, "Ocurrió un error al crear el pedido. Intente nuevamente.");
            await CargarViewBagLookups(ct);
            return View(pedido);
        }
    }

    public async Task<IActionResult> Edit(ulong id, CancellationToken ct = default)
    {
        var pedido = await _pedidoService.GetByIdAsync(id, ct);
        if (pedido is null) return NotFound();

        if (pedido.EstadoCodigo == "ENTREGADO" || pedido.EstadoCodigo == "CANCELADO")
        {
            TempData["Info"] = "El pedido se encuentra en un estado final y no puede editarse.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await CargarViewBagLookups(ct);
        ViewBag.Items = pedido.Items;

        var transiciones = await _pedidoService.GetTransicionesDisponiblesAsync(id, ct);
        ViewBag.Transiciones = transiciones;
        ViewBag.EstadoActual = new
        {
            Id = pedido.EstadoPedidoId,
            Codigo = pedido.EstadoCodigo,
            Nombre = pedido.EstadoNombre,
            Color = pedido.EstadoColor,
            EsFinal = pedido.EstadoCodigo == "ENTREGADO" || pedido.EstadoCodigo == "CANCELADO"
        };

        var updateDto = new UpdatePedidoDto
        {
            Id = pedido.Id,
            Fecha = pedido.Fecha,
            FechaEntrega = pedido.FechaEntrega,
            ClienteId = pedido.ClienteId,
            EmpleadoId = pedido.EmpleadoId,
            CanalVentaId = pedido.CanalVentaId,
            MedioContactoId = pedido.MedioContactoId,
            Subtotal = pedido.Subtotal,
            Descuento = pedido.Descuento,
            Total = pedido.Total,
            Observaciones = pedido.Observaciones,
            DireccionEntrega = pedido.DireccionEntrega
        };

        ViewBag.MotivoCancelacion = pedido.MotivoCancelacion;

        return View(updateDto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ulong id, UpdatePedidoDto pedido, CancellationToken ct = default)
    {
        if (id != pedido.Id) return BadRequest();

        var pedidoActual = await _pedidoService.GetByIdAsync(id, ct);
        if (pedidoActual is null) return NotFound();
        if (pedidoActual.EstadoCodigo == "ENTREGADO" || pedidoActual.EstadoCodigo == "CANCELADO")
        {
            TempData["Error"] = "El pedido se encuentra en un estado final y no puede editarse.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var soloDirYObs = pedidoActual.EstadoCodigo == "CONFIRMADO" || pedidoActual.EstadoCodigo == "EN_PREPARACION";
        if (soloDirYObs)
        {
            pedido.Fecha = pedidoActual.Fecha;
            pedido.FechaEntrega = pedidoActual.FechaEntrega;
            pedido.ClienteId = pedidoActual.ClienteId;
            pedido.EmpleadoId = pedidoActual.EmpleadoId;
            pedido.CanalVentaId = pedidoActual.CanalVentaId;
            pedido.MedioContactoId = pedidoActual.MedioContactoId;
            pedido.Subtotal = pedidoActual.Subtotal;
            pedido.Descuento = pedidoActual.Descuento;
            pedido.Total = pedidoActual.Total;

            foreach (var key in new[] { "Fecha", "FechaEntrega", "ClienteId", "EmpleadoId", "CanalVentaId", "MedioContactoId", "Subtotal", "Descuento", "Total" })
            {
                ModelState.Remove(key);
            }
        }

        if (!ModelState.IsValid)
        {
            await CargarViewBagLookups(ct);
            var existingPedido = await _pedidoService.GetByIdAsync(id, ct);
            ViewBag.Items = existingPedido?.Items ?? new List<PedidoItemDto>();
            return View(pedido);
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
            await CargarViewBagLookups(ct);
            var existingPedido = await _pedidoService.GetByIdAsync(id, ct);
            ViewBag.Items = existingPedido?.Items ?? new List<PedidoItemDto>();
            return View(pedido);
        }
        catch (Exception)
        {
            ModelState.AddModelError(string.Empty, "Ocurrió un error al actualizar el pedido. Intente nuevamente.");
            await CargarViewBagLookups(ct);
            var existingPedido = await _pedidoService.GetByIdAsync(id, ct);
            ViewBag.Items = existingPedido?.Items ?? new List<PedidoItemDto>();
            return View(pedido);
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
    public async Task<IActionResult> CambiarEstado(ulong id, ulong nuevoEstadoId, string? motivoCancelacion, CancellationToken ct = default)
    {
        try
        {
            var userId = GetCurrentUserId();
            var ok = await _pedidoService.CambiarEstadoAsync(id, nuevoEstadoId, motivoCancelacion, userId, ct);
            TempData[ok ? "Success" : "Error"] = ok
                ? "Estado del pedido actualizado correctamente."
                : "No se encontró el pedido.";

            if (ok)
            {
                var pedido = await _pedidoService.GetByIdAsync(id, ct);
                if (pedido?.EstadoCodigo == "CANCELADO" || pedido?.EstadoCodigo == "ENTREGADO")
                    return RedirectToAction(nameof(Details), new { id });
            }
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (Exception)
        {
            TempData["Error"] = "Ocurrió un error al cambiar el estado del pedido. Intente nuevamente.";
        }
        return RedirectToAction(nameof(Edit), new { id });
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
        var pedidoActual = await _pedidoService.GetByIdAsync(item.PedidoId, ct);
        if (pedidoActual is null) return NotFound();
        if (pedidoActual.EstadoCodigo != "PENDIENTE")
        {
            TempData["Error"] = $"No se pueden agregar items en estado {pedidoActual.EstadoNombre}. Solo se permite en estado Pendiente.";
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
        var pedidoActual = await _pedidoService.GetByIdAsync(pedidoId, ct);
        if (pedidoActual is null) return NotFound();
        if (pedidoActual.EstadoCodigo != "PENDIENTE")
        {
            TempData["Error"] = $"No se pueden eliminar items en estado {pedidoActual.EstadoNombre}. Solo se permite en estado Pendiente.";
            return RedirectToAction(nameof(Edit), new { id = pedidoId, fragment = "itemsTable" });
        }
        try
        {
            await _pedidoService.RemoveItemAsync(itemId, ct);
            TempData["Success"] = "Item eliminado correctamente.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Edit), new { id = pedidoId, fragment = "itemsTable" });
    }

    private async Task CargarViewBagLookups(CancellationToken ct)
    {
        ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
        ViewBag.Empleados = await _pedidoService.GetEmpleadosActivosAsync(ct);
        ViewBag.Estados = await _pedidoService.GetEstadosPedidoAsync(ct);
        ViewBag.Canales = await _pedidoService.GetCanalesVentaAsync(ct);
        ViewBag.MediosContacto = await _pedidoService.GetMediosContactoAsync(ct);
        ViewBag.Productos = await _productoService.GetActivosAsync(ct);
    }
}
