using AutoMapper;
using ExtraGasMVC.Constants;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Data.Entities.Views;
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

    // IDs de tipos de movimiento Garrafa sembrados para T07 (canje con
    // PedidoService). Coinciden con el orden del seed: CAMBIO_ESTADO=1,
    // ENTREGA_CLIENTE=2, DEVOLUCION_CLIENTE=3.
    private const ulong TipoMovimientoEntregaClienteId = 2;
    private const ulong TipoMovimientoDevolucionClienteId = 3;

    // IDs de proveedores sembrados para T10 (validar FK ProveedorId no
    // soft-deleted). Mismo patron que los clientes: id=1 vivo, id=2 baja.
    private const ulong ProveedorVivoId = 1;
    private const ulong ProveedorSoftDeletedId = 2;

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

        context.TiposMovimientoGarrafa.AddRange(
            new TipoMovimientoGarrafa
            {
                Id = TipoMovimientoCambioEstadoId,
                Codigo = "CAMBIO_ESTADO",
                Nombre = "Cambio de estado manual"
            },
            // Issue #182 T07: RegistrarMovimientoPorCanjeAsync recibe el tipo
            // por codigo (ENTREGA_CLIENTE / DEVOLUCION_CLIENTE) y lo resuelve
            // contra el catalogo. Sembramos los dos para que los tests
            // dedicados al canje unitario (sin Docker/Testcontainers) puedan
            // ejercitar el path completo de escritura del MovimientoGarrafa.
            new TipoMovimientoGarrafa
            {
                Id = TipoMovimientoEntregaClienteId,
                Codigo = "ENTREGA_CLIENTE",
                Nombre = "Entrega a cliente (canje pedido)"
            },
            new TipoMovimientoGarrafa
            {
                Id = TipoMovimientoDevolucionClienteId,
                Codigo = "DEVOLUCION_CLIENTE",
                Nombre = "Devolucion de cliente (canje pedido)"
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

        // Issue #182 T10: la validacion de ProveedorId en Create/Update sigue
        // el mismo patron que ClienteId (PR #183). Sembramos un proveedor vivo
        // y uno soft-deleted; el query filter global de proveedores oculta los
        // soft-deleted, asi que ValidarProveedorActivoAsync los rechaza via
        // AnyAsync. ProveedorConfiguration exige CUIT (regex 11 digitos).
        var now = DateTime.UtcNow;
        context.Proveedores.AddRange(
            new Proveedor
            {
                Id = ProveedorVivoId,
                Codigo = "PROV-001",
                RazonSocial = "Proveedor Vivo SA",
                Cuit = "20123456789",
                Activo = true,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new Proveedor
            {
                Id = ProveedorSoftDeletedId,
                Codigo = "PROV-002",
                RazonSocial = "Proveedor Dado de Baja SA",
                Cuit = "20987654321",
                Activo = false,
                CreatedAt = now,
                UpdatedAt = now,
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

        var ok = await service.CambiarEstadoAsync(creada.Id, creada.EstadoGarrafaId, dto, currentUserId: 5);

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

        var act = () => service.CambiarEstadoAsync(creada.Id, creada.EstadoGarrafaId, dto, currentUserId: 1);

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

        var act = () => service.CambiarEstadoAsync(creada.Id, creada.EstadoGarrafaId, dto, currentUserId: 1);

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
            estadoOrigenEsperadoId: EstadoLlenaDepositoId,
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

        var act = () => service.CambiarEstadoAsync(creada.Id, creada.EstadoGarrafaId, dto, currentUserId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*dado de baja*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // #182 T03 — Race en CambiarEstado POST: una sola lectura por request
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CambiarEstadoAsync_LanzaExcepcion_SiEstadoOrigenEsperadoDifiereDelActual()
    {
        // T03: el controller hace una sola lectura (ViewBag.Garrafa) y pasa
        // ese EstadoGarrafaId al service como `estadoOrigenEsperadoId`. Si
        // entre el read del controller y el FindAsync interno del service
        // otro operador cambio la garrafa, el entity del controller y el
        // entity del service quedan desfasados. El service debe rechazar
        // ANTES de cargar catalogos / transicionar / abrir transaccion,
        // con un mensaje claro que invite al operador a recargar.
        var (service, context) = NewService(
            nameof(CambiarEstadoAsync_LanzaExcepcion_SiEstadoOrigenEsperadoDifiereDelActual),
            seedCatalogos: true);
        var creada = await service.CreateAsync(NewCreateDto("GAR-T03"), usuarioId: 1);

        // Simulamos la mutacion del otro operador: cambiamos directamente el
        // EstadoGarrafaId de la fila persistida a otro estado valido.
        var entityStale = await context.Garrafas.IgnoreQueryFilters()
            .FirstAsync(g => g.Id == creada.Id);
        entityStale.EstadoGarrafaId = EstadoVaciaDepositoId;
        await context.SaveChangesAsync();

        // El controller llama CambiarEstadoAsync con el EstadoGarrafaId VIEJO
        // (el que vio en su read original). El service tiene la fila con
        // EstadoGarrafaId NUEVO en su FindAsync y debe detectar la dif.
        var dto = new CambiarEstadoGarrafaDto
        {
            NuevoEstadoId = EstadoDanadaId,
            Observaciones = "Intento con origen stale"
        };

        var act = () => service.CambiarEstadoAsync(
            creada.Id,
            estadoOrigenEsperadoId: EstadoLlenaDepositoId, // lo que vio el controller
            dto,
            currentUserId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*modificada por otro operador*",
                "el service debe detectar el race entre el read del controller y su FindAsync interno");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // #182 T04 — Token de concurrencia (BIGINT version manual-increment)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ExitosoIncrementaVersion_EnBD()
    {
        // Triangulacion del fix T04: el service hace snapshot del Version
        // antes del map y setea entity.Version = original + 1 antes de
        // SaveChanges. EF agrega el original al WHERE del UPDATE.
        var (service, context) = NewService(
            nameof(UpdateAsync_ExitosoIncrementaVersion_EnBD));
        var creada = await service.CreateAsync(NewCreateDto("GAR-T04-B"), usuarioId: 1);

        var entityInicial = await context.Garrafas.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(g => g.Id == creada.Id);
        var versionInicial = entityInicial.Version;
        versionInicial.Should().BeGreaterThan(0,
            "la migration hace backfill a 1 y CreateAsync setea explicitamente Version = 1");

        // Edit menor (solo capacidad) para ejercitar el path de UpdateAsync
        // sin disparar los rechazos de T01 (que bloquearia cambiar
        // EstadoGarrafaId / ClienteId).
        var updateDto = new UpdateGarrafaDto
        {
            Id = creada.Id,
            Codigo = creada.Codigo,
            CapacidadKg = 15,
            FechaCompra = creada.FechaCompra,
            EstadoGarrafaId = creada.EstadoGarrafaId,
            ClienteId = creada.ClienteId,
            Observaciones = "Cambio de capacidad para ejercicio del version"
        };
        await service.UpdateAsync(updateDto, usuarioId: 7);

        var entityFinal = await context.Garrafas.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(g => g.Id == creada.Id);
        entityFinal.Version.Should().Be(versionInicial + 1,
            "UpdateAsync debe incrementar Version antes de SaveChanges para que EF detecte concurrencia");
    }

    [Fact]
    public async Task UpdateAsync_ConVersionStale_LanzaInvalidOperationException()
    {
        // T04-a: simulamos el race clasico de Edit concurrente. Patron:
        //   1. Operador A lee la entity (OriginalValue.Version = X).
        //   2. Operador B lee la entity, modifica y guarda primero
        //      (la fila en BD queda con Version = X + 1).
        //   3. Operador A modifica su copia en memoria (con OriginalValue
        //      todavia en X), intenta SaveChanges — el WHERE version=X no
        //      matchea (BD tiene X+1) y EF tira DbUpdateConcurrencyException.
        //   4. El service traduce a InvalidOperationException con mensaje
        //      claro (el mismo patron que ProductoService.UpdateAsync).
        //
        // Para reproducirlo en InMemory precisamos dos contextos contra la
        // misma base. El factory `NewService(...)` devuelve un solo contexto,
        // asi que abrimos un segundo contexto a mano con el mismo
        // databaseName.
        var dbName = nameof(UpdateAsync_ConVersionStale_LanzaInvalidOperationException);
        var (service, _) = NewService(dbName);
        var creada = await service.CreateAsync(NewCreateDto("GAR-T04-A"), usuarioId: 1);

        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        // Paso 2: Operador B modifica y guarda primero.
        using (var ctxB = new ExtraGasDbContext(options))
        {
            var entityB = await ctxB.Garrafas.IgnoreQueryFilters()
                .FirstAsync(g => g.Id == creada.Id);
            entityB.CapacidadKg = 45;
            entityB.Version += 1;
            await ctxB.SaveChangesAsync();
        }

        // Paso 3: Operador A construye su updateDto y llama al service.
        // Su copia en memoria tiene OriginalValue.Version = X (lo que
        // OriginalValue.AsNoTracking().FirstAsync arriba no altero).
        var updateDto = new UpdateGarrafaDto
        {
            Id = creada.Id,
            Codigo = creada.Codigo,
            CapacidadKg = 25,
            FechaCompra = creada.FechaCompra,
            EstadoGarrafaId = creada.EstadoGarrafaId,
            ClienteId = creada.ClienteId,
        };

        var act = () => service.UpdateAsync(updateDto, usuarioId: 7);

        // El service debe traducir DbUpdateConcurrencyException a
        // InvalidOperationException con mensaje claro.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*modificada por otro operador*",
                "el service debe atrapar DbUpdateConcurrencyException y traducirla a InvalidOperationException");
    }

    [Fact]
    public async Task CambiarEstadoAsync_ExitosoIncrementaVersion_EnBD()
    {
        // T04-c: CambiarEstadoAsync exitoso debe incrementar el version
        // de la garrafa (el path de canje via RegistrarMovimientoPorCanje
        // tambien lo hace, pero este test cubre el flujo manual
        // CAMBIO_ESTADO que es el del Controller CambiarEstado POST).
        var (service, context) = NewService(
            nameof(CambiarEstadoAsync_ExitosoIncrementaVersion_EnBD),
            seedCatalogos: true);
        var creada = await service.CreateAsync(NewCreateDto("GAR-T04-C"), usuarioId: 1);

        var versionInicial = (await context.Garrafas.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(g => g.Id == creada.Id)).Version;

        var ok = await service.CambiarEstadoAsync(
            creada.Id,
            creada.EstadoGarrafaId,
            new CambiarEstadoGarrafaDto
            {
                NuevoEstadoId = EstadoVaciaDepositoId,
                Observaciones = "Ejercicio del token de concurrencia"
            },
            currentUserId: 5);

        ok.Should().BeTrue();
        var versionFinal = (await context.Garrafas.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(g => g.Id == creada.Id)).Version;
        versionFinal.Should().Be(versionInicial + 1,
            "CambiarEstadoAsync exitoso debe incrementar Version para que el token detecte races");
    }

    [Fact]
    public void GarrafaEntity_ExponeVersion_ComoConcurrencyToken()
    {
        // Triangulacion: la entity tiene la propiedad Version (ulong) y el
        // Configuration la marca como IsConcurrencyToken. Patron paralelo
        // a Robustez146_4_ProductoConfiguration_TieneRowVersion_ComoConcurrencyToken.
        typeof(Garrafa).GetProperty(nameof(Garrafa.Version))
            .Should().NotBeNull("la entity debe exponer Version para IsConcurrencyToken");
        typeof(Garrafa).GetProperty(nameof(Garrafa.Version))!
            .PropertyType.Should().Be<ulong>();

        using var context = new ExtraGasDbContext(
            new DbContextOptionsBuilder<ExtraGasDbContext>()
                .UseInMemoryDatabase(databaseName: nameof(GarrafaEntity_ExponeVersion_ComoConcurrencyToken))
                .Options);

        var entityType = context.Model.FindEntityType(typeof(Garrafa));
        entityType.Should().NotBeNull("la entity Garrafa debe estar registrada en el modelo");

        var version = entityType!.FindProperty(nameof(Garrafa.Version));
        version.Should().NotBeNull("Version debe ser una property de la entity");
        version!.IsConcurrencyToken.Should().BeTrue(
            "Version debe ser IsConcurrencyToken para que EF Core lo agregue al WHERE del UPDATE");
    }

    // ====================================================================
    // #182 T06 — Tests de los metodos de lectura que faltaban cobertura.
    //
    // Los 5 Gets simples (GetById/ByCodigo/All/ByCliente/ByEstado) ya tienen
    // tests en PR #181 (marcados como 'Reads'). Esta bateria agrega cobertura
    // dedicada para los 7 que quedaron descubiertos: GetPagedAsync (cubierto
    // por T08), GetTransicionesDisponiblesAsync, GetHistorialAsync,
    // GetMovimientosByPedidoAsync, GetStockAsync, GetEnClientesAsync,
    // GetEstadosAsync.
    // ====================================================================

    [Fact]
    public async Task GetTransicionesDisponiblesAsync_DevuelveLosDestinosDeLaMatriz_ParaOrigenNoTerminal()
    {
        // Happy path: LLENA_DEPOSITO tiene 4 destinos validos en la matriz
        // (EN_TRANSITO, EN_CLIENTE, VACIA_DEPOSITO, DANADA). El servicio los
        // devuelve como DTOs de EstadoGarrafa, ordenados por nombre.
        var (service, _) = NewService(
            nameof(GetTransicionesDisponiblesAsync_DevuelveLosDestinosDeLaMatriz_ParaOrigenNoTerminal),
            seedCatalogos: true);
        var garrafa = await service.CreateAsync(NewCreateDto("GAR-TRANS"), usuarioId: 1);

        var destinos = (await service.GetTransicionesDisponiblesAsync(garrafa.Id)).ToList();

        destinos.Should().HaveCount(4);
        destinos.Select(d => d.Codigo).Should().BeEquivalentTo(new[]
        {
            GarrafaEstados.EnTransito,
            GarrafaEstados.EnCliente,
            GarrafaEstados.VaciaDeposito,
            GarrafaEstados.Danada
        });
    }

    [Fact]
    public async Task GetTransicionesDisponiblesAsync_DevuelveEnumerableVacio_DesdeEstadoTerminal()
    {
        // Caso del borde de la matriz: FUERA_SERVICIO no tiene transiciones
        // salientes (es estado terminal, ver GarrafaTransiciones.Matriz). El
        // servicio devuelve enumerable vacio para que la UI oculte el dropdown
        // de "Cambiar estado".
        var (service, _) = NewService(
            nameof(GetTransicionesDisponiblesAsync_DevuelveEnumerableVacio_DesdeEstadoTerminal),
            seedCatalogos: true);
        // CreateAsync permite setear el estado inicial en cualquier codigo del
        // catalogo (no hay matriz para el alta). Sembramos directo en
        // FUERA_SERVICIO para testear la lectura.
        var garrafa = await service.CreateAsync(
            NewCreateDto("GAR-TERM", EstadoFueraServicioId), usuarioId: 1);

        var destinos = await service.GetTransicionesDisponiblesAsync(garrafa.Id);

        destinos.Should().BeEmpty(
            "FUERA_SERVICIO es terminal — la matriz no expone destinos salientes");
    }

    [Fact]
    public async Task GetTransicionesDisponiblesAsync_DevuelveEnumerableVacio_SiGarrafaNoExiste()
    {
        // Si el controller recibe un id inexistente (URL hand-crafted), el
        // servicio no debe lanzar — devuelve enumerable vacio y el caller
        // decide si mostrar un 404 o un dropdown vacio.
        var (service, _) = NewService(
            nameof(GetTransicionesDisponiblesAsync_DevuelveEnumerableVacio_SiGarrafaNoExiste));

        var destinos = await service.GetTransicionesDisponiblesAsync(99_999);

        destinos.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistorialAsync_DevuelveMovimientosOrdenadosPorFechaDesc_YGarrafaExiste()
    {
        // Happy path: garrafa con 2 movimientos en orden distinto al natural.
        // El servicio debe ordenarlos por Fecha DESC, Id DESC (tiebreaker
        // estable para que la paginacion o el group-by en la UI sea estable).
        var (service, context) = NewService(
            nameof(GetHistorialAsync_DevuelveMovimientosOrdenadosPorFechaDesc_YGarrafaExiste),
            seedCatalogos: true);
        var garrafa = await service.CreateAsync(NewCreateDto("GAR-HIST"), usuarioId: 1);
        var tipoCambioEstadoId = TipoMovimientoCambioEstadoId;

        var ahora = DateTime.UtcNow;
        context.MovimientosGarrafa.AddRange(
            new MovimientoGarrafa
            {
                GarrafaId = garrafa.Id,
                Fecha = ahora.AddHours(-2),
                TipoMovimientoId = tipoCambioEstadoId,
                EstadoOrigenId = EstadoLlenaDepositoId,
                EstadoDestinoId = EstadoVaciaDepositoId,
                CreatedAt = ahora.AddHours(-2),
            },
            new MovimientoGarrafa
            {
                GarrafaId = garrafa.Id,
                Fecha = ahora.AddHours(-1),
                TipoMovimientoId = tipoCambioEstadoId,
                EstadoOrigenId = EstadoVaciaDepositoId,
                EstadoDestinoId = EstadoLlenaDepositoId,
                CreatedAt = ahora.AddHours(-1),
            });
        await context.SaveChangesAsync();

        var historial = (await service.GetHistorialAsync(garrafa.Id)).ToList();

        historial.Should().HaveCount(2);
        // El mas reciente primero.
        historial[0].EstadoOrigenId.Should().Be(EstadoVaciaDepositoId);
        historial[1].EstadoOrigenId.Should().Be(EstadoLlenaDepositoId);
    }

    [Fact]
    public async Task GetHistorialAsync_DevuelveEnumerableVacio_SiGarrafaNoTieneMovimientos()
    {
        // Garrafa existe (la acabamos de crear) pero no tiene movimientos.
        // Devolver enumerable vacio — NO lanzar.
        var (service, _) = NewService(
            nameof(GetHistorialAsync_DevuelveEnumerableVacio_SiGarrafaNoTieneMovimientos),
            seedCatalogos: true);
        var garrafa = await service.CreateAsync(NewCreateDto("GAR-SIN-MOV"), usuarioId: 1);

        var historial = await service.GetHistorialAsync(garrafa.Id);

        historial.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistorialAsync_DevuelveEnumerableVacio_SiGarrafaNoExiste()
    {
        // El servicio chequea existencia via IgnoreQueryFilters (incluye
        // soft-deleted) para distinguir "garrafa borrada" de "garrafa sin
        // historial". Si no existe, enumerable vacio.
        var (service, _) = NewService(
            nameof(GetHistorialAsync_DevuelveEnumerableVacio_SiGarrafaNoExiste));

        var historial = await service.GetHistorialAsync(99_999);

        historial.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMovimientosByPedidoAsync_DevuelveSoloLosDelPedido_OrdenadosPorId()
    {
        // Sembramos movimientos para 2 pedidos distintos. El servicio debe
        // devolver solo los del pedido solicitado, en orden de Id (cronologico
        // de insercion — el servicio NO ordena por Fecha porque para movimientos
        // de un mismo pedido el Id es correlativo).
        var (service, context) = NewService(
            nameof(GetMovimientosByPedidoAsync_DevuelveSoloLosDelPedido_OrdenadosPorId),
            seedCatalogos: true);
        var garrafa = await service.CreateAsync(NewCreateDto("GAR-PED"), usuarioId: 1);
        var now = DateTime.UtcNow;

        const ulong pedidoA = 100;
        const ulong pedidoB = 200;

        context.MovimientosGarrafa.AddRange(
            new MovimientoGarrafa
            {
                GarrafaId = garrafa.Id, Fecha = now.AddMinutes(-2),
                TipoMovimientoId = TipoMovimientoEntregaClienteId,
                PedidoId = pedidoA, EstadoDestinoId = EstadoEnClienteId,
                CreatedAt = now.AddMinutes(-2),
            },
            new MovimientoGarrafa
            {
                GarrafaId = garrafa.Id, Fecha = now.AddMinutes(-1),
                TipoMovimientoId = TipoMovimientoDevolucionClienteId,
                PedidoId = pedidoB, EstadoDestinoId = EstadoLlenaDepositoId,
                CreatedAt = now.AddMinutes(-1),
            });
        await context.SaveChangesAsync();

        var delPedidoA = (await service.GetMovimientosByPedidoAsync(pedidoA)).ToList();

        delPedidoA.Should().HaveCount(1);
        delPedidoA[0].PedidoId.Should().Be(pedidoA);
        delPedidoA[0].TipoMovimientoId.Should().Be(TipoMovimientoEntregaClienteId);
    }

    [Fact]
    public async Task GetMovimientosByPedidoAsync_DevuelveEnumerableVacio_SiPedidoNoTieneCanje()
    {
        // Pedido sin movimientos vinculados (no se hizo canje o nunca existio).
        var (service, _) = NewService(
            nameof(GetMovimientosByPedidoAsync_DevuelveEnumerableVacio_SiPedidoNoTieneCanje));

        var movimientos = await service.GetMovimientosByPedidoAsync(99_999);

        movimientos.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStockAsync_DevuelveEnumerableVacio_CuandoVistaEstaVaciaEnInMemory()
    {
        // Las views (`v_stock_garrafas`, etc.) no existen en InMemory — la
        // configuracion las registra con `ToView(...)` y `HasNoKey()`, asi
        // que la query no devuelve filas aunque la tabla base tenga datos.
        // El test verifica que el metodo no lance y devuelva enumerable (no
        // explosiones de SQL). Cobertura real se valida en integration tests
        // contra MySQL (v_stock_garrafas se ejercita desde el controller).
        var (service, _) = NewService(nameof(GetStockAsync_DevuelveEnumerableVacio_CuandoVistaEstaVaciaEnInMemory));

        var stock = await service.GetStockAsync();

        stock.Should().NotBeNull();
        stock.Should().BeEmpty(
            "la view v_stock_garrafas no se popula en InMemory — los datos reales se validan contra MySQL");
    }

    [Fact]
    public async Task GetEnClientesAsync_DevuelveEnumerableVacio_CuandoVistaEstaVaciaEnInMemory_SinFiltro()
    {
        // Idem GetStockAsync: la view v_garrafas_en_clientes no existe en
        // InMemory. El metodo acepta clienteId=null (todas) o clienteId=X
        // (filtra); ambos devuelven PagedResult vacio sin lanzar.
        // Issue #182 T11: la firma ahora devuelve PagedResult<VGarrafaEnCliente>.
        var (service, _) = NewService(
            nameof(GetEnClientesAsync_DevuelveEnumerableVacio_CuandoVistaEstaVaciaEnInMemory_SinFiltro));

        var enClientes = await service.GetEnClientesAsync(clienteId: null);

        enClientes.Items.Should().NotBeNull();
        enClientes.Items.Should().BeEmpty();
        enClientes.Total.Should().Be(0,
            "el count de la view en InMemory es 0 — CountAsync traduce a SELECT COUNT(*) que no devuelve filas");
        enClientes.Page.Should().Be(1, "page default = 1 cuando no se especifica");
        enClientes.PageSize.Should().Be(20, "pageSize default = 20 cuando no se especifica");
    }

    [Fact]
    public async Task GetEnClientesAsync_DevuelveEnumerableVacio_CuandoVistaEstaVaciaEnInMemory_ConFiltro()
    {
        // Acepta clienteId sin lanzar — el controller puede pasar el filtro
        // del dropdown sin necesidad de chequear si la view tiene datos.
        // Issue #182 T11: paginación en SQL, count = 0, items vacio.
        var (service, _) = NewService(
            nameof(GetEnClientesAsync_DevuelveEnumerableVacio_CuandoVistaEstaVaciaEnInMemory_ConFiltro));

        var enClientes = await service.GetEnClientesAsync(clienteId: 1);

        enClientes.Items.Should().NotBeNull();
        enClientes.Items.Should().BeEmpty();
        enClientes.Total.Should().Be(0);
    }

    [Fact]
    public async Task GetEnClientesAsync_NormalizaPageNegativoAPagina1()
    {
        // Issue #182 T11: page < 1 cae a 1 — la query no debe explotar ni
        // devolver un universo. Mismo patron defensivo que GetPagedAsync.
        var (service, _) = NewService(
            nameof(GetEnClientesAsync_NormalizaPageNegativoAPagina1));

        var enClientes = await service.GetEnClientesAsync(clienteId: null, page: -5);

        enClientes.Page.Should().Be(1, "page < 1 debe normalizarse a 1");
        enClientes.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEnClientesAsync_NormalizaPageSizeExcedidoACap100()
    {
        // Issue #182 T11: pageSize > 100 cae a 100 (cap defensivo). Patron
        // identico a GetPagedAsync.pageSize>100 -> 100.
        var (service, _) = NewService(
            nameof(GetEnClientesAsync_NormalizaPageSizeExcedidoACap100));

        var enClientes = await service.GetEnClientesAsync(clienteId: null, pageSize: 50_000);

        enClientes.PageSize.Should().Be(100, "pageSize > 100 debe capearse a 100");
    }

    [Fact]
    public async Task GetEnClientesAsync_NormalizaPageSizeCeroODefault()
    {
        // Issue #182 T11: pageSize <= 0 cae a 20 (default razonable).
        var (service, _) = NewService(
            nameof(GetEnClientesAsync_NormalizaPageSizeCeroODefault));

        var enClientes = await service.GetEnClientesAsync(clienteId: null, pageSize: 0);

        enClientes.PageSize.Should().Be(20, "pageSize <= 0 debe caer al default (20)");
    }

    [Fact]
    public async Task GetEnClientesAsync_PaginaDos_UsaSkipCorrecto()
    {
        // Issue #182 T11: page=2 con pageSize=10 debe traducirse a
        // Skip((2-1)*10)=Skip(10). En InMemory la view no se popula, asi
        // que validamos que la query no lance y devuelva items vacios (la
        // cobertura real del Skip se valida contra MySQL en el Controller).
        var (service, _) = NewService(
            nameof(GetEnClientesAsync_PaginaDos_UsaSkipCorrecto));

        var enClientes = await service.GetEnClientesAsync(clienteId: null, page: 2, pageSize: 10);

        enClientes.Page.Should().Be(2,
            "page=2 debe respetarse despues de la normalizacion (pageSize valido)");
        enClientes.PageSize.Should().Be(10);
        enClientes.Items.Should().BeEmpty();
    }

    // ====================================================================
    // #182 T12 — GetEstadoIdByCodigoAsync
    //
    // Lookup puntual codigo -> Id del catalogo estados_garrafa. Reemplaza el
    // hardcode EstadoGarrafaId=1 en GarrafasController.Create. Tests planos
    // sobre el InMemory seed (mismo patron que el resto de la suite).
    // ====================================================================

    [Fact]
    public async Task GetEstadoIdByCodigoAsync_DevuelveIdDelCatalogo_ParaCodigoCanonico()
    {
        // Happy path: LLENA_DEPOSITO existe en el catalogo sembrado por
        // SeedCatalogos con Id=1 (mismo Id que la convencion del seed real
        // de MySQL). El helper devuelve ese Id numerico.
        var (service, _) = NewService(
            nameof(GetEstadoIdByCodigoAsync_DevuelveIdDelCatalogo_ParaCodigoCanonico),
            seedCatalogos: true);

        var id = await service.GetEstadoIdByCodigoAsync(GarrafaEstados.LlenaDeposito);

        id.Should().Be(EstadoLlenaDepositoId,
            "LLENA_DEPOSITO debe estar sembrado con Id=1 — mismo orden que el seed real");
    }

    [Fact]
    public async Task GetEstadoIdByCodigoAsync_DevuelveCero_ParaCodigoInexistente()
    {
        // Codigo que NO esta en el catalogo. El helper devuelve 0 (ulong
        // default de FirstOrDefaultAsync) sin lanzar — el caller decide si
        // 0 es valido o no. Cobertura: verifica que no rompe la query y que
        // no asume que todo codigo existe.
        var (service, _) = NewService(
            nameof(GetEstadoIdByCodigoAsync_DevuelveCero_ParaCodigoInexistente),
            seedCatalogos: true);

        var id = await service.GetEstadoIdByCodigoAsync("CODIGO_INVENTADO");

        id.Should().Be(0UL,
            "FirstOrDefault sobre una condicion que no matchea devuelve default(ulong)=0");
    }

    [Fact]
    public async Task GetEstadoIdByCodigoAsync_DevuelveCero_ParaCodigoNullOVacio()
    {
        // Null/empty: el helper hace guardia explicita y devuelve 0 sin
        // tocar la query. Es defensivo contra inputs sucios (form
        // hand-crafted con campo vacio).
        var (service, _) = NewService(
            nameof(GetEstadoIdByCodigoAsync_DevuelveCero_ParaCodigoNullOVacio),
            seedCatalogos: true);

        (await service.GetEstadoIdByCodigoAsync(null!)).Should().Be(0UL);
        (await service.GetEstadoIdByCodigoAsync(string.Empty)).Should().Be(0UL);
        (await service.GetEstadoIdByCodigoAsync("   ")).Should().Be(0UL);
    }

    [Fact]
    public async Task GetEstadosAsync_DevuelveTodosLosEstadosDelCatalogo_OrdenadosPorNombre()
    {
        // SeedCatalogos siembra 6 estados; GetEstadosAsync los devuelve todos,
        // ordenados por nombre para que el dropdown de UI sea estable.
        var (service, _) = NewService(
            nameof(GetEstadosAsync_DevuelveTodosLosEstadosDelCatalogo_OrdenadosPorNombre),
            seedCatalogos: true);

        var estados = (await service.GetEstadosAsync()).ToList();

        estados.Should().HaveCount(6,
            "SeedCatalogos siembra los 6 codigos canonicos de GarrafaEstados");
        // Orden por nombre alfabetico ascendente.
        var nombresOrdenados = estados.Select(e => e.Nombre).OrderBy(n => n).ToList();
        estados.Select(e => e.Nombre).Should().Equal(nombresOrdenados);
    }

    [Fact]
    public async Task GetEstadosAsync_DevuelveEnumerableVacio_SinCatalogoSembrado()
    {
        // Cuando seedCatalogos=false el catalogo queda vacio — el metodo no
        // debe lanzar y debe devolver enumerable vacio (consistente con el
        // comportamiento del resto de los Gets).
        var (service, _) = NewService(
            nameof(GetEstadosAsync_DevuelveEnumerableVacio_SinCatalogoSembrado),
            seedCatalogos: false);

        var estados = await service.GetEstadosAsync();

        estados.Should().BeEmpty();
    }

    // ====================================================================
    // #182 T07 — RegistrarMovimientoPorCanjeAsync (path unitario, sin Docker)
    //
    // Hoy el flujo de canje esta cubierto indirectamente via
    // PedidoCanjeIntegrationTests (MySQL real + trigger trg_mov_garrafa_ai).
    // Estos tests apuntan al path unitario del GarrafaService para que un
    // fallo en la escritura del MovimientoGarrafa o el seteo de clienteId
    // se detecte sin necesidad de Docker/Testcontainers.
    //
    // El trigger de BD actualiza garrafas.estado_garrafa_id en respuesta al
    // INSERT del movimiento — eso NO se aplica en InMemory, asi que estos
    // tests verifican lo que la APP escribe (MovimientoGarrafa + clienteId de
    // la garrafa), no lo que la BD hace despues. La cobertura del trigger se
    // mantiene en PedidoCanjeIntegrationTests.
    // ====================================================================

    [Fact]
    public async Task RegistrarMovimientoPorCanjeAsync_Entrega_CreaMovimientoYAsignaClienteALaGarrafa()
    {
        // Happy path ENTREGA: garrafa LLENA_DEPOSITO se entrega a un cliente.
        // El servicio debe (1) crear MovimientoGarrafa con tipo=ENTREGA_CLIENTE,
        // origen=LLENA_DEPOSITO, destino=EN_CLIENTE, ClienteId, PedidoId;
        // (2) setear garrafa.ClienteId al cliente del pedido. El trigger de BD
        // luego cambia estado_garrafa_id a EN_CLIENTE — fuera del alcance del
        // path unitario.
        var (service, context) = NewService(
            nameof(RegistrarMovimientoPorCanjeAsync_Entrega_CreaMovimientoYAsignaClienteALaGarrafa),
            seedCatalogos: true);
        var garrafa = await service.CreateAsync(NewCreateDto("GAR-CANJE-E"), usuarioId: 1);
        const ulong pedidoId = 500;

        await service.RegistrarMovimientoPorCanjeAsync(
            garrafaId: garrafa.Id,
            estadoDestinoId: EstadoEnClienteId,
            clienteId: 1,
            pedidoId: pedidoId,
            tipoMovimientoCodigo: "ENTREGA_CLIENTE",
            usuarioId: 7);

        var movimiento = await context.MovimientosGarrafa
            .SingleAsync(m => m.GarrafaId == garrafa.Id);
        movimiento.TipoMovimientoId.Should().Be(TipoMovimientoEntregaClienteId,
            "el tipo de movimiento debe ser ENTREGA_CLIENTE (resuelto por codigo en el service)");
        movimiento.EstadoOrigenId.Should().Be(EstadoLlenaDepositoId,
            "el origen es el estado actual de la garrafa al momento del canje");
        movimiento.EstadoDestinoId.Should().Be(EstadoEnClienteId,
            "el destino lo paso el caller como parametro estadoDestinoId");
        movimiento.ClienteId.Should().Be(1, "el cliente del movimiento es el del pedido");
        movimiento.PedidoId.Should().Be(pedidoId, "el pedido debe quedar registrado en el movimiento");
        movimiento.CreatedBy.Should().Be(7, "CreatedBy refleja el usuario del parametro usuarioId");

        var garrafaActualizada = await context.Garrafas.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(g => g.Id == garrafa.Id);
        garrafaActualizada.ClienteId.Should().Be(1,
            "el service debe actualizar garrafa.cliente_id al cliente del pedido en ENTREGAs");
    }

    [Fact]
    public async Task RegistrarMovimientoPorCanjeAsync_Devolucion_CreaMovimientoYLimpiaClienteDeLaGarrafa()
    {
        // Hermano del anterior: DEVOLUCION_CLIENTE. La garrafa estaba
        // EN_CLIENTE del cliente 1 (se la devuelven), el servicio registra
        // movimiento con destino=LLENA_DEPOSITO y setea garrafa.ClienteId=null.
        var (service, context) = NewService(
            nameof(RegistrarMovimientoPorCanjeAsync_Devolucion_CreaMovimientoYLimpiaClienteDeLaGarrafa),
            seedCatalogos: true);
        // Sembramos directo en EN_CLIENTE del cliente 1 — simula garrafa que
        // esta en el domicilio del cliente y nos la devuelven vacia.
        var now = DateTime.UtcNow;
        var garrafa = new Garrafa
        {
            Codigo = "GAR-CANJE-D",
            CapacidadKg = 10,
            FechaCompra = new DateOnly(2024, 1, 1),
            EstadoGarrafaId = EstadoEnClienteId,
            ClienteId = 1,
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.Garrafas.Add(garrafa);
        await context.SaveChangesAsync();
        const ulong pedidoId = 600;

        await service.RegistrarMovimientoPorCanjeAsync(
            garrafaId: garrafa.Id,
            estadoDestinoId: EstadoLlenaDepositoId,
            clienteId: null,
            pedidoId: pedidoId,
            tipoMovimientoCodigo: "DEVOLUCION_CLIENTE",
            usuarioId: 9);

        var movimiento = await context.MovimientosGarrafa
            .SingleAsync(m => m.GarrafaId == garrafa.Id);
        movimiento.TipoMovimientoId.Should().Be(TipoMovimientoDevolucionClienteId);
        movimiento.EstadoOrigenId.Should().Be(EstadoEnClienteId);
        movimiento.EstadoDestinoId.Should().Be(EstadoLlenaDepositoId);
        movimiento.ClienteId.Should().BeNull("una devolucion no lleva cliente");
        movimiento.PedidoId.Should().Be(pedidoId);

        var garrafaActualizada = await context.Garrafas.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(g => g.Id == garrafa.Id);
        garrafaActualizada.ClienteId.Should().BeNull(
            "el service debe limpiar garrafa.cliente_id en DEVOLUCIONes");
    }

    [Fact]
    public async Task RegistrarMovimientoPorCanjeAsync_CanjeMultiple_AmbosMovimientosApuntanAlMismoPedido()
    {
        // Canje multiple en el mismo pedido: 1 ENTREGA + 1 DEVOLUCION sobre
        // distintas garrafas. Ambos movimientos deben quedar vinculados al
        // mismo PedidoId para que el reporte de canje agrupe correctamente.
        var (service, context) = NewService(
            nameof(RegistrarMovimientoPorCanjeAsync_CanjeMultiple_AmbosMovimientosApuntanAlMismoPedido),
            seedCatalogos: true);

        // Garrafa A: LLENA_DEPOSITO (la entregamos al cliente).
        var garrafaA = await service.CreateAsync(NewCreateDto("GAR-CANJE-MULTI-A"), usuarioId: 1);
        // Garrafa B: EN_CLIENTE del cliente 1 (nos la devuelven vacia).
        var now = DateTime.UtcNow;
        var garrafaB = new Garrafa
        {
            Codigo = "GAR-CANJE-MULTI-B",
            CapacidadKg = 10,
            FechaCompra = new DateOnly(2024, 1, 1),
            EstadoGarrafaId = EstadoEnClienteId,
            ClienteId = 1,
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.Garrafas.Add(garrafaB);
        await context.SaveChangesAsync();

        const ulong pedidoId = 700;

        await service.RegistrarMovimientoPorCanjeAsync(
            garrafaId: garrafaA.Id, estadoDestinoId: EstadoEnClienteId,
            clienteId: 1, pedidoId: pedidoId,
            tipoMovimientoCodigo: "ENTREGA_CLIENTE", usuarioId: 1);

        await service.RegistrarMovimientoPorCanjeAsync(
            garrafaId: garrafaB.Id, estadoDestinoId: EstadoLlenaDepositoId,
            clienteId: null, pedidoId: pedidoId,
            tipoMovimientoCodigo: "DEVOLUCION_CLIENTE", usuarioId: 1);

        var movimientos = await context.MovimientosGarrafa
            .Where(m => m.PedidoId == pedidoId)
            .OrderBy(m => m.Id)
            .ToListAsync();

        movimientos.Should().HaveCount(2,
            "un pedido con 1 ENTREGA + 1 DEVOLUCION debe generar exactamente 2 movimientos");
        movimientos.Should().OnlyContain(m => m.PedidoId == pedidoId,
            "ambos movimientos quedan vinculados al pedido del canje");
        movimientos.Should().Contain(m =>
            m.TipoMovimientoId == TipoMovimientoEntregaClienteId
            && m.ClienteId == 1
            && m.EstadoDestinoId == EstadoEnClienteId);
        movimientos.Should().Contain(m =>
            m.TipoMovimientoId == TipoMovimientoDevolucionClienteId
            && m.ClienteId == null
            && m.EstadoDestinoId == EstadoLlenaDepositoId);
    }

    [Fact]
    public async Task RegistrarMovimientoPorCanjeAsync_SeteaPedidoIdEnElMovimiento_Explicitamente()
    {
        // Caso explicito: el PedidoId es propiedad central de la
        // trazabilidad del canje (lo usa el reporte de canje y
        // GetMovimientosByPedidoAsync). El servicio debe persistirlo en la
        // fila del MovimientoGarrafa — sin esto, el pedido queda sin vinculo
        // con las garrafas que se canjearon.
        var (service, context) = NewService(
            nameof(RegistrarMovimientoPorCanjeAsync_SeteaPedidoIdEnElMovimiento_Explicitamente),
            seedCatalogos: true);
        var garrafa = await service.CreateAsync(NewCreateDto("GAR-PEDIDO-ID"), usuarioId: 1);
        const ulong pedidoIdEsperado = 12_345;

        await service.RegistrarMovimientoPorCanjeAsync(
            garrafaId: garrafa.Id, estadoDestinoId: EstadoEnClienteId,
            clienteId: 1, pedidoId: pedidoIdEsperado,
            tipoMovimientoCodigo: "ENTREGA_CLIENTE", usuarioId: 1);

        var movimiento = await context.MovimientosGarrafa
            .SingleAsync(m => m.GarrafaId == garrafa.Id);
        movimiento.PedidoId.Should().Be(pedidoIdEsperado,
            "RegistrarMovimientoPorCanjeAsync debe persistir el PedidoId del parametro");
    }

    // ====================================================================
    // #182 T08 — GetPagedAsync: normalizacion defensiva + escape de LIKE.
    //
    // La normalizacion de page/pageSize/sortBy/sortDir ya existia
    // (introducida en PR #181); esta bateria la blinda con tests. El escape
    // de % y _ en el input de busqueda era un bug abierto — code fix + tests.
    // ====================================================================

    [Fact]
    public async Task GetPagedAsync_NormalizaPageNegativo_AUno()
    {
        // page=-5 cae por la guarda `if (page < 1) page = 1`. La query no
        // debe explotar ni devolver OFFSET negativo.
        var (service, _) = NewService(
            nameof(GetPagedAsync_NormalizaPageNegativo_AUno));

        var result = await service.GetPagedAsync(codigo: null, capacidad: null, page: -5);

        result.Page.Should().Be(1,
            "page < 1 debe normalizarse a 1 para que OFFSET no sea negativo");
    }

    [Fact]
    public async Task GetPagedAsync_CapPageSizeA100_CuandoExcedeTope()
    {
        // pageSize=9999 debe caer al tope maximo (100) — ver la guarda
        // `if (pageSize > 100) pageSize = 100` en GetPagedAsync. Sin esto un
        // cliente podia pedir una query de 10k filas y tumbar la UI.
        var (service, _) = NewService(
            nameof(GetPagedAsync_CapPageSizeA100_CuandoExcedeTope));

        var result = await service.GetPagedAsync(codigo: null, capacidad: null, page: 1, pageSize: 9999);

        result.PageSize.Should().Be(100,
            "pageSize > 100 debe coercerarse al tope maximo para evitar queries enormes");
    }

    [Fact]
    public async Task GetPagedAsync_SortByDesconocido_NoLanza_YUsaDefault()
    {
        // sortBy arbitrario (incluyendo intento de SQL injection) cae al
        // default `_ => OrderByCampoOrId(query, g => g.Codigo, desc)`. El
        // servicio debe devolver resultados ordenados por Codigo, no explotar.
        var (service, _) = NewService(
            nameof(GetPagedAsync_SortByDesconocido_NoLanza_YUsaDefault),
            seedCatalogos: true);
        await service.CreateAsync(NewCreateDto("GAR-Z"), usuarioId: 1);
        await service.CreateAsync(NewCreateDto("GAR-A"), usuarioId: 1);

        var result = await service.GetPagedAsync(
            codigo: null, capacidad: null,
            sortBy: "CodigoMalicioso; DROP TABLE garrafas--",
            sortDir: "asc");

        result.Items.Should().HaveCount(2);
        result.Items[0].Codigo.Should().Be("GAR-A",
            "sortBy desconocido cae al default (Codigo asc)");
        result.Items[1].Codigo.Should().Be("GAR-Z");
    }

    [Fact]
    public async Task GetPagedAsync_SortDirInvalido_NoLanza_YUsaAsc()
    {
        // sortDir != "desc" cae a asc (ver `var desc = string.Equals(sortDir, "desc", ...)`).
        var (service, _) = NewService(
            nameof(GetPagedAsync_SortDirInvalido_NoLanza_YUsaAsc),
            seedCatalogos: true);
        await service.CreateAsync(NewCreateDto("GAR-A"), usuarioId: 1);
        await service.CreateAsync(NewCreateDto("GAR-Z"), usuarioId: 1);

        var result = await service.GetPagedAsync(
            codigo: null, capacidad: null,
            sortBy: "codigo",
            sortDir: "sideways");

        result.Items.Should().HaveCount(2);
        result.Items[0].Codigo.Should().Be("GAR-A",
            "sortDir != 'desc' cae a asc");
        result.Items[1].Codigo.Should().Be("GAR-Z");
    }

    [Fact]
    public async Task GetPagedAsync_BusquedaConPorcentajeLiteral_NoDevuelveTodasLasFilas()
    {
        // T08: el codigo "%" en el input NO debe interpretarse como wildcard.
        // Sin el code fix (escapar % antes del LIKE), un POST con codigo=% 
        // matcheaba cualquier codigo (porque % es wildcard en SQL). Con el
        // fix, % se escapa a \% y solo matchea codigos que literalmente
        // contienen % — ninguno de los nuestros.
        var (service, _) = NewService(
            nameof(GetPagedAsync_BusquedaConPorcentajeLiteral_NoDevuelveTodasLasFilas),
            seedCatalogos: true);
        await service.CreateAsync(NewCreateDto("GAR-001"), usuarioId: 1);
        await service.CreateAsync(NewCreateDto("GAR-002"), usuarioId: 1);
        await service.CreateAsync(NewCreateDto("GAR-003"), usuarioId: 1);

        var result = await service.GetPagedAsync(codigo: "%", capacidad: null);

        result.Items.Should().BeEmpty(
            "el % literal del input debe escapar a \\% y NO actuar como wildcard");
        result.Total.Should().Be(0,
            "el conteo total tambien debe ser 0 — confirma que la query LIKE usa el caracter escapado");
    }

    [Fact]
    public async Task GetPagedAsync_BusquedaConUnderscoreLiteral_NoActuaComoWildcardDeUnCaracter()
    {
        // T08: el codigo "_" tampoco debe interpretarse como wildcard. Sin
        // el fix, "_" matchearia cualquier codigo con al menos 1 caracter
        // (porque _ es wildcard de 1 char en SQL). Con el fix, _ se escapa
        // a \_ y solo matchea codigos que contienen _ literal.
        var (service, _) = NewService(
            nameof(GetPagedAsync_BusquedaConUnderscoreLiteral_NoActuaComoWildcardDeUnCaracter),
            seedCatalogos: true);
        await service.CreateAsync(NewCreateDto("GAR-001"), usuarioId: 1);
        await service.CreateAsync(NewCreateDto("GAR-002"), usuarioId: 1);

        var result = await service.GetPagedAsync(codigo: "_", capacidad: null);

        result.Items.Should().BeEmpty(
            "el _ literal debe escapar a \\_ y NO actuar como wildcard de 1 caracter");
    }

    // ====================================================================
    // #182 T09 — Estado terminal como origen + self-transition.
    //
    // La matriz GarrafaTransiciones.Matriz ya rechaza ambos casos (FUERA_SERVICIO
    // tiene destinos vacios y EsValida() rechaza origen==destino explicitamente).
    // Los tests blindo el comportamiento desde el CambiarEstadoAsync para que
    // un cambio accidental en la matriz o la logica del service los rompa
    // visiblemente.
    // ====================================================================

    [Fact]
    public async Task CambiarEstadoAsync_RechazaTransicion_DesdeEstadoTerminalHaciaCualquierOtro()
    {
        // FUERA_SERVICIO es terminal en la matriz (ver GarrafaTransiciones).
        // Intentar salir de ahi (a cualquier otro estado) debe ser rechazado
        // con el mensaje claro de la matriz.
        var (service, _) = NewService(
            nameof(CambiarEstadoAsync_RechazaTransicion_DesdeEstadoTerminalHaciaCualquierOtro),
            seedCatalogos: true);
        // CreateAsync permite setear el estado inicial en cualquier codigo
        // del catalogo (no hay matriz para el alta), asi que sembramos una
        // garrafa ya en FUERA_SERVICIO.
        var garrafa = await service.CreateAsync(
            NewCreateDto("GAR-TERMINAL", EstadoFueraServicioId), usuarioId: 1);

        var dto = new CambiarEstadoGarrafaDto
        {
            NuevoEstadoId = EstadoVaciaDepositoId, // cualquier estado -> rechazar
            Observaciones = "Intento desde terminal"
        };

        var act = () => service.CambiarEstadoAsync(
            garrafa.Id, garrafa.EstadoGarrafaId, dto, currentUserId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Transición inválida*",
                "la matriz debe rechazar cualquier salida desde FUERA_SERVICIO");
    }

    [Fact]
    public async Task CambiarEstadoAsync_RechazaSelfTransition_CuandoOrigenEsIgualADestino()
    {
        // Auto-transicion (LLENA_DEPOSITO -> LLENA_DEPOSITO) es un no-op
        // semantico que la matriz rechaza explicitamente (ver
        // GarrafaTransiciones.EsValida). El test confirma que el path del
        // CambiarEstadoAsync tambien lo bloquea — un POST hand-crafted no
        // debe poder "refrescar" una garrafa sin registrar un movimiento real.
        var (service, _) = NewService(
            nameof(CambiarEstadoAsync_RechazaSelfTransition_CuandoOrigenEsIgualADestino),
            seedCatalogos: true);
        var garrafa = await service.CreateAsync(NewCreateDto("GAR-SELF"), usuarioId: 1);

        var dto = new CambiarEstadoGarrafaDto
        {
            NuevoEstadoId = EstadoLlenaDepositoId, // == origen
            Observaciones = "Self-transition"
        };

        var act = () => service.CambiarEstadoAsync(
            garrafa.Id, garrafa.EstadoGarrafaId, dto, currentUserId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Transición inválida*",
                "la matriz rechaza self-transitions explicitamente (origen == destino)");
    }

    // ====================================================================
    // #182 T10 — Validacion de ProveedorId en Create/Update (FK soft-deleted).
    //
    // CreateAsync y UpdateAsync NO validaban ProveedorId contra el query
    // filter global de proveedores (solo validaban ClienteId, ver PR #183).
    // Esta bateria agrega la validacion analoga + los tests. Los tests de
    // ClienteId soft-deleted en Create/Update/CambiarEstado ya quedaron
    // cubiertos por PR #183 (no se duplican).
    // ====================================================================

    [Fact]
    public async Task CreateAsync_LanzaExcepcion_SiProveedorIdApuntaASoftDeleted()
    {
        // T10: la cobertura por dropdown de proveedores (que filtra Activos)
        // no basta — un POST hand-crafted con un ProveedorId soft-deleted
        // podia crear una garrafa con FK invalida. Mismo patron que
        // ValidarClienteActivoAsync (PR #183) aplicado a proveedores.
        var (service, _) = NewService(
            nameof(CreateAsync_LanzaExcepcion_SiProveedorIdApuntaASoftDeleted),
            seedCatalogos: true);

        var dto = new CreateGarrafaDto
        {
            Codigo = "GAR-PROV-DEAD",
            CapacidadKg = 10,
            FechaCompra = new DateOnly(2024, 1, 15),
            EstadoGarrafaId = EstadoLlenaDepositoId, // RequiereCliente=false, ClienteId puede ser null
            ProveedorId = ProveedorSoftDeletedId,    // sembrado en SeedCatalogos con DeletedAt
        };

        var act = () => service.CreateAsync(dto, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*proveedor*",
                "CreateAsync debe rechazar un ProveedorId que el query filter global ya oculta como soft-deleted");
    }

    [Fact]
    public async Task UpdateAsync_LanzaExcepcion_SiProveedorIdQuedaApuntandoASoftDeleted()
    {
        // Caso analogo al de ClienteId: la garrafa se crea con proveedor
        // vivo, despues se da de baja el proveedor, y un Update posterior
        // (incluso sin cambiar el ProveedorId del DTO) debe revalidar.
        var (service, context) = NewService(
            nameof(UpdateAsync_LanzaExcepcion_SiProveedorIdQuedaApuntandoASoftDeleted),
            seedCatalogos: true);
        var creada = await service.CreateAsync(
            new CreateGarrafaDto
            {
                Codigo = "GAR-T10-UPD",
                CapacidadKg = 10,
                FechaCompra = new DateOnly(2024, 1, 15),
                EstadoGarrafaId = EstadoLlenaDepositoId,
                ProveedorId = ProveedorVivoId,
            },
            usuarioId: 1);

        // Soft-delete del proveedor.
        var proveedor = await context.Proveedores.IgnoreQueryFilters()
            .FirstAsync(p => p.Id == ProveedorVivoId);
        proveedor.DeletedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await context.SaveChangesAsync();

        var updateDto = new UpdateGarrafaDto
        {
            Id = creada.Id,
            Codigo = creada.Codigo,
            CapacidadKg = 15, // solo cambia capacidad — el ProveedorId se mantiene
            FechaCompra = creada.FechaCompra,
            EstadoGarrafaId = creada.EstadoGarrafaId,
            ProveedorId = creada.ProveedorId,
        };

        var act = () => service.UpdateAsync(updateDto, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*proveedor*",
                "UpdateAsync debe revalidar el ProveedorId contra el catalogo antes de SaveChanges");
    }
}