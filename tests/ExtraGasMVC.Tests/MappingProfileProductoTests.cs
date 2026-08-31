using AutoMapper;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using FluentAssertions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Contract tests para el <see cref="MappingProfile"/> respecto al mapeo de
/// Producto → ProductoDto en los campos de auditoría.
///
/// Issue #147 item 4 + regresión #118: los campos de auditoría
/// (<c>CreatedAt</c>, <c>UpdatedAt</c>, <c>CreatedByUserName</c>,
/// <c>UpdatedByUserName</c>) en el DTO deben poblarse explícitamente — el
/// Service los resuelve vía <c>LoadAuditUsersAsync</c> y NO se debe permitir
/// que AutoMapper los pise silenciosamente si alguien agrega un miembro
/// <c>CreatedBy</c> al DTO mañana. El <c>.ForMember(...).Ignore()</c>
/// explícito documenta y bloquea el camino.
///
/// Item 4 spec: "AutoMapper MUST NOT overwrite usernames — WHEN AutoMapper
/// maps Producto → ProductoDto, the four audit fields MUST be sourced from
/// explicit service-level resolution, not the entity directly."
/// </summary>
public class MappingProfileProductoTests
{
    private static IMapper NewMapper()
    {
        // Mismo criterio que MappingProfileClienteTests.NewMapper():
        // NO llamamos AssertConfigurationIsValid() porque el MappingProfile
        // tiene unmapped properties intencionales en otros entities
        // (CreateClienteDto/UpdateClienteDto/CreatePedidoDto/etc. — campos
        // de auditoría que el Service setea explícitamente). Estos
        // contratos están documentados y validados por separado en sus
        // tests respectivos. La auditoría de #147 item 4 es local al
        // mapeo Producto → ProductoDto.
        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        return config.CreateMapper();
    }

    [Fact]
    public void Producto_DtoExposesAuditFields()
    {
        // Sanity: los 4 miembros existen en el DTO. Si alguien los borra
        // por error, este test rompe antes que cualquier vista o servicio
        // (Details/Edit/audit-card) empiece a fallar en runtime.
        typeof(ProductoDto).GetProperty(nameof(ProductoDto.CreatedAt))
            .Should().NotBeNull("ProductoDto debe exponer CreatedAt");
        typeof(ProductoDto).GetProperty(nameof(ProductoDto.UpdatedAt))
            .Should().NotBeNull("ProductoDto debe exponer UpdatedAt");
        typeof(ProductoDto).GetProperty(nameof(ProductoDto.CreatedByUserName))
            .Should().NotBeNull("ProductoDto debe exponer CreatedByUserName");
        typeof(ProductoDto).GetProperty(nameof(ProductoDto.UpdatedByUserName))
            .Should().NotBeNull("ProductoDto debe exponer UpdatedByUserName");
    }

    [Fact]
    public void Producto_DtoFromEntity_CreatedByUserName_NotOverwrittenByEntityFK()
    {
        // Regresión #118 (también documentada en MappingProfileClienteTests):
        // el entity tiene `CreatedBy` (ulong FK a usuarios.id). Si AutoMapper
        // intentara mapear la FK al DTO por convención, pisaría el username
        // resuelto por el Service. El `.ForMember(...).Ignore()` en el profile
        // bloquea eso explícitamente.
        //
        // Arrange: entity con timestamps reales + FK de auditor seteada.
        var mapper = NewMapper();
        var fechaAlta = DateTime.UtcNow.AddYears(-1);
        var fechaModif = DateTime.UtcNow.AddDays(-3);
        var entity = new Producto
        {
            Id = 10,
            Codigo = "GAS-10",
            Nombre = "Garrafa 10kg",
            TipoProductoId = 1,
            UnidadVenta = "UNIDAD",
            PrecioActual = 15000m,
            ManejaGarrafaIndividual = true,
            Activo = true,
            CreatedAt = fechaAlta,
            UpdatedAt = fechaModif,
            CreatedBy = 5, // FK — NO debe terminar como string en CreatedByUserName
            UpdatedBy = 7, // FK — NO debe terminar como string en UpdatedByUserName
        };

        // Act: map directo entity → DTO. Sin enrichment del Service.
        var dto = mapper.Map<ProductoDto>(entity);

        // Assert: los timestamps se mapearon por convención...
        dto.CreatedAt.Should().Be(fechaAlta);
        dto.UpdatedAt.Should().Be(fechaModif);

        // ...pero los usernames NO fueron sobreescritos por la FK (ulong → string
        // no es un mapeo válido y el .Ignore() lo bloquea). El Service los
        // setea explícitamente vía LoadAuditUsersAsync + AplicarAudit.
        dto.CreatedByUserName.Should().BeNull(
            "el DTO sale del Map sin CreatedByUserName — el Service lo setea via LoadAuditUsersAsync");
        dto.UpdatedByUserName.Should().BeNull(
            "el DTO sale del Map sin UpdatedByUserName — el Service lo setea via LoadAuditUsersAsync");
    }
}