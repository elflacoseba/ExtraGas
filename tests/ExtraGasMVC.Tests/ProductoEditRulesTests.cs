using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Extensions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de la regla "no editable" del módulo Productos.
/// Garantizan que <see cref="ProductoEditRules.PreservarFlagsNoEditables"/>
/// impone la convención: <c>Activo</c> solo cambia vía Delete, evitando el
/// estado zombie <c>Activo=false</c> + <c>DeletedAt=null</c>.
///
/// <para>A diferencia de Cliente, <c>ManejaGarrafaIndividual</c> NO se
/// preserva — es config de negocio (editable).</para>
/// </summary>
public class ProductoEditRulesTests
{
    private static Producto NewEntity(bool activo, bool manejaGarrafa = false) => new()
    {
        Id = 1,
        Codigo = "GAS-10",
        Nombre = "Garrafa 10kg",
        TipoProductoId = 1,
        UnidadVenta = "UNIDAD",
        PrecioActual = 15000m,
        ManejaGarrafaIndividual = manejaGarrafa,
        Activo = activo,
    };

    [Fact]
    public void PreservarFlags_ConActivoOriginalTrue_YEntityFalse_LoRestauraATrue()
    {
        var entity = NewEntity(activo: true);
        entity.Activo = false; // valor que dejó el AutoMapper

        ProductoEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.True(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_ConActivoOriginalFalse_YEntityTrue_LoRestauraAFalse()
    {
        var entity = NewEntity(activo: false);
        entity.Activo = true;

        ProductoEditRules.PreservarFlagsNoEditables(entity, activoOriginal: false);

        Assert.False(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_ConActivoIgual_NoCambia()
    {
        var entity = NewEntity(activo: true);

        ProductoEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.True(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_NoTocaManejaGarrafaIndividual_ConfigDeNegocio()
    {
        // ManejaGarrafaIndividual es config de negocio: el operador puede
        // descubrir que un producto SÍ maneja garrafas y cambiarlo.
        var entity = NewEntity(activo: true, manejaGarrafa: false);
        entity.ManejaGarrafaIndividual = true; // operador corrigió

        ProductoEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.True(entity.ManejaGarrafaIndividual);
    }

    [Fact]
    public void PreservarFlags_NoTocaOtrosCampos()
    {
        var entity = NewEntity(activo: true);
        entity.Nombre = "Garrafa 15kg";
        entity.Activo = false;

        ProductoEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.True(entity.Activo);
        Assert.Equal("Garrafa 15kg", entity.Nombre);
    }
}

/// <summary>
/// Tests de regresión estructurales: garantizan que el contrato del DTO no
/// vuelve a exponer <c>Activo</c> como propiedad editable.
/// </summary>
public class ProductoDtoContratoTests
{
    [Fact]
    public void UpdateProductoDto_NoExponeActivo()
    {
        Assert.Null(typeof(UpdateProductoDto).GetProperty("Activo"));
    }

    [Fact]
    public void CreateProductoDto_NoExponeActivo()
    {
        Assert.Null(typeof(CreateProductoDto).GetProperty("Activo"));
    }

    [Fact]
    public void ProductoDto_SiExponeActivo_ParaDisplay()
    {
        Assert.NotNull(typeof(ProductoDto).GetProperty("Activo"));
    }

    [Fact]
    public void UpdateProductoDto_MantieneManejaGarrafaIndividual_PorqueEsConfigDeNegocio()
    {
        Assert.NotNull(typeof(UpdateProductoDto).GetProperty("ManejaGarrafaIndividual"));
    }

    [Fact]
    public void CreateProductoDto_MantieneManejaGarrafaIndividual_PorqueEsConfigDeNegocio()
    {
        Assert.NotNull(typeof(CreateProductoDto).GetProperty("ManejaGarrafaIndividual"));
    }
}
