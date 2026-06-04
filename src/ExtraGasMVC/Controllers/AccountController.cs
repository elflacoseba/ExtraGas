using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

public class AccountController : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Login(string usuario, string password, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError(string.Empty, "Debe ingresar usuario y contrasena.");
            return View();
        }
        if (usuario == "admin" && password == "admin")
        {
            TempData["Success"] = "Sesion iniciada.";
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Home");
        }
        ModelState.AddModelError(string.Empty, "Usuario o contrasena invalidos.");
        return View();
    }

    [HttpGet]
    public IActionResult Logout()
    {
        TempData["Info"] = "Cerro sesion.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();
}
