using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Extensions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de la regla "no editable" del módulo Usuarios.
/// Garantizan que <see cref="UsuarioEditRules.PreservarFlagsNoEditables"/>
/// impone la convención: <c>Activo</c> solo cambia vía Delete.
///
/// <para>La regla "no puede desactivarse a sí mismo" del Controller de Edit
/// quedó obsoleta con este fix (el DTO ya no expone Activo); la protección
/// real pasó a Delete, que ya comparaba id == currentUserId.</para>
/// </summary>
public class UsuarioEditRulesTests
{
    private static Usuario NewEntity(bool activo) => new()
    {
        Id = 1,
        Username = "jperez",
        PasswordHash = "hash",
        RolId = 1,
        Activo = activo,
    };

    [Fact]
    public void PreservarFlags_ConActivoOriginalTrue_YEntityFalse_LoRestauraATrue()
    {
        var entity = NewEntity(activo: true);
        entity.Activo = false;

        UsuarioEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.True(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_ConActivoOriginalFalse_YEntityTrue_LoRestauraAFalse()
    {
        var entity = NewEntity(activo: false);
        entity.Activo = true;

        UsuarioEditRules.PreservarFlagsNoEditables(entity, activoOriginal: false);

        Assert.False(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_ConActivoIgual_NoCambia()
    {
        var entity = NewEntity(activo: true);

        UsuarioEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.True(entity.Activo);
    }

    [Fact]
    public void PreservarFlags_NoTocaPasswordHash()
    {
        // Defense-in-depth: si alguien agrega Password al DTO por error,
        // el helper no debe pisar el hash persistido.
        var entity = NewEntity(activo: true);
        entity.PasswordHash = "hash-original";

        UsuarioEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.Equal("hash-original", entity.PasswordHash);
    }

    [Fact]
    public void PreservarFlags_NoTocaEmail_CampoEditable()
    {
        // Email sí es editable (el operador puede corregirlo).
        var entity = NewEntity(activo: true);
        entity.Email = "nuevo@email.com";

        UsuarioEditRules.PreservarFlagsNoEditables(entity, activoOriginal: true);

        Assert.Equal("nuevo@email.com", entity.Email);
    }
}

/// <summary>
/// Tests de regresión estructurales: garantizan que el contrato del DTO no
/// vuelve a exponer <c>Activo</c> como propiedad editable.
/// </summary>
public class UsuarioDtoContratoTests
{
    [Fact]
    public void UpdateUsuarioDto_NoExponeActivo()
    {
        Assert.Null(typeof(UpdateUsuarioDto).GetProperty("Activo"));
    }

    [Fact]
    public void CreateUsuarioDto_NoExponeActivo()
    {
        Assert.Null(typeof(CreateUsuarioDto).GetProperty("Activo"));
    }

    [Fact]
    public void UsuarioDto_SiExponeActivo_ParaDisplay()
    {
        Assert.NotNull(typeof(UsuarioDto).GetProperty("Activo"));
    }

    [Fact]
    public void UpdateUsuarioDto_MantieneEmailYRolId_PorqueSonEditables()
    {
        Assert.NotNull(typeof(UpdateUsuarioDto).GetProperty("Email"));
        Assert.NotNull(typeof(UpdateUsuarioDto).GetProperty("RolId"));
    }

    [Fact]
    public void UpdateUsuarioDto_NoExponeUsername_PorqueEsIdentidad()
    {
        // Username es identidad del usuario (no se cambia). Si alguien lo
        // agrega al DTO de Update, este test rompe en compilación.
        Assert.Null(typeof(UpdateUsuarioDto).GetProperty("Username"));
    }
}
