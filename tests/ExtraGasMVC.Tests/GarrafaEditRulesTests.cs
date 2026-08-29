using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Extensions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de la regla "no editable" del módulo Garrafas.
/// Garantizan que <see cref="GarrafaEditRules.PreservarFlagsNoEditables"/>
/// impone la convención: <c>Activo</c> solo cambia vía Delete.
///
/// <para>Garrafa tiene DOS flags ortogonales: <c>Activo</c> (soft-delete) y
/// <c>estado_garrafa_id</c> (situación operacional). Este helper solo
/// preserva <c>Activo</c>; el estado operacional se gestiona por la acción
/// dedicada con validación contra la matriz de transiciones.</para>
/// </summary>
public class GarrafaEditRulesTests
{
    private static Garrafa NewEntity(bool activo, ulong estadoId = 1) => new()
    {
        Id = 1,
        Codigo = "GAR-001",
        CapacidadKg = 10,
        FechaCompra = new DateOnly(2024, 1, 15),
        EstadoGarrafaId = estadoId,
        Activo = activo,
    };

    [Fact]
    public void PreservarFlags_ConActivoOriginalTrue_YEntityFalse_LoRestauraATrue()
    {
        var entity = NewEntity(activo: true);
        entity.Activo = false;

        GarrafaEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.True(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_ConActivoOriginalFalse_YEntityTrue_LoRestauraAFalse()
    {
        var entity = NewEntity(activo: false);
        entity.Activo = true;

        GarrafaEditRules.PreservarFlagsNoEditables(entity, activoOriginal: false);

        Assert.False(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_ConActivoIgual_NoCambia()
    {
        var entity = NewEntity(activo: true);

        GarrafaEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.True(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_NoTocaEstadoOperacional_CambioPorAccionDedicada()
    {
        // El estado operacional (estado_garrafa_id) SÍ es editable vía la
        // acción CambiarEstado con validación contra la matriz. Edit no
        // debe tocar este flag directamente — pero el helper NO lo restaura.
        // Eso lo deja en manos de la acción dedicada, que es la autoridad.
        var entity = NewEntity(activo: true, estadoId: 1);
        entity.EstadoGarrafaId = 3; // operador cambió el estado

        GarrafaEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.Equal((ulong)3, entity.EstadoGarrafaId);
    }

    [Fact]
    public void PreservarFlags_NoTocaOtrosCampos()
    {
        var entity = NewEntity(activo: true);
        entity.Observaciones = "Observación nueva";
        entity.Activo = false;

        GarrafaEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.True(entity.Activo);
        Assert.Equal("Observación nueva", entity.Observaciones);
    }
}

/// <summary>
/// Tests de regresión estructurales del DTO.
/// </summary>
public class GarrafaDtoContratoTests
{
    [Fact]
    public void UpdateGarrafaDto_NoExponeActivo()
    {
        Assert.Null(typeof(UpdateGarrafaDto).GetProperty("Activo"));
    }

    [Fact]
    public void CreateGarrafaDto_NoExponeActivo()
    {
        Assert.Null(typeof(CreateGarrafaDto).GetProperty("Activo"));
    }

    [Fact]
    public void GarrafaDto_SiExponeActivo_ParaDisplay()
    {
        Assert.NotNull(typeof(GarrafaDto).GetProperty("Activo"));
    }

    [Fact]
    public void UpdateGarrafaDto_MantieneEstadoGarrafaId_PorqueEsEstadoOperacional()
    {
        // El estado operacional SÍ es editable desde Edit (cambio de
        // situación). Distinto de Activo, que es soft-delete.
        Assert.NotNull(typeof(UpdateGarrafaDto).GetProperty("EstadoGarrafaId"));
    }
}
