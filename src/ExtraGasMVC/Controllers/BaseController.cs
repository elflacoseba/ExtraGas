using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace ExtraGasMVC.Controllers;

public abstract class BaseController : Controller
{
    protected ulong? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim is not null && ulong.TryParse(claim.Value, out var id) ? id : null;
    }
}
