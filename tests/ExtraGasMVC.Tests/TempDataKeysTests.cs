using ExtraGasMVC.Constants;
using FluentAssertions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests del contrato de las constantes de <see cref="TempDataKeys"/>.
/// Issue #136: las constantes se introdujeron para reducir magic strings
/// (SonarQube csharpsquid:S1192) en los Controllers. Aquí garantizamos
/// que los valores no cambien accidentalmente (porque los consumen las
/// vistas Razor como keys literales del TempData — un rename silencioso
/// rompería el binding de las alertas de UI).
/// </summary>
public class TempDataKeysTests
{
    [Fact]
    public void Success_EsLaMismaKeyQueEsperanLasVistas()
    {
        // Las vistas Razor comparan contra "Success" (string) en
        // _StatusMessage.cshtml. Renombrar la constante rompe el binding
        // de UI silenciosamente.
        TempDataKeys.Success.Should().Be("Success");
    }

    [Fact]
    public void Error_EsLaMismaKeyQueEsperanLasVistas()
    {
        TempDataKeys.Error.Should().Be("Error");
    }

    [Fact]
    public void Info_EsLaMismaKeyQueEsperanLasVistas()
    {
        TempDataKeys.Info.Should().Be("Info");
    }

    [Fact]
    public void TemporaryPassword_EsLaMismaKeyQueConsumeElEditDeUsuario()
    {
        // El controller hace Peek/Remove sobre esta key en Edit(GET) para
        // mostrar la password temporal una sola vez al admin.
        TempDataKeys.TemporaryPassword.Should().Be("TemporaryPassword");
    }

    [Fact]
    public void TemporaryPasswordUsername_EsLaMismaKeyQueConsumeElEditDeUsuario()
    {
        TempDataKeys.TemporaryPasswordUsername.Should().Be("TemporaryPasswordUsername");
    }

    [Fact]
    public void PedidoNotFoundMessage_TieneElMensajeEnCastellano()
    {
        // Mensaje user-facing: debe coincidir exactamente con lo que las
        // vistas esperan ver cuando un pedido no existe.
        TempDataKeys.PedidoNotFoundMessage.Should().Be("No se encontró el pedido.");
    }

    [Fact]
    public void TodasLasKeys_DistintasEntreSi()
    {
        // Guard: si dos keys colapsan a la misma string, los TempData
        // se pisan entre sí y un banner de éxito podría ocultar un error
        // (o viceversa).
        var values = new[]
        {
            TempDataKeys.Success,
            TempDataKeys.Error,
            TempDataKeys.Info,
            TempDataKeys.TemporaryPassword,
            TempDataKeys.TemporaryPasswordUsername,
            TempDataKeys.PedidoNotFoundMessage,
        };
        values.Should().OnlyHaveUniqueItems();
    }
}
