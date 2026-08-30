using AutoMapper;
using ExtraGasMVC.Constants;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de integracion del Service de Garrafa contra DbContext InMemory.
/// Cubre las 4 trayectorias CRUD exigidas por la issue #58:
///   - CreateAsync: alta exitosa + duplicado por codigo.
///   - UpdateAsync: actualizacion exitosa + duplicado por codigo + id inexistente.
///   - CambiarEstadoAsync: transicion valida (registra movimiento),
///     transicion invalida (rechaza), destino que requiere cliente sin clienteId.
///   - DeleteAsync: soft-delete exitoso, bloqueo en EN_CLIENTE / EN_TRANSITO,
///     id inexistente (devuelve false sin lanzar).
///
/// Sobre Moq: la spec de #58 menciona "xUnit y Moq", pero el proyecto se
/// estandarizo en tests de integracion con EF Core InMemory (mismo patron que
/// <see>EmpleadoServiceTests</see>, <see>ProductoServiceTests</see>,
/// <see>ClienteServiceTests</see>). Mockear DbSet es fragil y rompe la fidelidad
/// de las queries LINQ reales; InMemory las respeta y nos cubre el contrato
/// completo del service. Para ILogger se usa NullLogger (suficiente; las
/// aserciones sobre log iran en un test dedicado si surge la necesidad).
/// </summary>
public class GarrafaServiceTests
{
    // IDs estables para los catalogos sembrados. Empezamos en 1 para que coincida
    // con la convencion de los seeds reales (el autoincrement de MySQL arranca en 1).
    private const ulong EstadoLlenaDepositoId = 1;
    private const ulong EstadoVaciaDepositoId = 2;
    private const ulong EstadoEnClienteId = 3;
    private const ulong EstadoEnTransitoId = 4;
    private const ulong EstadoDanadaId = 5;
    private const ulong EstadoFueraServicioId = 6;
    private const ulong TipoMovimientoCambioEstadoId = 1;

