using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Controllers;

[Authorize(Policy = "OperadorOrAdmin")]
public class RecepcionesController : BaseController
{
    private readonly ExtraGasDbContext _context;
    private readonly IRecepcionService _recepcionService;
    private readonly IProveedorService _proveedorService;

    public RecepcionesController(
        ExtraGasDbContext context,
        IRecepcionService recepcionService,
        IProveedorService proveedorService)
    {
        _context = context;
        _recepcionService = recepcionService;
        _proveedorService = proveedorService;
    }

    public async Task<IActionResult> Index(int pagina = 1, int tamanio = 25, CancellationToken ct = default)
    {
        // Index sigue consultando el DbContext: el listado no entra en el
        // scope de issue #45, que apunta específicamente a la creación transaccional.
        var query = _context.RecepcionesProveedor.AsNoTracking().OrderByDescending(r => r.Fecha);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((pagina - 1) * tamanio).Take(tamanio).ToListAsync(ct);
        return View(new PagedResult<RecepcionProveedor>
        {
            Items = items,
            Total = total,
            Page = pagina,
            PageSize = tamanio
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        var vm = await BuildCreateViewModelAsync(new CrearRecepcionDto { Fecha = DateTime.Now }, ct);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CrearRecepcionDto input, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            var vm = await BuildCreateViewModelAsync(input, ct);
            return View(vm);
        }

        try
        {
            var userId = GetCurrentUserId();
            var created = await _recepcionService.CreateAsync(input, userId, ct);
            TempData["Success"] = $"Recepción {created.Numero} registrada correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var vm = await BuildCreateViewModelAsync(input, ct);
            return View(vm);
        }
        catch (Exception)
        {
            ModelState.AddModelError(string.Empty, "Ocurrió un error al registrar la recepción. Intente nuevamente.");
            var vm = await BuildCreateViewModelAsync(input, ct);
            return View(vm);
        }
    }

    private async Task<CrearRecepcionViewModel> BuildCreateViewModelAsync(
        CrearRecepcionDto recepcion,
        CancellationToken ct = default)
    {
        // Lookups SECUENCIALES: ambos servicios son Scoped y comparten la
        // misma instancia de DbContext dentro del request; EF no permite
        // operaciones concurrentes sobre un único DbContext.
        var proveedores = await _proveedorService.SearchAsync(
            null, soloActivos: true, pagina: 1, tamanio: 1000, ct);
        var productos = await _recepcionService.GetProductosActivosAsync(ct);

        return new CrearRecepcionViewModel
        {
            Recepcion = recepcion,
            Proveedores = proveedores.Items,
            Productos = productos,
        };
    }
}

[Authorize(Policy = "OperadorOrAdmin")]
public class PagosProveedorController : Controller
{
    private readonly ExtraGasDbContext _context;

    public PagosProveedorController(ExtraGasDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(int pagina = 1, int tamanio = 25, CancellationToken ct = default)
    {
        var query = _context.PagosProveedor.AsNoTracking().OrderByDescending(p => p.Fecha);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((pagina - 1) * tamanio).Take(tamanio).ToListAsync(ct);
        return View(new PagedResult<PagoProveedor>
        {
            Items = items,
            Total = total,
            Page = pagina,
            PageSize = tamanio
        });
    }
}

[Authorize(Policy = "OperadorOrAdmin")]
public class ReportesController : Controller
{
    private readonly ExtraGasDbContext _context;

    public ReportesController(ExtraGasDbContext context)
    {
        _context = context;
    }

    public IActionResult Index() => View();

    public async Task<IActionResult> ProductosMasVendidos(int dias = 30, CancellationToken ct = default)
    {
        var desde = DateTime.UtcNow.AddDays(-dias);
        var items = await _context.VProductosMasVendidos
            .AsNoTracking()
            .Where(v => v.Fecha >= desde)
            .ToListAsync(ct);
        ViewBag.Dias = dias;
        return View(items);
    }

    public async Task<IActionResult> RegularidadClientes(CancellationToken ct = default)
    {
        var items = await _context.VRegularidadClientes
            .AsNoTracking()
            .OrderBy(v => v.DiasPromedioEntrePedidos)
            .ToListAsync(ct);
        return View(items);
    }

    public async Task<IActionResult> PagosPorForma(CancellationToken ct = default)
    {
        var items = await _context.VPagosPorFormaPago
            .AsNoTracking()
            .OrderByDescending(v => v.MontoTotal)
            .ToListAsync(ct);
        return View(items);
    }
}