using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Extensions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de la regla "doble flag acoplado" del módulo Proveedores.
/// Garantizan que <see cref="ProveedorEditRules.PreservarFlagsNoEditables"/>
/// impone la convención: <c>Activo</c> solo cambia vía Delete, no vía Edit,
/// evitando la desincronización entre los flags Activo y DeletedAt.
/// </summary>
public class ProveedorEditRulesTests
{
    [Fact]
    public void PreservarFlags_ConActivoOriginalTrue_YEntityFalse_LoRestauraATrue()
    {
        // Escena: operador edita un proveedor activo y destilda la casilla Activo.
        // El AutoMapper aplicó el DTO → entity.Activo = false. La regla dura
        // tiene que restaurar el valor de BD.
        var entity = new Proveedor
        {
            Id = 1,
            RazonSocial = "Shell",
            Cuit = "30123456780",
            Activo = false, // valor que dejó el AutoMapper
        };

        ProveedorEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.True(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_ConActivoOriginalFalse_YEntityTrue_LoRestauraAFalse()
    {
        // Caso inverso: editar un proveedor inactivo y "re-activarlo" desde
        // el form no debe funcionar. Solo Delete puede hacerlo.
        var entity = new Proveedor
        {
            Id = 1,
            RazonSocial = "Shell",
            Cuit = "30123456780",
            Activo = true,
        };

        ProveedorEditRules.PreservarFlagsNoEditables(entity, activoOriginal: false);

        Assert.False(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_ConMismoValor_NoCambia()
    {
        // El caso "feliz": operador edita y deja Activo como estaba.
        var entity = new Proveedor
        {
            Id = 1,
            RazonSocial = "Shell",
            Cuit = "30123456780",
            Activo = true,
        };

        ProveedorEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.True(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_NoTocaOtrosCampos()
    {
        // La regla solo afecta Activo. Verifica que no se carga campos que
        // el form sí tiene permitido editar (ej. RazonSocial).
        var entity = new Proveedor
        {
            Id = 1,
            RazonSocial = "Nuevo nombre",
            Cuit = "30123456780",
            Activo = true,
        };

        ProveedorEditRules.PreservarFlagsNoEditables(entity, activoOriginal: false);

        Assert.False(entity.Activo);
        Assert.Equal("Nuevo nombre", entity.RazonSocial); // quedó como el form lo mandó
    }
}
