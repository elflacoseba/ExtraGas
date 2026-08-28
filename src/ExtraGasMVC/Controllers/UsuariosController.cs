using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

[Authorize(Policy = "AdminOnly")]
public class UsuariosController : BaseController
{
    private readonly IUsuarioService _usuarioService;
    private readonly IPasswordPolicyService _passwordPolicy;

    public UsuariosController(IUsuarioService usuarioService, IPasswordPolicyService passwordPolicy)
    {
        _usuarioService = usuarioService;
        _passwordPolicy = passwordPolicy;
    }

    public async Task<IActionResult> Index(string? busqueda, ulong? rolId, bool soloActivos = false,
        int pagina = 1, int tamanio = 25, CancellationToken ct = default)
    {
        var resultado = await _usuarioService.SearchAsync(busqueda, rolId, soloActivos, pagina, tamanio, ct);

        ViewBag.Busqueda = busqueda;
        ViewBag.RolId = rolId;
        ViewBag.SoloActivos = soloActivos;
        ViewBag.Pagina = resultado.Pagina;
        ViewBag.Tamanio = resultado.Tamanio;
        ViewBag.Total = resultado.Total;

        return View(resultado.Items);
    }

    public async Task<IActionResult> Details(ulong id, CancellationToken ct = default)
    {
        var usuario = await _usuarioService.GetByIdAsync(id, ct);
        if (usuario is null) return NotFound();
        return View(usuario);
    }

    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        await LoadViewBagsAsync(ct);
        return View(new CreateUsuarioDto { Activo = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUsuarioDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.EmpleadosSinUsuario = await _usuarioService.GetEmpleadosSinUsuarioAsync(ct);
            return View(dto);
        }

        var policyResult = _passwordPolicy.Validate(dto.Password);
        if (!policyResult.IsValid)
        {
            foreach (var err in policyResult.Errors)
                ModelState.AddModelError(nameof(dto.Password), err);
            ViewBag.EmpleadosSinUsuario = await _usuarioService.GetEmpleadosSinUsuarioAsync(ct);
            return View(dto);
        }

        var existing = await _usuarioService.GetByUsernameAsync(dto.Username, ct);
        if (existing is not null)
        {
            ModelState.AddModelError("Username", "El nombre de usuario ya esta en uso.");
            ViewBag.EmpleadosSinUsuario = await _usuarioService.GetEmpleadosSinUsuarioAsync(ct);
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
            ViewBag.EmpleadosSinUsuario = await _usuarioService.GetEmpleadosSinUsuarioAsync(ct);
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
        ViewBag.Roles = await _usuarioService.GetRolesAsync(ct);
        ViewBag.CurrentUserId = GetCurrentUserId();
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
            ViewBag.Roles = await _usuarioService.GetRolesAsync(ct);
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
            ViewBag.Roles = await _usuarioService.GetRolesAsync(ct);
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
    public async Task<IActionResult> ResetPassword(ulong id, CancellationToken ct = default)
    {
        var currentUserId = GetCurrentUserId();
        if (id == currentUserId)
        {
            TempData["Error"] = "No puede resetear su propia contrasena. Use el formulario de cambio.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        try
        {
            var temporaryPassword = await _usuarioService.ResetPasswordAsync(id, currentUserId, ct);
            TempData["TemporaryPassword"] = temporaryPassword;
            TempData["TemporaryPasswordUsername"] = (await _usuarioService.GetByIdAsync(id, ct))?.Username;
            TempData["Success"] = "Contrasena reseteada. La password temporal se muestra debajo UNA SOLA VEZ.";
        }
        catch (KeyNotFoundException)
        {
            TempData["Error"] = "No se encontro el usuario.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"No se pudo resetear la contrasena: {ex.Message}";
        }

        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ulong id, ChangePasswordDto dto, CancellationToken ct = default)
    {
        dto.UsuarioId = id;

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Verifique los datos ingresados.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var policyResult = _passwordPolicy.Validate(dto.NewPassword);
        if (!policyResult.IsValid)
        {
            TempData["Error"] = string.Join(" ", policyResult.Errors);
            return RedirectToAction(nameof(Edit), new { id });
        }

        var ok = await _usuarioService.ChangePasswordAsync(id, dto.CurrentPassword, dto.NewPassword, ct);
        TempData[ok ? "Success" : "Error"] = ok
            ? "Contrasena cambiada correctamente."
            : "La contrasena actual es incorrecta.";

        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task LoadViewBagsAsync(CancellationToken ct = default)
    {
        ViewBag.Roles = await _usuarioService.GetRolesAsync(ct);
        ViewBag.EmpleadosSinUsuario = await _usuarioService.GetEmpleadosSinUsuarioAsync(ct);
    }
}
