using System.Reflection;
using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests del fix para la issue #107: race condition en validación de DNI.
/// El check previo (IsDniUniqueAsync) es best-effort: dos requests concurrentes
/// pueden pasarlo y la BD rechaza el segundo INSERT/UPDATE con errno 1062
/// (duplicate entry). Sin este mapeo, el Controller mostraría al usuario un
/// error SQL crudo.
///
/// La estrategia es:
///   - Helper estático testeable que mapea DbUpdateException(1062) → InvalidOperationException.
///   - Tests unitarios del helper (positivos / negativos / consistencia de mensaje).
///   - Tests de integración end-to-end con un DbContext que override
///     SaveChangesAsync para simular el 1062 (InMemoryDatabase no invoca
///     SaveChangesInterceptors en EF Core, por eso vamos por este camino).
/// </summary>
public class ClienteServiceDniRaceConditionTests
{
    private const string TargetDni = "99999999";

    /// <summary>
    /// Crea un <see cref="MySqlException"/> con un <c>Number</c> específico.
    /// MySqlConnector 2.x hace TODOS sus constructores internos (Assembly-level,
    /// equivalentes a <c>internal</c> en C#). Invocamos por reflection el
    /// ctor <c>(MySqlErrorCode, string)</c> que es la forma oficial de la lib
    /// para crear instancias con un error code concreto. <c>Number</c> queda
    /// seteado correctamente porque ese ctor deriva Number de ErrorCode.
    /// </summary>
    private static MySqlException BuildMySqlException(int number, string message)
    {
        var errorCode = (MySqlErrorCode)number;
        var ctor = typeof(MySqlException).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(MySqlErrorCode), typeof(string) },
            modifiers: null)
            ?? throw new InvalidOperationException(
                "MySqlException(MySqlErrorCode, string) ctor not found; " +
                "MySqlConnector cambió su API interna. Actualizar este helper.");
        return (MySqlException)ctor.Invoke(new object[] { errorCode, message });
    }

    /// <summary>
    /// DbContext de testing que permite inyectar una <see cref="DbUpdateException"/>
    /// desde <c>SaveChangesAsync</c>. Replica el escenario real de race condition:
    /// el check previo (IsDniUniqueAsync) pasa porque InMemory no tiene UNIQUE
    /// constraint, pero SaveChangesAsync falla con 1062 como si fuera la BD real.
    /// </summary>
    private sealed class FakeDbContext : ExtraGasDbContext
    {
        public DbUpdateException? ToThrowOnSave { get; set; }

        public FakeDbContext(DbContextOptions<ExtraGasDbContext> options) : base(options) { }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
        {
            if (ToThrowOnSave is not null) throw ToThrowOnSave;
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        }
    }

    private static (ClienteService service, FakeDbContext context) NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new FakeDbContext(options);
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        var mapper = mapperConfig.CreateMapper();
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var service = new ClienteService(context, mapper, cache);
        return (service, context);
    }

    private static CreateClienteDto NewCreateDto(string dni) => new()
    {
        Nombre = "Juan",
        Apellido = "Perez",
        Dni = dni,
        TelefonoPrincipal = "1144556677",
    };

    private static UpdateClienteDto NewUpdateDto(ulong id, string newDni) => new()
    {
        Id = id,
        Nombre = "Juan",
        Apellido = "Perez",
        Dni = newDni,
        TelefonoPrincipal = "1144556677",
    };

    // ====================================================================
    // Tests unitarios del helper MapDuplicateDniException
    // ====================================================================

    [Fact]
    public void MapDuplicateDniException_con_MySqlException_1062_retorna_InvalidOperationException()
    {
        var inner = BuildMySqlException(1062,
            "Duplicate entry '99999999' for key 'idx_clientes_dni_unique'");
        var dbex = new DbUpdateException("INSERT failed", inner);

        var result = ClienteService.MapDuplicateDniException(dbex);

        result.Should().NotBeNull();
        result!.Message.Should().Be("El DNI ingresado ya está registrado.");
    }

    [Fact]
    public void MapDuplicateDniException_con_MySqlException_otro_numero_retorna_null()
    {
        // Otro error de MySQL (ej. FK violation) NO debe mapearse a "DNI duplicado".
        var inner = BuildMySqlException(1452,
            "Cannot add or update a child row: a foreign key constraint fails");
        var dbex = new DbUpdateException("FK failed", inner);

        var result = ClienteService.MapDuplicateDniException(dbex);

        result.Should().BeNull("un 1452 no es duplicate entry; dejar burbujear la excepción original");
    }

    [Fact]
    public void MapDuplicateDniException_con_inner_no_MySql_retorna_null()
    {
        // Algunos providers envuelven en algo distinto a MySqlException.
        // No debemos confundirnos.
        var inner = new InvalidOperationException("some other provider inner");
        var dbex = new DbUpdateException("INSERT failed", inner);

        var result = ClienteService.MapDuplicateDniException(dbex);

        result.Should().BeNull();
    }

    [Fact]
    public void MapDuplicateDniException_con_inner_null_retorna_null()
    {
        var dbex = new DbUpdateException("INSERT failed without inner");

        var result = ClienteService.MapDuplicateDniException(dbex);

        result.Should().BeNull();
    }

    [Fact]
    public void MapDuplicateDniException_mensaje_es_identico_al_check_previo()
    {
        // UX consistente: el operador que pase el check y choque con la BD
        // debe ver EXACTAMENTE el mismo mensaje que el que falló el check previo.
        // Si divergen, el operador percibe "dos errores distintos" para el
        // mismo problema de fondo y desconfía.
        var inner = BuildMySqlException(1062,
            "Duplicate entry '99999999' for key 'idx_clientes_dni_unique'");
        var dbex = new DbUpdateException("INSERT failed", inner);

        var mapped = ClienteService.MapDuplicateDniException(dbex);
        var checkPrevio = "El DNI ingresado ya está registrado."; // mensaje literal en CreateAsync/UpdateAsync

        mapped!.Message.Should().Be(checkPrevio);
    }

    // ====================================================================
    // Tests de integración: race condition end-to-end (FakeDbContext dispara 1062)
    // ====================================================================

    [Fact]
    public async Task CreateAsync_cuando_SaveChangesAsync_tira_1062_lanza_InvalidOperationException()
    {
        // El check previo pasa (InMemory no tiene UNIQUE constraint, no detecta duplicados),
        // luego SaveChangesAsync falla con DbUpdateException(1062) y el Service debe mapearla
        // a InvalidOperationException con el MISMO mensaje que el check previo.
        var dbName = nameof(CreateAsync_cuando_SaveChangesAsync_tira_1062_lanza_InvalidOperationException);
        var (service, context) = NewService(dbName);

        // Sembramos un cliente con el DNI target (escenario: ya existe otro cliente con ese DNI).
        await service.CreateAsync(NewCreateDto(TargetDni), createdBy: 1);

        // Configuramos el FakeDbContext para que el próximo SaveChangesAsync tire el 1062.
        var mysql = BuildMySqlException(1062,
            $"Duplicate entry '{TargetDni}' for key 'idx_clientes_dni_unique'");
        context.ToThrowOnSave = new DbUpdateException("INSERT failed: simulated 1062", mysql);

        var dto = NewCreateDto(TargetDni);

        var act = async () => await service.CreateAsync(dto, createdBy: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("El DNI ingresado ya está registrado.");
    }

    [Fact]
    public async Task UpdateAsync_cuando_SaveChangesAsync_tira_1062_lanza_InvalidOperationException()
    {
        // Cliente existe con DNI A. El operador intenta cambiarlo a DNI B (que ya está
        // tomado). El check previo lo permite porque solo compara A != A. SaveChangesAsync
        // choca con UNIQUE INDEX de la BD y tira 1062.
        var dbName = nameof(UpdateAsync_cuando_SaveChangesAsync_tira_1062_lanza_InvalidOperationException);
        var (service, context) = NewService(dbName);

        var seed = await service.CreateAsync(NewCreateDto("11111111"), createdBy: 1);
        await service.CreateAsync(NewCreateDto(TargetDni), createdBy: 1);

        // Ahora intentamos Update sobre `seed` cambiándole el DNI al target. El check
        // previo pasa porque compara DNI != otros (excluyéndose a sí mismo por Id).
        // El 1062 lo dispara SaveChangesAsync (simulado).
        var mysql = BuildMySqlException(1062,
            $"Duplicate entry '{TargetDni}' for key 'idx_clientes_dni_unique'");
        context.ToThrowOnSave = new DbUpdateException("UPDATE failed: simulated 1062", mysql);

        var updateDto = NewUpdateDto(seed.Id, TargetDni);

        var act = async () => await service.UpdateAsync(updateDto, updatedBy: 2);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("El DNI ingresado ya está registrado.");
    }

    [Fact]
    public async Task CreateAsync_cuando_SaveChangesAsync_tira_1062_no_persiste_el_cliente()
    {
        // Garantía: si la BD rechaza el INSERT, el Cliente NO debe quedar en la BD.
        var dbName = nameof(CreateAsync_cuando_SaveChangesAsync_tira_1062_no_persiste_el_cliente);
        var (service, context) = NewService(dbName);

        await service.CreateAsync(NewCreateDto(TargetDni), createdBy: 1);

        var mysql = BuildMySqlException(1062,
            $"Duplicate entry '{TargetDni}' for key 'idx_clientes_dni_unique'");
        context.ToThrowOnSave = new DbUpdateException("INSERT failed: simulated 1062", mysql);

        var act = async () => await service.CreateAsync(NewCreateDto(TargetDni), createdBy: 1);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Solo el primer cliente (sin throw) debe estar en la BD.
        var todos = await service.GetAllAsync();
        todos.Should().HaveCount(1, "el segundo INSERT fue rechazado por la BD");
    }

    [Fact]
    public async Task CreateAsync_cuando_SaveChangesAsync_tira_otra_DbUpdate_no_lanza_InvalidOperationException()
    {
        // Una DbUpdateException SIN inner MySqlException 1062 debe burbujear
        // tal cual (para que el Controller la registre como error genérico,
        // no como "DNI duplicado").
        var dbName = nameof(CreateAsync_cuando_SaveChangesAsync_tira_otra_DbUpdate_no_lanza_InvalidOperationException);
        var (service, context) = NewService(dbName);

        context.ToThrowOnSave = new DbUpdateException("connection lost");

        var dto = NewCreateDto(TargetDni);

        var act = async () => await service.CreateAsync(dto, createdBy: 1);

        await act.Should().ThrowAsync<DbUpdateException>()
            .WithMessage("connection lost");
    }
}
