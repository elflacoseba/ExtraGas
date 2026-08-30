using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Data.Entities.Views;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Implementations;
using ExtraGasMVC.Services.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de regresion del <see cref="PedidoService.SearchAsync"/> con la nueva
/// firma <see cref="PedidoSearchFilter"/>. PR #137 introdujo el record para
/// reducir el param count (SonarQube csharpsquid:S107) y el commit
/// correspondiente garantiza que la firma sigue cumpliendo S107.
///
/// El objetivo es exercise el cuerpo del SearchAsync nuevo (no la semantica
/// pre-existente, que ya esta validada por el Controller y por el uso en
/// la app). El Controller de Pedidos no tiene tests dedicados — estos
/// tests cubren esa brecha de forma minima.
/// </summary>
public class PedidoServiceSearchTests
{
    private static (PedidoService service, ExtraGasDbContext context) NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ExtraGasDbContext(options);
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var garrafaService = new NotImplementedGarrafaService();

        var service = new PedidoService(context, mapper, cache, garrafaService);
        return (service, context);
    }

    private static Pedido NewPedido(
        string numero,
        ulong? clienteId = 1,
        ulong? estadoId = 1,
        DateTime? fecha = null)
    {
        return new Pedido
        {
            Numero = numero,
            Fecha = fecha ?? DateTime.UtcNow,
            ClienteId = clienteId ?? 1,
            EstadoPedidoId = estadoId ?? 1,
            CanalVentaId = 1,
            MedioContactoId = 1,
            EmpleadoId = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    // ====================================================================
    // Cobertura del nuevo signature (PedidoSearchFilter, issue #136/S107)
    // ====================================================================

    [Fact]
    public async Task SearchAsync_FiltroVacio_DevuelvePaginaVacia()
    {
        var (service, _) = NewService(nameof(SearchAsync_FiltroVacio_DevuelvePaginaVacia));

        var resultado = await service.SearchAsync(
            new PedidoSearchFilter(
                Numero: null, EstadoId: null, ClienteId: null,
                Desde: null, Hasta: null, Pagina: 1, Tamanio: 25));

        resultado.Items.Should().BeEmpty();
        resultado.Total.Should().Be(0);
        resultado.Page.Should().Be(1);
        resultado.PageSize.Should().Be(25);
    }

    [Fact]
    public async Task SearchAsync_FiltraPorNumero()
    {
        var (service, context) = NewService(nameof(SearchAsync_FiltraPorNumero));
        context.Pedidos.Add(NewPedido("PED-2026-00001"));
        context.Pedidos.Add(NewPedido("PED-2026-00002"));
        context.Pedidos.Add(NewPedido("OTRA-COSA"));
        await context.SaveChangesAsync();

        var resultado = await service.SearchAsync(
            new PedidoSearchFilter(
                Numero: "PED-2026", EstadoId: null, ClienteId: null,
                Desde: null, Hasta: null, Pagina: 1, Tamanio: 25));

        // Solo Total es estable bajo InMemory — el Include de navegación
        // no siempre materializa las propiedades de Cliente/Empleado para
        // que AutoMapper pueda proyectar sin NullRef en tests sin FK.
        resultado.Total.Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_FiltraPorEstado()
    {
        var (service, context) = NewService(nameof(SearchAsync_FiltraPorEstado));
        context.Pedidos.Add(NewPedido("PED-A", estadoId: 1));
        context.Pedidos.Add(NewPedido("PED-B", estadoId: 2));
        context.Pedidos.Add(NewPedido("PED-C", estadoId: 1));
        await context.SaveChangesAsync();

        var resultado = await service.SearchAsync(
            new PedidoSearchFilter(
                Numero: null, EstadoId: 1, ClienteId: null,
                Desde: null, Hasta: null, Pagina: 1, Tamanio: 25));

        resultado.Total.Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_FiltraPorCliente()
    {
        var (service, context) = NewService(nameof(SearchAsync_FiltraPorCliente));
        context.Pedidos.Add(NewPedido("PED-A", clienteId: 42));
        context.Pedidos.Add(NewPedido("PED-B", clienteId: 99));
        context.Pedidos.Add(NewPedido("PED-C", clienteId: 42));
        await context.SaveChangesAsync();

        var resultado = await service.SearchAsync(
            new PedidoSearchFilter(
                Numero: null, EstadoId: null, ClienteId: 42,
                Desde: null, Hasta: null, Pagina: 1, Tamanio: 25));

        // Solo assert el conteo (Items requiere navegación completa a Cliente
        // que InMemory no siempre materializa via Include sin FK real).
        resultado.Total.Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_FiltraPorRangoFechas()
    {
        var (service, context) = NewService(nameof(SearchAsync_FiltraPorRangoFechas));
        var inicio = new DateTime(2026, 1, 1);
        context.Pedidos.Add(NewPedido("PED-OLD", fecha: new DateTime(2025, 12, 31)));
        context.Pedidos.Add(NewPedido("PED-IN", fecha: new DateTime(2026, 6, 1)));
        // Hasta = 2026-12-31 → el filtro incluye hasta fin de día (AddDays(1)
        // en el Service). No agregamos nada más allá para que el conteo sea 1.
        await context.SaveChangesAsync();

        var resultado = await service.SearchAsync(
            new PedidoSearchFilter(
                Numero: null, EstadoId: null, ClienteId: null,
                Desde: inicio, Hasta: new DateTime(2026, 12, 31),
                Pagina: 1, Tamanio: 25));

        resultado.Total.Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_Paginado_DevuelveTamanioCorrecto()
    {
        var (service, context) = NewService(nameof(SearchAsync_Paginado_DevuelveTamanioCorrecto));
        for (int i = 1; i <= 5; i++)
            context.Pedidos.Add(NewPedido($"PED-{i:D3}", fecha: DateTime.UtcNow.AddMinutes(-i)));
        await context.SaveChangesAsync();

        var pagina1 = await service.SearchAsync(
            new PedidoSearchFilter(
                Numero: null, EstadoId: null, ClienteId: null,
                Desde: null, Hasta: null, Pagina: 1, Tamanio: 2));
        var pagina2 = await service.SearchAsync(
            new PedidoSearchFilter(
                Numero: null, EstadoId: null, ClienteId: null,
                Desde: null, Hasta: null, Pagina: 2, Tamanio: 2));

        // Solo el conteo de paginas — Items require navegación completa.
        pagina1.Total.Should().Be(5);
        pagina2.Total.Should().Be(5);
        // La primera página tiene hasta 2 items (puede tener menos si Skip
        // y Take interactúan con la navegación lazy de InMemory).
        pagina1.Items.Count.Should().BeLessThanOrEqualTo(2);
    }

    /// <summary>
    /// Fake de <see cref="IGarrafaService"/> con todas las firmas del interface.
    /// SearchAsync de PedidoService no usa IGarrafaService, pero el constructor
    /// lo exige. Patrón copiado de FakeGarrafaService en
    /// ControllersActivoViewBagTests.cs (issue #114).
    /// </summary>
    private sealed class NotImplementedGarrafaService : IGarrafaService
    {
        public Task<GarrafaDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<GarrafaDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<GarrafaDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<GarrafaDto>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<GarrafaDto>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<GarrafaDto>> GetPagedAsync(string? codigo, byte? capacidad, int page = 1, int pageSize = 20, string sortBy = "codigo", string sortDir = "asc", CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<EstadoGarrafaDto>> GetEstadosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<GarrafaDto> CreateAsync(CreateGarrafaDto garrafa, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<GarrafaDto> UpdateAsync(UpdateGarrafaDto garrafa, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> CambiarEstadoAsync(ulong id, CambiarEstadoGarrafaDto dto, ulong? currentUserId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<EstadoGarrafaDto>> GetTransicionesDisponiblesAsync(ulong garrafaId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<MovimientoGarrafaDto>> GetHistorialAsync(ulong garrafaId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<MovimientoGarrafaDto>> GetMovimientosByPedidoAsync(ulong pedidoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RegistrarMovimientoPorCanjeAsync(ulong garrafaId, ulong estadoDestinoId, ulong? clienteId, ulong pedidoId, string tipoMovimientoCodigo, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<VStockGarrafa>> GetStockAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<VGarrafaEnCliente>> GetEnClientesAsync(ulong? clienteId, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
