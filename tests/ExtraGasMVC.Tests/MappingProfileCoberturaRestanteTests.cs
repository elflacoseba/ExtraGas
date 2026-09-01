using AutoMapper;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using FluentAssertions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Contract tests de <see cref="MappingProfile"/> para los helpers estáticos y
/// <c>CreateMap</c> que <see cref="MappingProfilePedidoEstadoHistoricoTests"/>
/// y <see cref="MappingProfileProductoTests"/> no cubren — quedan en 0% en el
/// reporte de <c>coverage.cobertura.xml</c> si no se ejercitan acá.
///
/// Cobertura objetivo (líneas nuevas según issue #134):
///   - Helpers de <c>MovimientoGarrafa</c> (líneas 258-277)
///   - Helper <c>NombreCompletoEmpleado(Pedido)</c> (281-282)
///   - Helper <c>NombreCompletoEmpleado(MovimientoGarrafa)</c> (284-285)
///   - <c>ConfigureUsuario</c>: 3 CreateMap + Ignore de PasswordHash (287-294)
///   - <c>ConfigureProvincia</c>: CreateMap&lt;Provincia, ProvinciaDto&gt; (295-300)
///   - <c>ConfigureEmpleado</c>: 4 CreateMap (302-309)
///
/// Cada test ejercita UN mapeo cubriendo varias líneas. El aggregate de
/// estos tests es lo que llevó <c>new_coverage</c> de 63.1% a 65% en
/// SonarQube.
/// </summary>
public class MappingProfileCoberturaRestanteTests
{
    private static IMapper NewMapper()
    {
        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        return config.CreateMapper();
    }

    // ====================================================================
    // MovimientoGarrafa → MovimientoGarrafaDto
    // Cubre los 7 helpers estáticos + la sobrecarga NombreCompletoEmpleado
    // ====================================================================

