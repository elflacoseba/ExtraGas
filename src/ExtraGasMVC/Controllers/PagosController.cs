using ExtraGasMVC.Data.Context;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Controllers;

[Authorize(Policy = "OperadorOrAdmin")]
public class PagosController : Controller
{
    private readonly IPagoService _pagoService;
    private readonly IPedidoService _pedidoService;
    private readonly IClienteService _clienteService;
    private readonly ExtraGasDbContext _context;

    public PagosController(
        IPagoService pagoService,
        IPedidoService pedidoService,
        IClienteService clienteService,
        ExtraGasDbContext context)
    {
        _pagoService = pagoService;
        _pedidoService = pedidoService;
        _clienteService = clienteService;
        _context = context;
    }

    private async Task LoadViewBagsAsync(CancellationToken ct = default)
    {
        ViewBag.Pedidos = await _pedidoService.GetPendientesAsync(ct);
        ViewBag.Clientes = await _clienteService.GetActivosAsync(ct);
        ViewBag.FormasPago = await _context.FormasPago.AsNoTracking().ToListAsync(ct);
    }

    public async Task<IActionResult> Index(ulong? pedidoId, int pagina = 1, int tamanio = 25, CancellationToken ct = default)
    {
        var pagos = await _pagoService.GetAllAsync(ct);
        if (pedidoId.HasValue) pagos = pagos.Where(p => p.PedidoId == pedidoId.Value);

        var total = pagos.Count();
        var items = pagos.OrderByDescending(p => p.Fecha)
            .Skip((pagina - 1) * tamanio).Take(tamanio).ToList();
        ViewBag.PedidoId = pedidoId;
        return View(new PagedResult<PagoDto>
        {
            Items = items,
            Total = total,
            Page = pagina,
            PageSize = tamanio
        });
    }

    public async Task<IActionResult> Details(ulong id, CancellationToken ct = default)
    {
        var pago = await _pagoService.GetByIdAsync(id, ct);
        if (pago is null) return NotFound();
        return View(pago);
    }

    public async Task<IActionResult> Create(ulong? pedidoId, CancellationToken ct = default)
    {
        await LoadViewBagsAsync(ct);
        return View(new CreatePagoDto { Fecha = DateTime.UtcNow, PedidoId = pedidoId ?? 0, FormaPagoId = 1 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreatePagoDto pago, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            await LoadViewBagsAsync(ct);
            return View(pago);
        }
        try
        {
            await _pagoService.CreateAsync(pago, ct);
            TempData["Success"] = "Pago registrado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo registrar el pago: {ex.Message}");
            await LoadViewBagsAsync(ct);
            return View(pago);
        }
    }

    public async Task<IActionResult> Edit(ulong id, CancellationToken ct = default)
    {
        var pago = await _pagoService.GetByIdAsync(id, ct);
        if (pago is null) return NotFound();
        await LoadViewBagsAsync(ct);

        var updateDto = new UpdatePagoDto
        {
            Id = pago.Id,
            Fecha = pago.Fecha,
            ClienteId = pago.ClienteId,
            PedidoId = pago.PedidoId,
            FormaPagoId = pago.FormaPagoId,
            Monto = pago.Monto,
            Referencia = pago.Referencia,
            Observaciones = pago.Observaciones
        };
        
        return View(updateDto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ulong id, UpdatePagoDto pago, CancellationToken ct = default)
    {
        if (id != pago.Id) return BadRequest();
        if (!ModelState.IsValid)
        {
            await LoadViewBagsAsync(ct);
            return View(pago);
        }
        try
        {
            await _pagoService.UpdateAsync(pago, ct);
            TempData["Success"] = "Pago actualizado.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo actualizar el pago: {ex.Message}");
            await LoadViewBagsAsync(ct);
            return View(pago);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(ulong id, CancellationToken ct = default)
    {
        var ok = await _pagoService.DeleteAsync(id, ct);
        TempData[ok ? "Success" : "Error"] = ok ? "Pago eliminado." : "No se encontro el pago.";
        return RedirectToAction(nameof(Index));
    }
}
