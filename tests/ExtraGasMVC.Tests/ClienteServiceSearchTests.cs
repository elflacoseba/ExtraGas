using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de regresión de <see cref="ClienteService.SearchAsync"/>.
/// Issue #112: la búsqueda con filtros combinados (texto + soloActivos +
/// paginación) y la normalización de DNI/teléfono en el query (Issue #113)
/// no estaban cubiertas. Estos tests blindan el contrato del método para
/// que un refactor no rompa los flujos del index, papelera o auto-complete
/// del operador.
///
/// Patrón: DbContext sobre InMemoryDatabase (unico por test) +
/// AutoMapper real + MemoryCache, igual que <see cref="ClienteServiceTests"/>.
/// InMemory no ejecuta el UNIQUE INDEX real (cubierto por
/// <see cref="ClienteIntegrationTests"/>) pero alcanza para ejercitar la
/// composición de LINQ del Service.
/// </summary>
public class ClienteServiceSearchTests
{
    private static (ClienteService service, ExtraGasDbContext context) NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ExtraGasDbContext(options);
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var service = new ClienteService(context, mapper, cache, NullLogger<ClienteService>.Instance);
        return (service, context);
    }

    private static CreateClienteDto NewCreateDto(
        string nombre,
        string apellido,
        string dni,
        string telefono,
        string cuitCuil = "20-12345678-9") => new()
    {
        Nombre = nombre,
        Apellido = apellido,
        Dni = dni,
        TelefonoPrincipal = telefono,
        CuitCuil = cuitCuil,
    };

    // ====================================================================
    // Búsqueda por texto: matches en Nombre / Apellido / DNI / CuitCuil / Teléfono
    // ====================================================================

    [Fact]
    public async Task SearchAsync_BusquedaPorNombre_DevuelveClienteQueMatchea()
    {
        var dbName = nameof(SearchAsync_BusquedaPorNombre_DevuelveClienteQueMatchea);
        var (service, _) = NewService(dbName);

        await service.CreateAsync(NewCreateDto("Juan", "Perez", "11111111", "1144556677"), createdBy: 1);
        await service.CreateAsync(NewCreateDto("Ana", "Gomez", "22222222", "1144556688"), createdBy: 1);

        var page = await service.SearchAsync(busqueda: "Juan", soloActivos: true, pagina: 1, tamanio: 25);

        page.Total.Should().Be(1);
        page.Items.Should().ContainSingle()
            .Which.Nombre.Should().Be("Juan");
    }

    [Fact]
    public async Task SearchAsync_BusquedaPorApellido_DevuelveClienteQueMatchea()
    {
        var dbName = nameof(SearchAsync_BusquedaPorApellido_DevuelveClienteQueMatchea);
        var (service, _) = NewService(dbName);

        await service.CreateAsync(NewCreateDto("Juan", "Perez", "11111111", "1144556677"), createdBy: 1);
        await service.CreateAsync(NewCreateDto("Ana", "Gomez", "22222222", "1144556688"), createdBy: 1);

        var page = await service.SearchAsync(busqueda: "Gomez", soloActivos: true, pagina: 1, tamanio: 25);

        page.Total.Should().Be(1);
        page.Items.Should().ContainSingle()
            .Which.Apellido.Should().Be("Gomez");
    }

    [Fact]
    public async Task SearchAsync_BusquedaPorDniNormalizado_DevuelveCliente_Issue113()
    {
        // Issue #113: el operador puede tipear el DNI con separadores
        // (" 12.345.678 ") y debe matchear al cliente cuyo DNI canónico
        // en BD es "12345678". Si esto se rompe, el buscador no encuentra
        // clientes que el operador "ve" en la lista.
        var dbName = nameof(SearchAsync_BusquedaPorDniNormalizado_DevuelveCliente_Issue113);
        var (service, _) = NewService(dbName);

        // El Service normaliza "12.345.678" al guardar -> BD queda con "12345678".
        await service.CreateAsync(NewCreateDto("Juan", "Perez", "12.345.678", "1144556677"), createdBy: 1);

        // Búsqueda con DNI en formato crudo (con puntos y espacios).
        var page = await service.SearchAsync(busqueda: "  12.345.678  ", soloActivos: true, pagina: 1, tamanio: 25);

        page.Total.Should().Be(1, "el DNI del query debe normalizarse para matchear el canónico en BD");
        page.Items.Should().ContainSingle()
            .Which.Dni.Should().Be("12345678");
    }

    [Fact]
    public async Task SearchAsync_BusquedaPorTelefonoNormalizado_DevuelveCliente_Issue113()
    {
        // Issue #113: idem DNI, pero para teléfono. NOTA: NormalizarTelefono
        // conserva el '+' inicial (prefijo internacional), así que el valor
        // canónico en BD es "+541144556677". El query "1144556677" (sin '+')
        // se normaliza a "1144556677" y matchea como subcadena sobre el
        // canónico de la BD.
        var dbName = nameof(SearchAsync_BusquedaPorTelefonoNormalizado_DevuelveCliente_Issue113);
        var (service, _) = NewService(dbName);

        await service.CreateAsync(NewCreateDto("Juan", "Perez", "11111111", "+54 11 4455-6677"), createdBy: 1);

        var page = await service.SearchAsync(busqueda: "1144556677", soloActivos: true, pagina: 1, tamanio: 25);

        page.Total.Should().Be(1, "el teléfono del query debe normalizarse");
        page.Items.Should().ContainSingle()
            .Which.TelefonoPrincipal.Should().Be("+541144556677",
                "NormalizarTelefono conserva el '+' inicial del código de país");
    }

    [Fact]
    public async Task SearchAsync_BusquedaSinMatch_DevuelveTotalCero()
    {
        var dbName = nameof(SearchAsync_BusquedaSinMatch_DevuelveTotalCero);
        var (service, _) = NewService(dbName);

        await service.CreateAsync(NewCreateDto("Juan", "Perez", "11111111", "1144556677"), createdBy: 1);

        var page = await service.SearchAsync(busqueda: "Inexistente XYZ", soloActivos: true, pagina: 1, tamanio: 25);

        page.Total.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    // ====================================================================
    // Filtro soloActivos: combina con QueryFilter global (soft-delete)
    // ====================================================================

    [Fact]
    public async Task SearchAsync_SoloActivosTrue_ExcluyeClienteSoftDeleted()
    {
        // Cliente soft-deleted NO debe aparecer aunque el query no lo excluya
        // explicitamente: el QueryFilter global del DbContext lo filtra.
        var dbName = nameof(SearchAsync_SoloActivosTrue_ExcluyeClienteSoftDeleted);
        var (service, _) = NewService(dbName);

        await service.CreateAsync(NewCreateDto("Juan", "Perez", "11111111", "1144556677"), createdBy: 1);
        await service.CreateAsync(NewCreateDto("Ana", "Gomez", "22222222", "1144556688"), createdBy: 1);
        var tercero = await service.CreateAsync(NewCreateDto("Luis", "Lopez", "33333333", "1144556699"), createdBy: 1);
        await service.DeleteAsync(tercero.Id, updatedBy: 1);

        var page = await service.SearchAsync(busqueda: null, soloActivos: true, pagina: 1, tamanio: 25);

        page.Total.Should().Be(2, "soft-deleted debe quedar fuera por el QueryFilter global");
        page.Items.Select(c => c.Id).Should().NotContain(tercero.Id);
    }

    [Fact]
    public async Task SearchAsync_SoloActivosFalse_TambienExcluyeSoftDeleted_PorQueryFilter()
    {
        // El QueryFilter global es independiente de soloActivos: aunque el
        // operador pida "todos", el service nunca devuelve soft-deleted en
        // SearchAsync (la papelera tiene su propio método: GetDeletedAsync).
        var dbName = nameof(SearchAsync_SoloActivosFalse_TambienExcluyeSoftDeleted_PorQueryFilter);
        var (service, _) = NewService(dbName);

        await service.CreateAsync(NewCreateDto("Juan", "Perez", "11111111", "1144556677"), createdBy: 1);
        var segundo = await service.CreateAsync(NewCreateDto("Ana", "Gomez", "22222222", "1144556688"), createdBy: 1);
        await service.DeleteAsync(segundo.Id, updatedBy: 1);

        var page = await service.SearchAsync(busqueda: null, soloActivos: false, pagina: 1, tamanio: 25);

        page.Total.Should().Be(1, "el QueryFilter global siempre aplica");
        page.Items.Should().ContainSingle()
            .Which.Nombre.Should().Be("Juan");
    }

    // ====================================================================
    // Paginación
    // ====================================================================

    [Fact]
    public async Task SearchAsync_Paginacion_RespetaTamanioYOrdenPorApellido()
    {
        var dbName = nameof(SearchAsync_Paginacion_RespetaTamanioYOrdenPorApellido);
        var (service, _) = NewService(dbName);

        // Sembrar 5 clientes con apellidos A, B, C, D, E.
        await service.CreateAsync(NewCreateDto("a", "Aaaa", "11111111", "1100000001"), createdBy: 1);
        await service.CreateAsync(NewCreateDto("b", "Bbbb", "22222222", "1100000002"), createdBy: 1);
        await service.CreateAsync(NewCreateDto("c", "Cccc", "33333333", "1100000003"), createdBy: 1);
        await service.CreateAsync(NewCreateDto("d", "Dddd", "44444444", "1100000004"), createdBy: 1);
        await service.CreateAsync(NewCreateDto("e", "Eeee", "55555555", "1100000005"), createdBy: 1);

        var pagina1 = await service.SearchAsync(busqueda: null, soloActivos: true, pagina: 1, tamanio: 2);
        var pagina2 = await service.SearchAsync(busqueda: null, soloActivos: true, pagina: 2, tamanio: 2);
        var pagina3 = await service.SearchAsync(busqueda: null, soloActivos: true, pagina: 3, tamanio: 2);

        pagina1.Total.Should().Be(5);
        pagina1.Items.Should().HaveCount(2);
        pagina2.Items.Should().HaveCount(2);
        pagina3.Items.Should().HaveCount(1);

        // Orden por Apellido asc: A, B | C, D | E
        pagina1.Items.Select(c => c.Apellido).Should().Equal("Aaaa", "Bbbb");
        pagina2.Items.Select(c => c.Apellido).Should().Equal("Cccc", "Dddd");
        pagina3.Items.Select(c => c.Apellido).Should().Equal("Eeee");
    }

    [Fact]
    public async Task SearchAsync_FiltrosCombinados_BusquedaYSoloActivosYPaginacion()
    {
        // El caso del index del operador: filtra por texto, solo activos,
        // y pagina. Verifica que los tres filtros componen correctamente.
        var dbName = nameof(SearchAsync_FiltrosCombinados_BusquedaYSoloActivosYPaginacion);
        var (service, _) = NewService(dbName);

        // 4 clientes apellido "Perez", 2 de ellos soft-deleted
        await service.CreateAsync(NewCreateDto("Juan", "Perez", "11111111", "1100000001"), createdBy: 1);
        await service.CreateAsync(NewCreateDto("Ana", "Perez", "22222222", "1100000002"), createdBy: 1);
        var soft1 = await service.CreateAsync(NewCreateDto("Luis", "Perez", "33333333", "1100000003"), createdBy: 1);
        var soft2 = await service.CreateAsync(NewCreateDto("Maria", "Perez", "44444444", "1100000004"), createdBy: 1);
        await service.DeleteAsync(soft1.Id, updatedBy: 1);
        await service.DeleteAsync(soft2.Id, updatedBy: 1);

        // Y un cliente con otro apellido para verificar que el filtro de texto aplica.
        await service.CreateAsync(NewCreateDto("Pedro", "Gomez", "55555555", "1100000005"), createdBy: 1);

        var page = await service.SearchAsync(busqueda: "Perez", soloActivos: true, pagina: 1, tamanio: 10);

        page.Total.Should().Be(2, "solo 2 clientes Perez activos (los soft-deleted se excluyen)");
        page.Items.Should().HaveCount(2);
        page.Items.Select(c => c.Apellido).Should().AllBe("Perez");
        page.Items.Select(c => c.Id).Should().NotContain(new[] { soft1.Id, soft2.Id });
    }
}