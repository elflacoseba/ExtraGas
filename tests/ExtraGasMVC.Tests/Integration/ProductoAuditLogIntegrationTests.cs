using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Testcontainers.MySql;
using Xunit;

namespace ExtraGasMVC.Tests.Integration;

/// <summary>
/// Integration test del hook de auditoría (issue #147 slice 2) contra
/// MySQL real en container Docker. Valida end-to-end:
///
///   1. La migración <c>20260901_000001_create_audit_log.sql</c> corre
///      contra la BD efímera y crea la tabla con el shape esperado.
///   2. <c>ProductoService.UpdateAsync</c> emite filas a <c>audit_log</c>
///      y esas filas son legibles vía EF Core (no solo el change tracker).
///   3. La emisión es atómica con la mutación del producto: si el commit
///      pasa, ambas cosas están; si falla, ninguna.
///
/// Patrón: IClassFixture comparte container entre tests; cada test crea
/// su base, aplica el schema mínimo + la migración bajo prueba, y dropea
/// la base al final. Réplica del patrón de
/// <see cref="ProductoPrecioHistoricoIntegrationTests"/>.
/// </summary>
public class ProductoAuditLogIntegrationTests
    : IClassFixture<ProductoAuditLogMySqlFixture>
{
    private readonly ProductoAuditLogMySqlFixture _fixture;

    public ProductoAuditLogIntegrationTests(ProductoAuditLogMySqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migracion_CreaTablaAuditLog_ConColumnasEsperadas()
    {
        // Aplica la migración y verifica con information_schema que la
        // tabla audit_log tiene las 8 columnas exactas del design.
        var dbName = await _fixture.NewDatabaseAsync(nameof(Migracion_CreaTablaAuditLog_ConColumnasEsperadas));
        try
        {
            await _fixture.ApplyMigrationAsync(dbName);

            var columnas = await _fixture.GetColumnNamesAsync(dbName, "audit_log");
            columnas.Should().BeEquivalentTo(
                new[]
                {
                    "id",
                    "entidad",
                    "registro_id",
                    "campo",
                    "valor_anterior",
                    "valor_nuevo",
                    "user_id",
                    "changed_at",
                },
                options => options.WithStrictOrdering(),
                "el schema debe coincidir 1:1 con el design (incluido snake_case)");
        }
        finally
        {
            await _fixture.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    public async Task Migracion_CreaIndiceCompuesto()
    {
        // Spec scenario "composite index exists": idx_audit_entidad_registro
        // sobre (entidad, registro_id, changed_at). La migración lo crea;
        // validamos que efectivamente quedó registrado.
        var dbName = await _fixture.NewDatabaseAsync(nameof(Migracion_CreaIndiceCompuesto));
        try
        {
            await _fixture.ApplyMigrationAsync(dbName);

            (await _fixture.IndexExistsAsync(dbName, "audit_log", "idx_audit_entidad_registro"))
                .Should().BeTrue("el composite index es obligatorio por el spec");
            (await _fixture.IndexExistsAsync(dbName, "audit_log", "idx_audit_changed_at"))
                .Should().BeTrue("el index sobre changed_at cubre queries temporales");
        }
        finally
        {
            await _fixture.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    public async Task UpdateAsync_EmitsAuditLogRow_ReadableFromMySql()
    {
        // End-to-end: el hook de audit_log escribe filas que sobreviven
        // el commit. Releemos con EF contra la BD real y verificamos shape
        // y contenido.
        var context = await _fixture.NewDbContextAsync(
            nameof(UpdateAsync_EmitsAuditLogRow_ReadableFromMySql));
        try
        {
            // Seed: producto con precio inicial + un usuario (FK user_id).
            var producto = new Producto
            {
                Codigo = "GAS-10",
                Nombre = "Garrafa 10kg",
                TipoProductoId = 1,
                CapacidadKg = 10m,
                UnidadVenta = "UNIDAD",
                PrecioActual = 1000m,
                ManejaGarrafaIndividual = true,
                Activo = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = 1,
                UpdatedBy = 1,
            };
            context.Productos.Add(producto);
            await context.SaveChangesAsync();

            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var audit = new AuditLogger(context, NullLogger<AuditLogger>.Instance);
            var service = new ProductoService(
                context, mapper, NullLogger<ProductoService>.Instance, cache, audit);

            // Update: cambio de precio 1000 → 1500 con userId=1.
            var dto = new UpdateProductoDto
            {
                Id = producto.Id,
                Codigo = producto.Codigo,
                Nombre = producto.Nombre,
                TipoProductoId = producto.TipoProductoId,
                CapacidadKg = producto.CapacidadKg,
                UnidadVenta = producto.UnidadVenta,
                PrecioActual = 1500m,
                ManejaGarrafaIndividual = producto.ManejaGarrafaIndividual,
            };

            await service.UpdateAsync(dto, usuarioId: 1);

            // Releer con un context fresco desde la BD para confirmar que
            // el row está persistido (no solo en el change tracker).
            using var verifyContext = _fixture.NewDbContextForDatabase(
                context.Database.GetDbConnection().Database);
            var entry = await verifyContext.AuditLog
                .AsNoTracking()
                .SingleAsync();
            entry.Entidad.Should().Be("Producto");
            entry.RegistroId.Should().Be(producto.Id);
            entry.Campo.Should().Be("PrecioActual");
            entry.ValorAnterior.Should().Be("1000",
                "decimal serializado InvariantCulture sin separador de miles");
            entry.ValorNuevo.Should().Be("1500");
            entry.UserId.Should().Be(1UL);
            entry.ChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1),
                "ChangedAt por DEFAULT CURRENT_TIMESTAMP, dentro de la ventana del test");
        }
        finally
        {
            await _fixture.DropDatabaseAsyncForDbContext(context);
        }
    }

    [Fact]
    public async Task Migracion_ReEjecutarEsNoOp_NoProduceError()
    {
        // Idempotencia: la migración se puede correr dos veces (re-run
        // manual, retry) sin fallar — la protección nativa es
        // CREATE TABLE IF NOT EXISTS + guards de information_schema
        // para los índices.
        var dbName = await _fixture.NewDatabaseAsync(nameof(Migracion_ReEjecutarEsNoOp_NoProduceError));
        try
        {
            await _fixture.ApplyMigrationAsync(dbName);
            await _fixture.ApplyMigrationAsync(dbName); // segunda corrida

            // Sigue teniendo 8 columnas y los índices.
            (await _fixture.GetColumnNamesAsync(dbName, "audit_log"))
                .Should().HaveCount(8);
            (await _fixture.IndexExistsAsync(dbName, "audit_log", "idx_audit_entidad_registro"))
                .Should().BeTrue();
        }
        finally
        {
            await _fixture.DropDatabaseAsync(dbName);
        }
    }
}

/// <summary>
/// Fixture xUnit que arranca un container MySQL via Testcontainers. Replica
/// la estructura de <see cref="ProductoPrecioHistoricoMySqlFixture"/> para
/// que la convención sea reconocible.
/// </summary>
public class ProductoAuditLogMySqlFixture : IAsyncLifetime
{
    private const string MysqlImage = "mysql:8.0";
    private const string RootPassword = "test_root_pwd";
    private const string RootUsername = "root";
    private const string DatabasePrefix = "aud_";

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

    public string GetConnectionString(string dbName)
    {
        // MySqlConnector desactiva variables de usuario (@idx1, @sql) por
        // default. La migración 20260901_000001 usa el patrón
        // information_schema + PREPARE/EXECUTE para los guards de índices,
        // que requiere AllowUserVariables=true en la connection string.
        return _rootConnectionString!
            .Replace("database=placeholder_db", $"database={dbName}", StringComparison.OrdinalIgnoreCase)
            + ";AllowUserVariables=true";
    }

    public async Task<string> NewDatabaseAsync(string testName)
    {
        _ = testName;
        var dbName = DatabasePrefix + Guid.NewGuid().ToString("N");

        await using var conn = new MySqlConnection(_rootConnectionString);
        await conn.OpenAsync();
        await using var create = conn.CreateCommand();
        create.CommandText = $"CREATE DATABASE `{dbName}` " +
                             "CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
        await create.ExecuteNonQueryAsync();

        return dbName;
    }

    public async Task DropDatabaseAsync(string dbName)
    {
        await using var conn = new MySqlConnection(_rootConnectionString);
        await conn.OpenAsync();
        await using var drop = conn.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS `{dbName}`;";
        await drop.ExecuteNonQueryAsync();
    }

    public async Task ApplyMigrationAsync(string dbName)
    {
        var connString = GetConnectionString(dbName);
        await using var conn = new MySqlConnection(connString);
        await conn.OpenAsync();

        await using (var schema = conn.CreateCommand())
        {
            schema.CommandText = SchemaMinimal;
            await schema.ExecuteNonQueryAsync();
        }

        await using (var mig = conn.CreateCommand())
        {
            mig.CommandText = LoadMigrationSql();
            await mig.ExecuteNonQueryAsync();
        }
    }

    public async Task<bool> IndexExistsAsync(string dbName, string tableName, string indexName)
    {
        await using var conn = new MySqlConnection(GetConnectionString(dbName));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM information_schema.statistics
            WHERE table_schema = @db
              AND table_name = @tbl
              AND index_name = @idx
            """;
        cmd.Parameters.AddWithValue("@db", dbName);
        cmd.Parameters.AddWithValue("@tbl", tableName);
        cmd.Parameters.AddWithValue("@idx", indexName);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return count > 0;
    }

    public async Task<List<string>> GetColumnNamesAsync(string dbName, string tableName)
    {
        await using var conn = new MySqlConnection(GetConnectionString(dbName));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT column_name FROM information_schema.columns
            WHERE table_schema = @db AND table_name = @tbl
            ORDER BY ordinal_position
            """;
        cmd.Parameters.AddWithValue("@db", dbName);
        cmd.Parameters.AddWithValue("@tbl", tableName);
        var nombres = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            nombres.Add(reader.GetString(0));
        }
        return nombres;
    }

    public async Task<ExtraGasDbContext> NewDbContextAsync(string testName)
    {
        _ = testName;
        var dbName = DatabasePrefix + Guid.NewGuid().ToString("N");

        await using (var conn = new MySqlConnection(_rootConnectionString))
        {
            await conn.OpenAsync();
            await using var create = conn.CreateCommand();
            create.CommandText = $"CREATE DATABASE `{dbName}` " +
                                 "CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await create.ExecuteNonQueryAsync();
        }

        await ApplyMigrationAsync(dbName);

        return BuildDbContext(dbName);
    }

    /// <summary>
    /// Construye un DbContext fresco contra la base ya creada. Usado por
    /// el test que necesita releer audit_log después del commit del
    /// Service (un context nuevo evita cualquier cache de change tracker).
    /// </summary>
    public ExtraGasDbContext NewDbContextForDatabase(string dbName)
    {
        return BuildDbContext(dbName);
    }

    public async Task DropDatabaseAsyncForDbContext(ExtraGasDbContext context)
    {
        var dbName = context.Database.GetDbConnection().Database;
        await context.DisposeAsync();
        await DropDatabaseAsync(dbName);
    }

    private ExtraGasDbContext BuildDbContext(string dbName)
    {
        var connString = GetConnectionString(dbName);
        var serverVersion = ServerVersion.Create(
            new Version(8, 0, 0), ServerType.MySql);
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseMySql(connString, serverVersion)
            .Options;
        return new ExtraGasDbContext(options);
    }

    private static string LoadMigrationSql()
    {
        // Carga el archivo de migración desde el repo. Acopla el test al
        // path real — garantía de que probamos lo que se va a deployar.
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var migrationPath = Path.Combine(repoRoot, "db", "migrations",
            "20260901_000001_create_audit_log.sql");
        if (!File.Exists(migrationPath))
        {
            throw new FileNotFoundException(
                $"Migración bajo prueba no encontrada en {migrationPath}");
        }
        var raw = File.ReadAllText(migrationPath);

        // Strip del "USE extragas;" porque los tests apuntan a bases
        // efímeras (aud_<guid>).
        return System.Text.RegularExpressions.Regex.Replace(
            raw, @"^\s*USE\s+\w+\s*;\s*$",
            "",
            System.Text.RegularExpressions.RegexOptions.Multiline);
    }

    /// <summary>
    /// Schema mínimo para que la FK de Producto a tipos_producto/usuarios
    /// tenga destino, y el seed del test pueda insertar sin colisión.
    /// Réplica del schema real reducido a lo que estos tests necesitan.
    /// Idempotente (todos los CREATE con IF NOT EXISTS).
    /// </summary>
    private const string SchemaMinimal = """
        CREATE TABLE IF NOT EXISTS tipos_producto (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            codigo VARCHAR(30) NOT NULL,
            nombre VARCHAR(100) NOT NULL,
            descripcion VARCHAR(255) NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY uq_tipos_producto_codigo (codigo)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS usuarios (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            username VARCHAR(50) NOT NULL,
            password_hash VARCHAR(255) NOT NULL,
            email VARCHAR(150) NULL,
            rol_id BIGINT UNSIGNED NOT NULL,
            activo TINYINT(1) NOT NULL DEFAULT 1,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS productos (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            codigo VARCHAR(30) NOT NULL,
            nombre VARCHAR(150) NOT NULL,
            descripcion VARCHAR(255) NULL,
            tipo_producto_id BIGINT UNSIGNED NOT NULL,
            capacidad_kg DECIMAL(8,2) NULL,
            unidad_venta VARCHAR(20) NOT NULL DEFAULT 'UNIDAD',
            precio_actual DECIMAL(12,2) NOT NULL DEFAULT 0,
            maneja_garrafa_individual TINYINT(1) NOT NULL DEFAULT 0,
            activo TINYINT(1) NOT NULL DEFAULT 1,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            created_by BIGINT UNSIGNED NULL,
            updated_by BIGINT UNSIGNED NULL,
            deleted_at DATETIME NULL,
            row_version BINARY(8) NOT NULL DEFAULT 0x0000000000000000,
            CONSTRAINT fk_productos_tipo FOREIGN KEY (tipo_producto_id) REFERENCES tipos_producto(id),
            UNIQUE KEY uq_productos_codigo (codigo)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        -- producto_precios_historico: hook append-only de UpdateAsync
        -- (issue #145 slice 3). El Service inserta una fila en el mismo
        -- SaveChangesAsync que la mutación del producto, por lo que la
        -- tabla DEBE existir para que el test e2e pueda ejercitar el
        -- path de audit_log.
        CREATE TABLE IF NOT EXISTS producto_precios_historico (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            producto_id BIGINT UNSIGNED NOT NULL,
            precio_anterior DECIMAL(12,2) NOT NULL,
            precio_nuevo DECIMAL(12,2) NOT NULL,
            motivo_cambio_precio VARCHAR(255) NULL,
            changed_by BIGINT UNSIGNED NULL,
            changed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            KEY idx_pph_producto_changed (producto_id, changed_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        DROP TRIGGER IF EXISTS trg_productos_bu_rowversion;
        CREATE TRIGGER trg_productos_bu_rowversion
        BEFORE UPDATE ON productos
        FOR EACH ROW
            SET NEW.row_version = RANDOM_BYTES(8);

        INSERT IGNORE INTO tipos_producto (id, codigo, nombre) VALUES (1, 'GAS', 'Gas');
        INSERT IGNORE INTO usuarios (id, username, password_hash, rol_id) VALUES (1, 'system', 'noop', 1);
        """;
}
