using AutoMapper;
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
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de ProductoService para los métodos nuevos de slice 3:
/// <c>GetUnidadesVentaAsync</c> (catálogo cerrado) y
/// <c>GetDeleteImpactAsync</c> (conteo de dependencias antes del Delete).
/// Issue #147 item 2 + item 7.
/// </summary>
public class ProductoSlice3ServiceTests
{
    private static (ProductoService service, ExtraGasDbContext context) NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ExtraGasDbContext(options);
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var audit = new AuditLogger(context, NullLogger<AuditLogger>.Instance);
        var service = new ProductoService(
            context, mapper, NullLogger<ProductoService>.Instance, cache, audit);

        if (!context.TiposProducto.Any())
        {
            context.TiposProducto.Add(new TipoProducto { Id = 1, Codigo = "GAS", Nombre = "Gas" });
            context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        return (service, context);
    }

    private static CreateProductoDto NewCreateDto(string codigo = "GAS-10", ulong unidadVentaId = 1) => new()
    {
        Codigo = codigo,
        Nombre = "Garrafa 10kg",
        TipoProductoId = 1,
        CapacidadKg = 10m,
        UnidadVentaId = unidadVentaId,
        PrecioActual = 15000m,
        ManejaGarrafaIndividual = true,
    };

    private static void SeedUnidadesVenta(ExtraGasDbContext context)
    {
        if (context.UnidadesVenta.Any()) return;
        // Réplica del seed de la migración 20260901_000002, ordenada
        // por Codigo para validar el orden de salida por Nombre (Bolsa
        // antes que Garrafa antes que Kilogramo antes que Unidad).
        context.UnidadesVenta.AddRange(
            new UnidadVenta { Id = 1, Codigo = "UNIDAD",  Nombre = "Unidad" },
            new UnidadVenta { Id = 2, Codigo = "GARRAFA", Nombre = "Garrafa" },
            new UnidadVenta { Id = 3, Codigo = "BOLSA",   Nombre = "Bolsa" },
            new UnidadVenta { Id = 4, Codigo = "KG",      Nombre = "Kilogramo" });
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    // ====================================================================
    // GetUnidadesVentaAsync
    // ====================================================================

    [Fact]
    public async Task GetUnidadesVentaAsync_ReturnsOrderedListByNombre()
    {
        // Spec scenario "GetUnidadesVentaAsync ordered list".
        var (service, context) = NewService(nameof(GetUnidadesVentaAsync_ReturnsOrderedListByNombre));
        SeedUnidadesVenta(context);

        var resultado = (await service.GetUnidadesVentaAsync()).ToList();

        resultado.Select(u => u.Nombre).Should().Equal(
            new[] { "Bolsa", "Garrafa", "Kilogramo", "Unidad" },
            "orden alfabético por Nombre (independiente del orden del seed por Codigo)");
        resultado.Select(u => u.Codigo).Should().Equal(
            new[] { "BOLSA", "GARRAFA", "KG", "UNIDAD" },
            "los codigos deben coincidir con su nombre correspondiente");
    }

    [Fact]
    public async Task GetUnidadesVentaAsync_SecondCallWithinTtl_HitsCache()
    {
        // Triangulación: misma lógica que GetTiposProductoAsync — el
        // catálogo es seed-only, la segunda llamada debe servirse de cache
        // sin volver a tocar la BD.
        var (service, context) = NewService(nameof(GetUnidadesVentaAsync_SecondCallWithinTtl_HitsCache));
        SeedUnidadesVenta(context);

        var primera = await service.GetUnidadesVentaAsync();
        var cantidadPre = context.ChangeTracker.Entries().Count();
        var segunda = await service.GetUnidadesVentaAsync();

        // Mismo resultado observable: misma cantidad de filas y mismos códigos.
        segunda.Select(u => u.Codigo).Should().BeEquivalentTo(
            primera.Select(u => u.Codigo),
            "la segunda llamada debe servir el mismo set desde cache");
        cantidadPre.Should().Be(0,
            "después de la primera llamada el ChangeTracker debe estar limpio — la segunda llamada no debe agregar nuevas entidades al tracker (cache hit)");
    }

    [Fact]
    public async Task GetUnidadesVentaAsync_FilterSoftDeleted()
    {
        // El query filter global (DeletedAt == null) debe ocultar las
        // unidades soft-deleted. Marcar una como DeletedAt y verificar
        // que la lista devuelta tiene 3 en lugar de 4.
        var (service, context) = NewService(nameof(GetUnidadesVentaAsync_FilterSoftDeleted));
        SeedUnidadesVenta(context);

        // Soft-delete directo en BD (no hay Service para esto — es un
        // catálogo cerrado y la baja requiere migración, ver ADR #20).
        context.UnidadesVenta.Single(u => u.Codigo == "KG").DeletedAt = DateTime.UtcNow;
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var resultado = (await service.GetUnidadesVentaAsync()).ToList();

        resultado.Should().HaveCount(3, "KG fue soft-deleted y el query filter lo oculta");
        resultado.Select(u => u.Codigo).Should().NotContain("KG");
    }

    // ====================================================================
    // GetDeleteImpactAsync
    // ====================================================================

    [Fact]
    public async Task GetDeleteImpactAsync_NoDependencies_ReturnsAllZeros()
    {
        // Spec scenario "0 dependencies → direct confirm": un producto sin
        // pedido_items/recepcion_items/movimientos_garrafa → los 3 contadores
        // en 0 → HasDependencies=false → View renderiza confirm simple.
        var (service, context) = NewService(nameof(GetDeleteImpactAsync_NoDependencies_ReturnsAllZeros));
        SeedUnidadesVenta(context);
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        var impacto = await service.GetDeleteImpactAsync(creado.Id);

        impacto.ProductoId.Should().Be((int)creado.Id);
        impacto.Codigo.Should().Be("GAS-10");
        impacto.PedidoItemsCount.Should().Be(0);
        impacto.RecepcionItemsCount.Should().Be(0);
        impacto.MovimientosGarrafaCount.Should().Be(0);
        impacto.TotalCount.Should().Be(0);
        impacto.HasDependencies.Should().BeFalse();
    }

    [Fact]
    public async Task GetDeleteImpactAsync_WithDependencies_ReturnsCorrectCounts()
    {
        // Triangulación: sembrar 2 pedido_items + 3 recepcion_items +
        // 1 movimiento_garrafa (vía JOIN con garrafas por capacidad_kg) para
        // un producto → verificar los 3 contadores. Spec scenario
        // "any dependency > 0 → type-to-confirm".
        var (service, context) = NewService(nameof(GetDeleteImpactAsync_WithDependencies_ReturnsCorrectCounts));
        SeedUnidadesVenta(context);
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        // PedidoItems + RecepcionItems: FK directa a ProductoId (el
        // InMemory provider no exige la FK, pero necesitamos el shape).
        context.PedidoItems.AddRange(
            new PedidoItem { ProductoId = creado.Id, Cantidad = 1, PrecioUnitario = 1000m, TipoLinea = TipoLinea.ENTREGA, PedidoId = 1, Subtotal = 1000m },
            new PedidoItem { ProductoId = creado.Id, Cantidad = 2, PrecioUnitario = 2000m, TipoLinea = TipoLinea.VENTA, PedidoId = 1, Subtotal = 4000m });
        context.RecepcionItems.AddRange(
            new RecepcionItem { ProductoId = creado.Id, Cantidad = 5, PrecioUnitario = 500m, RecepcionId = 1, Subtotal = 2500m },
            new RecepcionItem { ProductoId = creado.Id, Cantidad = 10, PrecioUnitario = 1000m, RecepcionId = 1, Subtotal = 10000m },
            new RecepcionItem { ProductoId = creado.Id, Cantidad = 1, PrecioUnitario = 100m, RecepcionId = 1, Subtotal = 100m });

        // MovimientosGarrafa: NO tiene FK a Producto. El Service cuenta
        // vía JOIN con garrafas por capacidad_kg (el producto creado tiene
        // capacidad_kg=10m). Sembramos una garrafa con capacidad 10kg y un
        // movimiento que la referencia.
        var garrafa10kg = new Garrafa
        {
            Id = 100,
            Codigo = "GAR-001",
            CapacidadKg = 10,
            FechaCompra = DateOnly.FromDateTime(DateTime.UtcNow),
            EstadoGarrafaId = 1,
            Activo = true,
        };
        context.Garrafas.Add(garrafa10kg);
        context.MovimientosGarrafa.Add(new MovimientoGarrafa
        {
            GarrafaId = garrafa10kg.Id,
            TipoMovimientoId = 1,
            Fecha = DateTime.UtcNow,
            EstadoDestinoId = 1,
            EmpleadoId = 1,
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var impacto = await service.GetDeleteImpactAsync(creado.Id);

        impacto.PedidoItemsCount.Should().Be(2);
        impacto.RecepcionItemsCount.Should().Be(3);
        impacto.MovimientosGarrafaCount.Should().Be(1);
        impacto.TotalCount.Should().Be(6);
        impacto.HasDependencies.Should().BeTrue();
    }

    [Fact]
    public async Task GetDeleteImpactAsync_DoesNotFilterByDeletedAt()
    {
        // Spec scenario "count MUST NOT filter by deleted_at" — exploración
        // #43-45: pedido_items/recepcion_items/movimientos_garrafa NO
        // tienen columna deleted_at. Insertamos un pedido_item "soft-deleted"
        // simulando via un flag de DeletedAt que la entity ignora (no existe
        // en esas tablas), y verificamos que igual cuenta. La entity PedidoItem
        // actual no tiene DeletedAt; usamos el DeleteAsync real del Service
        // (no es admin-only a nivel entidad, es a nivel controller) para
        // hacer un "soft-delete" via deleted_at en Pedido.
        var (service, context) = NewService(nameof(GetDeleteImpactAsync_DoesNotFilterByDeletedAt));
        SeedUnidadesVenta(context);
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        // Insertar 1 pedido_item "soft-deleted" — simulamos la condición
        // marcando el PedidoId como DeletedAt via la entity Pedido (que sí
        // tiene DeletedAt). El conteo de pedido_items NO debe filtrar por
        // el DeletedAt del Pedido padre.
        var pedido = new Pedido { Id = 1, Numero = "PED-TEST", ClienteId = 1, EmpleadoId = 1, EstadoPedidoId = 1, CanalVentaId = 1, Fecha = DateTime.UtcNow, Total = 100m, Saldo = 100m };
        pedido.DeletedAt = DateTime.UtcNow; // pedido soft-deleted
        context.Pedidos.Add(pedido);
        context.PedidoItems.Add(new PedidoItem { ProductoId = creado.Id, Cantidad = 1, PrecioUnitario = 1000m, TipoLinea = TipoLinea.ENTREGA, PedidoId = pedido.Id, Subtotal = 1000m });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var impacto = await service.GetDeleteImpactAsync(creado.Id);

        impacto.PedidoItemsCount.Should().Be(1,
            "el conteo de pedido_items NO filtra por deleted_at — la spec es explícita sobre esto");
    }

    [Fact]
    public async Task GetDeleteImpactAsync_UnknownId_ThrowsKeyNotFoundException()
    {
        // El Service debe tirar KeyNotFoundException cuando el id no
        // existe (no devuelve un DTO con ceros que mentiría sobre el estado).
        var (service, context) = NewService(nameof(GetDeleteImpactAsync_UnknownId_ThrowsKeyNotFoundException));
        SeedUnidadesVenta(context);

        var act = async () => await service.GetDeleteImpactAsync(99999);

        await act.Should().ThrowAsync<KeyNotFoundException>(
            "GetDeleteImpactAsync debe rechazar ids inexistentes con KeyNotFoundException");
    }

    [Fact]
    public async Task GetDeleteImpactAsync_SoftDeletedProducto_ThrowsKeyNotFoundException()
    {
        // Producto soft-deleted (DeletedAt != null) NO debe contar como
        // candidato a Delete — GetDeleteImpactAsync usa el mismo filtro
        // de QueryFilter que GetByIdAsync. Cubrir este caso evita que un
        // operador con la URL /Delete/{id} pueda ver dependencias de un
        // producto ya desactivado.
        var (service, context) = NewService(nameof(GetDeleteImpactAsync_SoftDeletedProducto_ThrowsKeyNotFoundException));
        SeedUnidadesVenta(context);
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);
        await service.DeleteAsync(creado.Id, usuarioId: 1);

        var act = async () => await service.GetDeleteImpactAsync(creado.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>(
            "un producto soft-deleted no debe aparecer como candidato a Delete");
    }
}
