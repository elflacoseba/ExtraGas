using ExtraGasMVC.Models.ViewModels;
using FluentAssertions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests del record <see cref="PedidoSearchFilter"/>. Introducido en el
/// PR #137 para reducir el param count de
/// <c>IPedidoService.SearchAsync</c> (SonarQube csharpsquid:S107).
/// El record es un data carrier puro, sin lógica, pero cubrimos el
/// constructor y el equality-by-value que el record provee.
/// </summary>
public class PedidoSearchFilterTests
{
    [Fact]
    public void Constructor_AsignaTodosLosCampos()
    {
        var fecha = new DateTime(2026, 8, 30);
        var filter = new PedidoSearchFilter(
            Numero: "PED-2026-00001",
            EstadoId: 3,
            ClienteId: 42,
            Desde: fecha,
            Hasta: fecha.AddDays(7),
            Pagina: 2,
            Tamanio: 50);

        filter.Numero.Should().Be("PED-2026-00001");
        filter.EstadoId.Should().Be(3);
        filter.ClienteId.Should().Be(42);
        filter.Desde.Should().Be(fecha);
        filter.Hasta.Should().Be(fecha.AddDays(7));
        filter.Pagina.Should().Be(2);
        filter.Tamanio.Should().Be(50);
    }

    [Fact]
    public void Constructor_AceptaValoresNulosParaFiltrosOpcionales()
    {
        // El Controller de Pedidos pasa ClienteId = null porque ese filtro
        // vive en CuentasCorrientes, no en Index. Igual EstadoId y fechas
        // son opcionales.
        var filter = new PedidoSearchFilter(
            Numero: null,
            EstadoId: null,
            ClienteId: null,
            Desde: null,
            Hasta: null,
            Pagina: 1,
            Tamanio: 25);

        filter.Numero.Should().BeNull();
        filter.EstadoId.Should().BeNull();
        filter.ClienteId.Should().BeNull();
        filter.Desde.Should().BeNull();
        filter.Hasta.Should().BeNull();
    }

    [Fact]
    public void Equality_RecordsConMismosValores_SonIguales()
    {
        // Records proveen equality por valor. Verificamos que la semántica
        // de record sigue vigente aunque solo expongamos el ctor primario.
        var a = new PedidoSearchFilter("PED-X", 1, 2, null, null, 1, 25);
        var b = new PedidoSearchFilter("PED-X", 1, 2, null, null, 1, 25);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_RecordsConValoresDistintos_NoSonIguales()
    {
        var a = new PedidoSearchFilter("PED-X", 1, 2, null, null, 1, 25);
        var b = new PedidoSearchFilter("PED-Y", 1, 2, null, null, 1, 25);

        a.Should().NotBe(b);
    }
}
