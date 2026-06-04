using ExtraGasMVC.Data.Context;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Controllers;

public class RecepcionesController : Controller
{
    private readonly ExtraGasDbContext _context;

    public RecepcionesController(ExtraGasDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(int pagina = 1, int tamanio = 25, CancellationToken ct = default)
    {
        var query = _context.RecepcionesProveedor.AsNoTracking().OrderByDescending(r => r.Fecha);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((pagina - 1) * tamanio).Take(tamanio).ToListAsync(ct);
        ViewBag.Pagina = pagina;
        ViewBag.Tamanio = tamanio;
        ViewBag.Total = total;
        return View(items);
    }

    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(ExtraGasMVC.Data.Entities.RecepcionProveedor recepcion)
    {
        if (!ModelState.IsValid) return View(recepcion);
        recepcion.CreatedAt = DateTime.UtcNow;
        recepcion.UpdatedAt = DateTime.UtcNow;
        _context.RecepcionesProveedor.Add(recepcion);
        _context.SaveChanges();
        TempData["Success"] = "Recepcion registrada.";
        return RedirectToAction(nameof(Index));
    }
}

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
        ViewBag.Pagina = pagina;
        ViewBag.Tamanio = tamanio;
        ViewBag.Total = total;
        return View(items);
    }
}

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
