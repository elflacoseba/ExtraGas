using ExtraGasMVC.Data.Entities.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations.Views;

public class VPedidoResumenConfiguration : IEntityTypeConfiguration<VPedidoResumen>
{
    public void Configure(EntityTypeBuilder<VPedidoResumen> builder)
    {
        builder.ToView("v_pedidos_resumen");
        builder.HasNoKey();

        builder.Property(v => v.Id).HasColumnName("id");
        builder.Property(v => v.Numero).HasColumnName("numero").HasMaxLength(20);
        builder.Property(v => v.Fecha).HasColumnName("fecha").HasColumnType("datetime");
        builder.Property(v => v.FechaEntrega).HasColumnName("fecha_entrega").HasColumnType("datetime");
        builder.Property(v => v.ClienteId).HasColumnName("cliente_id");
        builder.Property(v => v.Cliente).HasColumnName("cliente").HasMaxLength(201);
        builder.Property(v => v.ClienteTelefono).HasColumnName("cliente_telefono").HasMaxLength(25);
        builder.Property(v => v.EmpleadoId).HasColumnName("empleado_id");
        builder.Property(v => v.Empleado).HasColumnName("empleado").HasMaxLength(201);
        builder.Property(v => v.EstadoPedidoId).HasColumnName("estado_pedido_id");
        builder.Property(v => v.EstadoCodigo).HasColumnName("estado_codigo").HasMaxLength(30);
        builder.Property(v => v.EstadoNombre).HasColumnName("estado_nombre").HasMaxLength(100);
        builder.Property(v => v.CanalVentaId).HasColumnName("canal_venta_id");
        builder.Property(v => v.CanalCodigo).HasColumnName("canal_codigo").HasMaxLength(30);
        builder.Property(v => v.Subtotal).HasColumnName("subtotal").HasPrecision(12, 2);
        builder.Property(v => v.Descuento).HasColumnName("descuento").HasPrecision(12, 2);
        builder.Property(v => v.Total).HasColumnName("total").HasPrecision(12, 2);
        builder.Property(v => v.MontoPagado).HasColumnName("monto_pagado").HasPrecision(12, 2);
        builder.Property(v => v.Saldo).HasColumnName("saldo").HasPrecision(12, 2);
        builder.Property(v => v.EstadoPago).HasColumnName("estado_pago").HasMaxLength(10);
    }
}
