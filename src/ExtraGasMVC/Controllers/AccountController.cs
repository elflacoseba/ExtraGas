using System.Security.Claims;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

public class AccountController : BaseController
{
    private readonly IUsuarioService _usuarioService;
    private readonly IAuditoriaLoginService _auditoriaService;
    private readonly IPasswordPolicyService _passwordPolicy;

    public AccountController(
        IUsuarioService usuarioService,
        IAuditoriaLoginService auditoriaService,
        IPasswordPolicyService passwordPolicy)
    {
        _usuarioService = usuarioService;
        _auditoriaService = auditoriaService;
        _passwordPolicy = passwordPolicy;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string usuario, string password, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError(string.Empty, "Debe ingresar usuario y contrasena.");
            return View();
        }

        var loginResult = await _usuarioService.ValidateAndLoadForAuthAsync(usuario, password);

        // Registrar el intento en auditoria (siempre, exista o no el usuario).
        await _auditoriaService.RecordAsync(
            usernameIntentado: usuario,
            usuarioId: loginResult.User?.Id,
            exito: loginResult.Success,
            motivoFallo: loginResult.FailureReason,
            ipOrigen: GetClientIp(),
            userAgent: GetUserAgent(),
            ct: default);

        if (!loginResult.Success)
        {
            ModelState.AddModelError(string.Empty, MapLoginFailureToMessage(loginResult.FailureReason));
            return View();
        }

        var userDto = loginResult.User!;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userDto.Id.ToString()),
            new(ClaimTypes.Name, userDto.Username),
            new(ClaimTypes.Role, userDto.RolCodigo ?? string.Empty),
        };

        if (!string.IsNullOrEmpty(userDto.RolNombre))
            claims.Add(new Claim("RoleName", userDto.RolNombre));

        if (!string.IsNullOrEmpty(userDto.Email))
            claims.Add(new Claim(ClaimTypes.Email, userDto.Email));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        TempData["Success"] = "Sesion iniciada.";

        // Forzar cambio de password si fue marcado por un reset admin-assisted.
        if (userDto.DebeCambiarPassword)
        {
            TempData["Warning"] = "Tu password fue reseteada por un administrador. Debes cambiarla antes de continuar.";
            return RedirectToAction(nameof(ChangePassword));
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword()
    {
        return View(new AccountChangePasswordDto());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> ChangePassword(AccountChangePasswordDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return View(dto);

        var policyResult = _passwordPolicy.Validate(dto.NewPassword);
        if (!policyResult.IsValid)
        {
            foreach (var err in policyResult.Errors)
                ModelState.AddModelError(nameof(dto.NewPassword), err);
            return View(dto);
        }

        var userId = GetCurrentUserId();
        if (userId is null)
            return RedirectToAction(nameof(Logout));

        try
        {
            await _usuarioService.ChangePasswordWithoutCurrentAsync(userId.Value, dto.NewPassword, ct);
            TempData["Success"] = "Contrasena actualizada correctamente.";

            // Re-issue el cookie claim para que el flag refreshed no quede inconsistente.
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo actualizar la contrasena: {ex.Message}");
            return View(dto);
        }
    }

    private static string MapLoginFailureToMessage(LoginFailureReason reason) => reason switch
    {
        LoginFailureReason.LockedOut =>
            "Cuenta bloqueada temporalmente por demasiados intentos fallidos. Intenta nuevamente en unos minutos.",
        LoginFailureReason.UserInactive =>
            "El usuario esta inactivo. Contacta a un administrador.",
        // UserNotFound, UserDeleted, InvalidPassword: mensaje generico para no delatar
        // si el usuario existe.
        _ => "Usuario o contrasena invalidos."
    };

    private string? GetClientIp()
    {
        // Connection.RemoteIpAddress puede ser null detras de ciertos proxies;
        // en produccion se suele complementar con X-Forwarded-For.
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private string? GetUserAgent()
    {
        var ua = HttpContext.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(ua) ? null : (ua.Length > 255 ? ua[..255] : ua);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();
}
