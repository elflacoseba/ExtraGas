using ExtraGasMVC.Configuration;
using ExtraGasMVC.Controllers;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services;
using ExtraGasMVC.Services.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Test de regresión para el bug del combo de roles vacío en
/// <c>Usuarios/Create.cshtml</c> después de un error de validación.
///
/// Historia: cuando el POST a /Usuarios/Create fallaba por validación
/// (ej. contraseña corta) y el controller re-renderizaba la vista,
/// <c>ViewBag.Roles</c> quedaba en null porque solo se recargaba
/// <c>ViewBag.EmpleadosSinUsuario</c>. La vista bindea el combo con
/// <c>@if (roles != null)</c> así que el placeholder "-- Seleccionar rol --"
/// aparecía solo y el usuario no podía elegir ningún rol.
///
/// El fix fue centralizar la carga en <c>LoadViewBagsAsync(ct)</c> y llamarlo
/// en cada <c>return View(dto)</c> por error del POST.
///
/// Este archivo cubre las 3 ramas de error del POST (ModelState inválido,
/// password policy fallida, username duplicado) verificando que en todas
/// <c>ViewBag.Roles</c> queda poblado. Si alguien vuelve a olvidar poblar
/// el combo en alguna de las ramas, este test rompe.
/// </summary>
public sealed class UsuariosCreateViewBagTests
{
    private const string TestUserId = "1";

    // ---------- Rama 1: ModelState inválido ----------
    //
    // En un unit test el ModelBinding no corre, así que populamos ModelState
    // manualmente para simular que el binder rechazó algo (ej. un atributo
    // [Required] no cumplido). Esta es la rama `if (!ModelState.IsValid)` del
    // POST (líneas 52-56 del controller).
    [Fact]
    public async Task Create_PostWithInvalidModelState_RepopulatesRolesViewBag()
    {
        var controller = NewController(
            usuarioService: NewUsuarioService(roles: RolesEsperados),
            passwordPolicy: OkPolicy());

        controller.ModelState.AddModelError(
            nameof(CreateUsuarioDto.Username),
            "El campo Username es obligatorio.");

        var dto = ValidDto();

        var result = await controller.Create(dto, CancellationToken.None);

        result.Should().BeOfType<ViewResult>();
        AssertRolesViewBagEsperaLos(controller, RolesEsperados);
    }

    // ---------- Rama 2: password policy fallida ----------
    //
    // ModelState válido al inicio, pero el fake PasswordPolicy rechaza el
    // password. Cae en la rama de las líneas 58-65 del controller.
    [Fact]
    public async Task Create_PostWithPasswordPolicyFailure_RepopulatesRolesViewBag()
    {
        var controller = NewController(
            usuarioService: NewUsuarioService(roles: RolesEsperados),
            passwordPolicy: FailingPolicy());

        var dto = ValidDto();
        dto.Password = "abc"; // 3 chars → falla la password policy

        var result = await controller.Create(dto, CancellationToken.None);

        result.Should().BeOfType<ViewResult>();
        AssertRolesViewBagEsperaLos(controller, RolesEsperados);

        // Verificacion adicional: en esta rama el controller debe haber
        // registrado el error de la password policy en ModelState.
        controller.ModelState.Should().ContainKey(nameof(CreateUsuarioDto.Password));
    }

