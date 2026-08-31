using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Testcontainers.MySql;
using Xunit;

namespace ExtraGasMVC.Tests.Integration;

/// <summary>
/// Integration test de la migración <c>20260901_000002_create_unidades_venta_and_fk.sql</c>
/// (issue #147 slice 3 item 7).
///
/// <para>El test valida tres contratos del design.md / spec:</para>
/// <list type="number">
///   <item>La migración corre y crea la tabla <c>unidades_venta</c> con
///   4 valores seed (UNIDAD, GARRAFA, BOLSA, KG).</item>
///   <item>El backfill <c>UPDATE productos JOIN unidades_venta</c>
///   resuelve la columna legacy <c>unidad_venta</c> (VARCHAR) al FK
///   <c>unidad_venta_id</c> recién creada.</item>
///   <item>La FK constraint <c>fk_productos_unidad_venta</c> queda
///   efectivamente aplicada (el índice y la FK existen en information_schema).</item>
/// </list>
///
/// <para>Patrón IClassFixture compartido con
/// <see cref="ProductoAuditLogIntegrationTests"/>: container MySQL efímero,
/// schema mínimo para que la FK de Producto a tipos_producto/usuarios tenga
/// destino, aplicación de la migración bajo prueba, drop de la base al final.</para>
/// </summary>
public class UnidadesVentaMigrationIntegrationTests
    : IClassFixture<UnidadesVentaMySqlFixture>
{
    private readonly UnidadesVentaMySqlFixture _fixture;

    public UnidadesVentaMigrationIntegrationTests(UnidadesVentaMySqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migracion_SembraryCreaTablaUnidadesVenta_ConCuatroValores()
    {
        // Spec scenario "seed contains 4 values": UNIDAD, GARRAFA, BOLSA, KG.
        var dbName = await _fixture.NewDatabaseAsync(nameof(Migracion_SembraryCreaTablaUnidadesVenta_ConCuatroValores));
        try
        {
            await _fixture.ApplyMigrationAsync(dbName);

            await using var conn = new MySqlConnection(_fixture.GetConnectionString(dbName));
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT codigo FROM unidades_venta
                WHERE deleted_at IS NULL
                ORDER BY codigo
                """;
            var codigos = new List<string>();
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    codigos.Add(reader.GetString(0));
                }
            }
            codigos.Should().BeEquivalentTo(
                new[] { "BOLSA", "GARRAFA", "KG", "UNIDAD" },
                options => options.WithStrictOrdering(),
                "el seed de la migración debe contener exactamente estos 4 códigos canónicos");
        }
        finally
        {
            await _fixture.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    public async Task Migracion_CreaColumnaUnidadVentaId_YFkHaciaUnidadesVenta()
    {
        // Spec scenario "FK to unidades_venta.id": la columna unidad_venta_id
        // existe en productos y la constraint fk_productos_unidad_venta está
        // registrada en information_schema.
        var dbName = await _fixture.NewDatabaseAsync(nameof(Migracion_CreaColumnaUnidadVentaId_YFkHaciaUnidadesVenta));
        try
        {
            await _fixture.ApplyMigrationAsync(dbName);

            (await _fixture.ColumnExistsAsync(dbName, "productos", "unidad_venta_id"))
                .Should().BeTrue("la migración debe agregar la columna unidad_venta_id");
            (await _fixture.ForeignKeyExistsAsync(dbName, "productos", "fk_productos_unidad_venta"))
                .Should().BeTrue("la migración debe crear la FK fk_productos_unidad_venta");
        }
        finally
        {
            await _fixture.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    public async Task Migracion_Backfill_ResuelveUnidadVentaStringAFkId()
    {
        // Spec scenario "migration order: seed BEFORE ALTER" + backfill:
        // un producto con unidad_venta='GARRAFA' (legacy VARCHAR) debe terminar
        // con unidad_venta_id apuntando al id de la fila GARRAFA en unidades_venta.
        //
        // Para probar el backfill correctamente, el producto pre-existente debe
        // estar en la BD ANTES de que la migración corra — el backfill es un
        // UPDATE que solo afecta filas pre-existentes (insertadas entre el
        // schema minimal y la migración).
        var dbName = await _fixture.NewDatabaseAsync(nameof(Migracion_Backfill_ResuelveUnidadVentaStringAFkId));
        try
        {
            // 1. Crear schema mínimo (sin la migración todavía).
            await using (var conn = new MySqlConnection(_fixture.GetConnectionString(dbName)))
            {
                await conn.OpenAsync();
                await using var schema = conn.CreateCommand();
                schema.CommandText = SchemaMinimalForBackfill;
                await schema.ExecuteNonQueryAsync();

                // 2. Insertar producto con el VARCHAR legacy ANTES de la migración.
                await using var insert = conn.CreateCommand();
                insert.CommandText = """
                    INSERT INTO productos
                      (codigo, nombre, tipo_producto_id, unidad_venta, precio_actual)
                    VALUES
                      ('GAS-10-LEGACY', 'Garrafa legacy', 1, 'GARRAFA', 100.00)
                    """;
                await insert.ExecuteNonQueryAsync();
            }

            // 3. Aplicar la migración (incluye el backfill del step 4).
            await _fixture.ApplyMigrationFileAsync(dbName);

            // 4. Verificar que el FK quedó asignado al id de GARRAFA.
            ulong garrafaId;
            ulong? expectedUnidadVentaId;
            await using (var conn = new MySqlConnection(_fixture.GetConnectionString(dbName)))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id FROM unidades_venta WHERE codigo = 'GARRAFA'";
                garrafaId = Convert.ToUInt64(await cmd.ExecuteScalarAsync());
                garrafaId.Should().BeGreaterThan(0, "GARRAFA debe existir en el seed");

                cmd.CommandText = "SELECT unidad_venta_id FROM productos WHERE codigo = 'GAS-10-LEGACY'";
                var raw = await cmd.ExecuteScalarAsync();
                raw.Should().NotBeNull("el backfill debe haber poblado unidad_venta_id");
                expectedUnidadVentaId = Convert.ToUInt64(raw);
            }
            expectedUnidadVentaId.Should().Be(garrafaId,
                "el backfill debe haber asignado el id de GARRAFA al producto legacy");
        }
        finally
        {
            await _fixture.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// Schema mínimo sin columnas/constraints de la migración bajo prueba.
    /// Réplica del <see cref="UnidadesVentaMySqlFixture.SchemaMinimal"/> pero
    /// SIN incluir el seed de unidades_venta (queremos probar el backfill).
    /// </summary>
    private const string SchemaMinimalForBackfill = """
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
            CONSTRAINT fk_productos_tipo FOREIGN KEY (tipo_producto_id) REFERENCES tipos_producto(id),
            CONSTRAINT fk_productos_created_by FOREIGN KEY (created_by) REFERENCES usuarios(id),
            CONSTRAINT fk_productos_updated_by FOREIGN KEY (updated_by) REFERENCES usuarios(id),
            UNIQUE KEY uq_productos_codigo (codigo)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        INSERT IGNORE INTO tipos_producto (id, codigo, nombre) VALUES (1, 'GAS', 'Gas');
        INSERT IGNORE INTO usuarios (id, username, password_hash, rol_id) VALUES (1, 'system', 'noop', 1);
        """;

    [Fact]
    public async Task Migracion_ReEjecutarEsNoOp_NoProduceError()
    {
        // Idempotencia: la migración se puede correr dos veces (re-run
        // manual, retry) sin fallar — CREATE TABLE IF NOT EXISTS +
        // information_schema guards para columnas / FKs / índices.
        var dbName = await _fixture.NewDatabaseAsync(nameof(Migracion_ReEjecutarEsNoOp_NoProduceError));
        try
        {
            await _fixture.ApplyMigrationAsync(dbName);
            await _fixture.ApplyMigrationAsync(dbName); // segunda corrida

            // Sigue teniendo los 4 valores del seed.
            await using var conn = new MySqlConnection(_fixture.GetConnectionString(dbName));
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM unidades_venta";
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            count.Should().Be(4, "segunda corrida no debe duplicar el seed (INSERT IGNORE)");
        }
        finally
        {
            await _fixture.DropDatabaseAsync(dbName);
        }
    }
}

/// <summary>
/// Fixture xUnit que arranca un container MySQL via Testcontainers. Replica
/// el patrón de <see cref="ProductoAuditLogMySqlFixture"/>.
/// </summary>
public class UnidadesVentaMySqlFixture : IAsyncLifetime
{
    private const string MysqlImage = "mysql:8.0";
    private const string RootPassword = "test_root_pwd";
    private const string RootUsername = "root";
    private const string DatabasePrefix = "uv_";

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
        // AllowUserVariables=true: la migración usa PREPARE/EXECUTE con
        // @col_exists / @sql. Mismo motivo que ProductoAuditLogMySqlFixture.
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

    /// <summary>
    /// Aplica SOLO el archivo de migración bajo prueba (sin schema minimal).
    /// Usado por el test de backfill que necesita sembrar un producto
    /// pre-existente entre el schema minimal y la migración.
    /// </summary>
    public async Task ApplyMigrationFileAsync(string dbName)
    {
        var connString = GetConnectionString(dbName);
        await using var conn = new MySqlConnection(connString);
        await conn.OpenAsync();

        await using var mig = conn.CreateCommand();
        mig.CommandText = LoadMigrationSql();
        await mig.ExecuteNonQueryAsync();
    }

    public async Task<bool> ColumnExistsAsync(string dbName, string tableName, string columnName)
    {
        await using var conn = new MySqlConnection(GetConnectionString(dbName));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = @db
              AND table_name = @tbl
              AND column_name = @col
            """;
        cmd.Parameters.AddWithValue("@db", dbName);
        cmd.Parameters.AddWithValue("@tbl", tableName);
        cmd.Parameters.AddWithValue("@col", columnName);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return count > 0;
    }

    public async Task<bool> ForeignKeyExistsAsync(string dbName, string tableName, string fkName)
    {
        await using var conn = new MySqlConnection(GetConnectionString(dbName));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM information_schema.table_constraints
            WHERE table_schema = @db
              AND table_name = @tbl
              AND constraint_name = @fk
              AND constraint_type = 'FOREIGN KEY'
            """;
        cmd.Parameters.AddWithValue("@db", dbName);
        cmd.Parameters.AddWithValue("@tbl", tableName);
        cmd.Parameters.AddWithValue("@fk", fkName);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return count > 0;
    }

    private static string LoadMigrationSql()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var migrationPath = Path.Combine(repoRoot, "db", "migrations",
            "20260901_000002_create_unidades_venta_and_fk.sql");
        if (!File.Exists(migrationPath))
        {
            throw new FileNotFoundException(
                $"Migración bajo prueba no encontrada en {migrationPath}");
        }
        var raw = File.ReadAllText(migrationPath);

        // Strip del "USE extragas;" porque los tests apuntan a bases efímeras.
        return System.Text.RegularExpressions.Regex.Replace(
            raw, @"^\s*USE\s+\w+\s*;\s*$",
            "",
            System.Text.RegularExpressions.RegexOptions.Multiline);
    }

    /// <summary>
    /// Schema mínimo para que la migración pueda correr: requiere tipos_producto
    /// (FK de productos) y usuarios (FK de created_by/updated_by). Réplica del
    /// patrón de ProductoAuditLogMySqlFixture. Sin trigger row_version — el
    /// backfill de esta migración solo escribe en unidad_venta_id, no toca
    /// updated_at de productos, así que no necesitamos el trigger.
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
            CONSTRAINT fk_productos_tipo FOREIGN KEY (tipo_producto_id) REFERENCES tipos_producto(id),
            CONSTRAINT fk_productos_created_by FOREIGN KEY (created_by) REFERENCES usuarios(id),
            CONSTRAINT fk_productos_updated_by FOREIGN KEY (updated_by) REFERENCES usuarios(id),
            UNIQUE KEY uq_productos_codigo (codigo)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        INSERT IGNORE INTO tipos_producto (id, codigo, nombre) VALUES (1, 'GAS', 'Gas');
        INSERT IGNORE INTO usuarios (id, username, password_hash, rol_id) VALUES (1, 'system', 'noop', 1);
        """;
}
