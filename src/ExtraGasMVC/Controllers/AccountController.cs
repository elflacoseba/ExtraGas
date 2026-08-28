using System.Security.Claims;
using ExtraGasMVC.Services;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    private readonly IUsuarioService _usuarioService;

    public AccountController(IUsuarioService usuarioService)
    {
        _usuarioService = usuarioService;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string usuario, string password, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError(string.Empty, "Debe ingresar usuario y contrasena.");
            return View();
        }

        var loginResult = await _usuarioService.ValidateAndLoadForAuthAsync(usuario, password);
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

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home");
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();
}