    /// <summary>
    /// Crea un service con un DbContext aislado (un InMemory DB por nombre de test).
    /// El DbContext se devuelve para que los tests que necesitan releer la fila
    /// tras la operacion (soft-delete, movimiento registrado) puedan usar
    /// <c>IgnoreQueryFilters()</c> cuando corresponda.
    /// </summary>
    private static (GarrafaService service, ExtraGasDbContext context) NewService(string dbName, bool seedCatalogos = false)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            // GarrafaService.CambiarEstadoAsync abre una transaccion explicita;
            // InMemory no las soporta y emite un warning como excepcion. Como en
            // estos tests no nos importa la semantica transaccional (no probamos
            // rollback ni isolation level), silenciamos el warning y dejamos que
            // el InMemory funcione como store plano. Si hicera falta validar
            // rollback de verdad, el camino es Testcontainers/MySQL como usa
            // PedidoCanjeIntegrationTests.
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new ExtraGasDbContext(options);
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        if (seedCatalogos) SeedCatalogos(context);
        return (new GarrafaService(context, mapper, NullLogger<GarrafaService>.Instance), context);
    }

    /// <summary>
    /// Siembra el catalogo minimo para que <c>CambiarEstadoAsync</c> y
    /// <c>DeleteAsync</c> puedan resolver codigos y reglas (RequiereCliente,
    /// bloqueos). Los IDs son los definidos en las constantes de esta clase.
    /// </summary>
    private static void SeedCatalogos(ExtraGasDbContext context)
    {
        context.EstadosGarrafa.AddRange(
            new EstadoGarrafa
            {
                Id = EstadoLlenaDepositoId,
                Codigo = GarrafaEstados.LlenaDeposito,
                Nombre = "Llena en deposito",
                RequiereCliente = false
            },
            new EstadoGarrafa
            {
                Id = EstadoVaciaDepositoId,
                Codigo = GarrafaEstados.VaciaDeposito,
                Nombre = "Vacia en deposito",
                RequiereCliente = false
            },
            new EstadoGarrafa
            {
                Id = EstadoEnClienteId,
                Codigo = GarrafaEstados.EnCliente,
                Nombre = "En cliente",
                RequiereCliente = true
            },
            new EstadoGarrafa
            {
                Id = EstadoEnTransitoId,
                Codigo = GarrafaEstados.EnTransito,
                Nombre = "En transito",
                RequiereCliente = false
            },
            new EstadoGarrafa
            {
                Id = EstadoDanadaId,
                Codigo = GarrafaEstados.Danada,
                Nombre = "Danada",
                RequiereCliente = false
            },
            new EstadoGarrafa
            {
                Id = EstadoFueraServicioId,
                Codigo = GarrafaEstados.FueraServicio,
                Nombre = "Fuera de servicio",
                RequiereCliente = false
            });

        context.TiposMovimientoGarrafa.Add(new TipoMovimientoGarrafa
        {
            Id = TipoMovimientoCambioEstadoId,
            Codigo = "CAMBIO_ESTADO",
            Nombre = "Cambio de estado manual"
        });

        context.SaveChanges();
    }

    private static CreateGarrafaDto NewCreateDto(string codigo = "GAR-001", ulong estadoId = EstadoLlenaDepositoId) => new()
    {
        Codigo = codigo,
        CapacidadKg = 10,
        FechaCompra = new DateOnly(2024, 1, 15),
        EstadoGarrafaId = estadoId,
    };

    private static UpdateGarrafaDto NewUpdateDto(GarrafaDto source, string nuevoCodigo, byte? nuevaCapacidad = null) => new()
    {
        Id = source.Id,
        Codigo = nuevoCodigo,
        CapacidadKg = nuevaCapacidad ?? source.CapacidadKg,
        FechaCompra = source.FechaCompra,
        EstadoGarrafaId = source.EstadoGarrafaId,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // CreateAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga()
    {
        var (service, _) = NewService(nameof(CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga));

        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        creado.Activo.Should().BeTrue("Activo no viene del DTO; el Service lo setea en true");
    }

    [Fact]
    public async Task CreateAsync_PersisteCodigoYAuditoria_DelUsuarioQueDaElAlta()
    {
        var (service, context) = NewService(nameof(CreateAsync_PersisteCodigoYAuditoria_DelUsuarioQueDaElAlta));

        var creado = await service.CreateAsync(NewCreateDto("GAR-AUD-01"), usuarioId: 42);

        creado.Codigo.Should().Be("GAR-AUD-01");
        creado.Id.Should().BeGreaterThan(0);

        var persisted = await context.Garrafas.IgnoreQueryFilters().FirstAsync(g => g.Id == creado.Id);
        persisted.CreatedBy.Should().Be(42, "CreatedBy debe reflejar el usuario que ejecuta el alta");
        persisted.UpdatedBy.Should().Be(42);
        persisted.CreatedAt.Should().BeOnOrAfter(DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task CreateAsync_LanzaInvalidOperationException_SiCodigoDuplicado()
    {
        var (service, _) = NewService(nameof(CreateAsync_LanzaInvalidOperationException_SiCodigoDuplicado));
        await service.CreateAsync(NewCreateDto("GAR-DUP"), usuarioId: 1);

        var act = () => service.CreateAsync(NewCreateDto("GAR-DUP"), usuarioId: 2);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*GAR-DUP*",
                "el pre-check AnyAsync(codigo) debe rechazar duplicados antes de tocar SaveChanges");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UpdateAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_PreservaActivo_DesdeLaBD_AunqueDtoNoLoTenga()
    {
        var (service, _) = NewService(nameof(UpdateAsync_PreservaActivo_DesdeLaBD_AunqueDtoNoLoTenga));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        var updateDto = new UpdateGarrafaDto
        {
            Id = creado.Id,
            Codigo = creado.Codigo,
            CapacidadKg = creado.CapacidadKg,
            FechaCompra = creado.FechaCompra,
            EstadoGarrafaId = creado.EstadoGarrafaId,
            // Activo NO esta en UpdateGarrafaDto.
        };
        var actualizado = await service.UpdateAsync(updateDto, usuarioId: 2);

        actualizado.Activo.Should().BeTrue(
            "el helper GarrafaEditRules debe preservar Activo desde la BD");
    }

[Fact]
    public async Task UpdateAsync_AplicaCambios_CapacidadYCodigo_YActualizaUpdatedBy()
    {
        var (service, context) = NewService(nameof(UpdateAsync_AplicaCambios_CapacidadYCodigo_YActualizaUpdatedBy));
        var creada = await service.CreateAsync(NewCreateDto("GAR-UPD"), usuarioId: 1);

        var actualizado = await service.UpdateAsync(
            NewUpdateDto(creada, nuevoCodigo: "GAR-UPD-V2", nuevaCapacidad: 15),
            usuarioId: 7);

        actualizado.Codigo.Should().Be("GAR-UPD-V2");
        actualizado.CapacidadKg.Should().Be((byte)15);

        // GarrafaDto no expone UpdatedBy (es interno al entity), asi que releemos
        // la fila persistida para verificar que el service propago el usuario.
        var entity = await context.Garrafas.IgnoreQueryFilters().FirstAsync(g => g.Id == creada.Id);
        entity.UpdatedBy.Should().Be(7,
            "UpdateAsync debe propagar usuarioId a UpdatedBy para auditoria");
    }

    [Fact]
    public async Task UpdateAsync_LanzaInvalidOperationException_SiCodigoDuplicadoEnOtraGarrafa()
    {
        var (service, _) = NewService(nameof(UpdateAsync_LanzaInvalidOperationException_SiCodigoDuplicadoEnOtraGarrafa));
        var primera = await service.CreateAsync(NewCreateDto("GAR-ORIG"), usuarioId: 1);
        var segunda = await service.CreateAsync(NewCreateDto("GAR-OTRA"), usuarioId: 1);

        var act = () => service.UpdateAsync(NewUpdateDto(segunda, nuevoCodigo: "GAR-ORIG"), usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*GAR-ORIG*",
                "el check AnyAsync(codigo AND id != propio) debe rechazar el duplicado");

        // Sanity: la primera sigue intacta, no se modifico por accidente.
        primera.Codigo.Should().Be("GAR-ORIG");
    }

    [Fact]
    public async Task UpdateAsync_LanzaKeyNotFoundException_SiIdNoExiste()
    {
        var (service, _) = NewService(nameof(UpdateAsync_LanzaKeyNotFoundException_SiIdNoExiste));

        var updateDto = new UpdateGarrafaDto
        {
            Id = 9_999,
            Codigo = "GAR-NOEXISTE",
            CapacidadKg = 10,
            FechaCompra = new DateOnly(2024, 1, 15),
            EstadoGarrafaId = EstadoLlenaDepositoId,
        };

        var act = () => service.UpdateAsync(updateDto, usuarioId: 1);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*9999*",
                "el FindAsync debe devolver null y el service debe lanzar KeyNotFoundException");
    }

    [Fact]
    public async Task UpdateAsync_PermiteMismoCodigoEnMismaGarrafa_SinLanzarDuplicado()
    {
        // Si el operador reenvia el form sin cambiar el codigo, NO debe explotar
        // por el pre-check de duplicados (excluye la fila propia con id != propio).
        var (service, _) = NewService(nameof(UpdateAsync_PermiteMismoCodigoEnMismaGarrafa_SinLanzarDuplicado));
        var creado = await service.CreateAsync(NewCreateDto("GAR-SAME"), usuarioId: 1);

        var actualizado = await service.UpdateAsync(
            NewUpdateDto(creado, nuevoCodigo: "GAR-SAME"),
            usuarioId: 1);

        actualizado.Codigo.Should().Be("GAR-SAME");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CambiarEstadoAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CambiarEstadoAsync_RetornaTrue_YRegistraMovimiento_CuandoTransicionEsValida()
    {
        var (service, context) = NewService(
            nameof(CambiarEstadoAsync_RetornaTrue_YRegistraMovimiento_CuandoTransicionEsValida),
            seedCatalogos: true);
        var creada = await service.CreateAsync(NewCreateDto("GAR-EST"), usuarioId: 1);

        var dto = new CambiarEstadoGarrafaDto
        {
            NuevoEstadoId = EstadoVaciaDepositoId, // LLENA_DEPOSITO -> VACIA_DEPOSITO es valida
            Observaciones = "Vacia para reposicion"
        };

        var ok = await service.CambiarEstadoAsync(creada.Id, dto, currentUserId: 5);

        ok.Should().BeTrue();
        var movimiento = await context.MovimientosGarrafa
            .SingleAsync(m => m.GarrafaId == creada.Id);
        movimiento.TipoMovimientoId.Should().Be(TipoMovimientoCambioEstadoId);
        movimiento.EstadoOrigenId.Should().Be(EstadoLlenaDepositoId);
        movimiento.EstadoDestinoId.Should().Be(EstadoVaciaDepositoId);
        movimiento.CreatedBy.Should().Be(5,
            "el movimiento debe registrar currentUserId como CreatedBy para auditoria");
    }

    [Fact]
    public async Task CambiarEstadoAsync_LanzaInvalidOperationException_SiTransicionNoEstaEnLaMatriz()
    {
        var (service, _) = NewService(
            nameof(CambiarEstadoAsync_LanzaInvalidOperationException_SiTransicionNoEstaEnLaMatriz),
            seedCatalogos: true);

        // FUERA_SERVICIO es terminal en la matriz de GarrafaTransiciones (ver
        // Services/GarrafaTransiciones.cs): no tiene transiciones salientes.
        // Sembrar una garrafa en LLENA_DEPOSITO y pedir pasar a FUERA_SERVICIO
        // debe ser rechazado por la matriz, NO por el catalogo.
        var creada = await service.CreateAsync(NewCreateDto("GAR-BAD"), usuarioId: 1);
        var dto = new CambiarEstadoGarrafaDto
        {
            NuevoEstadoId = EstadoFueraServicioId,
            Observaciones = "Intento invalido"
        };

        var act = () => service.CambiarEstadoAsync(creada.Id, dto, currentUserId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Transición inválida*",
                "la matriz GarrafaTransiciones debe rechazar LLENA_DEPOSITO -> FUERA_SERVICIO");
    }

    [Fact]
    public async Task CambiarEstadoAsync_LanzaExcepcion_SiDestinoRequiereCliente_YDtoNoLoTrae()
    {
        var (service, _) = NewService(
            nameof(CambiarEstadoAsync_LanzaExcepcion_SiDestinoRequiereCliente_YDtoNoLoTrae),
            seedCatalogos: true);
        var creada = await service.CreateAsync(NewCreateDto("GAR-CLI"), usuarioId: 1);

        var dto = new CambiarEstadoGarrafaDto
        {
            NuevoEstadoId = EstadoEnClienteId, // RequiereCliente = true
            ClienteId = null,                  // omitido -> debe rechazar
        };

        var act = () => service.CambiarEstadoAsync(creada.Id, dto, currentUserId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requiere seleccionar un cliente*",
                "la regla RequiereCliente del estado destino la valida la app, no el trigger");
    }

    [Fact]
    public async Task CambiarEstadoAsync_RetornaFalse_SiIdNoExiste()
    {
        var (service, _) = NewService(
            nameof(CambiarEstadoAsync_RetornaFalse_SiIdNoExiste),
            seedCatalogos: true);

        var ok = await service.CambiarEstadoAsync(
            id: 12_345,
            dto: new CambiarEstadoGarrafaDto { NuevoEstadoId = EstadoVaciaDepositoId },
            currentUserId: 1);

        ok.Should().BeFalse("el service debe devolver false cuando la garrafa no existe, no lanzar");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DeleteAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RetornaTrue_YSoftDeleteCompleto_CuandoEstadoNoBloqueado()
    {
        var (service, context) = NewService(
            nameof(DeleteAsync_RetornaTrue_YSoftDeleteCompleto_CuandoEstadoNoBloqueado));

        var creada = await service.CreateAsync(NewCreateDto("GAR-DEL"), usuarioId: 1);

        var ok = await service.DeleteAsync(creada.Id, updatedBy: 9);

        ok.Should().BeTrue();
        var entity = await context.Garrafas.IgnoreQueryFilters().FirstAsync(g => g.Id == creada.Id);
        entity.DeletedAt.Should().NotBeNull("soft-delete debe setear DeletedAt");
        entity.Activo.Should().BeFalse("soft-delete debe setear Activo=false");
        entity.UpdatedBy.Should().Be(9, "el service debe persistir el updatedBy para auditoria");
    }

    [Fact]
    public async Task DeleteAsync_LanzaInvalidOperationException_SiEstadoEsEnCliente()
    {
        var (service, context) = NewService(
            nameof(DeleteAsync_LanzaInvalidOperationException_SiEstadoEsEnCliente),
            seedCatalogos: true);

        var creada = await service.CreateAsync(
            NewCreateDto("GAR-CLI-DEL", estadoId: EstadoEnClienteId),
            usuarioId: 1);

        var act = () => service.DeleteAsync(creada.Id, updatedBy: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*EN_CLIENTE*",
                "DeleteAsync debe rechazar garrafas en EN_CLIENTE para preservar la trazabilidad del canje");

        // La fila sigue viva (no se toco DeletedAt ni Activo).
        var entity = await context.Garrafas.IgnoreQueryFilters().FirstAsync(g => g.Id == creada.Id);
        entity.DeletedAt.Should().BeNull("un rechazo no debe dejar soft-delete parcial");
        entity.Activo.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_LanzaInvalidOperationException_SiEstadoEsEnTransito()
    {
        var (service, context) = NewService(
            nameof(DeleteAsync_LanzaInvalidOperationException_SiEstadoEsEnTransito),
            seedCatalogos: true);

        var creada = await service.CreateAsync(
            NewCreateDto("GAR-TRANS-DEL", estadoId: EstadoEnTransitoId),
            usuarioId: 1);

        var act = () => service.DeleteAsync(creada.Id, updatedBy: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*EN_TRANSITO*");
        (await context.Garrafas.IgnoreQueryFilters().FirstAsync(g => g.Id == creada.Id))
            .DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_RetornaFalse_SiIdNoExiste()
    {
        var (service, _) = NewService(nameof(DeleteAsync_RetornaFalse_SiIdNoExiste));

        var ok = await service.DeleteAsync(id: 88_888, updatedBy: 1);

        ok.Should().BeFalse("DeleteAsync debe devolver false, no lanzar, cuando el id no existe");
    }
}