using System.ComponentModel.DataAnnotations;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Extensions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de DataAnnotations sobre los DTOs del módulo Proveedores.
/// Cubren las restricciones que el operador puede romper: CUIT con formato
/// inválido, razón social de más de 150 caracteres (B2), campos requeridos
/// vacíos, etc. La idea es fijar las reglas y detectar regresiones si alguien
/// cambia un atributo accidentalmente.
/// </summary>
public class ProveedorDtoValidationTests
{
    // Issue #161 (CA1859): cambiamos a `List<ValidationResult>` porque CA1859
    // marca el uso de IReadOnlyList cuando el método siempre materializa una
    // List internamente — el wrapper genérico agrega una capa sin beneficio de
    // performance. Los callers siguen pudiendo tratar el resultado como
    // IEnumerable<ValidationResult> sin cambios.
    private static List<ValidationResult> Validar(object dto)
    {
        var ctx = new ValidationContext(dto);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, ctx, results, validateAllProperties: true);
        return results;
    }

    private static CreateProveedorDto ProveedorValido() => new()
    {
        Codigo = "PROV-001",
        RazonSocial = "Shell Argentina S.A.",
        NombreFantasia = "Shell",
        Cuit = CuitValidator.Generar(prefijo: 30, dni: 12345678),
        TelefonoPrincipal = "1141234567",
        Email = "shell@example.com",
        Calle = "Av. Corrientes",
        Numero = "1234",
        Ciudad = "CABA",
        ProvinciaId = 1,
    };

    // ---------- CreateProveedorDto ----------

    [Fact]
    public void CreateProveedorDto_ConDatosValidos_NoTieneErrores()
    {
        var errors = Validar(ProveedorValido());

        Assert.Empty(errors);
    }

    [Fact]
    public void CreateProveedorDto_SinRazonSocial_TieneErrorDeRequired()
    {
        var dto = ProveedorValido();
        dto.RazonSocial = "";

        var errors = Validar(dto);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateProveedorDto.RazonSocial)));
    }

    [Fact]
    public void CreateProveedorDto_RazonSocialDeMasDe150Caracteres_TieneErrorDeLength()
    {
        var dto = ProveedorValido();
        dto.RazonSocial = new string('A', 151);

        var errors = Validar(dto);

        Assert.Contains(errors, e =>
            e.MemberNames.Contains(nameof(CreateProveedorDto.RazonSocial))
            && e.ErrorMessage!.Contains("150"));
    }

    [Fact]
    public void CreateProveedorDto_RazonSocialDe150Caracteres_NoTieneError()
    {
        // Borde superior: 150 chars es válido.
        var dto = ProveedorValido();
        dto.RazonSocial = new string('A', 150);

        var errors = Validar(dto);

        Assert.DoesNotContain(errors, e => e.MemberNames.Contains(nameof(CreateProveedorDto.RazonSocial)));
    }

    [Fact]
    public void CreateProveedorDto_SinCuit_TieneErrorDeRequired()
    {
        var dto = ProveedorValido();
        dto.Cuit = "";

        var errors = Validar(dto);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateProveedorDto.Cuit)));
    }

    [Fact]
    public void CreateProveedorDto_CuitConFormatoInvalido_TieneErrorDeFormato()
    {
        var dto = ProveedorValido();
        dto.Cuit = "20-12345678-9"; // con guiones

        var errors = Validar(dto);

        Assert.Contains(errors, e =>
            e.MemberNames.Contains(nameof(CreateProveedorDto.Cuit))
            && e.ErrorMessage!.Contains("11 dígitos"));
    }

    [Fact]
    public void CreateProveedorDto_CuitConDvMal_TieneErrorDeDv()
    {
        var dto = ProveedorValido();
        var cuitValido = CuitValidator.Generar(prefijo: 20, dni: 12345678);
        var dvOriginal = cuitValido[10] - '0';
        var dvAlterado = (dvOriginal + 1) % 10;
        dto.Cuit = cuitValido.Substring(0, 10) + dvAlterado;

        var errors = Validar(dto);

        Assert.Contains(errors, e =>
            e.MemberNames.Contains(nameof(CreateProveedorDto.Cuit))
            && e.ErrorMessage!.Contains("dígito verificador"));
    }

    [Fact]
    public void CreateProveedorDto_CuitValido_NoTieneErrorDeCuit()
    {
        var dto = ProveedorValido();
        dto.Cuit = CuitValidator.Generar(prefijo: 33, dni: 76543210);

        var errors = Validar(dto);

        Assert.DoesNotContain(errors, e => e.MemberNames.Contains(nameof(CreateProveedorDto.Cuit)));
    }

    [Fact]
    public void CreateProveedorDto_EmailInvalido_TieneErrorDeFormato()
    {
        var dto = ProveedorValido();
        dto.Email = "no-es-un-email";

        var errors = Validar(dto);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateProveedorDto.Email)));
    }

    // ---------- UpdateProveedorDto (mismo contrato de validación) ----------

    [Fact]
    public void UpdateProveedorDto_ConDatosValidos_NoTieneErrores()
    {
        var dto = new UpdateProveedorDto
        {
            Id = 1,
            RazonSocial = "YPF S.A.",
            Cuit = CuitValidator.Generar(prefijo: 30, dni: 87654321),
        };

        var errors = Validar(dto);

        Assert.Empty(errors);
    }

    [Fact]
    public void UpdateProveedorDto_RazonSocialDeMasDe150Caracteres_TieneErrorDeLength()
    {
        // Regresión B2: si alguien vuelve a poner [StringLength(200)], este test rompe.
        var dto = new UpdateProveedorDto
        {
            Id = 1,
            RazonSocial = new string('B', 200),
            Cuit = CuitValidator.Generar(prefijo: 30, dni: 11111111),
        };

        var errors = Validar(dto);

        Assert.Contains(errors, e =>
            e.MemberNames.Contains(nameof(UpdateProveedorDto.RazonSocial))
            && e.ErrorMessage!.Contains("150"));
    }

    [Fact]
    public void UpdateProveedorDto_CuitConDvMal_TieneErrorDeDv()
    {
        var cuitValido = CuitValidator.Generar(prefijo: 20, dni: 12345678);
        var dvOriginal = cuitValido[10] - '0';
        var dvAlterado = (dvOriginal + 1) % 10;
        var cuitAlterado = cuitValido.Substring(0, 10) + dvAlterado;

        var dto = new UpdateProveedorDto
        {
            Id = 1,
            RazonSocial = "Proveedor Test",
            Cuit = cuitAlterado,
        };

        var errors = Validar(dto);

        Assert.Contains(errors, e =>
            e.MemberNames.Contains(nameof(UpdateProveedorDto.Cuit))
            && e.ErrorMessage!.Contains("dígito verificador"));
    }
}
