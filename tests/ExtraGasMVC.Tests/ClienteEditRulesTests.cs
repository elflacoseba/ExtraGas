using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Extensions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de la regla "no editable" del módulo Clientes.
/// Garantizan que <see cref="ClienteEditRules.PreservarFechaAlta"/> impone la
/// convención: <c>FechaAlta</c> es audit trail inmutable del alta. El flag
/// <c>Activo</c> ya no existe en la entity (Issue #115) — el estado se
/// deriva de <c>DeletedAt</c>, que el form de Edit no expone, así que esta
/// clase no necesita preservar nada más.
/// </summary>
public class ClienteEditRulesTests
{
    private static Cliente NewEntity(DateOnly fechaAlta) => new()
    {
        Id = 1,
        Nombre = "Juan",
        Apellido = "Pérez",
        TelefonoPrincipal = "1144556677",
        FechaAlta = fechaAlta,
    };

    [Fact]
    public void PreservarFechaAlta_ConFechaAltaDistinta_LaRestaura()
    {
        // Escena: operador edita y retrocede la fecha de alta. La regla
        // tiene que restaurar el valor de BD.
        var fechaAltaOriginal = new DateOnly(2024, 1, 15);
        var entity = NewEntity(fechaAlta: fechaAltaOriginal);
        entity.FechaAlta = new DateOnly(2020, 6, 1); // valor que dejó el AutoMapper

        ClienteEditRules.PreservarFechaAlta(entity, fechaAltaOriginal);

        Assert.Equal(fechaAltaOriginal, entity.FechaAlta);
    }

    [Fact]
    public void PreservarFechaAlta_ConFechaAltaIgual_NoCambia()
    {
        var fechaAlta = new DateOnly(2024, 1, 15);
        var entity = NewEntity(fechaAlta: fechaAlta);

        ClienteEditRules.PreservarFechaAlta(entity, fechaAltaOriginal: fechaAlta);

        Assert.Equal(fechaAlta, entity.FechaAlta);
    }

    [Fact]
    public void PreservarFechaAlta_NoTocaOtrosCampos()
    {
        // La regla solo afecta FechaAlta. Verifica que no toca campos que
        // el form sí puede editar (ej. Nombre).
        var fechaAltaOriginal = new DateOnly(2024, 1, 15);
        var entity = NewEntity(fechaAlta: fechaAltaOriginal);
        entity.Nombre = "Nuevo nombre";
        entity.FechaAlta = new DateOnly(1999, 12, 31);

        ClienteEditRules.PreservarFechaAlta(entity, fechaAltaOriginal);

        Assert.Equal(fechaAltaOriginal, entity.FechaAlta);
        Assert.Equal("Nuevo nombre", entity.Nombre); // quedó como el form lo mandó
    }
}

/// <summary>
/// Tests de regresión estructurales: garantizan que el contrato del DTO no
/// vuelve a exponer <c>FechaAlta</c> como propiedad editable. Si alguien
/// vuelve a ponerla en el base o en Update, estos tests fallan en
/// compilación.
///
/// <para>Issue #115: el flag <c>Activo</c> ya no es un campo escribible del
/// DTO (es un getter derivado de <c>DeletedAt</c>). El contrato testea que
/// el getter sigue existiendo para las vistas, pero nadie puede asignarlo.</para>
/// </summary>
public class ClienteDtoContratoTests
{
    [Fact]
    public void UpdateClienteDto_NoExponeFechaAlta()
    {
        Assert.Null(typeof(UpdateClienteDto).GetProperty("FechaAlta"));
    }

    [Fact]
    public void CreateClienteDto_NoExponeFechaAlta()
    {
        Assert.Null(typeof(CreateClienteDto).GetProperty("FechaAlta"));
    }

    [Fact]
    public void ClienteDto_ExponeActivoComoGetterYFechaAlta_ParaDisplay()
    {
        // Issue #115: `Activo` sigue existiendo como propiedad pública del
        // ClienteDto, pero ahora es getter-only (sin setter). La
        // verificación `GetProperty` la encuentra igual — solo confirma que
        // las vistas (Details, Index) pueden seguir leyéndola. Lo que ya
        // NO se permite es que alguien la asigne (no hay setter → no
        // compila).
        var activoProp = typeof(ClienteDto).GetProperty("Activo");
        Assert.NotNull(activoProp);
        Assert.False(activoProp!.CanWrite,
            "Activo es derivado de DeletedAt: no puede tener setter, solo getter");

        Assert.NotNull(typeof(ClienteDto).GetProperty("FechaAlta"));
    }
}