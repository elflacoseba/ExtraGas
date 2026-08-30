using System.ComponentModel.DataAnnotations;

namespace ExtraGasMVC.DTOs;

/// <summary>
/// DTO de salida para <see cref="Data.Entities.Views.VSaldoCliente"/>.
/// Proyecta las 5 columnas de la vista <c>v_saldo_clientes</c>:
/// cliente_id, cliente (formateado "Apellido, Nombre"), teléfono principal,
/// pedidos pendientes (count) y saldo total (DECIMAL 12,2).
///
/// Se usa en /Clientes/CuentasCorrientes para resolver el N+1 que producía
/// <see cref="Data.Entities.Cliente"/> + iteración por cliente (issue #109):
/// la vista entrega cliente + saldo + pedidos pendientes en una sola fila
/// agregada en MySQL.
/// </summary>
public class VSaldoClienteDto
{
    [Display(Name = "Cliente")]
    public string Cliente { get; set; } = null!;

    [Display(Name = "Teléfono")]
    public string? TelefonoPrincipal { get; set; }

    [Display(Name = "Pedidos pendientes")]
    public int PedidosPendientes { get; set; }

    [Display(Name = "Saldo")]
    public decimal SaldoTotal { get; set; }

    /// <summary>
    /// Id del cliente. No se muestra en la grilla — se usa como route param
    /// para los links de acción (ir a Details del cliente).
    /// </summary>
    public ulong ClienteId { get; set; }
}