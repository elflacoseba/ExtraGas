using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de integracion del Service de Producto contra DbContext InMemory.
/// Cubren las lineas nuevas del issue #114 + el refactor del DeleteAsync
/// (PR #121) que ahora hace soft-delete completo (DeletedAt + Activo=false)
/// + RestoreAsync de Slice 2 (issue #145).
/// </summary>
public class ProductoServiceTests
{
    private static (ProductoService service, ExtraGasDbContext context) NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ExtraGasDbContext(options);
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        // Issue #145 Slice 2: ILogger<ProductoService> requerido para trazabilidad
        // de operaciones de escritura (RestoreAsync). Los tests existentes no
        // asertan sobre el log; usamos NullLogger.
        var service = new ProductoService(context, mapper, NullLogger<ProductoService>.Instance);

        // Issue #146.1: el Service valida FK TipoProductoId antes de
        // SaveChanges. Los tests pre-existentes asumían un DbContext
        // vacío y seteaban TipoProductoId=1; sembramos acá para mantener
        // el escenario sin obligar a cada test a duplicar el setup.
        if (!context.TiposProducto.Any())
        {
            context.TiposProducto.Add(new TipoProducto
            {
                Id = 1,
                Codigo = "GAS",
                Nombre = "Gas",
            });
            context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        return (service, context);
    }

    /// <summary>
    /// Helper que siembra un Usuario con username único (issue #147 item 4:
    /// LoadAuditUsersAsync resuelve las FKs de CreatedBy/UpdatedBy a usernames
    /// legibles). Devuelve el Id del usuario sembrado.
    /// </summary>
    private static ulong SeedUsuario(ExtraGasDbContext context, string username)
    {
        var usuario = new Usuario
        {
            Id = (ulong)(context.Usuarios.Count() + 1) * 10,
            Username = username,
            PasswordHash = "test-hash",
            RolId = 1,
            Activo = true,
            DebeCambiarPassword = false,
        };
        context.Usuarios.Add(usuario);
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return usuario.Id;
    }

    private static CreateProductoDto NewCreateDto(string codigo = "GAS-10") => new()
    {
        Codigo = codigo,
        Nombre = "Garrafa 10kg",
        // Issue #146.1: pre-check FK TipoProductoId en el Service. Los
        // tests pre-existentes usaban Id=1; el helper queda parametrizable
        // por si un test futuro necesita otro Id. El caller debe invocar
        // SeedTipoProducto antes (los tests que usan NewCreateDto directo
        // lo agregan al cuerpo).
        TipoProductoId = 1,
        // Issue #146.3: el Service ahora exige capacidad_kg > 0 cuando
        // ManejaGarrafaIndividual=true. Los tests pre-existentes settaban
        // el flag sin capacidad; seteamos 10m para mantener el escenario
        // "producto GARRAFA estándar" y seguir cubriendo los asserts.
        CapacidadKg = 10m,
        UnidadVenta = "UNIDAD",
        PrecioActual = 15000m,
        ManejaGarrafaIndividual = true,
    };

    [Fact]
    public async Task CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga()
    {
        var (service, _) = NewService(nameof(CreateAsync_SeteaActivoTrue_AunqueDtoNoLoTenga));

        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        creado.Activo.Should().BeTrue("Activo no viene del DTO; el Service lo setea en true");
    }

    [Fact]
    public async Task UpdateAsync_PreservaActivo_DesdeLaBD_AunqueDtoNoLoTenga()
    {
        var (service, _) = NewService(nameof(UpdateAsync_PreservaActivo_DesdeLaBD_AunqueDtoNoLoTenga));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        var updateDto = new UpdateProductoDto
        {
            Id = creado.Id,
            Codigo = creado.Codigo,
            Nombre = "Garrafa 10kg v2",
            TipoProductoId = creado.TipoProductoId,
            CapacidadKg = creado.CapacidadKg,
            UnidadVenta = creado.UnidadVenta,
            PrecioActual = creado.PrecioActual,
            ManejaGarrafaIndividual = creado.ManejaGarrafaIndividual,
            // Activo NO esta en UpdateProductoDto.
        };
        var actualizado = await service.UpdateAsync(updateDto, usuarioId: 2);

        actualizado.Activo.Should().BeTrue(
            "el helper ProductoEditRules debe preservar Activo desde la BD");
        actualizado.Nombre.Should().Be("Garrafa 10kg v2");
    }

