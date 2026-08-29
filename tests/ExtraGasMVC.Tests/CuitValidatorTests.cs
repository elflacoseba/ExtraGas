using System.ComponentModel.DataAnnotations;
using ExtraGasMVC.Extensions;
using Xunit;

namespace ExtraGasMVC.Tests;

public class CuitValidatorTests
{
    // ---------- CalcularDigitoVerificador ----------

    [Fact]
    public void CalcularDigitoVerificador_CuitCorto_Throws()
    {
        Assert.Throws<ArgumentException>(() => CuitValidator.CalcularDigitoVerificador("123456789"));
    }

    [Fact]
    public void CalcularDigitoVerificador_CuitLargo_Throws()
    {
        Assert.Throws<ArgumentException>(() => CuitValidator.CalcularDigitoVerificador("123456789012"));
    }

    [Fact]
    public void CalcularDigitoVerificador_ConLetras_Throws()
    {
        Assert.Throws<ArgumentException>(() => CuitValidator.CalcularDigitoVerificador("12345678a0"));
    }

    [Fact]
    public void CalcularDigitoVerificador_ConCeros_RetornaCero()
    {
        // prefijo 00 y DNI 00000000 → suma = 0 → resto = 0 → DV = 0
        int dv = CuitValidator.CalcularDigitoVerificador("0000000000");
        Assert.Equal(0, dv);
    }

    // ---------- EsValido ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EsValido_NullOrWhitespace_False(string? input)
    {
        Assert.False(CuitValidator.EsValido(input));
    }

    [Theory]
    [InlineData("1234567890")]   // 10 dígitos
    [InlineData("123456789012")] // 12 dígitos
    [InlineData("20-12345678-9")] // guiones
    [InlineData("20 12345678 9")] // espacios
    public void EsValido_FormatoInvalido_False(string input)
    {
        Assert.False(CuitValidator.EsValido(input));
    }

    [Theory]
    [InlineData("12345678abc")]
    [InlineData("20.123.456-9")]
    [InlineData("abcdefghijk")]
    public void EsValido_ConCaracteresNoNumericos_False(string input)
    {
        Assert.False(CuitValidator.EsValido(input));
    }

    [Fact]
    public void EsValido_CuitGeneradoConDvCorrecto_True()
    {
        var cuit = CuitValidator.Generar(prefijo: 20, dni: 12345678);
        Assert.True(CuitValidator.EsValido(cuit));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(23)]
    [InlineData(27)]
    [InlineData(30)]
    [InlineData(33)]
    [InlineData(34)]
    public void EsValido_VariosPrefijos_True(int prefijo)
    {
        var cuit = CuitValidator.Generar(prefijo, dni: 12345678);
        Assert.True(CuitValidator.EsValido(cuit));
    }

    [Fact]
    public void EsValido_DvIncorrecto_False()
    {
        var valido = CuitValidator.Generar(prefijo: 20, dni: 12345678);
        var dvOriginal = valido[10] - '0';
        var dvAlterado = (dvOriginal + 1) % 10;
        var cuitAlterado = valido.Substring(0, 10) + dvAlterado;

        Assert.False(CuitValidator.EsValido(cuitAlterado));
    }

    // ---------- Generar ----------

    [Fact]
    public void Generar_PrefijoInvalido_Throws()
    {
        Assert.Throws<ArgumentException>(() => CuitValidator.Generar(prefijo: 200, dni: 12345678));
    }

    [Fact]
    public void Generar_ResultadoTiene11Digitos()
    {
        var cuit = CuitValidator.Generar(prefijo: 20, dni: 12345678);
        Assert.Equal(11, cuit.Length);
        Assert.True(cuit.All(char.IsDigit));
    }

    [Fact]
    public void Generar_ResultadoEsValido()
    {
        var cuit = CuitValidator.Generar(prefijo: 30, dni: 76543210);
        Assert.True(CuitValidator.EsValido(cuit));
    }

    [Fact]
    public void Generar_DnisDiferentes_ResultadosDiferentes()
    {
        var a = CuitValidator.Generar(prefijo: 20, dni: 10000000);
        var b = CuitValidator.Generar(prefijo: 20, dni: 10000001);
        Assert.NotEqual(a, b);
    }

    // ---------- CuitAttribute (integración con DataAnnotations) ----------

    private sealed class Holder
    {
        [Cuit]
        public string? Cuit { get; set; }
    }

    [Fact]
    public void Atributo_Null_NoAgregaError()
    {
        var holder = new Holder { Cuit = null };
        var ctx = new ValidationContext(holder);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(holder, ctx, results, validateAllProperties: true);
        // [Cuit] no se queja de null (lo cubre [Required]); no hay [Required] acá.
        Assert.Empty(results);
    }

    [Fact]
    public void Atributo_Vacio_NoAgregaError_RequiredEsElResponsable()
    {
        // El atributo Cuit solo valida CUITs presentes. El "vacio" lo cubre Required.
        var attr = new CuitAttribute();
        Assert.True(attr.IsValid(string.Empty));
        Assert.True(attr.IsValid("   "));
    }

    [Fact]
    public void Atributo_FormatoInvalido_MensajeEspecificoDeFormato()
    {
        var attr = new CuitAttribute();
        Assert.False(attr.IsValid("12345"));
        Assert.Equal("El CUIT debe contener 11 dígitos numéricos.", attr.ErrorMessage);
    }

    [Fact]
    public void Atributo_ConLetras_MensajeDeFormato()
    {
        var attr = new CuitAttribute();
        Assert.False(attr.IsValid("20a12345678"));
        Assert.Equal("El CUIT debe contener 11 dígitos numéricos.", attr.ErrorMessage);
    }

    [Fact]
    public void Atributo_DvIncorrecto_MensajeDeDv()
    {
        var attr = new CuitAttribute();
        var valido = CuitValidator.Generar(prefijo: 20, dni: 12345678);
        var dvOriginal = valido[10] - '0';
        var dvAlterado = (dvOriginal + 1) % 10;
        var cuitAlterado = valido.Substring(0, 10) + dvAlterado;

        Assert.False(attr.IsValid(cuitAlterado));
        Assert.Equal("El CUIT es inválido. Verifique el dígito verificador.", attr.ErrorMessage);
    }

    [Fact]
    public void Atributo_CuitValido_SinError()
    {
        var attr = new CuitAttribute();
        var cuit = CuitValidator.Generar(prefijo: 20, dni: 12345678);
        Assert.True(attr.IsValid(cuit));
    }

    [Fact]
    public void Atributo_NoString_False()
    {
        var attr = new CuitAttribute();
        Assert.False(attr.IsValid(123));
    }
}
