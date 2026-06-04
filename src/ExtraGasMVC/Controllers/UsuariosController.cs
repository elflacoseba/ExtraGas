using System.Security.Claims;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Controllers;

[Authorize(Policy = "AdminOnly")]
public class UsuariosController : Controller
{
    private readonly IUsuarioService _usuarioService;
    private readonly ExtraGasDbContext _context;

    public UsuariosController(IUsuarioService usuarioService, ExtraGasDbContext context)
    {
        _usuarioService = usuarioService;
        _context = context;
    }

    public async Task<IActionResult> Index(string? busqueda, ulong? rolId, bool soloActivos = true,
        int pagina = 1, int tamanio = 25, CancellationToken ct = default)
    {
        var usuarios = await _usuarioService.GetAllAsync(ct);

        if (soloActivos)
            usuarios = usuarios.Where(u => u.Activo);

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var q = busqueda.Trim().ToLower();
            usuarios = usuarios.Where(u =>
                u.Username.ToLower().Contains(q)
                || (u.Email ?? string.Empty).ToLower().Contains(q));
        }

        if (rolId.HasValue)
            usuarios = usuarios.Where(u => u.RolId == rolId.Value);

        var total = usuarios.Count();
        var items = usuarios
            .OrderBy(u => u.Username)
            .Skip((pagina - 1) * tamanio)
            .Take(tamanio)
            .ToList();

        ViewBag.Busqueda = busqueda;
        ViewBag.RolId = rolId;
        ViewBag.SoloActivos = soloActivos;
        ViewBag.Pagina = pagina;
        ViewBag.Tamanio = tamanio;
        ViewBag.Total = total;

        return View(items);
    }

    public async Task<IActionResult> Details(ulong id, CancellationToken ct = default)
    {
        var usuario = await _usuarioService.GetByIdAsync(id, ct);
        if (usuario is null) return NotFound();
        return View(usuario);
    }

    public async Task<IActionResult> Create()
    {
        await LoadViewBagsAsync();
        return View(new CreateUsuarioDto { Activo = true });
    }

    private async Task LoadViewBagsAsync()
    {
        ViewBag.Roles = await _context.Roles.AsNoTracking().OrderBy(r => r.Nombre).ToListAsync();
        ViewBag.EmpleadosSinUsuario = await GetEmpleadosSinUsuario();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUsuarioDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.EmpleadosSinUsuario = await GetEmpleadosSinUsuario();
            return View(dto);
        }

        var existing = await _usuarioService.GetByUsernameAsync(dto.Username, ct);
        if (existing is not null)
        {
            ModelState.AddModelError("Username", "El nombre de usuario ya esta en uso.");
            ViewBag.EmpleadosSinUsuario = await GetEmpleadosSinUsuario();
            return View(dto);
        }

        try
        {
            var currentUserId = GetCurrentUserId();
            await _usuarioService.CreateAsync(dto, currentUserId, ct);
            TempData["Success"] = $"Usuario {dto.Username} creado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo crear el usuario: {ex.Message}");
            ViewBag.EmpleadosSinUsuario = await GetEmpleadosSinUsuario();
            return View(dto);
        }
    }

    public async Task<IActionResult> Edit(ulong id, CancellationToken ct = default)
    {
        var usuario = await _usuarioService.GetByIdAsync(id, ct);
        if (usuario is null) return NotFound();

        var updateDto = new UpdateUsuarioDto
        {
            Id = usuario.Id,
            Email = usuario.Email,
            RolId = usuario.RolId,
            Activo = usuario.Activo
        };

        ViewBag.Usuario = usuario;
        ViewBag.Roles = await _context.Roles.AsNoTracking().OrderBy(r => r.Nombre).ToListAsync();
        return View(updateDto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ulong id, UpdateUsuarioDto dto, CancellationToken ct = default)
    {
        if (id != dto.Id) return BadRequest();
        if (!ModelState.IsValid)
        {
            ViewBag.Usuario = await _usuarioService.GetByIdAsync(id, ct);
            return View(dto);
        }

        try
        {
            var currentUserId = GetCurrentUserId();
            await _usuarioService.UpdateAsync(dto, currentUserId, ct);
            TempData["Success"] = "Usuario actualizado.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo actualizar el usuario: {ex.Message}");
            ViewBag.Usuario = await _usuarioService.GetByIdAsync(id, ct);
            ViewBag.Roles = await _context.Roles.AsNoTracking().OrderBy(r => r.Nombre).ToListAsync();
            return View(dto);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(ulong id, CancellationToken ct = default)
    {
        var currentUserId = GetCurrentUserId();
        if (id == currentUserId)
        {
            TempData["Error"] = "No puede eliminarse a si mismo.";
            return RedirectToAction(nameof(Index));
        }

        var ok = await _usuarioService.DeleteAsync(id, ct);
        TempData[ok ? "Success" : "Error"] = ok
            ? "Usuario desactivado correctamente."
            : "No se encontro el usuario.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ulong id, string currentPassword, string newPassword, string confirmPassword, CancellationToken ct = default)
    {
        if (newPassword != confirmPassword)
        {
            TempData["Error"] = "Las contrasenas no coinciden.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            TempData["Error"] = "La nueva contrasena debe tener al menos 6 caracteres.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var ok = await _usuarioService.ChangePasswordAsync(id, currentPassword, newPassword, ct);
        TempData[ok ? "Success" : "Error"] = ok
            ? "Contrasena cambiada correctamente."
            : "La contrasena actual es incorrecta.";

        return RedirectToAction(nameof(Edit), new { id });
    }

    private ulong GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim is not null && ulong.TryParse(claim.Value, out var id) ? id : 0;
    }

    private async Task<IEnumerable<object>> GetEmpleadosSinUsuario()
    {
        var empleados = await _context.Empleados
            .AsNoTracking()
            .Where(e => e.UsuarioId == null && e.Activo)
            .OrderBy(e => e.Apellido)
            .ThenBy(e => e.Nombre)
            .Select(e => new { e.Id, NombreCompleto = e.Apellido + ", " + e.Nombre })
            .ToListAsync();

        return empleados;
    }
}
