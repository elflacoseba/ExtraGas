using AutoMapper;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using FluentAssertions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Contract tests para el <see cref="MappingProfile"/>: verifican que los
/// miembros de auditoría NO se mapean desde los DTOs de escritura
/// (<see cref="CreateClienteDto"/>, <see cref="UpdateClienteDto"/>) hacia la
/// entity <see cref="Cliente"/>.
///
/// Issue #118: hoy la auditoría sobrevive porque los DTOs no exponen los
/// campos y AutoMapper los ignora por convención. Eso es frágil: si alguien
/// agrega uno de esos miembros a un DTO o un <c>.MapFrom(...)</c> al profile,
/// el Service pierde auditoría silenciosamente. Estos tests blindan el
/// contrato en el plano observable:
///   1. Update preserva auditoría real (merge sobre entity cargada de BD).
///   2. Create produce auditoría en default (obliga al Service a setearla).
///
/// El <c>.ForMember(..., o => o.Ignore())</c> en el profile es defensa en
/// profundidad: si un futuro refactor agrega un campo de auditoría al DTO
/// por error, AutoMapper lo silenciaría igual por convención (el DTO source
/// no tiene ese miembro), pero el <c>Ignore()</c> explícito documenta el
/// contrato y bloquea intentos de <c>.MapFrom(...)</c> en el profile.
/// </summary>
public class MappingProfileClienteTests
{
    private static IMapper NewMapper()
    {
        // No llamamos AssertConfigurationIsValid() porque el MappingProfile tiene
        // unmapped properties intencionales en otros entities (Usuario, Empleado)
        // fuera del scope de este test — la auditoría de #118 es local a Cliente.
        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        return config.CreateMapper();
    }

    [Fact]
    public void Update_Map_NoPisaAuditoria_SobreEntityExistente()
    {
        // Arrange: entity ya cargada de BD con timestamps y autores reales.
        // Simula lo que hace UpdateAsync después de FindAsync.
        var mapper = NewMapper();
        var fechaOriginal = DateTime.UtcNow.AddDays(-30);
        var updatedOriginal = DateTime.UtcNow.AddDays(-1);
        var entity = new Cliente
        {
            Id = 42,
            Nombre = "Vacio",
            Apellido = "Antes",
            TelefonoPrincipal = "1100000000",
            FechaAlta = DateOnly.FromDateTime(fechaOriginal),
            CreatedAt = fechaOriginal,
            UpdatedAt = updatedOriginal,
            CreatedBy = 7,
            UpdatedBy = 8,
            DeletedAt = null
        };

        var dto = new UpdateClienteDto
        {
            Id = 42,
            Nombre = "Juan",
            Apellido = "Perez",
            Dni = "12345678",
            TelefonoPrincipal = "1144556677"
            // Sin FechaAlta / CreatedAt / UpdatedAt / CreatedBy / UpdatedBy /
            // DeletedAt: el DTO no los expone.
        };

        // Act: merge del DTO sobre la entity (mismo flujo que UpdateAsync).
        mapper.Map(dto, entity);

        // Assert: los datos del DTO se aplican...
        entity.Nombre.Should().Be("Juan");
        entity.Apellido.Should().Be("Perez");
        entity.Dni.Should().Be("12345678");
        entity.TelefonoPrincipal.Should().Be("1144556677");

        // ...pero la auditoría queda intacta. El Service es el único que
        // toca UpdatedAt/UpdatedBy después del Map (ver ClienteService.UpdateAsync).
        entity.FechaAlta.Should().Be(DateOnly.FromDateTime(fechaOriginal),
            "FechaAlta es audit trail del alta y no debe cambiar en un Edit");
        entity.CreatedAt.Should().Be(fechaOriginal, "CreatedAt solo lo setea CreateAsync");
        entity.UpdatedAt.Should().Be(updatedOriginal, "UpdatedAt lo pisará el Service después");
        entity.CreatedBy.Should().Be(7, "CreatedBy solo lo setea CreateAsync");
        entity.UpdatedBy.Should().Be(8, "UpdatedBy lo pisará el Service después");
        entity.DeletedAt.Should().BeNull("DeletedAt lo manejan Delete/Restore, no el mapeo");
    }

    [Fact]
    public void Create_Map_DevuelveAuditoriaEnDefault()
    {
        // Arrange: CreateAsync parte de un CreateClienteDto. El Service espera
        // setear CreatedAt/UpdatedAt/CreatedBy/UpdatedBy/FechaAlta él mismo,
        // por lo que la entity resultante del Map debe tener esos miembros en
        // default — si vinieran con valores del profile, el Service todavía
        // ganaría (es la última escritura), pero el contrato "la auditoría la
        // maneja el Service" se rompe.
        var mapper = NewMapper();
        var dto = new CreateClienteDto
        {
            Nombre = "Juan",
            Apellido = "Perez",
            Dni = "12345678",
            TelefonoPrincipal = "1144556677"
        };

        // Act
        var entity = mapper.Map<Cliente>(dto);

        // Assert
        entity.Nombre.Should().Be("Juan");
        entity.Apellido.Should().Be("Perez");
        entity.Dni.Should().Be("12345678");
        entity.TelefonoPrincipal.Should().Be("1144556677");

        entity.FechaAlta.Should().Be(default(DateOnly),
            "FechaAlta la setea el Service con la fecha del alta");
        entity.CreatedAt.Should().Be(default(DateTime),
            "CreatedAt lo setea CreateAsync, no el mapeo");
        entity.UpdatedAt.Should().Be(default(DateTime),
            "UpdatedAt lo setea CreateAsync/UpdateAsync, no el mapeo");
        entity.CreatedBy.Should().BeNull("CreatedBy lo setea CreateAsync desde el caller");
        entity.UpdatedBy.Should().BeNull("UpdatedBy lo setea CreateAsync/UpdateAsync desde el caller");
        entity.DeletedAt.Should().BeNull("DeletedAt lo setean Delete/Restore, no el mapeo");
    }
}