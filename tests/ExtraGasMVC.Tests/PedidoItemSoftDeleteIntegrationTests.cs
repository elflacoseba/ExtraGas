using AutoMapper;
using ExtraGasMVC.Constants;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Data.Entities.Enums;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using ExtraGasMVC.Services.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Testcontainers.MySql;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de integración del soft-delete de <see cref="PedidoItem"/> contra un
/// MySQL real dentro de un container Docker (Testcontainers.MySql).
///
/// Issue #17: la entity <c>PedidoItem</c> debe implementar soft-delete per
/// AGENTS.md convention #6, igual que el resto del modelo (Pedido, Cliente,
/// Producto, etc.). Antes la columna <c>deleted_at</c> existía en BD
/// (migración 20260607_000003) pero el código EF nunca la usaba: el service
/// hacía hard-delete con <c>_context.PedidoItems.Remove(item)</c>.
///
/// Estos tests cubren el ciclo completo contra MySQL real:
/// <list type="bullet">
///   <item><c>RemoveItemAsync</c> setea <c>DeletedAt</c> en lugar de borrar.</item>
///   <item>El <c>HasQueryFilter</c> oculta items soft-deleted de
///         <c>GetItemsByPedidoAsync</c>, <c>RecalculateTotalsAsync</c> y
///         <c>LoadItemsParaCanjeAsync</c>.</item>
///   <item><c>IgnoreQueryFilters()</c> permite ver items soft-deleted (audit).</item>
///   <item>Se puede re-agregar el mismo (pedido, producto, tipo_linea) tras un
///         soft-delete gracias a la columna virtual <c>unique_hash</c> que
///         cambia al setear <c>DeletedAt</c>.</item>
///   <item>Los totales del pedido excluyen items soft-deleted.</item>
/// </list>
///
/// Patrón: <see cref="IClassFixture{T}"/> comparte el container entre los
/// tests. Cada test crea su propia base efímera con schema mínimo. Réplica
/// del patrón de <see cref="PedidoCanjeMySqlFixture"/> pero con el schema de
/// <c>pedido_items</c> extendido con <c>deleted_at</c> + <c>unique_hash</c> +
/// unique index + <c>idx_pedido_items_deleted_at</c>, que refleja el estado
/// real de la BD tras aplicar la migración 20260607_000003.
/// </summary>
public class PedidoItemSoftDeleteIntegrationTests : IClassFixture<PedidoItemSoftDeleteMySqlFixture>
{
    private readonly PedidoItemSoftDeleteMySqlFixture _fixture;

    public PedidoItemSoftDeleteIntegrationTests(PedidoItemSoftDeleteMySqlFixture fixture)
    {
        _fixture = fixture;
    }

    // ====================================================================
    // RemoveItemAsync: la fila NO se borra físicamente
    // ====================================================================

    [Fact]
    public async Task RemoveItemAsync_NoHaceHardDelete_SeteaDeletedAt()
    {
        // Cubre el cambio principal de #17: RemoveItemAsync pasa de hard-delete
        // a soft-delete. La fila sigue existiendo en BD pero con DeletedAt
        // no nulo.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(RemoveItemAsync_NoHaceHardDelete_SeteaDeletedAt));
        var service = NewService(ctx);
        var seed = await SeedPedidoConDosItemsAsync(ctx);

        var ok = await service.RemoveItemAsync(seed.ItemAId);

        ok.Should().BeTrue();

        // 1) La fila SIGUE existiendo en BD (verificación con
        //    IgnoreQueryFilters porque el HasQueryFilter global la ocultaría).
        var itemDesdeBd = await ctx.PedidoItems
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == seed.ItemAId);
        itemDesdeBd.Should().NotBeNull(
            "issue #17: RemoveItemAsync debe hacer soft-delete, no hard-delete");

