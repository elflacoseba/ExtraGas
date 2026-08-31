using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Testcontainers.MySql;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de integración del slice 1 de #145 (DB foundation) contra MySQL real
/// en un container Docker (Testcontainers.MySql). Validan:
///   - Que la migración <c>20260830_000001_producto_precios_historico.sql</c>
///     crea la tabla con todas las columnas esperadas.
///   - Que la FK <c>changed_by → usuarios.id</c> rechaza inserts con
///     <c>errno 1452</c> (FK constraint fail) — esto es lo que blinda la
///     auditoría contra IDs basura.
///   - Que re-ejecutar el script es un no-op (idempotencia real).
///
/// Patrón: IClassFixture comparte el container entre los tests (los containers
/// son caros de arrancar). Cada test crea su propia base y aplica el schema
/// mínimo + la migración bajo prueba. Réplica del patrón de
/// <see cref="ClienteIntegrationTests"/> y <see cref="PedidoCanjeIntegrationTests"/>.
/// </summary>
public class ProductoPrecioHistoricoIntegrationTests
    : IClassFixture<ProductoPrecioHistoricoMySqlFixture>
{
    private readonly ProductoPrecioHistoricoMySqlFixture _fixture;

    public ProductoPrecioHistoricoIntegrationTests(ProductoPrecioHistoricoMySqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migracion_CreaTabla_ConTodasLasColumnasEsperadas()
    {
        // Aplica la migración y verifica con information_schema que la tabla
        // existe con las columnas exactas del design + spec.
        var dbName = await _fixture.NewDatabaseAsync(nameof(Migracion_CreaTabla_ConTodasLasColumnasEsperadas));
        try
        {
            await _fixture.ApplyMigrationAsync(dbName);

            var columnas = await _fixture.GetColumnNamesAsync(dbName, "producto_precios_historico");
            columnas.Should().BeEquivalentTo(
                new[]
                {
                    "id",
                    "producto_id",
                    "precio_anterior",
                    "precio_nuevo",
                    "motivo_cambio_precio",
                    "changed_by",
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
    public async Task Migracion_CreaIndiceIdxPphProductoChanged()
    {
        // El spec exige "(producto_id, changed_at DESC)". EF Core no soporta
        // índices DESC en modelos; el orden DESC se aplica en la migración SQL.
        var dbName = await _fixture.NewDatabaseAsync(nameof(Migracion_CreaIndiceIdxPphProductoChanged));
        try
        {
            await _fixture.ApplyMigrationAsync(dbName);

            var existe = await _fixture.IndexExistsAsync(dbName, "producto_precios_historico", "idx_pph_producto_changed");
            existe.Should().BeTrue("el índice (producto_id, changed_at) es obligatorio por el spec");
        }
        finally
        {
            await _fixture.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    public async Task Migracion_ReEjecutarEsNoOp_NoProduceError()
    {
        // Idempotencia: si por algún motivo install.sh corre el archivo dos
        // veces (re-run manual, retry, etc.) no debe fallar. La protección
        // nativa es CREATE TABLE IF NOT EXISTS, y la real es schema_migrations
        // (skip-by-checksum). Aquí validamos el camino IF NOT EXISTS.
        var dbName = await _fixture.NewDatabaseAsync(nameof(Migracion_ReEjecutarEsNoOp_NoProduceError));
        try
        {
            await _fixture.ApplyMigrationAsync(dbName);
            var act = async () => await _fixture.ApplyMigrationAsync(dbName);

            await act.Should().NotThrowAsync(
                "CREATE TABLE IF NOT EXISTS garantiza idempotencia sin necesidad de PREPARE/EXECUTE");
        }
        finally
        {
            await _fixture.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    public async Task InsertConChangedByInexistente_FallaConError1452()
    {
        // La FK changed_by → usuarios.id debe rechazar IDs inválidos con
        // errno 1452. Sin este constraint, la auditoría podría terminar
        // apuntando a usuarios fantasma.
        var dbName = await _fixture.NewDatabaseAsync(nameof(InsertConChangedByInexistente_FallaConError1452));
        try
        {
            await _fixture.ApplyMigrationAsync(dbName);
            // Sembrar el producto (FK producto_id también debe existir para
            // llegar al chequeo de changed_by; un 1452 de producto sería
            // indistinguible).
            await _fixture.SeedProductoAsync(dbName, productoId: 100);

            const string sql = """
                INSERT INTO producto_precios_historico
                    (producto_id, precio_anterior, precio_nuevo, motivo_cambio_precio, changed_by)
                VALUES (100, 1000, 1200, 'test', 9999)
                """;
            // changed_by=9999 no existe en usuarios.

            var connString = _fixture.GetConnectionString(dbName);
            var act = async () =>
            {
                await using var conn = new MySqlConnection(connString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            };

            var ex = await act.Should().ThrowAsync<MySqlException>(
                "changed_by=9999 no existe en usuarios.id y la FK debe rechazarlo");
            ex.Which.Number.Should().Be(1452,
                "errno 1452 es 'Cannot add or update a child row: a foreign key constraint fails'");
        }
        finally
        {
            await _fixture.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    public async Task InsertConChangedByNull_PersisteCorrectamente()
    {
        // El spec permite changed_by NULL (cambios del sistema). La columna
        // debe ser NULL-able y la FK no debe rechazar este caso.
        var dbName = await _fixture.NewDatabaseAsync(nameof(InsertConChangedByNull_PersisteCorrectamente));
        try
        {
            await _fixture.ApplyMigrationAsync(dbName);
            await _fixture.SeedProductoAsync(dbName, productoId: 200);

            await using var conn = new MySqlConnection(_fixture.GetConnectionString(dbName));
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO producto_precios_historico
                    (producto_id, precio_anterior, precio_nuevo, motivo_cambio_precio, changed_by)
                VALUES (200, 500, 600, 'migracion_inicial', NULL)
                """;
            await cmd.ExecuteNonQueryAsync();

            await using var verify = conn.CreateCommand();
            verify.CommandText = "SELECT COUNT(*) FROM producto_precios_historico WHERE producto_id = 200 AND changed_by IS NULL";
            var count = Convert.ToInt32(await verify.ExecuteScalarAsync());
            count.Should().Be(1, "la fila con changed_by NULL debe persistirse");
        }
        finally
        {
            await _fixture.DropDatabaseAsync(dbName);
        }
    }

    // ====================================================================
    // Issue #145 Slice 3: integración end-to-end del hook contra MySQL real.
    // El unit test con InMemory ya cubre la lógica del hook; este test
    // valida que el Service funciona contra Pomelo + FKs reales + el
    // default CURRENT_TIMESTAMP del ChangedAt. Si la migración no se aplicó,
    // SaveChangesAsync tira InvalidOperationException con "Unknown table" —
    // eso es exactamente lo que queremos cazar antes del deploy.
    // ====================================================================

    [Fact]
    public async Task UpdateAsync_PriceChange_PersisteFilaConFKRealAUsuarios()
    {
        var context = await _fixture.NewDbContextAsync(
            nameof(UpdateAsync_PriceChange_PersisteFilaConFKRealAUsuarios));
        try
        {
            // Seed mínimo: un producto con precio=1000 y un usuario (operator).
            // Las FKs de producto_precios_historico apuntan a ambos.
            var producto = new Producto
            {
                Codigo = "GAS-10",
                Nombre = "Garrafa 10kg",
                TipoProductoId = 1,
                // Issue #146.3: capacidad_kg > 0 cuando ManejaGarrafaIndividual=true.
                // El test pre-existente sembraba sin capacidad y confiaba en que la
                // validación tardía en RecepcionService.ValidarCodigosGarrafaAsync
                // se ocupara. Ahora el Service lo rechaza en el borde y se setea
                // explícito para que el escenario "producto GARRAFA real" siga
                // siendo representativo.
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
            var service = new ProductoService(context, mapper, NullLogger<ProductoService>.Instance);

            // Update con cambio de precio 1000 → 1500 + motivo.
            var dto = new UpdateProductoDto
            {
                Id = producto.Id,
                Codigo = producto.Codigo,
                Nombre = producto.Nombre,
                TipoProductoId = producto.TipoProductoId,
                // Issue #146.3: propagamos la capacidad seteada en el seed
                // para que la regla GARRAFA ⇒ CapacidadKg > 0 no rechace el
                // UpdateAsync y se pueda ejercitar el path de histórico de
                // precios — que es lo que este test cubre.
                CapacidadKg = producto.CapacidadKg,
                UnidadVenta = producto.UnidadVenta,
                PrecioActual = 1500m,
                ManejaGarrafaIndividual = producto.ManejaGarrafaIndividual,
                MotivoCambioPrecio = "Ajuste por inflacion",
            };

            var actualizado = await service.UpdateAsync(dto, usuarioId: 1, ct: default);

            actualizado.PrecioActual.Should().Be(1500m);

            var fila = await context.ProductoPreciosHistorico
                .AsNoTracking()
                .FirstAsync(p => p.ProductoId == producto.Id);
            fila.PrecioAnterior.Should().Be(1000m, "snapshot antes del Map");
            fila.PrecioNuevo.Should().Be(1500m, "valor post-update");
            fila.MotivoCambioPrecio.Should().Be("Ajuste por inflacion");
            fila.ChangedBy.Should().Be(1UL, "FK a usuarios.id válida — operator sembrado");
            fila.ChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1),
                "ChangedAt usa CURRENT_TIMESTAMP por default del schema");
        }
        finally
        {
            await _fixture.DropDatabaseAsyncForDbContext(context);
        }
    }
}

/// <summary>
/// Fixture xUnit que arranca un container MySQL via Testcontainers y expone
/// helpers para crear bases frescas, aplicar la migración bajo prueba y
/// sembrar las FKs mínimas (tipos_producto + productos) que el schema necesita.
/// </summary>
public class ProductoPrecioHistoricoMySqlFixture : IAsyncLifetime
{
    private const string MysqlImage = "mysql:8.0";
    private const string RootPassword = "test_root_pwd";
    private const string RootUsername = "root";
    private const string DatabasePrefix = "pph_";

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
        return _rootConnectionString!
            .Replace("database=placeholder_db", $"database={dbName}", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// Aplica el SQL mínimo para que la FK changed_by → usuarios.id tenga
    /// destino, y luego corre la migración bajo prueba. Replica los DDL
    /// mínimos de las migraciones 20260102_000001 / 20260102_000003 + la
    /// 20260830_000001 (esta misma).
    /// </summary>
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

    public async Task SeedProductoAsync(string dbName, ulong productoId)
    {
        await using var conn = new MySqlConnection(GetConnectionString(dbName));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO productos (id, codigo, nombre, tipo_producto_id)
            VALUES ({productoId}, 'TEST-{productoId}', 'Producto Test', 1)
            """;
        await cmd.ExecuteNonQueryAsync();
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

    /// <summary>
    /// Crea una base fresca + aplica el schema mínimo + crea un DbContext
    /// Pomelo conectado. Usado por el test de Slice 3 (hook end-to-end) que
    /// necesita ejecutar <c>ProductoService.UpdateAsync</c> contra MySQL real
    /// para validar que la FK <c>changed_by → usuarios.id</c> se cumple cuando
    /// el operator existe, y que el row insertado es legible vía EF.
    /// </summary>
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

        var connString = GetConnectionString(dbName);
        var serverVersion = ServerVersion.Create(
            new Version(8, 0, 0), ServerType.MySql);
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseMySql(connString, serverVersion)
            .Options;
        return new ExtraGasDbContext(options);
    }

    /// <summary>
    /// Cierra un DbContext abierto por <see cref="NewDbContextAsync"/> y
    /// elimina su base efímera. Helper para el patrón using en el test.
    /// </summary>
    public async Task DropDatabaseAsyncForDbContext(ExtraGasDbContext context)
    {
        var dbName = context.Database.GetDbConnection().Database;
        await context.DisposeAsync();
        await DropDatabaseAsync(dbName);
    }

    private static string LoadMigrationSql()
    {
        // Carga el archivo de migración desde el repo. Esto acopla el test al
        // path real, pero garantiza que probamos lo que se va a deployar.
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var migrationPath = Path.Combine(repoRoot, "db", "migrations",
            "20260830_000001_producto_precios_historico.sql");
        if (!File.Exists(migrationPath))
        {
            throw new FileNotFoundException(
                $"Migración bajo prueba no encontrada en {migrationPath}");
        }
        var raw = File.ReadAllText(migrationPath);

        // Strip del "USE extragas;" porque los tests apuntan a bases efímeras
        // (pph_<guid>) y la cláusula USE generaría "Unknown database 'extragas'".
        // La migración real conserva el USE para el flujo install.sh.
        return System.Text.RegularExpressions.Regex.Replace(
            raw, @"^\s*USE\s+\w+\s*;\s*$",
            "",
            System.Text.RegularExpressions.RegexOptions.Multiline);
    }

    /// <summary>
    /// Schema mínimo para que las FKs de la migración bajo prueba tengan
    /// destino. Réplica de los DDL de 20260102_000001 (tipos_producto) +
    /// 20260102_000003 (productos, con FK a tipos_producto y usuarios).
    /// Idempotente: todas las CREATE usan IF NOT EXISTS para que el test
    /// <c>Migracion_ReEjecutarEsNoOp_NoProduceError</c> pueda aplicar el
    /// schema mínimo dos veces sin fallar.
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

        -- usuarios: tabla mínima para que la FK changed_by → usuarios(id) tenga destino.
        -- No replicamos todas las columnas del schema real porque no las necesitamos
        -- y agrega ruido a estos tests focused del slice 1.
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
            -- Issue #146.4: concurrencia optimista via row_version (mismo DDL que
            -- la migración real 20260831_000001_add_productos_row_version.sql).
            row_version BINARY(8) NOT NULL DEFAULT 0x0000000000000000,
            CONSTRAINT fk_productos_tipo FOREIGN KEY (tipo_producto_id) REFERENCES tipos_producto(id),
            UNIQUE KEY uq_productos_codigo (codigo)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        DROP TRIGGER IF EXISTS trg_productos_bu_rowversion;
        CREATE TRIGGER trg_productos_bu_rowversion
        BEFORE UPDATE ON productos
        FOR EACH ROW
            SET NEW.row_version = RANDOM_BYTES(8);

        -- Seed del lookup + un usuario para que las FKs tengan destino en el
        -- test InsertConChangedByInexistente. Las FKs de producto_precios_historico
        -- apuntan a usuarios(id) y productos(id); sembramos solo lo mínimo.
        INSERT IGNORE INTO tipos_producto (id, codigo, nombre) VALUES (1, 'GAS', 'Gas');
        INSERT IGNORE INTO usuarios (id, username, password_hash, rol_id) VALUES (1, 'system', 'noop', 1);
        """;
}
