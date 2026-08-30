using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MySqlConnector;
using Testcontainers.MySql;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de integración del módulo Clientes contra un MySQL real dentro de
/// un container Docker (Testcontainers.MySql). Complementan los tests sobre
/// InMemoryDatabase de <see cref="ClienteServiceTests"/>, que NO ejecuta
/// constraints ni triggers.
///
/// Issue #112: el módulo Clientes tiene 3 garantías que SOLO se pueden
/// verificar contra MySQL real:
///   - Issue #105: el UNIQUE INDEX sobre la columna VIRTUAL <c>dni_unique</c>
///     rechaza DNI duplicados entre activos y libera el DNI tras soft-delete.
///   - Issue #107: el errno 1062 de MySQL (no el de InMemory) es el que el
///     Service mapea a InvalidOperationException. Aquí verificamos el camino
///     completo con la BD detrás.
///   - Normalización # #113: el valor canónico guardado es el que el UNIQUE
///     INDEX evalúa.
///
/// Patrón: IClassFixture comparte el container entre los 3 tests (los
/// containers son caros de arrancar). Cada test crea su propia base y aplica
/// el schema mínimo para tener aislamiento.
/// </summary>
public class ClienteIntegrationTests : IClassFixture<ClienteMySqlFixture>
{
    private readonly ClienteMySqlFixture _fixture;

    public ClienteIntegrationTests(ClienteMySqlFixture fixture)
    {
        _fixture = fixture;
    }

    // ====================================================================
    // Tests
    // ====================================================================

