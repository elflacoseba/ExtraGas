using AutoMapper;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using FluentAssertions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Contract tests para <see cref="MappingProfile.ConfigurePedidoEstadoHistorico"/>
/// (issue #165). Los 7 helpers estáticos que extrajimos para bajar la
/// complejidad cognitiva (<see cref="MappingProfile.EstadoAnteriorCodigo"/>,
/// <see cref="MappingProfile.EstadoAnteriorNombre"/>,
/// <see cref="MappingProfile.EstadoAnteriorColor"/>,
/// <see cref="MappingProfile.EstadoNuevoCodigo"/>,
/// <see cref="MappingProfile.EstadoNuevoNombre"/>,
/// <see cref="MappingProfile.EstadoNuevoColor"/> y
/// <see cref="MappingProfile.UsuarioNombre"/>) son <c>private static</c>: solo
/// se ejercitan vía AutoMapper. Estos tests cubren el contrato observable
/// (los campos display del DTO se resuelven correctamente con lookups
/// poblados, nulos o mixtos).
///
/// Cobertura: este test aporta líneas nuevas para <c>new_coverage</c> en
/// SonarQube — sin él, los 7 helpers quedan en 0% porque ninguna otra ruta
/// los invoca (las entradas de historial las inserta <c>PedidoService</c>
/// por EF, no vía el mapeo de display).
/// </summary>
public class MappingProfilePedidoEstadoHistoricoTests
{
    private static IMapper NewMapper()
    {
        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        return config.CreateMapper();
    }

    /// <summary>
    /// Caso del primer estado de un pedido (estado anterior = null) y sin
    /// usuario que lo creó (registros legacy sin usuario). Cubre el path
    /// `null` de los 3 helpers de EstadoAnterior + Usuario, y el path de
    /// string.Empty de EstadoNuevo cuando el lookup está poblado.
    /// </summary>
    [Fact]
    public void Map_LookupsNulosOLegacy_DevuelveDtosConNullsYStringEmpty()
    {
        var mapper = NewMapper();

        var entity = new PedidoEstadoHistorico
        {
            Id = 1,
            PedidoId = 100,
            EstadoAnteriorId = null,        // primer estado del pedido
            EstadoNuevoId = 2,
            MotivoCancelacion = null,
            UsuarioId = null,                // sin usuario que disparó la transición
            CreatedAt = DateTime.UtcNow,
            EstadoAnterior = null,           // navigation property sin cargar
            EstadoNuevo = new EstadoPedido
            {
                Id = 2,
                Codigo = "CONFIRMADO",
                Nombre = "Confirmado",
                Color = "#0d6efd",
            },
            Usuario = null,
        };

        var dto = mapper.Map<PedidoEstadoHistoricoDto>(entity);

        dto.Id.Should().Be(1);
        dto.PedidoId.Should().Be(100);
        dto.EstadoAnteriorId.Should().BeNull();
        dto.EstadoNuevoId.Should().Be(2);

        // EstadoAnterior: todo el grupo deriva del null del navigation.
        dto.EstadoAnteriorCodigo.Should().BeNull();
        dto.EstadoAnteriorNombre.Should().BeNull();
        dto.EstadoAnteriorColor.Should().BeNull();

        // EstadoNuevo: el lookup sí está cargado, los strings viajan con valor.
        dto.EstadoNuevoCodigo.Should().Be("CONFIRMADO");
        dto.EstadoNuevoNombre.Should().Be("Confirmado");
        dto.EstadoNuevoColor.Should().Be("#0d6efd");

        // Usuario: navigation null → string null.
        dto.UsuarioNombre.Should().BeNull();
    }

    /// <summary>
    /// Caso normal: ambos estados y el usuario están cargados. Cubre el
    /// path no-null de los 7 helpers. Si este test pasa, sabemos que
    /// los helpers saben proyectar 1:1 cuando el lookup existe.
    /// </summary>
    [Fact]
    public void Map_LookupsPoblados_ProyectaCodigoNombreColorYUsername()
    {
        var mapper = NewMapper();

        var entity = new PedidoEstadoHistorico
        {
            Id = 7,
            PedidoId = 100,
            EstadoAnteriorId = 1,
            EstadoNuevoId = 2,
            MotivoCancelacion = null,
            UsuarioId = 42,
            CreatedAt = DateTime.UtcNow,
            EstadoAnterior = new EstadoPedido
            {
                Id = 1,
                Codigo = "PENDIENTE",
                Nombre = "Pendiente",
                Color = "#ffc107",
            },
            EstadoNuevo = new EstadoPedido
            {
                Id = 2,
                Codigo = "CONFIRMADO",
                Nombre = "Confirmado",
                Color = "#0d6efd",
            },
            Usuario = new Usuario
            {
                Id = 42,
                Username = "operario1",
                PasswordHash = "no-se-mapea",
                RolId = 1,
                Activo = true,
            },
        };

        var dto = mapper.Map<PedidoEstadoHistoricoDto>(entity);

        dto.EstadoAnteriorCodigo.Should().Be("PENDIENTE");
        dto.EstadoAnteriorNombre.Should().Be("Pendiente");
        dto.EstadoAnteriorColor.Should().Be("#ffc107");

        dto.EstadoNuevoCodigo.Should().Be("CONFIRMADO");
        dto.EstadoNuevoNombre.Should().Be("Confirmado");
        dto.EstadoNuevoColor.Should().Be("#0d6efd");

        dto.UsuarioNombre.Should().Be("operario1");
    }

    /// <summary>
    /// Caso de transición a CANCELADO con motivo: el navigation de EstadoNuevo
    /// está cargado pero EstadoAnterior también, y el Usuario también. Esto
    /// verifica que el helper `EstadoNuevoCodigo` (no-nullable en el DTO)
    /// sigue produciendo un string aunque los códigos no tengan semántica de
    /// cancelación — el helper de AutoMapper solo proyecta.
    /// </summary>
    [Fact]
    public void Map_CancelacionConMotivo_EstadoNuevoCamposResueltos()
    {
        var mapper = NewMapper();

        var entity = new PedidoEstadoHistorico
        {
            Id = 99,
            PedidoId = 200,
            EstadoAnteriorId = 2,
            EstadoNuevoId = 5,
            MotivoCancelacion = "Cliente canceló por tormenta",
            UsuarioId = 7,
            CreatedAt = DateTime.UtcNow,
            EstadoAnterior = new EstadoPedido
            {
                Id = 2,
                Codigo = "CONFIRMADO",
                Nombre = "Confirmado",
                Color = "#0d6efd",
            },
            EstadoNuevo = new EstadoPedido
            {
                Id = 5,
                Codigo = "CANCELADO",
                Nombre = "Cancelado",
                Color = "#dc3545",
            },
            Usuario = new Usuario
            {
                Id = 7,
                Username = "admin",
                PasswordHash = "no-se-mapea",
                RolId = 1,
                Activo = true,
            },
        };

        var dto = mapper.Map<PedidoEstadoHistoricoDto>(entity);

        dto.MotivoCancelacion.Should().Be("Cliente canceló por tormenta");
        dto.EstadoNuevoCodigo.Should().Be("CANCELADO");
        dto.EstadoNuevoNombre.Should().Be("Cancelado");
        dto.EstadoNuevoColor.Should().Be("#dc3545");
        dto.EstadoAnteriorCodigo.Should().Be("CONFIRMADO");
        dto.UsuarioNombre.Should().Be("admin");
    }
}
