using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Extensions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de la regla "no editable" del módulo Clientes.
/// Garantizan que <see cref="ClienteEditRules.PreservarFlagsNoEditables"/>
/// impone la convención: <c>Activo</c> solo cambia vía Delete/Restore
/// (evita el estado zombie <c>Activo=false</c> + <c>DeletedAt=null</c>) y
/// <c>FechaAlta</c> es audit trail inmutable del alta.
/// </summary>
public class ClienteEditRulesTests
{
    private static Cliente NewEntity(bool activo, DateOnly fechaAlta) => new()
    {
        Id = 1,
        Nombre = "Juan",
        Apellido = "Pérez",
        TelefonoPrincipal = "1144556677",
        Activo = activo,
        FechaAlta = fechaAlta,
    };

    [Fact]
    public void PreservarFlags_ConActivoOriginalTrue_YEntityFalse_LoRestauraATrue()
    {
        // Escena: operador edita un cliente activo y destilda la casilla Activo.
        // El AutoMapper aplicó el DTO → entity.Activo = false. La regla dura
        // tiene que restaurar el valor de BD.
        var entity = NewEntity(activo: true, fechaAlta: new DateOnly(2024, 1, 15));
        entity.Activo = false; // valor que dejó el AutoMapper

        ClienteEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true, fechaAltaOriginal: new DateOnly(2024, 1, 15));

        Assert.True(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_ConActivoOriginalFalse_YEntityTrue_LoRestauraAFalse()
    {
        // Caso inverso: editar un cliente inactivo y "re-activarlo" desde el
        // form no debe funcionar. Solo Restore puede hacerlo.
        var entity = NewEntity(activo: false, fechaAlta: new DateOnly(2024, 1, 15));
        entity.Activo = true; // valor que dejó el AutoMapper

        ClienteEditRules.PreservarFlagsNoEditables(entity, activoOriginal: false, fechaAltaOriginal: new DateOnly(2024, 1, 15));

        Assert.False(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_ConActivoIgual_NoCambia()
    {
        // El caso "feliz": operador edita y deja Activo como estaba.
        var entity = NewEntity(activo: true, fechaAlta: new DateOnly(2024, 1, 15));

        ClienteEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true, fechaAltaOriginal: new DateOnly(2024, 1, 15));

        Assert.True(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_ConFechaAltaDistinta_LaRestaura()
    {
        // Escena: operador edita y retrocede la fecha de alta. La regla
        // tiene que restaurar el valor de BD.
        var fechaAltaOriginal = new DateOnly(2024, 1, 15);
        var entity = NewEntity(activo: true, fechaAlta: fechaAltaOriginal);
        entity.FechaAlta = new DateOnly(2020, 6, 1); // valor que dejó el AutoMapper

        ClienteEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true, fechaAltaOriginal: fechaAltaOriginal);

        Assert.Equal(fechaAltaOriginal, entity.FechaAlta);
    }

    [Fact]
    public void PreservarFlags_ConFechaAltaIgual_NoCambia()
    {
        var fechaAlta = new DateOnly(2024, 1, 15);
        var entity = NewEntity(activo: true, fechaAlta: fechaAlta);

        ClienteEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true, fechaAltaOriginal: fechaAlta);

        Assert.Equal(fechaAlta, entity.FechaAlta);
    }

    [Fact]
    public void PreservarFlags_NoTocaOtrosCampos()
    {
        // La regla solo afecta Activo y FechaAlta. Verifica que no toca
        // campos que el form sí puede editar (ej. Nombre).
        var fechaAltaOriginal = new DateOnly(2024, 1, 15);
        var entity = NewEntity(activo: true, fechaAlta: fechaAltaOriginal);
        entity.Nombre = "Nuevo nombre";
        entity.Activo = false;
        entity.FechaAlta = new DateOnly(1999, 12, 31);

        ClienteEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true, fechaAltaOriginal: fechaAltaOriginal);

        Assert.True(entity.Activo);
        Assert.Equal(fechaAltaOriginal, entity.FechaAlta);
        Assert.Equal("Nuevo nombre", entity.Nombre); // quedó como el form lo mandó
    }
}

/// <summary>
/// Tests de regresión estructurales: garantizan que el contrato del DTO no
/// vuelve a exponer <c>Activo</c> ni <c>FechaAlta</c> como propiedades
/// editables. Si alguien vuelve a ponerlas en el base o en Update, estos
/// tests fallan en compilación.
/// </summary>
public class ClienteDtoContratoTests
{
    [Fact]
    public void UpdateClienteDto_NoExponeActivo()
    {
        // Si la propiedad vuelve al DTO, este test rompe en compilación.
        // Es la red de seguridad del refactor: el contrato es inmutable.
        Assert.Null(typeof(UpdateClienteDto).GetProperty("Activo"));
    }

    [Fact]
    public void UpdateClienteDto_NoExponeFechaAlta()
    {
        Assert.Null(typeof(UpdateClienteDto).GetProperty("FechaAlta"));
    }

    [Fact]
    public void CreateClienteDto_NoExponeActivo()
    {
        Assert.Null(typeof(CreateClienteDto).GetProperty("Activo"));
    }

    [Fact]
    public void CreateClienteDto_NoExponeFechaAlta()
    {
        Assert.Null(typeof(CreateClienteDto).GetProperty("FechaAlta"));
    }

    [Fact]
    public void ClienteDto_SiExponeActivoYFechaAlta_ParaDisplay()
    {
        // ClienteDto sigue exponiéndolos porque los necesitan Details, Index
        // y listados. Si los sacás, esos views rompen.
        Assert.NotNull(typeof(ClienteDto).GetProperty("Activo"));
        Assert.NotNull(typeof(ClienteDto).GetProperty("FechaAlta"));
    }
}
