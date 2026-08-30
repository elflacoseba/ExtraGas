using ExtraGasMVC.Extensions;
using FluentAssertions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests del normalizador de strings de identidad/contacto (DNI, teléfono).
/// Issue #113: garantiza que variantes por formato (espacios, puntos, guiones,
/// paréntesis, signo +) convergen al mismo valor canónico, y que entradas
/// vacías / solo-separadores devuelven <c>null</c>.
/// </summary>
public class StringNormalizerTests
{
    // --- NormalizarDni ---

    [Fact]
    public void NormalizarDni_Null_DevuelveNull()
    {
        StringNormalizer.NormalizarDni(null).Should().BeNull();
    }

    [Fact]
    public void NormalizarDni_Vacio_DevuelveNull()
    {
        StringNormalizer.NormalizarDni(string.Empty).Should().BeNull();
    }

    [Fact]
    public void NormalizarDni_Whitespace_DevuelveNull()
    {
        StringNormalizer.NormalizarDni("   ").Should().BeNull();
    }

    [Fact]
    public void NormalizarDni_SoloSeparadores_DevuelveNull()
    {
        StringNormalizer.NormalizarDni(" - . - ").Should().BeNull();
    }

    [Fact]
    public void NormalizarDni_SinSeparadores_DevuelveMismo()
    {
        StringNormalizer.NormalizarDni("12345678").Should().Be("12345678");
    }

    [Fact]
    public void NormalizarDni_EspaciosAlrededor_Trimea()
    {
        StringNormalizer.NormalizarDni(" 12345678 ").Should().Be("12345678");
    }

    [Fact]
    public void NormalizarDni_ConPuntos_LosRemueve()
    {
        StringNormalizer.NormalizarDni("12.345.678").Should().Be("12345678");
    }

    [Fact]
    public void NormalizarDni_ConGuiones_LosRemueve()
    {
        StringNormalizer.NormalizarDni("12-3456-78").Should().Be("12345678");
    }

    [Fact]
    public void NormalizarDni_FormatoArgentino_DevuelveSoloDigitos()
    {
        StringNormalizer.NormalizarDni(" 12.345.678 ").Should().Be("12345678");
    }

    [Fact]
    public void NormalizarDni_DniVacioTrasRemoverSeparadores_DevuelveNull()
    {
        StringNormalizer.NormalizarDni(" - . ").Should().BeNull();
    }

    // --- NormalizarTelefono ---

    [Fact]
    public void NormalizarTelefono_Null_DevuelveNull()
    {
        StringNormalizer.NormalizarTelefono(null).Should().BeNull();
    }

    [Fact]
    public void NormalizarTelefono_Vacio_DevuelveNull()
    {
        StringNormalizer.NormalizarTelefono(string.Empty).Should().BeNull();
    }

    [Fact]
    public void NormalizarTelefono_Whitespace_DevuelveNull()
    {
        StringNormalizer.NormalizarTelefono("   ").Should().BeNull();
    }

    [Fact]
    public void NormalizarTelefono_SoloSeparadores_DevuelveNull()
    {
        StringNormalizer.NormalizarTelefono(" - ( ) . ").Should().BeNull();
    }

    [Fact]
    public void NormalizarTelefono_SinSeparadores_DevuelveMismo()
    {
        StringNormalizer.NormalizarTelefono("1144556677").Should().Be("1144556677");
    }

    [Fact]
    public void NormalizarTelefono_EspaciosAlrededor_Trimea()
    {
        StringNormalizer.NormalizarTelefono(" 1144556677 ").Should().Be("1144556677");
    }

    [Fact]
    public void NormalizarTelefono_ConGuiones_LosRemueve()
    {
        StringNormalizer.NormalizarTelefono("11-4455-6677").Should().Be("1144556677");
    }

    [Fact]
    public void NormalizarTelefono_ConParentesis_LosRemueve()
    {
        StringNormalizer.NormalizarTelefono("(011) 4455-6677").Should().Be("01144556677");
    }

    [Fact]
    public void NormalizarTelefono_ConPuntos_LosRemueve()
    {
        StringNormalizer.NormalizarTelefono("11.4455.6677").Should().Be("1144556677");
    }

    [Fact]
    public void NormalizarTelefono_ConPrefijoInternacional_ConservaSignoMas()
    {
        StringNormalizer.NormalizarTelefono("+54 11 4455-6677").Should().Be("+541144556677");
    }

    [Fact]
    public void NormalizarTelefono_FormatoCompleto_DevuelveSoloMasYDigitos()
    {
        StringNormalizer.NormalizarTelefono(" +54 (011) 4455-6677 ").Should().Be("+5401144556677");
    }

    [Fact]
    public void NormalizarTelefono_SoloSignoMasSinDigitos_DevuelveNull()
    {
        // Issue #136 (S3358): con el refactor del ternario anidado a
        // if/else, esta rama cubre el path 'sb.Length <= longitudMinima'
        // cuando el operador tipea solo '+' (sin dígitos) — no es un
        // teléfono válido y debe devolver null, no string.Empty ni '+'.
        StringNormalizer.NormalizarTelefono("+").Should().BeNull();
    }

    [Fact]
    public void NormalizarTelefono_SoloSeparadoresConMasInicial_DevuelveNull()
    {
        // Variante: '+' seguido de puros separadores ('+ - . ( )').
        // Tras trim queda "+- . ( )" — el loop agrega '+' al sb pero
        // descarta el resto, así que sb.Length == 1 == longitudMinima → null.
        StringNormalizer.NormalizarTelefono("+ - . ( )").Should().BeNull();
    }
}