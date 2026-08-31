using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
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
/// Tests del hook de auditoría genérica en <see cref="ProductoService.UpdateAsync"/>
/// (issue #147 slice 2).
///
/// <para>Contrato del spec scenario "Auditoría de cambios por campo":</para>
/// <list type="bullet">
///   <item>Un cambio real de un campo auditable debe dejar exactamente UNA
///   fila en <c>audit_log</c> con <c>entidad="Producto"</c>, <c>registro_id</c>
///   del producto, <c>campo</c> = nombre del campo, <c>valor_anterior</c>/
///   <c>valor_nuevo</c> como string, y <c>user_id</c> del operator.</item>
///   <item>Un UPDATE sin cambios (no-op) NO debe generar filas.</item>
///   <item>Cambios en múltiples campos generan múltiples filas (1:1).</item>
/// </list>
/// </summary>
public class ProductoAuditLogTests
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

    private static CreateProductoDto NewCreateDto(string codigo = "GAS-10") => new()
    {
        Codigo = codigo,
        Nombre = "Garrafa 10kg",
        TipoProductoId = 1,
        CapacidadKg = 10m,
        UnidadVenta = "UNIDAD",
        PrecioActual = 1000m,
        ManejaGarrafaIndividual = true,
    };

    private static UpdateProductoDto NewUpdateDto(ProductoDto creado, decimal? nuevoPrecio = null) => new()
    {
        Id = creado.Id,
        Codigo = creado.Codigo,
        Nombre = creado.Nombre,
        Descripcion = creado.Descripcion,
        TipoProductoId = creado.TipoProductoId,
        CapacidadKg = creado.CapacidadKg,
        UnidadVenta = creado.UnidadVenta,
        PrecioActual = nuevoPrecio ?? creado.PrecioActual,
        ManejaGarrafaIndividual = creado.ManejaGarrafaIndividual,
        MotivoCambioPrecio = null,
    };

    [Fact]
    public async Task UpdateAsync_PriceChange_EmitsOneAuditLogRow()
    {
        // Spec scenario "precio change emits one row":
        // PrecioActual 1000 → 1500 con userId=42 → exactamente una fila
        // con campo="PrecioActual", valor_anterior="1000", valor_nuevo="1500",
        // user_id=42.
        var (service, context) = NewService(nameof(UpdateAsync_PriceChange_EmitsOneAuditLogRow));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        await service.UpdateAsync(NewUpdateDto(creado, nuevoPrecio: 1500m), usuarioId: 42);

        var entries = await context.AuditLog.AsNoTracking().ToListAsync();
        entries.Should().ContainSingle(
            "un solo cambio de precio debe dejar exactamente una fila en audit_log");
        var entry = entries[0];
        entry.Entidad.Should().Be("Producto");
        entry.RegistroId.Should().Be(creado.Id);
        entry.Campo.Should().Be("PrecioActual");
        entry.ValorAnterior.Should().Be("1000",
            "PrecioActual anterior serializado con la representación que el Service eligió");
        entry.ValorNuevo.Should().Be("1500");
        entry.UserId.Should().Be(42UL);
    }

    [Fact]
    public async Task UpdateAsync_NoChange_EmitsZeroAuditLogRows()
    {
        // Spec scenario "no-op update emits zero rows": el operador
        // reenvía el form sin tocar nada → no hay diff → no se loguea.
        var (service, context) = NewService(nameof(UpdateAsync_NoChange_EmitsZeroAuditLogRows));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        await service.UpdateAsync(NewUpdateDto(creado), usuarioId: 1);

        var entries = await context.AuditLog.AsNoTracking().ToListAsync();
        entries.Should().BeEmpty(
            "un Update sin cambios reales no debe generar filas de audit");
    }

    [Fact]
    public async Task UpdateAsync_MultipleFieldChange_EmitsOneRowPerChangedField()
    {
        // Triangulación: cambiar 3 campos en el mismo UPDATE debe dejar
        // 3 filas (no 1, no 4+). Esto valida que la iteración es por
        // campo y no por Update.
        var (service, context) = NewService(nameof(UpdateAsync_MultipleFieldChange_EmitsOneRowPerChangedField));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        var dto = new UpdateProductoDto
        {
            Id = creado.Id,
            Codigo = creado.Codigo,
            Nombre = "Garrafa 10kg v2",         // cambio 1
            Descripcion = "Nueva descripción",   // cambio 2
            TipoProductoId = creado.TipoProductoId,
            CapacidadKg = creado.CapacidadKg,
            UnidadVenta = creado.UnidadVenta,
            PrecioActual = 1500m,                // cambio 3
            ManejaGarrafaIndividual = creado.ManejaGarrafaIndividual,
        };
        await service.UpdateAsync(dto, usuarioId: 7);

        var entries = await context.AuditLog.AsNoTracking().ToListAsync();
        entries.Should().HaveCount(3, "1 fila por cada campo modificado (Nombre + Descripcion + PrecioActual)");
        entries.Select(e => e.Campo).Should().BeEquivalentTo(new[] { "Nombre", "Descripcion", "PrecioActual" });
        entries.Should().OnlyContain(e => e.Entidad == "Producto");
        entries.Should().OnlyContain(e => e.RegistroId == creado.Id);
        entries.Should().OnlyContain(e => e.UserId == 7UL);
    }

    [Fact]
    public async Task UpdateAsync_AuditEntryChangedAt_IsWithinCallWindow()
    {
        // Smoke test: la fila de audit lleva un ChangedAt now-ish.
        // Importante para el orden temporal cuando se hacen varios updates
        // consecutivos sobre el mismo producto.
        var (service, context) = NewService(nameof(UpdateAsync_AuditEntryChangedAt_IsWithinCallWindow));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        var before = DateTime.UtcNow.AddSeconds(-1);
        await service.UpdateAsync(NewUpdateDto(creado, nuevoPrecio: 1500m), usuarioId: 1);
        var after = DateTime.UtcNow.AddSeconds(1);

        var entry = await context.AuditLog.AsNoTracking().SingleAsync();
        entry.ChangedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public async Task UpdateAsync_AuditLog_AtomicWithProductUpdate()
    {
        // Contrato atómico (design §"IAuditLogger" + "Transaction model"):
        // la fila de audit y el cambio del producto viven en el mismo
        // SaveChangesAsync. Si el commit falla, no queda fila huérfana.
        // Verificamos que tras un UpdateAsync exitoso, AMBAS escrituras
        // son visibles. (El caso negativo — SaveChanges fallido — se cubre
        // en la rama concurrencia del UpdateAsync, fuera de scope de este
        // test.)
        var (service, context) = NewService(nameof(UpdateAsync_AuditLog_AtomicWithProductUpdate));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        await service.UpdateAsync(NewUpdateDto(creado, nuevoPrecio: 1500m), usuarioId: 1);

        var productoActualizado = await context.Productos.AsNoTracking()
            .FirstAsync(p => p.Id == creado.Id);
        productoActualizado.PrecioActual.Should().Be(1500m,
            "el cambio del producto debe estar persistido");
        (await context.AuditLog.CountAsync()).Should().Be(1,
            "la fila de audit debe estar persistida en la misma transacción");
    }
}