        // 2) DeletedAt quedó seteado con un timestamp razonable (últimos 60s).
        itemDesdeBd!.DeletedAt.Should().NotBeNull();
        itemDesdeBd.DeletedAt!.Value.Should().BeCloseTo(
            DateTime.UtcNow, TimeSpan.FromSeconds(60));
    }

    // ====================================================================
    // HasQueryFilter: items soft-deleted desaparecen de las queries default
    // ====================================================================

    [Fact]
    public async Task GetItemsByPedidoAsync_OcultaItemsSoftDeleted()
    {
        // Cubre que el HasQueryFilter global se aplica en queries de lectura:
        // la UI del detalle del pedido no debe ver items borrados.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(GetItemsByPedidoAsync_OcultaItemsSoftDeleted));
        var service = NewService(ctx);
        var seed = await SeedPedidoConDosItemsAsync(ctx);

        await service.RemoveItemAsync(seed.ItemAId);

        var itemsVisibles = (await service.GetItemsByPedidoAsync(seed.PedidoId)).ToList();
        itemsVisibles.Should().HaveCount(1);
        itemsVisibles[0].Id.Should().Be(seed.ItemBId,
            "el item A fue soft-deleted, solo debe quedar el B visible");
    }

    [Fact]
    public async Task GetItemsByPedidoAsync_ConIgnoreQueryFilters_DevuelveItemsSoftDeleted()
    {
        // Cubre el camino de audit: con IgnoreQueryFilters() se pueden ver
        // todos los items, incluyendo los soft-deleted. La UI de admin lo usa
        // para reconstruir el historial de un pedido.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(GetItemsByPedidoAsync_ConIgnoreQueryFilters_DevuelveItemsSoftDeleted));
        var service = NewService(ctx);
        var seed = await SeedPedidoConDosItemsAsync(ctx);

        await service.RemoveItemAsync(seed.ItemAId);

        var todosLosItems = await ctx.PedidoItems
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.PedidoId == seed.PedidoId)
            .OrderBy(i => i.Id)
            .ToListAsync();
        todosLosItems.Should().HaveCount(2,
            "IgnoreQueryFilters debe traer activos + soft-deleted");
        todosLosItems.Single(i => i.Id == seed.ItemAId)
            .DeletedAt.Should().NotBeNull();
        todosLosItems.Single(i => i.Id == seed.ItemBId)
            .DeletedAt.Should().BeNull();
    }

    // ====================================================================
    // RecalculateTotalsAsync: excluye items soft-deleted + PERSISTE el subtotal
    // ====================================================================

    [Fact]
    public async Task RemoveItemAsync_RecalculaYPersisteSubtotal_ExcluyendoItemsSoftDeleted()
    {
        // Cubre dos cosas juntas:
        // 1) RecalculateTotalsAsync aplica el HasQueryFilter, así que tras
        //    un soft-delete el subtotal computado excluye el item borrado.
        // 2) El subtotal se PERSISTE en `pedidos` (SaveChangesAsync dentro
        //    del SaveChangesAsync se commitea junto con la transacción del
        //    RemoveItemAsync). Antes del fix #17 este método se llamaba
        //    RecalculateTotalsInternalAsync y NO llamaba SaveChanges, así
        //    que el subtotal quedaba desactualizado en BD — v_pedidos_resumen
        //    mostraba saldo stale. Ahora está arreglado.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(RemoveItemAsync_RecalculaYPersisteSubtotal_ExcluyendoItemsSoftDeleted));
        var service = NewService(ctx);
        var seed = await SeedPedidoConDosItemsAsync(ctx);

        // Pre-condición: el seed dejó el subtotal en 1500 (A=1000 + B=500).
        var pedidoAntes = await ctx.Pedidos.AsNoTracking()
            .FirstAsync(p => p.Id == seed.PedidoId);
        pedidoAntes.Subtotal.Should().Be(1500m);
        pedidoAntes.Total.Should().Be(1500m);

        await service.RemoveItemAsync(seed.ItemAId);

        // 1) Persistencia: el subtotal en BD se actualizó a 500 (solo B).
        var pedidoDespues = await ctx.Pedidos.AsNoTracking()
            .FirstAsync(p => p.Id == seed.PedidoId);
        pedidoDespues.Subtotal.Should().Be(500m,
            "RemoveItemAsync debe persistir el subtotal recalculado excluyendo el item soft-deleted");
        pedidoDespues.Total.Should().Be(500m);

        // 2) Filtro: GetItemsByPedidoAsync devuelve solo el B.
        var itemsVisibles = (await service.GetItemsByPedidoAsync(seed.PedidoId)).ToList();
        itemsVisibles.Should().HaveCount(1);
        itemsVisibles[0].Id.Should().Be(seed.ItemBId);
    }

    [Fact]
    public async Task AddItemAsync_RecalculaYPersisteSubtotal_ConNuevoItem()
    {
        // Cubre el mismo camino de persistencia que el test anterior pero
        // desde el lado de AddItemAsync — también participaba del bug
        // porque llamaba RecalculateTotalsInternalAsync.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(AddItemAsync_RecalculaYPersisteSubtotal_ConNuevoItem));
        var service = NewService(ctx);
        var seed = await SeedPedidoConDosItemsAsync(ctx);

        var pedidoAntes = await ctx.Pedidos.AsNoTracking()
            .FirstAsync(p => p.Id == seed.PedidoId);
        pedidoAntes.Subtotal.Should().Be(1500m);

        // Agregar un item de producto C, cantidad 4, precio 1000 → +4000 al subtotal.
        var productoCId = (await ctx.Productos.AsNoTracking()
            .MaxAsync(p => (ulong?)p.Id) ?? 0) + 1;
        var now = DateTime.UtcNow;
        var tipoProductoId = (await ctx.TiposProducto.FirstAsync()).Id;
        var productoC = new Producto
        {
            Codigo = "PROD-C",
            Nombre = "Producto C",
            TipoProductoId = tipoProductoId,
            UnidadVenta = "UNIDAD",
            PrecioActual = 1000m,
            ManejaGarrafaIndividual = false,
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Productos.Add(productoC);
        await ctx.SaveChangesAsync();

        var add = new CreatePedidoItemDto
        {
            PedidoId = seed.PedidoId,
            ProductoId = productoC.Id,
            TipoLinea = "VENTA",
            Cantidad = 4m,
        };
        await service.AddItemAsync(add);

        // Persistencia: subtotal 1500 + 4000 = 5500.
        var pedidoDespues = await ctx.Pedidos.AsNoTracking()
            .FirstAsync(p => p.Id == seed.PedidoId);
        pedidoDespues.Subtotal.Should().Be(5500m,
            "AddItemAsync debe persistir el subtotal recalculado incluyendo el nuevo item");
        pedidoDespues.Total.Should().Be(5500m);
    }

    // ====================================================================
    // Re-add: la unique_hash permite re-agregar el mismo (pedido, prod, tipo)
    // ====================================================================

    [Fact]
    public async Task AddItemAsync_PermiteReAgregarItemSoftDeleted_MismoPedidoProductoTipo()
    {
        // Cubre el caso de uso real que motivó el soft-delete + unique_hash:
        // el operador borra un item por error, luego quiere re-agregar el
        // mismo (producto, tipo_linea) sin que la unique constraint de BD
        // lo rechace. La columna virtual unique_hash incluye deleted_at
        // (COALESCE(deleted_at, '0')), así que cambia al soft-delear.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(AddItemAsync_PermiteReAgregarItemSoftDeleted_MismoPedidoProductoTipo));
        var service = NewService(ctx);
        var seed = await SeedPedidoConDosItemsAsync(ctx);

        // Borrar el item A.
        await service.RemoveItemAsync(seed.ItemAId);

        // Re-agregar el mismo (producto, tipo_linea) en el mismo pedido.
        // Antes del soft-delete esto explotaba por la unique constraint.
        // Nota: CreatePedidoItemDto NO tiene PrecioUnitario — el service lo
        // toma de producto.PrecioActual (línea ~397 de PedidoService.cs).
        var reAdd = new CreatePedidoItemDto
        {
            PedidoId = seed.PedidoId,
            ProductoId = seed.ProductoId,
            TipoLinea = "VENTA",
            Cantidad = 3m,
        };

        var act = async () => await service.AddItemAsync(reAdd);

        await act.Should().NotThrowAsync(
            "después de soft-deleted, el unique_hash cambió y se puede re-agregar");

        var reAgregado = (await service.GetItemsByPedidoAsync(seed.PedidoId))
            .Single(i => i.Cantidad == 3m);
        reAgregado.ProductoId.Should().Be(seed.ProductoId);
        reAgregado.TipoLinea.Should().Be("VENTA");

        // Sanity: siguen existiendo ambos registros en BD (el soft-deleted
        // y el nuevo). El total es 500 (B) + 3000 (re-agregado) = 3500.
        var countEnBd = await ctx.PedidoItems.IgnoreQueryFilters()
            .CountAsync(i => i.PedidoId == seed.PedidoId);
        countEnBd.Should().Be(3,
            "soft-deleted + nuevo item coexisten en BD (audit intacto)");
    }

    [Fact]
    public async Task AddItemAsync_RechazaDuplicadoSiHayOtroItemActivoMismoProductoTipo()
    {
        // Caso simétrico al anterior: si hay un item ACTIVO con el mismo
        // (pedido, producto, tipo_linea), AddItemAsync sigue rechazando
        // (el unique_hash del activo es idéntico al que se intenta insertar).
        // Garantiza que el soft-delete no rompió la protection contra
        // duplicados "vivos".
        var ctx = await _fixture.NewDbContextAsync(
            nameof(AddItemAsync_RechazaDuplicadoSiHayOtroItemActivoMismoProductoTipo));
        var service = NewService(ctx);
        var seed = await SeedPedidoConDosItemsAsync(ctx);

        // B sigue activo (VENTA del mismo producto). Intentar agregar otro
        // VENTA del mismo producto debe fallar.
        var duplicado = new CreatePedidoItemDto
        {
            PedidoId = seed.PedidoId,
            ProductoId = seed.ProductoId,
            TipoLinea = "VENTA",
            Cantidad = 1m,
        };

        var act = async () => await service.AddItemAsync(duplicado);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ya está agregado*");
    }

    // ====================================================================
    // Idempotencia: RemoveItemAsync sobre item ya soft-deleted
    // ====================================================================

    [Fact]
    public async Task RemoveItemAsync_SobreItemYaSoftDeleted_NoRompe_Idempotente()
    {
        // EF Core `FindAsync` NO aplica el HasQueryFilter global — es por
        // diseño de EF para PK lookups. Eso significa que un segundo
        // RemoveItemAsync sobre el mismo item encuentra la fila (ya
        // soft-deleted) y la "soft-deletea" de nuevo (sobreescribiendo
        // DeletedAt con NOW y bumping UpdatedAt). El resultado es
        // idempotente: la fila queda soft-deleted, sin error, y devuelve
        // true. Mismo patrón que PedidoService.DeleteAsync y que el
        // resto de los DeleteAsync de la app (Cliente, Producto).
        //
        // El contrato alternativo (devolver false porque el item "ya está
        // borrado") requeriría usar IgnoreQueryFilters() explícito + un
        // check de DeletedAt — fuera del scope de #17.
        var ctx = await _fixture.NewDbContextAsync(
            nameof(RemoveItemAsync_SobreItemYaSoftDeleted_NoRompe_Idempotente));
        var service = NewService(ctx);
        var seed = await SeedPedidoConDosItemsAsync(ctx);

        var firstCall = await service.RemoveItemAsync(seed.ItemAId);
        firstCall.Should().BeTrue();

        var secondCall = await service.RemoveItemAsync(seed.ItemAId);
        secondCall.Should().BeTrue("RemoveItemAsync es idempotente: FindAsync ignora el HasQueryFilter y la fila sigue ahí");

        // La fila sigue existiendo en BD y sigue soft-deleted.
        var itemFinal = await ctx.PedidoItems.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(i => i.Id == seed.ItemAId);
        itemFinal.DeletedAt.Should().NotBeNull(
            "el segundo RemoveItemAsync no debe romper el estado soft-deleted");
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    /// <summary>
    /// Construye un <see cref="PedidoService"/> con AutoMapper real y un
    /// <see cref="IMemoryCache"/> vacío. No se mockea <see cref="IGarrafaService"/>
    /// porque estos tests no ejercitan el canje.
    /// </summary>
    private static PedidoService NewService(ExtraGasDbContext ctx)
    {
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var garrafaService = new GarrafaService(ctx, mapper, NullLogger<GarrafaService>.Instance);
        return new PedidoService(ctx, mapper, cache, garrafaService);
    }

    /// <summary>
    /// Crea un pedido PENDIENTE con 2 items VENTA de productos distintos:
    /// item A (cantidad 1, precio 1000) + item B (cantidad 0.5, precio 1000).
    /// El estado PENDIENTE es el único donde RemoveItemAsync permite borrar.
    /// Productos distintos para evitar violar la constraint
    /// <c>uk_pedido_items_pedido_producto_tipo</c> sobre
    /// <c>(pedido_id, producto_id, tipo_linea, deleted_at)</c>.
    /// </summary>
    private static async Task<PedidoItemSeed> SeedPedidoConDosItemsAsync(ExtraGasDbContext ctx)
    {
        var pendienteId = (await ctx.EstadosPedido.FirstAsync(e => e.Codigo == PedidoEstados.Pendiente)).Id;
        var canalVentaId = (await ctx.CanalesVenta.FirstAsync()).Id;
        var tipoProductoId = (await ctx.TiposProducto.FirstAsync()).Id;

        var now = DateTime.UtcNow;
        var fecha = DateOnly.FromDateTime(now);

        var empleado = new Empleado
        {
            Nombre = "Juan",
            Apellido = "Empleado",
            Telefono = "1100000000",
            FechaIngreso = fecha,
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Empleados.Add(empleado);
        await ctx.SaveChangesAsync();

        var cliente = new Cliente
        {
            Nombre = "Pedro",
            Apellido = "Garcia",
            Dni = "11222333",
            TelefonoPrincipal = "1144556677",
            FechaAlta = fecha,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();

        var productoA = new Producto
        {
            Codigo = "PROD-A",
            Nombre = "Producto A",
            TipoProductoId = tipoProductoId,
            UnidadVenta = "UNIDAD",
            PrecioActual = 1000m,
            ManejaGarrafaIndividual = false,
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var productoB = new Producto
        {
            Codigo = "PROD-B",
            Nombre = "Producto B",
            TipoProductoId = tipoProductoId,
            UnidadVenta = "UNIDAD",
            PrecioActual = 1000m,
            ManejaGarrafaIndividual = false,
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Productos.AddRange(productoA, productoB);
        await ctx.SaveChangesAsync();

        var pedido = new Pedido
        {
            Fecha = now,
            ClienteId = cliente.Id,
            EmpleadoId = empleado.Id,
            EstadoPedidoId = pendienteId,
            CanalVentaId = canalVentaId,
            Subtotal = 0m,
            Descuento = 0m,
            Total = 0m,
            MontoPagado = 0m,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Pedidos.Add(pedido);
        await ctx.SaveChangesAsync();

        var itemA = new PedidoItem
        {
            PedidoId = pedido.Id,
            ProductoId = productoA.Id,
            TipoLinea = TipoLinea.VENTA,
            Cantidad = 1m,
            PrecioUnitario = 1000m,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var itemB = new PedidoItem
        {
            PedidoId = pedido.Id,
            ProductoId = productoB.Id,
            TipoLinea = TipoLinea.VENTA,
            Cantidad = 0.5m,
            PrecioUnitario = 1000m,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.PedidoItems.AddRange(itemA, itemB);
        await ctx.SaveChangesAsync();

        // Recalcular totales manualmente porque el seed los creó con
        // subtotal=0 y el EF aún no ejecutó RecalculateTotalsAsync.
        // Hacemos un UPDATE directo para no depender del método del service
        // y mantener el seed simple.
        var subtotal = itemA.Cantidad * itemA.PrecioUnitario
                     + itemB.Cantidad * itemB.PrecioUnitario;
        await ctx.Database.ExecuteSqlRawAsync(
            "UPDATE pedidos SET subtotal = {0}, total = {0} WHERE id = {1}",
            subtotal, pedido.Id);

        return new PedidoItemSeed(
            PedidoId: pedido.Id,
            ProductoId: productoA.Id,
            ItemAId: itemA.Id,
            ItemBId: itemB.Id);
    }

    private sealed record PedidoItemSeed(
        ulong PedidoId,
        ulong ProductoId,
        ulong ItemAId,
        ulong ItemBId);
}

/// <summary>
/// Fixture xUnit que arranca un container MySQL via Testcontainers y provee
/// un método para crear bases frescas con el schema mínimo necesario para
/// probar el soft-delete de <c>pedido_items</c>.
///
/// Réplica del patrón de <see cref="PedidoCanjeMySqlFixture"/> pero con el
/// schema de <c>pedido_items</c> extendido para incluir <c>deleted_at</c> +
/// <c>unique_hash</c> virtual + <c>uk_pedido_items_pedido_producto_tipo</c> +
/// <c>idx_pedido_items_deleted_at</c>, reflejando el estado real de la BD
/// tras la migración 20260607_000003.
///
/// No incluye tablas de garrafas / movimientos / trigger <c>trg_mov_garrafa_ai</c>
/// porque estos tests no ejercitan el flujo de canje.
/// </summary>
public class PedidoItemSoftDeleteMySqlFixture : IAsyncLifetime
{
    private const string MysqlImage = "mysql:8.0";
    private const string RootPassword = "test_root_pwd";
    private const string RootUsername = "root";
    private const string DatabasePrefix = "pi_";

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

    public async Task<ExtraGasDbContext> NewDbContextAsync(string testName)
    {
        _ = testName; // reservado para logging futuro; el nombre es GUID.
        var dbName = DatabasePrefix + Guid.NewGuid().ToString("N");

        await using (var conn = new MySqlConnection(_rootConnectionString))
        {
            await conn.OpenAsync();
            await using var create = conn.CreateCommand();
            create.CommandText = $"CREATE DATABASE `{dbName}` " +
                                 "CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await create.ExecuteNonQueryAsync();
        }

        var connString = _rootConnectionString!
            .Replace("database=placeholder_db", $"database={dbName}", StringComparison.OrdinalIgnoreCase);

        await using (var conn = new MySqlConnection(connString))
        {
            await conn.OpenAsync();
            await using var schema = conn.CreateCommand();
            schema.CommandText = SchemaMinimal;
            await schema.ExecuteNonQueryAsync();
        }

        var serverVersion = ServerVersion.Create(new Version(8, 0, 0), Pomelo.EntityFrameworkCore.MySql.Infrastructure.ServerType.MySql);
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseMySql(connString, serverVersion)
            .Options;
        return new ExtraGasDbContext(options);
    }

    /// <summary>
    /// Schema mínimo para los tests de soft-delete de pedido_items. Réplica
    /// parcial del DDL real, incluyendo la columna <c>deleted_at</c>, la
    /// columna virtual <c>unique_hash</c> y los índices
    /// <c>uk_pedido_items_pedido_producto_tipo</c> +
    /// <c>idx_pedido_items_deleted_at</c> de la migración 20260607_000003.
    /// Sin tablas de garrafas (no se ejercita el flujo de canje acá).
    /// </summary>
    private const string SchemaMinimal = """
        -- Lookups
        CREATE TABLE tipos_producto (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            codigo VARCHAR(30) NOT NULL,
            nombre VARCHAR(100) NOT NULL,
            descripcion VARCHAR(255) NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY uq_tipos_producto_codigo (codigo)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE estados_pedido (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            codigo VARCHAR(30) NOT NULL,
            nombre VARCHAR(100) NOT NULL,
            descripcion VARCHAR(255) NULL,
            es_final BOOLEAN NOT NULL DEFAULT FALSE,
            color VARCHAR(7) NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY uq_estados_pedido_codigo (codigo)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE canales_venta (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            codigo VARCHAR(30) NOT NULL,
            nombre VARCHAR(100) NOT NULL,
            descripcion VARCHAR(255) NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY uq_canales_venta_codigo (codigo)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        -- Personas (columnas mínimas para que EF pueda INSERTar con todas
        -- las propiedades de las entities Empleado/Cliente).
        CREATE TABLE empleados (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            nombre VARCHAR(100) NOT NULL,
            apellido VARCHAR(100) NOT NULL,
            dni VARCHAR(15) NULL,
            cuil VARCHAR(15) NULL,
            telefono VARCHAR(25) NULL,
            email VARCHAR(150) NULL,
            calle VARCHAR(150) NULL,
            numero VARCHAR(10) NULL,
            piso VARCHAR(10) NULL,
            depto VARCHAR(10) NULL,
            ciudad VARCHAR(100) NULL,
            codigo_postal VARCHAR(10) NULL,
            provincia_id BIGINT UNSIGNED NULL,
            fecha_ingreso DATE NULL,
            fecha_egreso DATE NULL,
            usuario_id BIGINT UNSIGNED NULL,
            activo BOOLEAN NOT NULL DEFAULT TRUE,
            observaciones TEXT NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            created_by BIGINT UNSIGNED NULL,
            updated_by BIGINT UNSIGNED NULL,
            deleted_at DATETIME NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE clientes (
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
            activo BOOLEAN NOT NULL DEFAULT 1,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            created_by BIGINT UNSIGNED NULL,
            updated_by BIGINT UNSIGNED NULL,
            deleted_at DATETIME NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE productos (
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

        CREATE TABLE pedidos (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            numero VARCHAR(20) NULL,
            fecha DATETIME NOT NULL,
            fecha_entrega DATETIME NULL,
            cliente_id BIGINT UNSIGNED NOT NULL,
            empleado_id BIGINT UNSIGNED NOT NULL,
            estado_pedido_id BIGINT UNSIGNED NOT NULL,
            canal_venta_id BIGINT UNSIGNED NOT NULL,
            medio_contacto_id BIGINT UNSIGNED NULL,
            subtotal DECIMAL(12,2) NOT NULL DEFAULT 0,
            descuento DECIMAL(12,2) NOT NULL DEFAULT 0,
            total DECIMAL(12,2) NOT NULL DEFAULT 0,
            monto_pagado DECIMAL(12,2) NOT NULL DEFAULT 0,
            saldo DECIMAL(12,2) GENERATED ALWAYS AS (total - monto_pagado) STORED,
            observaciones TEXT NULL,
            motivo_cancelacion VARCHAR(500) NULL,
            direccion_entrega VARCHAR(255) NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            created_by BIGINT UNSIGNED NULL,
            updated_by BIGINT UNSIGNED NULL,
            deleted_at DATETIME NULL,
            CONSTRAINT fk_pedidos_cliente FOREIGN KEY (cliente_id) REFERENCES clientes(id),
            CONSTRAINT fk_pedidos_empleado FOREIGN KEY (empleado_id) REFERENCES empleados(id),
            CONSTRAINT fk_pedidos_estado FOREIGN KEY (estado_pedido_id) REFERENCES estados_pedido(id),
            CONSTRAINT fk_pedidos_canal FOREIGN KEY (canal_venta_id) REFERENCES canales_venta(id),
            CONSTRAINT chk_pedidos_total CHECK (total >= 0),
            CONSTRAINT chk_pedidos_monto_pagado CHECK (monto_pagado >= 0),
            KEY idx_pedidos_cliente (cliente_id, fecha),
            KEY idx_pedidos_estado (estado_pedido_id),
            KEY idx_pedidos_deleted_at (deleted_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        -- pedido_items CON el schema completo de soft-delete:
        -- deleted_at + unique_hash virtual + uk + idx.
        -- Réplica del estado real tras la migración 20260607_000003.
        CREATE TABLE pedido_items (
            id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            pedido_id BIGINT UNSIGNED NOT NULL,
            producto_id BIGINT UNSIGNED NOT NULL,
            tipo_linea ENUM('ENTREGA','DEVOLUCION','VENTA') NOT NULL DEFAULT 'VENTA',
            cantidad DECIMAL(10,2) NOT NULL,
            precio_unitario DECIMAL(12,2) NOT NULL,
            subtotal DECIMAL(12,2) GENERATED ALWAYS AS (cantidad * precio_unitario) STORED,
            observaciones VARCHAR(255) NULL,
            deleted_at DATETIME NULL,
            unique_hash VARCHAR(255) GENERATED ALWAYS AS (
                CONCAT(
                    CAST(pedido_id AS CHAR), '-',
                    CAST(producto_id AS CHAR), '-',
                    tipo_linea, '-',
                    COALESCE(CAST(deleted_at AS CHAR), '0')
                )
            ) VIRTUAL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            CONSTRAINT fk_pedido_items_pedido FOREIGN KEY (pedido_id) REFERENCES pedidos(id) ON DELETE CASCADE,
            CONSTRAINT fk_pedido_items_producto FOREIGN KEY (producto_id) REFERENCES productos(id),
            CONSTRAINT chk_pedido_items_cantidad CHECK (cantidad > 0),
            CONSTRAINT chk_pedido_items_precio CHECK (precio_unitario >= 0),
            KEY idx_pedido_items_pedido (pedido_id),
            KEY idx_pedido_items_producto (producto_id),
            KEY idx_pedido_items_tipo (tipo_linea),
            KEY idx_pedido_items_deleted_at (deleted_at),
            UNIQUE KEY uk_pedido_items_pedido_producto_tipo (unique_hash)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        -- Catálogo sembrado (idempotente gracias a INSERT IGNORE).
        INSERT IGNORE INTO tipos_producto (codigo, nombre) VALUES ('GAS', 'Gas');
        INSERT IGNORE INTO estados_pedido (codigo, nombre, es_final) VALUES
            ('PENDIENTE', 'Pendiente', FALSE),
            ('CONFIRMADO', 'Confirmado', FALSE),
            ('EN_PREPARACION', 'En preparación', FALSE),
            ('ENTREGADO', 'Entregado', TRUE),
            ('CANCELADO', 'Cancelado', TRUE);
        INSERT IGNORE INTO canales_venta (codigo, nombre) VALUES ('PRESENCIAL', 'Presencial');
        """;
}