    [Fact]
    public async Task DeleteAsync_SeteaDeletedAtYActivoFalse_SoftDeleteCompleto()
    {
        // PR #121: antes DeleteAsync solo seteaba DeletedAt, dejando Activo=true
        // (un zombie). Ahora setea ambos para mantener la invariante.
        var (service, context) = NewService(nameof(DeleteAsync_SeteaDeletedAtYActivoFalse_SoftDeleteCompleto));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        var ok = await service.DeleteAsync(creado.Id, ct: default);

        ok.Should().BeTrue();
        var entity = await context.Productos.IgnoreQueryFilters().FirstAsync(p => p.Id == creado.Id);
        entity.DeletedAt.Should().NotBeNull("soft-delete debe setear DeletedAt");
        entity.Activo.Should().BeFalse("soft-delete debe setear Activo=false (PR #121)");
    }

    // ====================================================================
    // Issue #145 Slice 2: RestoreAsync para revertir soft-delete
    // ====================================================================

    [Fact]
    public async Task RestoreAsync_ReactivatesSoftDeletedProducto()
    {
        // Soft-delete deja DeletedAt != null y Activo = false.
        // Restore debe volver ambos a su estado original (invariante
        // Activo=false => DeletedAt != null de #114/#121).
        var (service, context) = NewService(nameof(RestoreAsync_ReactivatesSoftDeletedProducto));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);
        await service.DeleteAsync(creado.Id, ct: default);

        var ok = await service.RestoreAsync(creado.Id, updatedBy: 99);

