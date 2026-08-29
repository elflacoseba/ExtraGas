using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Extensions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de la regla "no editable" del módulo Empleados.
/// Garantizan que <see cref="EmpleadoEditRules.PreservarFlagsNoEditables"/>
/// impone la convención: <c>Activo</c> solo cambia vía Delete, evitando el
/// estado zombie <c>Activo=false</c> + <c>DeletedAt=null</c>.
///
/// <para>A diferencia de Cliente, <c>FechaIngreso</c> NO se preserva — es
/// dato de negocio del empleado (editable).</para>
/// </summary>
public class EmpleadoEditRulesTests
{
    private static Empleado NewEntity(bool activo) => new()
    {
        Id = 1,
        Nombre = "Juan",
        Apellido = "Pérez",
        Activo = activo,
    };

    [Fact]
    public void PreservarFlags_ConActivoOriginalTrue_YEntityFalse_LoRestauraATrue()
    {
        var entity = NewEntity(activo: true);
        entity.Activo = false; // valor que dejó el AutoMapper

        EmpleadoEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.True(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_ConActivoOriginalFalse_YEntityTrue_LoRestauraAFalse()
    {
        var entity = NewEntity(activo: false);
        entity.Activo = true; // valor que dejó el AutoMapper

        EmpleadoEditRules.PreservarFlagsNoEditables(entity, activoOriginal: false);

        Assert.False(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_ConActivoIgual_NoCambia()
    {
        var entity = NewEntity(activo: true);

        EmpleadoEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.True(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_NoTocaFechaIngreso_DatoDeNegocio()
    {
        // A diferencia de Cliente, FechaIngreso es dato del empleado y debe
        // poder ser editado libremente desde el form.
        var entity = NewEntity(activo: true);
        entity.FechaIngreso = new DateOnly(2024, 6, 1);
        entity.FechaIngreso = new DateOnly(2025, 1, 15); // operador corrigió la fecha

        EmpleadoEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.Equal(new DateOnly(2025, 1, 15), entity.FechaIngreso);
    }

    [Fact]
    public void PreservarFlags_NoTocaOtrosCampos()
    {
        // La regla solo afecta Activo. Verifica que no toca campos que el
        // form sí puede editar (ej. Nombre).
        var entity = NewEntity(activo: true);
        entity.Nombre = "Nuevo nombre";
        entity.Activo = false;

        EmpleadoEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.True(entity.Activo);
        Assert.Equal("Nuevo nombre", entity.Nombre); // quedó como el form lo mandó
    }
}

/// <summary>
/// Tests de regresión estructurales: garantizan que el contrato del DTO no
/// vuelve a exponer <c>Activo</c> como propiedad editable. Si alguien la
/// vuelve a poner, estos tests fallan en compilación.
/// </summary>
public class EmpleadoDtoContratoTests
{
    [Fact]
    public void UpdateEmpleadoDto_NoExponeActivo()
    {
        Assert.Null(typeof(UpdateEmpleadoDto).GetProperty("Activo"));
    }

    [Fact]
    public void CreateEmpleadoDto_NoExponeActivo()
    {
        Assert.Null(typeof(CreateEmpleadoDto).GetProperty("Activo"));
    }

    [Fact]
    public void EmpleadoDto_SiExponeActivo_ParaDisplay()
    {
        // EmpleadoDto sigue exponiéndolo porque Details/Index lo necesitan.
        Assert.NotNull(typeof(EmpleadoDto).GetProperty("Activo"));
    }

    [Fact]
    public void EmpleadoDtoBase_MantieneFechaIngreso_PorqueEsDatoDeNegocio()
    {
        // A diferencia de Cliente (donde FechaAlta es audit trail y salió del
        // base), FechaIngreso sigue siendo editable: el operador lo carga al
        // alta y puede corregirlo. Por eso queda en EmpleadoDtoBase.
        Assert.NotNull(typeof(EmpleadoDtoBase).GetProperty("FechaIngreso"));
    }
}