    [Fact]
    public async Task CreateAsync_DniDuplicado_LanzaInvalidOperationException_PorUnicoIndiceReal()
    {
        // Issue #107 end-to-end: dos clientes activos con el mismo DNI.
        // El primero pasa el check previo Y el INSERT. El segundo pasa el
        // check previo (en condiciones de carrera reales es "best-effort")
        // pero la BD real lo rechaza con errno 1062.
        var ctx = await _fixture.NewDbContextAsync(nameof(CreateAsync_DniDuplicado_LanzaInvalidOperationException_PorUnicoIndiceReal));
        var service = NewService(ctx);

        await service.CreateAsync(NewDto("12345678"), createdBy: 1);

        var act = async () => await service.CreateAsync(NewDto("12345678"), createdBy: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("El DNI ingresado ya está registrado.",
                "el mapeo de errno 1062 a este mensaje es lo que blinda la UX");
    }

    [Fact]
    public async Task DeleteAsync_LuegoRestoreAsync_LiberaDniParaReRegistro_PorColumnaVirtualDniUnique()
    {
        // Issue #105 end-to-end: tras soft-delete, el UNIQUE INDEX permite
        // re-registrar el mismo DNI porque dni_unique = NULL cuando
        // deleted_at IS NOT NULL. Sin la columna virtual, el índice
        // original rechazaría el segundo INSERT.
        var ctx = await _fixture.NewDbContextAsync(nameof(DeleteAsync_LuegoRestoreAsync_LiberaDniParaReRegistro_PorColumnaVirtualDniUnique));
        var service = NewService(ctx);

        var primero = await service.CreateAsync(NewDto("12345678"), createdBy: 1);
        await service.DeleteAsync(primero.Id, updatedBy: 1);

        // Ahora puedo re-registrar el mismo DNI: la columna virtual
        // dni_unique del soft-deleted es NULL, así que el índice único no
        // choca.
        var segundo = await service.CreateAsync(NewDto("12345678"), createdBy: 1);
        segundo.Id.Should().NotBe(primero.Id);

        // La BD debe tener 2 filas con DNI '12345678', una activa y otra
        // soft-deleted. Lo verificamos a nivel BD (no a través del Service,
        // porque el QueryFilter global oculta la soft-deleted).
        var todasLasFilas = await ctx.Clientes.IgnoreQueryFilters()
            .Where(c => c.Dni == "12345678")
            .ToListAsync();
        todasLasFilas.Should().HaveCount(2);
        todasLasFilas.Count(c => c.DeletedAt == null).Should().Be(1, "la activa");
        todasLasFilas.Count(c => c.DeletedAt != null).Should().Be(1, "la soft-deleted");
    }

    [Fact]
    public async Task CreateAsync_DniConSeparadores_GuardaFormaNormalizadaYUnicoIndiceEvaluaSobreCanonico()
    {
        // Issue #113 end-to-end: si el operador tipea "12.345.678", el Service
        // normaliza a "12345678" antes del INSERT. El UNIQUE INDEX evalúa
        // sobre el valor canónico guardado, así que un intento posterior con
        // cualquier variante debe ser rechazado.
        var ctx = await _fixture.NewDbContextAsync(nameof(CreateAsync_DniConSeparadores_GuardaFormaNormalizadaYUnicoIndiceEvaluaSobreCanonico));
        var service = NewService(ctx);

        var primero = await service.CreateAsync(NewDto("12.345.678"), createdBy: 1);
        primero.Dni.Should().Be("12345678", "CreateAsync normaliza antes de persistir");

        // Segundo INSERT con DNI en formato crudo (mismo canónico, distinto
        // separador) debe chocar con el UNIQUE INDEX.
        var act = async () => await service.CreateAsync(NewDto("  12-345-678 "), createdBy: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("El DNI ingresado ya está registrado.");
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static ClienteService NewService(ExtraGasDbContext context)
    {
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new ClienteService(context, mapper, cache);
    }

    private static CreateClienteDto NewDto(string dni) => new()
    {
        Nombre = "Juan",
        Apellido = "Perez",
        Dni = dni,
        TelefonoPrincipal = "1144556677",
    };
}

/// <summary>
/// Fixture xUnit que arranca un container MySQL via Testcontainers y provee
/// un método para crear bases frescas con el schema mínimo del módulo
/// Clientes. Se comparte entre los tests de <see cref="ClienteIntegrationTests"/>
/// (IClassFixture) — los containers tardan segundos en arrancar, no vale
/// la pena pagar ese costo por test.
///
/// Requiere Docker daemon accesible. Si el CI no tiene Docker, los tests se
/// pueden saltar con un filtro de xUnit a nivel de pipeline
/// (ej. <c>dotnet test --filter "FullyQualifiedName!~ClienteIntegrationTests"</c>).
/// </summary>
public class ClienteMySqlFixture : IAsyncLifetime
{
    private const string MysqlImage = "mysql:8.0";
    private const string RootPassword = "test_root_pwd";
    private const string RootUsername = "root";
    // MySQL limita identificadores a 64 chars. Prefix corto + Guid.NewGuid().ToString("N")
    // (32 chars hex) = 2 + 32 = 34 chars, lejos del límite.
    private const string DatabasePrefix = "ec_";

    private MySqlContainer? _container;
    private string? _rootConnectionString;

    public async Task InitializeAsync()
    {
        _container = new MySqlBuilder()
            .WithImage(MysqlImage)
            .WithUsername(RootUsername)
            .WithPassword(RootPassword)
            .WithDatabase("placeholder_db")
            .Build();
        await _container.StartAsync();
        _rootConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>
    /// Crea una base nueva con nombre único (prefix + GUID) y aplica el
    /// schema mínimo necesario para que <see cref="ClienteService"/>
    /// funcione (tabla <c>clientes</c> con columna VIRTUAL <c>dni_unique</c>
    /// + UNIQUE INDEX, como la migración 20260829_000001). Devuelve un
    /// DbContext listo para usar contra esa base.
    ///
    /// El parámetro <paramref name="testName"/> se ignora para nombres de
    /// BD (usamos GUID para unicidad y para mantener el nombre corto).
    /// Lo conservamos en la firma para que los mensajes de test reporten
    /// qué intentaron hacer.
    /// </summary>
    public async Task<ExtraGasDbContext> NewDbContextAsync(string testName)
    {
        _ = testName; // reservado para logging futuro; el nombre es GUID.
        var dbName = DatabasePrefix + Guid.NewGuid().ToString("N");

        // 1) Crear la base vía root connection.
        await using (var conn = new MySqlConnection(_rootConnectionString))
        {
            await conn.OpenAsync();
            await using var create = conn.CreateCommand();
            create.CommandText = $"CREATE DATABASE `{dbName}` " +
                                 "CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await create.ExecuteNonQueryAsync();
        }

        // 2) Conectar a la nueva base y aplicar el schema mínimo.
        var connString = _rootConnectionString!
            .Replace("database=placeholder_db", $"database={dbName}", StringComparison.OrdinalIgnoreCase);

        await using (var conn = new MySqlConnection(connString))
        {
            await conn.OpenAsync();
            await using var schema = conn.CreateCommand();
            schema.CommandText = ClientesSchemaMinimal;
            await schema.ExecuteNonQueryAsync();
        }

        // 3) Armar DbContext con Pomelo contra esa base.
        var serverVersion = ServerVersion.Create(new Version(8, 0, 0), Pomelo.EntityFrameworkCore.MySql.Infrastructure.ServerType.MySql);
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseMySql(connString, serverVersion)
            .Options;
        return new ExtraGasDbContext(options);
    }

    /// <summary>
    /// Schema mínimo para que el módulo Clientes funcione contra MySQL
    /// real. NO incluye tablas relacionadas (provincias, usuarios) porque
    /// los FKs son nullable y los tests no las ejercitan. Reproduce el
    /// patrón de la migración 20260829_000001: columna VIRTUAL
    /// <c>dni_unique</c> + UNIQUE INDEX que libera el DNI tras soft-delete.
    /// </summary>
    private const string ClientesSchemaMinimal = """
        CREATE TABLE IF NOT EXISTS clientes (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            codigo VARCHAR(20) NULL,
            nombre VARCHAR(100) NOT NULL,
            apellido VARCHAR(100) NOT NULL,
            dni VARCHAR(15) NULL,
            cuit_cuil VARCHAR(15) NULL,
            telefono_principal VARCHAR(25) NOT NULL,
            telefono_secundario VARCHAR(25) NULL,
            email VARCHAR(150) NULL,
            calle VARCHAR(150) NULL,
            numero VARCHAR(10) NULL,
            piso VARCHAR(10) NULL,
            depto VARCHAR(10) NULL,
            ciudad VARCHAR(100) NULL,
            codigo_postal VARCHAR(10) NULL,
            provincia_id BIGINT UNSIGNED NULL,
            referencias TEXT NULL,
            observaciones TEXT NULL,
            fecha_alta DATE NOT NULL,
            activo TINYINT(1) NOT NULL DEFAULT 1,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            created_by BIGINT UNSIGNED NULL,
            updated_by BIGINT UNSIGNED NULL,
            deleted_at DATETIME NULL,
            dni_unique VARCHAR(15) GENERATED ALWAYS AS (
                CASE WHEN deleted_at IS NULL THEN dni ELSE NULL END
            ) VIRTUAL,
            UNIQUE KEY idx_clientes_dni_unique (dni_unique),
            KEY idx_clientes_apellido (apellido, nombre),
            KEY idx_clientes_telefono (telefono_principal),
            KEY idx_clientes_dni_lookup (dni),
            KEY idx_clientes_codigo (codigo),
            KEY idx_clientes_deleted_at (deleted_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        """;
}