        ok.Should().BeTrue();
        var entity = await context.Productos.IgnoreQueryFilters().FirstAsync(p => p.Id == creado.Id);
        entity.DeletedAt.Should().BeNull("Restore debe limpiar DeletedAt");
        entity.Activo.Should().BeTrue("Restore debe reactivar Activo (Producto retiene la columna por #114)");
        entity.UpdatedBy.Should().Be(99, "Restore debe registrar quién lo reactivó");
    }

    [Fact]
    public async Task RestoreAsync_OnAlreadyActive_ReturnsFalse()
    {
        // Tarea 2.1 (tasks.md): producto activo (DeletedAt == null) no debe
        // ser "restaurado" — devolver false para que el Controller mapee
        // TempData[Error]. Patrón tomado de PedidoService.RestoreAsync.
        var (service, _) = NewService(nameof(RestoreAsync_OnAlreadyActive_ReturnsFalse));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        var ok = await service.RestoreAsync(creado.Id, updatedBy: 1);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreAsync_OnNonExistent_ReturnsFalse()
    {
        var (service, _) = NewService(nameof(RestoreAsync_OnNonExistent_ReturnsFalse));

        var ok = await service.RestoreAsync(999_999UL, updatedBy: 1);

        ok.Should().BeFalse();
    }

    // ====================================================================
    // Issue #145 Slice 3: hook de histórico de precios en UpdateAsync.
    // El spec exige que producto_precios_historico reciba UNA fila por cambio
    // real (precio_anterior != precio_nuevo && precio_anterior != 0). El
    // guard precioAnterior != 0 evita phantom rows cuando se hace un primer
    // update sobre un producto recién creado con precio=0.
    // ====================================================================

    /// <summary>
    /// Helper para construir un UpdateProductoDto a partir de un entity
    /// existente con un precio nuevo opcional. Mantiene todos los demás
    /// campos invariantes para que el Mapper solo vea cambio de precio.
    /// </summary>
    private static UpdateProductoDto NewUpdateDto(ProductoDto creado, decimal nuevoPrecio)
        => new()
        {
            Id = creado.Id,
            Codigo = creado.Codigo,
            Nombre = creado.Nombre,
            Descripcion = creado.Descripcion,
            TipoProductoId = creado.TipoProductoId,
            CapacidadKg = creado.CapacidadKg,
            UnidadVenta = creado.UnidadVenta,
            PrecioActual = nuevoPrecio,
            ManejaGarrafaIndividual = creado.ManejaGarrafaIndividual,
            MotivoCambioPrecio = null,
        };

    [Fact]
    public async Task UpdateAsync_PriceChange_CreatesHistoryRow()
    {
        // Spec task 3.1 (a): un cambio real de precio debe dejar exactamente
        // una fila en producto_precios_historico con PrecioAnterior/PrecioNuevo
        // correctos y ChangedBy igual al operator que invocó UpdateAsync.
        var (service, context) = NewService(nameof(UpdateAsync_PriceChange_CreatesHistoryRow));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);
        // PrecioActual del seed es 15000 — UpdateAsync lo sube a 18000.

        var actualizado = await service.UpdateAsync(NewUpdateDto(creado, 18000m), usuarioId: 42);

        actualizado.PrecioActual.Should().Be(18000m, "el update debe persistir el nuevo precio");
        var filas = await context.ProductoPreciosHistorico
            .AsNoTracking()
            .Where(p => p.ProductoId == creado.Id)
            .ToListAsync();
        filas.Should().HaveCount(1, "un cambio real debe registrar exactamente una fila");
        var fila = filas[0];
        fila.PrecioAnterior.Should().Be(15000m, "el precio anterior se snapshot antes del Map");
        fila.PrecioNuevo.Should().Be(18000m, "el precio nuevo es el que quedó en la entity");
        fila.ChangedBy.Should().Be(42UL, "el operator se propaga al histórico");
    }

    [Fact]
    public async Task UpdateAsync_PriceUnchanged_NoHistoryRow()
    {
        // Spec task 3.1 (b): un update sin cambio de precio NO debe ensuciar
        // la tabla append-only. Caso típico: el operador reenvía el form sin
        // tocar el campo PrecioActual — el Service lo deja igual y el hook
        // no inserta.
        var (service, context) = NewService(nameof(UpdateAsync_PriceUnchanged_NoHistoryRow));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        await service.UpdateAsync(NewUpdateDto(creado, creado.PrecioActual), usuarioId: 1);

        var filas = await context.ProductoPreciosHistorico
            .AsNoTracking()
            .Where(p => p.ProductoId == creado.Id)
            .ToListAsync();
        filas.Should().BeEmpty(
            "un update sin cambio de precio no debe generar fila de histórico");
    }

    [Fact]
    public async Task UpdateAsync_PriorZero_NoHistoryRow()
    {
        // Spec task 3.1 (c): guard `precioAnterior != 0`. Si la entity ya
        // tenía PrecioActual=0 (caso raro: seed manual o backfill), el primer
        // update a un valor real NO debe registrar histórico — sería un
        // phantom row que documenta un cambio "desde 0", no un cambio real.
        var (service, context) = NewService(nameof(UpdateAsync_PriorZero_NoHistoryRow));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);
        // Sobreescribir PrecioActual a 0 vía context directo para simular el
        // estado previo del phantom-guard. CreateAsync setea CreatedBy/Activo,
        // pero el precio es data de carga — no hay invariante que impida 0.
        var entity = await context.Productos.FirstAsync(p => p.Id == creado.Id);
        entity.PrecioActual = 0m;
        await context.SaveChangesAsync();

        await service.UpdateAsync(NewUpdateDto(creado, 1000m), usuarioId: 1);

        var filas = await context.ProductoPreciosHistorico
            .AsNoTracking()
            .Where(p => p.ProductoId == creado.Id)
            .ToListAsync();
        filas.Should().BeEmpty(
            "el guard precioAnterior != 0 debe impedir phantom rows en el primer cambio");
    }

    [Fact]
    public async Task UpdateAsync_PriceChange_StoresMotivoCambioPrecio()
    {
        // Spec task 3.1 (d): cuando el operador documenta un motivo de cambio,
        // el string se persiste tal cual en la fila del histórico. La columna
        // es VARCHAR(255) NULL — null se persiste como null (no "" vacío).
        var (service, context) = NewService(nameof(UpdateAsync_PriceChange_StoresMotivoCambioPrecio));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);
        var dto = NewUpdateDto(creado, 18000m);
        dto.MotivoCambioPrecio = "Ajuste por inflacion Q3";

        await service.UpdateAsync(dto, usuarioId: 1);

        var fila = await context.ProductoPreciosHistorico
            .AsNoTracking()
            .FirstAsync(p => p.ProductoId == creado.Id);
        fila.MotivoCambioPrecio.Should().Be("Ajuste por inflacion Q3",
            "el motivo del DTO debe persistirse verbatim en la fila del histórico");
    }

    [Fact]
    public async Task UpdateAsync_PriceChange_LogsInformation()
    {
        // Triangulación: además de la fila persistida, el Service debe loggear
        // el evento a nivel Information para auditoría (operación que toca
        // precios, sensible para el negocio). Usamos TestLogger spy (no Moq,
        // no está en el repo).
        var dbName = nameof(UpdateAsync_PriceChange_LogsInformation);
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        using var context = new ExtraGasDbContext(options);
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var logger = new TestLogger<ProductoService>();
        var service = new ProductoService(context, mapper, logger);

        // Issue #146.1: el Service valida FK TipoProductoId antes de
        // SaveChanges. Sembramos el catálogo para que el helper NewCreateDto
        // (que setea TipoProductoId=1) pueda ejecutar sin tirar
        // ValidationException — queremos probar el log del histórico, no la
        // validación de FK.
        context.TiposProducto.Add(new TipoProducto
        {
            Id = 1,
            Codigo = "GAS",
            Nombre = "Gas",
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);
        var dto = NewUpdateDto(creado, 18000m);
        dto.MotivoCambioPrecio = "Ajuste";

        await service.UpdateAsync(dto, usuarioId: 7);

        logger.Entries.Should().ContainSingle(
            e => e.Level == LogLevel.Information && e.Message.Contains("cambió de precio"),
            "el hook debe emitir un Information cuando registra histórico");
        var entryPrecio = logger.Entries.Single(
            e => e.Level == LogLevel.Information && e.Message.Contains("cambió de precio"));
        entryPrecio.Message.Should().Contain("18000").And.Contain("Ajuste");
    }

[Fact]
    public void UpdateProductoDto_MotivoCambioPrecio_RechazaMasDe255Chars()
    {
        // DataAnnotations del DTO: la columna es VARCHAR(255) — el límite se
        // enforce a nivel modelo para que el Controller rechche el POST antes
        // de invocar al Service. Test plano sobre las anotaciones, sin EF.
        var dto = new UpdateProductoDto
        {
            Id = 1,
            Codigo = "GAS-10",
            Nombre = "Garrafa 10kg",
            TipoProductoId = 1,
            UnidadVenta = "UNIDAD",
            PrecioActual = 15000m,
            ManejaGarrafaIndividual = true,
            MotivoCambioPrecio = new string('x', 256), // 256 > 255
        };

        var ctx = new System.ComponentModel.DataAnnotations.ValidationContext(dto);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            dto, ctx, results, validateAllProperties: true);

        isValid.Should().BeFalse(
            "un motivo de 256 chars debe fallar la validación [StringLength(255)] antes de llegar al Service");
        results.Should().Contain(r =>
            r.MemberNames.Contains(nameof(UpdateProductoDto.MotivoCambioPrecio)));
    }

    // ====================================================================
    // Issue #147 item 6: normalización de Codigo (trim + upper) en el Service.
    // El operador puede tipear " gas-10 " en el form; el Service debe
    // persistir "GAS-10" para que coincida con el índice único
    // `uq_productos_codigo` y con futuras búsquedas.
    // ====================================================================

    [Fact]
    public async Task CreateAsync_CodigoConEspaciosYLowercase_PersisteNormalizado()
    {
        // Spec scenario "Create persists normalized": input " gas-10 " →
        // persistido "GAS-10". El DTO llega con el valor crudo del form;
        // el Service es responsable de aplicar TrimAndUpper antes del INSERT.
        var (service, context) = NewService(nameof(CreateAsync_CodigoConEspaciosYLowercase_PersisteNormalizado));
        var dto = NewCreateDto(codigo: " gas-10 ");

        var creado = await service.CreateAsync(dto, usuarioId: 1);

        creado.Codigo.Should().Be("GAS-10",
            "el Service debe normalizar Codigo (trim + upper) antes de persistir");
        var entity = await context.Productos.AsNoTracking().FirstAsync(p => p.Id == creado.Id);
        entity.Codigo.Should().Be("GAS-10",
            "el valor normalizado debe quedar en la entity persistida");
    }

    [Fact]
    public async Task UpdateAsync_CodigoCambiaAFormaNormalizada_PersisteNormalizado()
    {
        // Spec scenario "Index search normalizes input" (vía Update): si el
        // operador edita un producto y manda " gas-10 " desde el form, el
        // Service debe persistir el valor canónico, no el input crudo.
        var (service, context) = NewService(nameof(UpdateAsync_CodigoCambiaAFormaNormalizada_PersisteNormalizado));
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: 1);

        var updateDto = new UpdateProductoDto
        {
            Id = creado.Id,
            Codigo = " gas-10 ", // mismo codigo en lowercase+espacios
            Nombre = creado.Nombre,
            TipoProductoId = creado.TipoProductoId,
            CapacidadKg = creado.CapacidadKg,
            UnidadVenta = creado.UnidadVenta,
            PrecioActual = creado.PrecioActual,
            ManejaGarrafaIndividual = creado.ManejaGarrafaIndividual,
        };

        var actualizado = await service.UpdateAsync(updateDto, usuarioId: 2);

        actualizado.Codigo.Should().Be("GAS-10",
            "UpdateAsync debe normalizar Codigo igual que CreateAsync");
        var entity = await context.Productos.AsNoTracking().FirstAsync(p => p.Id == creado.Id);
        entity.Codigo.Should().Be("GAS-10");
    }

    [Fact]
    public async Task GetByCodigoAsync_InputLowercase_MatcheaStoredUppercase()
    {
        // Spec scenario "GetByCodigoAsync matches normalized input":
        // producto persistido como "GAS-10", query con "gas-10" → debe
        // matchear. El Service normaliza el input del lookup igual que el
        // persist (canónico a ambos lados).
        var (service, _) = NewService(nameof(GetByCodigoAsync_InputLowercase_MatcheaStoredUppercase));
        await service.CreateAsync(NewCreateDto(codigo: "GAS-10"), usuarioId: 1);

        var encontrado = await service.GetByCodigoAsync("gas-10");

        encontrado.Should().NotBeNull("la query normalizada debe matchear el producto persistido canónico");
        encontrado!.Codigo.Should().Be("GAS-10");
    }

    [Fact]
    public async Task GetPagedAsync_BusquedaLowercase_MatcheaCodigoUppercase()
    {
        // Spec scenario "Index search normalizes input": busqueda " gas "
        // debe matchear el Codigo "GAS-10" porque el LIKE corre contra el
        // valor normalizado. La collation utf8mb4_unicode_ci ya hace el
        // match case-insensitive, pero la normalización del input garantiza
        // que espacios al borde no rompan la búsqueda.
        var (service, _) = NewService(nameof(GetPagedAsync_BusquedaLowercase_MatcheaCodigoUppercase));
        await service.CreateAsync(NewCreateDto(codigo: "GAS-10"), usuarioId: 1);

        var resultado = await service.GetPagedAsync(busqueda: " gas ", soloActivos: true, page: 1, pageSize: 25);

        resultado.Total.Should().Be(1,
            "la búsqueda normalizada debe matchear el producto persistido");
        resultado.Items.Should().ContainSingle().Which.Codigo.Should().Be("GAS-10");
    }

    // ====================================================================
    // Issue #147 item 4: auditoría visible en Details/Edit.
    // GetByIdAsync resuelve los usernames de CreatedBy/UpdatedBy via
    // LoadAuditUsersAsync + AplicarAudit. Los timestamps los mapea
    // AutoMapper por convención desde la entity.
    // ====================================================================

    [Fact]
    public async Task GetByIdAsync_PopulatesAuditFields_WithResolvingUsernames()
    {
        // Spec scenario "ProductoDto populates 4 audit fields":
        // sembramos 2 usuarios (creador y modificador), creamos un producto
        // con uno, lo editamos con el otro, y verificamos que el DTO
        // expone los 4 miembros con los usernames resueltos.
        var (service, context) = NewService(nameof(GetByIdAsync_PopulatesAuditFields_WithResolvingUsernames));
        var creadorId = SeedUsuario(context, "creador");
        var modificadorId = SeedUsuario(context, "modificador");
        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: creadorId);

        // Re-edit con un usuario distinto para que UpdatedBy != CreatedBy.
        var updateDto = new UpdateProductoDto
        {
            Id = creado.Id,
            Codigo = creado.Codigo,
            Nombre = creado.Nombre + " v2",
            TipoProductoId = creado.TipoProductoId,
            CapacidadKg = creado.CapacidadKg,
            UnidadVenta = creado.UnidadVenta,
            PrecioActual = creado.PrecioActual,
            ManejaGarrafaIndividual = creado.ManejaGarrafaIndividual,
        };
        await service.UpdateAsync(updateDto, usuarioId: modificadorId);

        var dto = await service.GetByIdAsync(creado.Id);

        dto.Should().NotBeNull();
        dto!.CreatedAt.Should().Be(creado.CreatedAt,
            "CreatedAt del entity se mapea por convención desde Producto.CreatedAt");
        dto.UpdatedAt.Should().BeAfter(creado.CreatedAt,
            "UpdatedAt del entity se mapea por convención y debe ser posterior al Create");
        dto.CreatedByUserName.Should().Be("creador",
            "LoadAuditUsersAsync resuelve CreatedBy FK → username");
        dto.UpdatedByUserName.Should().Be("modificador",
            "LoadAuditUsersAsync resuelve UpdatedBy FK → username");
    }

    [Fact]
    public async Task GetByIdAsync_AuditFields_NullWhenUsuarioNoExiste()
    {
        // Defensa: si el FK apunta a un usuario que fue hard-deleted (no
        // debería pasar, pero el FK NO es restrict en BD para auditoría),
        // el DTO debe devolver null en lugar de tirar. LoadAuditUsersAsync
        // usa IgnoreQueryFilters() para incluir soft-deleted, pero no
        // puede incluir hard-deleted — el TryGetValue devuelve false y
        // el username queda null.
        var (service, context) = NewService(nameof(GetByIdAsync_AuditFields_NullWhenUsuarioNoExiste));
        var dto = NewCreateDto();
        var entity = new Producto
        {
            Codigo = dto.Codigo,
            Nombre = dto.Nombre,
            TipoProductoId = dto.TipoProductoId,
            CapacidadKg = dto.CapacidadKg,
            UnidadVenta = dto.UnidadVenta,
            PrecioActual = dto.PrecioActual,
            ManejaGarrafaIndividual = dto.ManejaGarrafaIndividual,
            Activo = true,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddDays(-1),
            CreatedBy = 999_999UL, // FK que NO existe
            UpdatedBy = 999_998UL, // FK que NO existe
        };
        context.Productos.Add(entity);
        await context.SaveChangesAsync();

        var result = await service.GetByIdAsync(entity.Id);

        result.Should().NotBeNull();
        result!.CreatedByUserName.Should().BeNull(
            "CreatedBy FK sin usuario → username null (no excepción)");
        result.UpdatedByUserName.Should().BeNull(
            "UpdatedBy FK sin usuario → username null (no excepción)");
    }

    // ====================================================================
    // Issue #147 item 5: 7 branches faltantes en ProductoService. El
    // código de producción YA maneja estos casos — los tests documentan
    // el contrato explícitamente para evitar que un refactor los rompa
    // silenciosamente. Patrón "approval tests for spec scenarios" del
    // strict-tdd.md.
    // ====================================================================

    [Fact]
    public async Task GetByCodigoAsync_NotFound_ReturnsNull()
    {
        // Spec scenario "GetByCodigoAsync missing → null": no hay producto
        // con ese código → la query no devuelve filas → el método devuelve
        // null (no lanza excepción).
        var (service, _) = NewService(nameof(GetByCodigoAsync_NotFound_ReturnsNull));

        var resultado = await service.GetByCodigoAsync("GAS-INEXISTENTE");

        resultado.Should().BeNull();
    }

    [Fact]
    public async Task GetByCodigoAsync_SoftDeleted_ReturnsNull()
    {
        // Spec scenario "GetByCodigoAsync soft-deleted → null": el
        // QueryFilter global (`p => p.DeletedAt == null`) oculta los
        // productos soft-deleted de las queries de lectura. Un producto
        // con DeletedAt != null pero Codigo = "GAS-10" NO debe aparecer.
        var (service, context) = NewService(nameof(GetByCodigoAsync_SoftDeleted_ReturnsNull));
        var creado = await service.CreateAsync(NewCreateDto(codigo: "GAS-10"), usuarioId: 1);
        await service.DeleteAsync(creado.Id, ct: default);
        // Sanity: el producto realmente está soft-deleted en BD.
        var entity = await context.Productos.IgnoreQueryFilters().FirstAsync(p => p.Id == creado.Id);
        entity.DeletedAt.Should().NotBeNull();

        var resultado = await service.GetByCodigoAsync("GAS-10");

        resultado.Should().BeNull(
            "el QueryFilter global oculta soft-deleted de GetByCodigoAsync");
    }

    [Fact]
    public async Task GetByTipoAsync_UnknownTipo_ReturnsEmpty()
    {
        // Spec scenario "GetByTipoAsync empty list": no hay productos con
        // ese tipo → lista vacía (no null, no excepción). El operador
        // puede estar filtrando por un TipoProductoId recién creado y sin
        // productos asignados.
        var (service, _) = NewService(nameof(GetByTipoAsync_UnknownTipo_ReturnsEmpty));

        var resultado = await service.GetByTipoAsync(999UL);

        resultado.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task GetActivosAsync_MixedStatus_ReturnsOnlyActive()
    {
        // Spec scenario "GetActivosAsync filters inactives": con un mix de
        // activos + soft-deleted, solo Activo=true AND DeletedAt IS NULL
        // llegan al resultado. Soft-deleted sin DeletedAt != null ya está
        // excluido por el QueryFilter; este test verifica además que
        // Activo=false (sin soft-delete) tampoco aparece.
        var (service, context) = NewService(nameof(GetActivosAsync_MixedStatus_ReturnsOnlyActive));
        var activo = await service.CreateAsync(NewCreateDto(codigo: "GAS-10"), usuarioId: 1);

        // Producto soft-deleted: Activo=false + DeletedAt!=null → excluido
        // por ambos (QueryFilter + filtro Activo).
        var softDeleted = await service.CreateAsync(NewCreateDto(codigo: "GAS-15"), usuarioId: 1);
        await service.DeleteAsync(softDeleted.Id, ct: default);

        // Producto "inactivo" sin soft-delete (caso raro: Activo=false pero
        // DeletedAt=null — un zombie). Configuración manual vía BD para
        // forzar el estado.
        var zombie = new Producto
        {
            Codigo = "GAS-45",
            Nombre = "Garrafa 45kg",
            TipoProductoId = 1,
            UnidadVenta = "UNIDAD",
            PrecioActual = 30000m,
            ManejaGarrafaIndividual = false,
            Activo = false, // sin soft-delete
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        context.Productos.Add(zombie);
        await context.SaveChangesAsync();

        var resultado = (await service.GetActivosAsync()).ToList();

        resultado.Should().ContainSingle()
            .Which.Id.Should().Be(activo.Id,
                "solo el producto activo (Activo=true + DeletedAt=null) debe aparecer");
        resultado.Should().NotContain(p => p.Id == softDeleted.Id,
            "soft-deleted debe estar excluido");
        resultado.Should().NotContain(p => p.Id == zombie.Id,
            "zombie (Activo=false sin soft-delete) debe estar excluido");
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ThrowsKeyNotFoundException()
    {
        // Spec scenario "UpdateAsync unknown Id → KeyNotFoundException": el
        // FindAsync del Service devuelve null → el método lanza
        // KeyNotFoundException con un mensaje claro. El Controller traduce
        // eso a NotFound o ModelState según el patrón existente.
        // Importante: CapacidadKg > 0 cuando ManejaGarrafaIndividual=true
        // (issue #146.3). Sin esto, el método tira ValidationException
        // ANTES de llegar al FindAsync y nunca veríamos el
        // KeyNotFoundException que queremos verificar.
        var (service, _) = NewService(nameof(UpdateAsync_UnknownId_ThrowsKeyNotFoundException));
        var dto = new UpdateProductoDto
        {
            Id = 999_999UL, // no existe
            Codigo = "GAS-10",
            Nombre = "Garrafa 10kg",
            TipoProductoId = 1,
            UnidadVenta = "UNIDAD",
            CapacidadKg = 10m,
            PrecioActual = 15000m,
            ManejaGarrafaIndividual = true,
        };

        var act = async () => await service.UpdateAsync(dto, usuarioId: 1);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*999999*",
                "el mensaje debe incluir el Id buscado para que el Controller pueda mostrarlo");
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        // Spec scenario "DeleteAsync unknown Id → false": a diferencia de
        // UpdateAsync, Delete NO lanza — devuelve false para que el
        // Controller mapee TempData[Error]. Coherente con RestoreAsync
        // que también devuelve false para Id inexistente.
        var (service, _) = NewService(nameof(DeleteAsync_UnknownId_ReturnsFalse));

        var resultado = await service.DeleteAsync(999_999UL);

        resultado.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_NullUser_Succeeds()
    {
        // Spec scenario "CreateAsync null userId → no crash": usado por
        // tests automatizados, seeds y scripts de bootstrap que no tienen
        // un usuario "humano" detrás. La entity persiste con
        // CreatedBy=NULL/UpdatedBy=NULL — la auditoría queda "huérfana"
        // pero la operación no falla.
        var (service, context) = NewService(nameof(CreateAsync_NullUser_Succeeds));

        var creado = await service.CreateAsync(NewCreateDto(), usuarioId: null);

        creado.Should().NotBeNull();
        creado.Id.Should().BeGreaterThan(0);
        var entity = await context.Productos.AsNoTracking().FirstAsync(p => p.Id == creado.Id);
        entity.CreatedBy.Should().BeNull(
            "null usuario → CreatedBy NULL en BD (operación sincrónica válida)");
        entity.UpdatedBy.Should().BeNull();
    }
}