    // ---------- Rama 3: username duplicado ----------
    //
    // ModelState válido y password OK, pero el username ya existe. Cae en la
    // rama de las líneas 67-73 del controller.
    [Fact]
    public async Task Create_PostWithDuplicateUsername_RepopulatesRolesViewBag()
    {
        var controller = NewController(
            usuarioService: NewUsuarioService(roles: RolesEsperados, usernameExists: true),
            passwordPolicy: OkPolicy());

        var dto = ValidDto();

        var result = await controller.Create(dto, CancellationToken.None);

        result.Should().BeOfType<ViewResult>();
        AssertRolesViewBagEsperaLos(controller, RolesEsperados);

        // Verificacion adicional: en esta rama el controller debe haber
        // registrado el error de username duplicado.
        controller.ModelState.Should().ContainKey(nameof(CreateUsuarioDto.Username));
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static readonly List<RolDto> RolesEsperados = new()
    {
        new() { Id = 1, Nombre = "ADMIN" },
        new() { Id = 2, Nombre = "OPERADOR" },
        new() { Id = 3, Nombre = "CONSULTA" },
    };

    private static void AssertRolesViewBagEsperaLos(UsuariosController controller, List<RolDto> expected)
    {
        var rolesEnViewBag = controller.ViewBag.Roles as List<RolDto>;

        rolesEnViewBag.Should().NotBeNull(
            "ViewBag.Roles no puede ser null despues de un error de validacion; " +
            "de lo contrario el <select> de Roles queda solo con el placeholder " +
            "y el usuario no puede elegir un rol (regresion del bug original).");

        rolesEnViewBag.Should().BeEquivalentTo(expected,
            options => options.WithStrictOrdering(),
            "los roles deben coincidir con los que devuelve el service");

        var empleadosEnViewBag = controller.ViewBag.EmpleadosSinUsuario as List<EmpleadoSinUsuarioDto>;
        empleadosEnViewBag.Should().NotBeNull(
            "ViewBag.EmpleadosSinUsuario tampoco puede quedar null (ya estaba bien antes del bug).");
    }

    private static CreateUsuarioDto ValidDto() => new()
    {
        Username = "nuevo.test",
        Email = "nuevo@test.local",
        RolId = 1,
        Password = "Valida123!",
        // Issue #114: Activo ya no se setea desde el DTO — lo hace el Service.
    };

    private static FakeUsuarioService NewUsuarioService(
        List<RolDto> roles,
        bool usernameExists = false) => new()
    {
        Roles = roles,
        UsernameExists = usernameExists,
    };

    private static FakePasswordPolicyService OkPolicy() =>
        new() { Result = PasswordPolicyResult.Ok() };

    private static FakePasswordPolicyService FailingPolicy() =>
        new() { Result = PasswordPolicyResult.Fail("La contrasena debe tener al menos 6 caracteres.") };

    private static UsuariosController NewController(
        IUsuarioService usuarioService,
        IPasswordPolicyService passwordPolicy)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    new[]
                    {
                        new System.Security.Claims.Claim(
                            System.Security.Claims.ClaimTypes.NameIdentifier, TestUserId),
                    },
                    "TestAuth")),
        };

        return new UsuariosController(usuarioService, passwordPolicy)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData(),
            },
        };
    }

    // ====================================================================
    // Fakes (mismo patron que ChangePasswordTempDataFlowTests)
    // ====================================================================

    /// <summary>
    /// Fake de IUsuarioService que devuelve las listas controladas que se le
    /// configuren. Solo implementa los metodos usados por el Create del
    /// controller; el resto tira NotImplementedException para detectar
    /// regresiones de wiring.
    /// </summary>
    private sealed class FakeUsuarioService : IUsuarioService
    {
        public List<RolDto> Roles { get; init; } = new();
        public List<EmpleadoSinUsuarioDto> EmpleadosSinUsuario { get; init; } = new();
        public bool UsernameExists { get; init; }

        public Task<List<RolDto>> GetRolesAsync(CancellationToken ct = default)
            => Task.FromResult(Roles);

        public Task<List<EmpleadoSinUsuarioDto>> GetEmpleadosSinUsuarioAsync(CancellationToken ct = default)
            => Task.FromResult(EmpleadosSinUsuario);

        public Task<UsuarioDto?> GetByUsernameAsync(string username, CancellationToken ct = default)
            => Task.FromResult<UsuarioDto?>(UsernameExists ? new UsuarioDto { Username = username } : null);

        public Task<UsuarioDto> CreateAsync(CreateUsuarioDto d, ulong? c, CancellationToken ct = default)
            => Task.FromResult(new UsuarioDto { Id = 99, Username = d.Username });

        public Task<UsuarioDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SearchResultDto<UsuarioDto>> SearchAsync(string? b, ulong? r, bool s, int p, int t, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<UsuarioDto> UpdateAsync(UpdateUsuarioDto d, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ChangePasswordAsync(ulong id, string cp, string np, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ChangePasswordWithoutCurrentAsync(ulong id, string n, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> ResetPasswordAsync(ulong id, ulong? u, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<LoginResult> ValidateAndLoadForAuthAsync(string u, string p, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RequestPasswordResetAsync(string e, string? ip, string? ua, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ConsumeResetTokenResult> ConsumePasswordResetTokenAsync(string t, string p, CancellationToken ct = default) => throw new NotImplementedException();
    }

    /// <summary>
    /// Fake de IPasswordPolicyService con un Result configurable.
    /// </summary>
    private sealed class FakePasswordPolicyService : IPasswordPolicyService
    {
        public PasswordPolicyResult Result { get; init; } = PasswordPolicyResult.Ok();
        public PasswordPolicyResult Validate(string? password) => Result;
    }
}
