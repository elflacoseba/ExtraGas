using ExtraGasMVC.Configuration;
using ExtraGasMVC.Controllers;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Reproduce el flujo exacto de ChangePassword: controller POST setea
/// TempData["Error"], ASP.NET persiste via SaveTempData, y un GET
/// siguiente lee el TempData persistido.
///
/// Si este test pasa, el TempData del ChangePassword funciona bien y el
/// bug del usuario debe estar en otro lado (render de la vista o JS).
/// </summary>
public class ChangePasswordTempDataFlowTests
{
    [Fact]
    public async Task WrongCurrentPassword_TempDataError_PersistsAcrossRequests()
    {
        var services = new ServiceCollection()
            .AddSingleton<ITempDataProvider, InMemoryTempDataProvider>()
            .AddSingleton<ITempDataDictionaryFactory, TempDataDictionaryFactory>()
            .AddSingleton<IUrlHelperFactory, UrlHelperFactory>()
            .BuildServiceProvider();

        var usuarioService = new FakeUsuarioService { ChangePasswordResult = false };
        var passwordPolicy = new FakePasswordPolicyService { Result = PasswordPolicyResult.Ok() };

        // ----- POST -----
        var postContext = new DefaultHttpContext { RequestServices = services };
        postContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "1") },
                "TestAuth"));
        var postController = new UsuariosController(usuarioService, passwordPolicy)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = postContext,
                RouteData = new RouteData()
            }
        };

        var dto = new ChangePasswordDto
        {
            CurrentPassword = "WRONG",
            NewPassword = "NewValid123!",
            ConfirmPassword = "NewValid123!"
        };
        var postResult = await postController.ChangePassword(1, dto, default);

        Assert.IsType<RedirectToActionResult>(postResult);

        // ASP.NET Core ejecuta SaveTempData via SaveTempDataFilter al final del POST.
        var postFactory = services.GetRequiredService<ITempDataDictionaryFactory>();
        var postTempData = postFactory.GetTempData(postContext);
        postTempData.Save();

        // ----- GET (nuevo HttpContext, mismo provider/servicios) -----
        var getContext = new DefaultHttpContext { RequestServices = services };
        getContext.User = postContext.User;
        var getFactory = services.GetRequiredService<ITempDataDictionaryFactory>();
        var getTempData = getFactory.GetTempData(getContext);

        // El partial _StatusMessage.cshtml leera este TempData["Error"] en el GET.
        Assert.True(getTempData.ContainsKey("Error"),
            "Tras el redirect, GET deberia ver TempData['Error']");
        Assert.Equal("La contrasena actual es incorrecta.", getTempData["Error"]);
    }

    private class FakeUsuarioService : IUsuarioService
    {
        public bool ChangePasswordResult { get; set; }
        public Task<bool> ChangePasswordAsync(ulong id, string currentPassword, string newPassword, CancellationToken ct = default)
            => Task.FromResult(ChangePasswordResult);
        public Task<UsuarioDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => Task.FromResult<UsuarioDto?>(null);
        public Task<SearchResultDto<UsuarioDto>> SearchAsync(string? b, ulong? r, bool s, int p, int t, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<RolDto>> GetRolesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<EmpleadoSinUsuarioDto>> GetEmpleadosSinUsuarioAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<UsuarioDto?> GetByUsernameAsync(string u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<UsuarioDto> CreateAsync(CreateUsuarioDto d, ulong? c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<UsuarioDto> UpdateAsync(UpdateUsuarioDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ChangePasswordWithoutCurrentAsync(ulong id, string n, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> ResetPasswordAsync(ulong id, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<LoginResult> ValidateAndLoadForAuthAsync(string u, string p, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private class FakePasswordPolicyService : IPasswordPolicyService
    {
        public PasswordPolicyResult Result { get; set; } = PasswordPolicyResult.Ok();
        public PasswordPolicyResult Validate(string? password) => Result;
    }

    private class InMemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> Store { get; } = new Dictionary<string, object?>();

        public IDictionary<string, object?> LoadTempData(HttpContext context) => Store;

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
            Store.Clear();
            foreach (var kv in values) Store[kv.Key] = kv.Value;
        }
    }
}