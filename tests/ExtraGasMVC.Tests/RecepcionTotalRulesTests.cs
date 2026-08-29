using ExtraGasMVC.DTOs;
using ExtraGasMVC.Extensions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests del calculo del Total de una recepcion de proveedor.
/// Garantizan que <see cref="RecepcionTotalRules.Calcular"/> respeta la
/// invariante contable: Total = Subtotal - Descuento, sin negativos,
/// sin descuento mayor al subtotal.
/// </summary>
public class RecepcionTotalRulesTests
{
    [Fact]
    public void Calcular_ConSubtotal100YDescuento10_Devuelve90()
    {
        Assert.Equal(90m, RecepcionTotalRules.Calcular(subtotal: 100m, descuento: 10m));
    }

    [Fact]
    public void Calcular_ConDescuentoCero_DevuelveSubtotal()
    {
        Assert.Equal(100m, RecepcionTotalRules.Calcular(subtotal: 100m, descuento: 0m));
    }

    [Fact]
    public void Calcular_ConSubtotalCero_DevuelveCero()
    {
        Assert.Equal(0m, RecepcionTotalRules.Calcular(subtotal: 0m, descuento: 0m));
    }

    [Fact]
    public void Calcular_ConDescuentoIgualASubtotal_DevuelveCero()
    {
        Assert.Equal(0m, RecepcionTotalRules.Calcular(subtotal: 50m, descuento: 50m));
    }

    [Fact]
    public void Calcular_ConSubtotalNegativo_LanzaExcepcion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RecepcionTotalRules.Calcular(subtotal: -1m, descuento: 0m));
    }

    [Fact]
    public void Calcular_ConDescuentoNegativo_LanzaExcepcion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RecepcionTotalRules.Calcular(subtotal: 100m, descuento: -1m));
    }

    [Fact]
    public void Calcular_ConDescuentoMayorQueSubtotal_LanzaExcepcion()
    {
        // Caso tipico: operador tipea Subtotal=100 y Descuento=150 por error.
        // El calculo debe rechazar, no devolver un Total negativo.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RecepcionTotalRules.Calcular(subtotal: 100m, descuento: 150m));
    }
}

/// <summary>
/// Tests de regresion estructural del DTO. Garantizan que Total no vuelve
/// al DTO como input editable.
/// </summary>
public class CrearRecepcionDtoContratoTests
{
    [Fact]
    public void CrearRecepcionDto_NoExponeTotal_PorqueEsDerivado()
    {
        Assert.Null(typeof(CrearRecepcionDto).GetProperty("Total"));
    }

    [Fact]
    public void CrearRecepcionDto_SiExponeSubtotalYDescuento_PorqueSonInputsDeNegocio()
    {
        // Subtotal y Descuento siguen siendo inputs del operador: son
        // decisiones de negocio (cuanto compro, cuanto descuento aplico).
        // Solo Total es derivado y se calcula en el Service.
        Assert.NotNull(typeof(CrearRecepcionDto).GetProperty("Subtotal"));
        Assert.NotNull(typeof(CrearRecepcionDto).GetProperty("Descuento"));
    }
}
