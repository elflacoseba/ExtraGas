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

    // CA1861: arrays literales repetidos en asserts deben ser static readonly
    // (FluentAssertions los pasa a métodos que pueden llamarse varias veces
    // en la suite y reasignar el array entre iteraciones es un footgun).
    private static readonly string[] CodigosGetAllEsperados = ["GAR-A", "GAR-M", "GAR-Z"];
    private static readonly string[] CodigosPorEstadoEsperados = ["GAR-LL", "GAR-LL-2"];

    /// <summary>
    /// Crea un service con un DbContext aislado (un InMemory DB por nombre de test).
    /// El DbContext se devuelve para que los tests que necesitan releer la fila
    /// tras la operacion (soft-delete, movimiento registrado) puedan usar
    /// <c>IgnoreQueryFilters()</c> cuando corresponda.
    /// </summary>
    /// <param name="seedCatalogos">
    /// Default <c>true</c>: siembra el catálogo mínimo (estados_garrafa y
    /// tipos_movimiento_garrafa). El default cambió con la issue #182 T02 —
    /// <c>CreateAsync</c> ahora valida que el <c>EstadoGarrafaId</c> exista en
    /// el catálogo, así que los tests deben reflejar la realidad de producción
    /// (donde el catálogo siempre está sembrado por la migration inicial).
    /// Pasarlo en <c>false</c> sólo tiene sentido para tests que validan
    /// explícitamente el rechazo por estado inexistente.
    /// </param>
    private static (GarrafaService service, ExtraGasDbContext context) NewService(string dbName, bool seedCatalogos = true)
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

        // Issue #182 T05: algunos tests necesitan un cliente activo (no soft-deleted)
        // para validar Create/Update/Cambiar estado. Sembramos dos: el id=1 está
        // "vivo" (DeletedAt=null), el id=2 está dado de baja (DeletedAt set). El
        // query filter global oculta los soft-deleted, así que ValidarClienteActivoAsync
        // los rechaza vía AnyAsync.
        context.Clientes.AddRange(
            new Cliente
            {
                Id = 1,
                Codigo = "CLI-001",
                Apellido = "Garrafa",
                Nombre = "Test",
                Dni = "11111111",
                TelefonoPrincipal = "1111111111",
                FechaAlta = new DateOnly(2024, 1, 1),
                CreatedBy = 1,
                UpdatedBy = 1,
            },
            new Cliente
            {
                Id = 2,
                Codigo = "CLI-002",
                Apellido = "Dado",
                Nombre = "Baja",
                Dni = "22222222",
                TelefonoPrincipal = "2222222222",
                FechaAlta = new DateOnly(2024, 1, 1),
                CreatedBy = 1,
                UpdatedBy = 1,
                DeletedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });

        context.SaveChanges();
    }

    private static CreateGarrafaDto NewCreateDto(
        string codigo = "GAR-001",
        ulong estadoId = EstadoLlenaDepositoId,
        ulong? clienteId = null) => new()
    {
        Codigo = codigo,
        CapacidadKg = 10,
        FechaCompra = new DateOnly(2024, 1, 15),
        EstadoGarrafaId = estadoId,
        ClienteId = clienteId,
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
            NewCreateDto("GAR-CLI-DEL", estadoId: EstadoEnClienteId, clienteId: 1),
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

    // ─────────────────────────────────────────────────────────────────────────
    // Reads (issue #47: navegaciones cargadas para mostrar nombres en UI) — PR #181
    //
    // Estos tests cubren los 5 Get* del Service que NO estaban testeados.
    // Cada uno ejercita una rama de LINQ distinta (FirstOrDefault / Where /
    // OrderBy) y el bloque `Include(...)` para las navegaciones que el
    // MappingProfile proyecta a los nombres legibles.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_DevuelveDtoConNombresPoblados_CuandoGarrafaExiste()
    {
        var (service, context) = NewService(nameof(GetByIdAsync_DevuelveDtoConNombresPoblados_CuandoGarrafaExiste), seedCatalogos: true);

        var creada = await service.CreateAsync(NewCreateDto("GAR-001", EstadoLlenaDepositoId), usuarioId: 1);

        var dto = await service.GetByIdAsync(creada.Id);

        dto.Should().NotBeNull();
        dto!.Codigo.Should().Be("GAR-001");
        dto.EstadoNombre.Should().Be("Llena en deposito",
            "GetByIdAsync carga EstadoGarrafa para que el MappingProfile proyecte EstadoNombre");
    }

    [Fact]
    public async Task GetByIdAsync_DevuelveNull_CuandoIdNoExiste()
    {
        var (service, _) = NewService(nameof(GetByIdAsync_DevuelveNull_CuandoIdNoExiste));

        var dto = await service.GetByIdAsync(999_999);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task GetByCodigoAsync_DevuelveDtoPorCodigoExacto()
    {
        var (service, _) = NewService(nameof(GetByCodigoAsync_DevuelveDtoPorCodigoExacto), seedCatalogos: true);
        await service.CreateAsync(NewCreateDto("GAR-COD-001"), usuarioId: 1);
        await service.CreateAsync(NewCreateDto("GAR-COD-002"), usuarioId: 1);

        var dto = await service.GetByCodigoAsync("GAR-COD-002");

        dto.Should().NotBeNull();
        dto!.Codigo.Should().Be("GAR-COD-002");
    }

    [Fact]
    public async Task GetByCodigoAsync_DevuelveNull_CuandoCodigoNoExiste()
    {
        var (service, _) = NewService(nameof(GetByCodigoAsync_DevuelveNull_CuandoCodigoNoExiste));

        var dto = await service.GetByCodigoAsync("NO-EXISTE");

        dto.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_DevuelveTodasLasCreadas()
    {
        var (service, _) = NewService(nameof(GetAllAsync_DevuelveTodasLasCreadas), seedCatalogos: true);
        await service.CreateAsync(NewCreateDto("GAR-Z"), usuarioId: 1);
        await service.CreateAsync(NewCreateDto("GAR-A"), usuarioId: 1);
        await service.CreateAsync(NewCreateDto("GAR-M"), usuarioId: 1);

        var dtos = (await service.GetAllAsync()).ToList();

        // No aserto el orden: InMemoryDatabase no respeta OrderBy igual que
        // MySQL. El contrato del Service es "devolver todas las garrafas
        // con navegaciones cargadas" — el orden es responsabilidad del caller
        // (la UI lo aplica en cliente o el repo lo fuerza en SQL).
        dtos.Should().HaveCount(3);
        dtos.Select(d => d.Codigo).Should().BeEquivalentTo(CodigosGetAllEsperados);
        dtos.Should().OnlyContain(d => d.EstadoNombre != null,
            "GetAllAsync carga EstadoGarrafa; el MappingProfile debe proyectar EstadoNombre");
    }

    [Fact]
    public async Task GetByClienteAsync_FiltraPorClienteId()
    {
        var (service, context) = NewService(nameof(GetByClienteAsync_FiltraPorClienteId), seedCatalogos: true);

        // Sembramos directamente una garrafa con ClienteId seteado porque
        // CreateAsync no acepta ClienteId en el DTO (lo setea el operador
        // al asignar la garrafa a un cliente vía el flujo de canje).
        var now = DateTime.UtcNow;
        context.Garrafas.Add(new Garrafa
        {
            Codigo = "GAR-CLI-A",
            CapacidadKg = 10,
            FechaCompra = new DateOnly(2024, 1, 1),
            EstadoGarrafaId = EstadoLlenaDepositoId,
            ClienteId = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await context.SaveChangesAsync();

        var dtosCliente1 = (await service.GetByClienteAsync(1)).ToList();
        var dtosCliente99 = (await service.GetByClienteAsync(99)).ToList();

        dtosCliente1.Should().HaveCount(1);
        dtosCliente1[0].Codigo.Should().Be("GAR-CLI-A");
        dtosCliente99.Should().BeEmpty(
            "GetByClienteAsync filtra por ClienteId; un cliente sin garrafas devuelve lista vacía");
    }

    [Fact]
    public async Task GetByEstadoAsync_FiltraPorEstadoGarrafaId()
    {
        var (service, _) = NewService(nameof(GetByEstadoAsync_FiltraPorEstadoGarrafaId), seedCatalogos: true);
        await service.CreateAsync(NewCreateDto("GAR-LL", EstadoLlenaDepositoId), usuarioId: 1);
        await service.CreateAsync(NewCreateDto("GAR-VA", EstadoVaciaDepositoId), usuarioId: 1);
        await service.CreateAsync(NewCreateDto("GAR-LL-2", EstadoLlenaDepositoId), usuarioId: 1);

        var llenas = (await service.GetByEstadoAsync(EstadoLlenaDepositoId)).ToList();

        llenas.Should().HaveCount(2);
        llenas.Select(d => d.Codigo).Should().BeEquivalentTo(CodigosPorEstadoEsperados);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // #182 T01 — UpdateAsync rechaza cambios de EstadoGarrafaId / ClienteId
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_RechazaCambioDeEstadoGarrafaId_DirectamenteEnElDto()
    {
        // Backdoor de la issue #182 T01: un POST hand-crafted al endpoint Edit
        // podría cambiar el EstadoGarrafaId de la garrafa sin pasar por
        // CambiarEstadoAsync (que registra el MovimientoGarrafa). UpdateAsync
        // debe rechazarlo desde la app — el trigger trg_garrafas_bi_validate
        // sólo cubre INSERT.
        var (service, context) = NewService(
            nameof(UpdateAsync_RechazaCambioDeEstadoGarrafaId_DirectamenteEnElDto));
        var creada = await service.CreateAsync(NewCreateDto("GAR-T01"), usuarioId: 1);

        var dto = new UpdateGarrafaDto
        {
            Id = creada.Id,
            Codigo = creada.Codigo,
            CapacidadKg = creada.CapacidadKg,
            FechaCompra = creada.FechaCompra,
            EstadoGarrafaId = EstadoEnClienteId, // cambio respecto a LLENA_DEPOSITO
        };

        var act = () => service.UpdateAsync(dto, usuarioId: 2);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cambiar estado*",
                "UpdateAsync debe forzar el flujo dedicado Cambiar estado, no permitir el cambio directo");

        // La fila en BD no debe haberse tocado. Usamos AsNoTracking() porque la
        // entity devuelta por FindAsync (dentro del service) fue mutada por
        // AutoMapper antes de que lanzáramos la excepción; queremos leer el
        // estado REAL persistido, no la copia en memoria del tracker.
        var entity = await context.Garrafas.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(g => g.Id == creada.Id);
        entity.EstadoGarrafaId.Should().Be(EstadoLlenaDepositoId,
            "el rechazo no debe haber tocado el estado original de la fila");
    }

    [Fact]
    public async Task UpdateAsync_RechazaCambioDeClienteId_DirectamenteEnElDto()
    {
        // Hermano del anterior: cambiar ClienteId sin registrar un MovimientoGarrafa
        // rompe la trazabilidad de la garrafa (no queda historial de a quién se la
        // entregamos / quién nos la devolvió). Misma defensa, mismo rechazo.
        var (service, context) = NewService(
            nameof(UpdateAsync_RechazaCambioDeClienteId_DirectamenteEnElDto));
        var creada = await service.CreateAsync(NewCreateDto("GAR-T01B"), usuarioId: 1);

        var dto = new UpdateGarrafaDto
        {
            Id = creada.Id,
            Codigo = creada.Codigo,
            CapacidadKg = creada.CapacidadKg,
            FechaCompra = creada.FechaCompra,
            EstadoGarrafaId = creada.EstadoGarrafaId,
            ClienteId = 1, // antes era null — cambio directo
        };

        var act = () => service.UpdateAsync(dto, usuarioId: 2);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cambiar estado*");

        // Ver nota de AsNoTracking() en el otro test de T01 — la entity tracker
        // ya tiene ClienteId=1 después del map; queremos leer el valor real.
        var entity = await context.Garrafas.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(g => g.Id == creada.Id);
        entity.ClienteId.Should().BeNull("el rechazo no debe persistir el cambio");
    }

    [Fact]
    public async Task UpdateAsync_PermiteEditarOtrosCampos_SinTocarEstadoNiCliente()
    {
        // Happy path del fix T01: editar CapacidadKg, Observaciones o Codigo
        // (sin cambiar EstadoGarrafaId ni ClienteId) debe seguir funcionando
        // como antes. Esto confirma que el rechazo no sobre-restringe.
        var (service, context) = NewService(
            nameof(UpdateAsync_PermiteEditarOtrosCampos_SinTocarEstadoNiCliente));
        var creada = await service.CreateAsync(NewCreateDto("GAR-T01C"), usuarioId: 1);

        var dto = new UpdateGarrafaDto
        {
            Id = creada.Id,
            Codigo = "GAR-T01C-V2",
            CapacidadKg = 15,
            FechaCompra = creada.FechaCompra,
            EstadoGarrafaId = creada.EstadoGarrafaId, // se mantiene
            ClienteId = creada.ClienteId,          // se mantiene
            Observaciones = "Editada sin tocar estado",
        };

        var actualizado = await service.UpdateAsync(dto, usuarioId: 7);

        actualizado.Codigo.Should().Be("GAR-T01C-V2");
        actualizado.CapacidadKg.Should().Be((byte)15);
        actualizado.Observaciones.Should().Be("Editada sin tocar estado");

        var entity = await context.Garrafas.IgnoreQueryFilters().FirstAsync(g => g.Id == creada.Id);
        entity.EstadoGarrafaId.Should().Be(creada.EstadoGarrafaId, "el rechazo no aplica si el campo no cambió");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // #182 T02 — CreateAsync valida estado destino y RequiereCliente
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_LanzaExcepcion_SiEstadoGarrafaIdNoExisteEnCatalogo()
    {
        var (service, _) = NewService(
            nameof(CreateAsync_LanzaExcepcion_SiEstadoGarrafaIdNoExisteEnCatalogo),
            seedCatalogos: false); // forzamos catálogo vacío para verificar el rechazo

        var dto = new CreateGarrafaDto
        {
            Codigo = "GAR-EST-NOEXISTE",
            CapacidadKg = 10,
            FechaCompra = new DateOnly(2024, 1, 15),
            EstadoGarrafaId = 9999,
        };

        var act = () => service.CreateAsync(dto, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*9999*",
                "CreateAsync debe rechazar un EstadoGarrafaId que no existe en el catálogo");
    }

    [Fact]
    public async Task CreateAsync_LanzaExcepcion_SiEstadoRequiereCliente_YDtoNoLoTrae()
    {
        // Red de seguridad en la app, además del trigger trg_garrafas_bi_validate.
        // El InMemory del test no aplica triggers, así que verificamos que la
        // app también lo valida y devuelve un mensaje útil.
        var (service, _) = NewService(
            nameof(CreateAsync_LanzaExcepcion_SiEstadoRequiereCliente_YDtoNoLoTrae));

        var dto = new CreateGarrafaDto
        {
            Codigo = "GAR-REQC",
            CapacidadKg = 10,
            FechaCompra = new DateOnly(2024, 1, 15),
            EstadoGarrafaId = EstadoEnClienteId, // RequiereCliente = true
            ClienteId = null,                   // omitido → debe rechazar
        };

        var act = () => service.CreateAsync(dto, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requiere asignar un cliente*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // #182 T05 — ClienteId activo (no soft-deleted) en Create/Update/CambiarEstado
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_LanzaExcepcion_SiClienteIdApuntaASoftDeleted()
    {
        // T05: la cobertura por dropdown (GetActivosAsync) no basta — un POST
        // hand-crafted podría llegar con un ClienteId que el catálogo ya
        // filtraba como baja. La app debe rechazar.
        var (service, _) = NewService(
            nameof(CreateAsync_LanzaExcepcion_SiClienteIdApuntaASoftDeleted));

        var dto = new CreateGarrafaDto
        {
            Codigo = "GAR-CLI-DEAD",
            CapacidadKg = 10,
            FechaCompra = new DateOnly(2024, 1, 15),
            EstadoGarrafaId = EstadoEnClienteId,
            ClienteId = 2, // cliente sembrado con DeletedAt no nulo
        };

        var act = () => service.CreateAsync(dto, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*dado de baja*");
    }

    [Fact]
    public async Task UpdateAsync_LanzaExcepcion_SiClienteIdQuedaApuntandoASoftDeleted()
    {
        // La garrafa se crea válida. Después se da de baja el cliente.
        // Un Update posterior (incluso sin cambiar el DTO del cliente) debe
        // detectar la inconsistencia porque la app re-valida antes de SaveChanges.
        var (service, context) = NewService(
            nameof(UpdateAsync_LanzaExcepcion_SiClienteIdQuedaApuntandoASoftDeleted));
        var creada = await service.CreateAsync(
            NewCreateDto("GAR-T05-UPD", estadoId: EstadoEnClienteId, clienteId: 1),
            usuarioId: 1);

        // Soft-delete del cliente vía EF directo.
        var cliente = await context.Clientes.IgnoreQueryFilters().FirstAsync(c => c.Id == 1);
        cliente.DeletedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await context.SaveChangesAsync();

        var dto = new UpdateGarrafaDto
        {
            Id = creada.Id,
            Codigo = creada.Codigo,
            CapacidadKg = 15, // sólo cambia la capacidad
            FechaCompra = creada.FechaCompra,
            EstadoGarrafaId = creada.EstadoGarrafaId,
            ClienteId = creada.ClienteId,
        };

        var act = () => service.UpdateAsync(dto, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*dado de baja*",
                "UpdateAsync debe revalidar el ClienteId contra el catálogo antes de SaveChanges");
    }

    [Fact]
    public async Task CambiarEstadoAsync_LanzaExcepcion_SiClienteIdApuntaASoftDeleted()
    {
        // T05 también aplica a CambiarEstadoAsync — alineamos las 3 rutas de
        // escritura para que ninguna acepte un cliente soft-deleted.
        var (service, _) = NewService(
            nameof(CambiarEstadoAsync_LanzaExcepcion_SiClienteIdApuntaASoftDeleted),
            seedCatalogos: true);
        var creada = await service.CreateAsync(NewCreateDto("GAR-T05-CE"), usuarioId: 1);

        var dto = new CambiarEstadoGarrafaDto
        {
            NuevoEstadoId = EstadoEnClienteId,
            ClienteId = 2, // soft-deleted, sembrado en SeedCatalogos
        };

        var act = () => service.CambiarEstadoAsync(creada.Id, dto, currentUserId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*dado de baja*");
    }
}