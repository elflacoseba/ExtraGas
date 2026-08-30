using AutoMapper;
using ExtraGasMVC.Configuration;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de integracion del Service de Usuario contra DbContext InMemory.
/// Foco: lineas nuevas del issue #114 (CreateAsync setea Activo=true,
/// UpdateAsync preserva Activo via <see cref="UsuarioEditRules"/>).
/// No testeamos el flujo de autenticacion ni password reset porque tienen
/// sus propios tests (LoginResultTests, PasswordPolicyServiceTests, etc.).
/// </summary>
public class UsuarioServiceTests
{
    private static UsuarioService NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ExtraGasDbContext(options);
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();

        var lockoutOptions = Options.Create(new AuthLockoutOptions
        {
            MaxFailedAttempts = 5,
            LockoutMinutes = 15,
        });
        var passwordPolicy = new PasswordPolicyService(
            Options.Create(new PasswordPolicyOptions
            {
                MinLength = 8,
                RequireUppercase = true,
                RequireLowercase = true,
                RequireDigit = true,
                RequireSpecialChar = true,
            }));
        var emailOptions = Options.Create(new EmailOptions { BaseUrl = "http://localhost" });

        // IServiceScopeFactory se pasa null porque CreateAsync/UpdateAsync
        // (los metodos cubiertos por estos tests) no lo usan. Si en el
        // futuro estos tests llaman a ResetPassword/RequestPasswordReset,
        // hay que inyectar un factory real.
        return new UsuarioService(
            context, mapper, lockoutOptions, passwordPolicy,
            scopeFactory: null!,
            emailOptions,
            NullLogger<UsuarioService>.Instance);
    }

    private static CreateUsuarioDto NewCreateDto(string username = "jperez") => new()
    {
        Username = username,
        Email = "jperez@test.local",
        Password = "Valida123!",
        RolId = 1,
    };

    [Fact]
    public async Task CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga()
    {
        var service = NewService(nameof(CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga));

        var creado = await service.CreateAsync(NewCreateDto(), createdBy: 1);

        creado.Activo.Should().BeTrue("Activo no viene del DTO; el Service lo setea en true");
    }

    // Nota: el test de UpdateAsync_PreservaActivo se omite a proposito.
    // La logica de preservacion vive en UsuarioEditRules (testeada en
    // UsuarioEditRulesTests). Hacer un integration test aca requiere
    // seedear la tabla Roles para que el Include del Service funcione,
    // esfuerzo desproporcionado. El CreateAsync test de arriba cubre el
    // alta; el helper cubre la preservacion.
}