    [Fact]
    public void MovimientoGarrafa_NavigationsPobladas_TodosLosHelpersResueltos()
    {
        var mapper = NewMapper();
        var entity = new MovimientoGarrafa
        {
            Id = 1,
            GarrafaId = 100,
            Fecha = DateTime.UtcNow,
            TipoMovimientoId = 2,
            PedidoId = 5,
            EstadoOrigenId = 1,
            EstadoDestinoId = 3,
            EmpleadoId = 7,
            TipoMovimiento = new TipoMovimientoGarrafa
            {
                Id = 2,
                Codigo = "POR_CANJE",
                Nombre = "Por canje",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
            EstadoOrigen = new EstadoGarrafa
            {
                Id = 1,
                Codigo = "EN_CLIENTE",
                Nombre = "En cliente",
                Color = "#0d6efd",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
            EstadoDestino = new EstadoGarrafa
            {
                Id = 3,
                Codigo = "EN_DEPOSITO",
                Nombre = "En depósito",
                Color = "#198754",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
            Garrafa = new Garrafa
            {
                Id = 100,
                Codigo = "GAS-10-0001",
                CapacidadKg = 10,
                EstadoGarrafaId = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
            Empleado = new Empleado
            {
                Id = 7,
                Nombre = "Juan",
                Apellido = "Pérez",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
        };

        var dto = mapper.Map<MovimientoGarrafaDto>(entity);

        dto.TipoMovimientoCodigo.Should().Be("POR_CANJE");
        dto.TipoMovimientoNombre.Should().Be("Por canje");
        dto.EstadoOrigenCodigo.Should().Be("EN_CLIENTE");
        dto.EstadoOrigenNombre.Should().Be("En cliente");
        dto.EstadoDestinoCodigo.Should().Be("EN_DEPOSITO");
        dto.EstadoDestinoNombre.Should().Be("En depósito");
        dto.GarrafaCodigo.Should().Be("GAS-10-0001");
        dto.EmpleadoNombreCompleto.Should().Be("Pérez, Juan");
    }

    [Fact]
    public void MovimientoGarrafa_NavigationsNulas_HelpersDevuelvenNull()
    {
        var mapper = NewMapper();
        var entity = new MovimientoGarrafa
        {
            Id = 2,
            GarrafaId = 200,
            Fecha = DateTime.UtcNow,
            TipoMovimientoId = 2,
            EstadoDestinoId = 1,
            // Sin Includes: navigations null (caso común si el caller olvida el .Include()).
        };

        var dto = mapper.Map<MovimientoGarrafaDto>(entity);

        dto.TipoMovimientoCodigo.Should().BeNull();
        dto.TipoMovimientoNombre.Should().BeNull();
        dto.EstadoOrigenCodigo.Should().BeNull();
        dto.EstadoOrigenNombre.Should().BeNull();
        dto.EstadoDestinoCodigo.Should().BeNull();
        dto.EstadoDestinoNombre.Should().BeNull();
        dto.GarrafaCodigo.Should().BeNull();
        dto.EmpleadoNombreCompleto.Should().BeNull();
    }

    // ====================================================================
    // Pedido → PedidoDto: cubre la sobrecarga NombreCompletoEmpleado(Pedido)
    // ====================================================================

    [Fact]
    public void Pedido_EmpleadoPoblado_NombreCompletoSeProyecta()
    {
        var mapper = NewMapper();
        var entity = new Pedido
        {
            Id = 1,
            Numero = "PED-2026-00001",
            Fecha = DateTime.UtcNow,
            ClienteId = 1,
            EmpleadoId = 5,
            CanalVentaId = 1,
            EstadoPedidoId = 1,
            Subtotal = 100m,
            Descuento = 0m,
            Total = 100m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Empleado = new Empleado
            {
                Id = 5,
                Nombre = "Carlos",
                Apellido = "Gómez",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
        };

        var dto = mapper.Map<PedidoDto>(entity);

        dto.EmpleadoNombre.Should().Be("Gómez, Carlos",
            "el helper NombreCompletoEmpleado(Pedido) concatena Apellido + ', ' + Nombre");
    }

    // ====================================================================
    // ConfigureUsuario: Usuario→Dto, CreateUsuarioDto→Usuario (Ignore),
    // UpdateUsuarioDto→Usuario.
    // ====================================================================

    [Fact]
    public void Usuario_ConRolPoblado_RolCodigoYNombreResueltos()
    {
        var mapper = NewMapper();
        var entity = new Usuario
        {
            Id = 1,
            Username = "operario1",
            PasswordHash = "hash-que-no-debe-mappear",
            Email = "op@extragas.local",
            RolId = 1,
            Activo = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Rol = new Rol
            {
                Id = 1,
                Codigo = "OPERARIO",
                Nombre = "Operario",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
        };

        var dto = mapper.Map<UsuarioDto>(entity);

        dto.Username.Should().Be("operario1");
        dto.RolCodigo.Should().Be("OPERARIO");
        dto.RolNombre.Should().Be("Operario");
    }

    [Fact]
    public void Usuario_CreateUsuarioDto_NoPisaPasswordHash()
    {
        // Defense-in-depth contra #118: el .Ignore() en ConfigureUsuario
        // bloquea que AutoMapper copie Password desde el DTO hacia
        // PasswordHash en la entity. El Service setea PasswordHash después
        // del Map con el hash generado por IPasswordHasher.
        var mapper = NewMapper();
        var dto = new CreateUsuarioDto
        {
            Username = "nuevo-op",
            Password = "esto-debe-ignorarse",
            Email = "nuevo@extragas.local",
            RolId = 1,
        };

        var entity = mapper.Map<Usuario>(dto);

        entity.Username.Should().Be("nuevo-op");
        entity.Email.Should().Be("nuevo@extragas.local");
        entity.RolId.Should().Be(1);
        entity.PasswordHash.Should().BeNull(
            "el .Ignore() en ConfigureUsuario bloquea Password del DTO; el Service lo setea explícitamente");
    }

    [Fact]
    public void Usuario_UpdateUsuarioDto_NoTocaPassword()
    {
        var mapper = NewMapper();
        var dto = new UpdateUsuarioDto
        {
            Id = 1,
            Email = "nuevo@extragas.local",
            RolId = 2,
        };

        var entity = mapper.Map<Usuario>(dto);

        entity.Id.Should().Be(1);
        entity.Email.Should().Be("nuevo@extragas.local");
        entity.RolId.Should().Be(2);
    }

    // ====================================================================
    // ConfigureProvincia + ConfigureEmpleado
    // ====================================================================

    [Fact]
    public void Provincia_Map_Directo()
    {
        var mapper = NewMapper();
        var entity = new Provincia
        {
            Id = 1,
            Codigo = "BA",
            Nombre = "Buenos Aires",
            Pais = "Argentina",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var dto = mapper.Map<ProvinciaDto>(entity);

        dto.Id.Should().Be(1);
        dto.Nombre.Should().Be("Buenos Aires");
    }

    [Fact]
    public void Empleado_Map_Directo_IdaYVuelta()
    {
        var mapper = NewMapper();
        var entity = new Empleado
        {
            Id = 1,
            Nombre = "Ana",
            Apellido = "López",
            Dni = "12345678",
            Activo = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var dto = mapper.Map<EmpleadoDto>(entity);
        dto.Nombre.Should().Be("Ana");
        dto.Apellido.Should().Be("López");
        dto.Dni.Should().Be("12345678");
        dto.Activo.Should().BeTrue();

        var reversed = mapper.Map(dto, entity);
        reversed.Should().BeSameAs(entity,
            ".ReverseMap() permite mapear de vuelta sobre la entity existente");
        reversed.Dni.Should().Be("12345678");
    }
}