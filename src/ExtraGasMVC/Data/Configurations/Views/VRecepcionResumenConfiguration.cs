using ExtraGasMVC.Data.Entities.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations.Views;

public class VRecepcionResumenConfiguration : IEntityTypeConfiguration<VRecepcionResumen>
{
    public void Configure(EntityTypeBuilder<VRecepcionResumen> builder)
    {
        builder.ToView("v_recepciones_resumen");
        builder.HasNoKey();

        builder.Property(v => v.Id).HasColumnName("id");
        builder.Property(v => v.Numero).HasColumnName("numero").HasMaxLength(20);
        builder.Property(v => v.Fecha).HasColumnName("fecha").HasColumnType("datetime");
        builder.Property(v => v.ProveedorId).HasColumnName("proveedor_id");
        builder.Property(v => v.Proveedor).HasColumnName("proveedor").HasMaxLength(150);
        builder.Property(v => v.ProveedorCuit).HasColumnName("proveedor_cuit").HasMaxLength(15);
        builder.Property(v => v.EmpleadoId).HasColumnName("empleado_id");
        builder.Property(v => v.Empleado).HasColumnName("empleado").HasMaxLength(201);
        builder.Property(v => v.NumeroFacturaProveedor).HasColumnName("numero_factura_proveedor").HasMaxLength(50);
        builder.Property(v => v.Subtotal).HasColumnName("subtotal").HasPrecision(12, 2);
        builder.Property(v => v.Descuento).HasColumnName("descuento").HasPrecision(12, 2);
        builder.Property(v => v.Total).HasColumnName("total").HasPrecision(12, 2);
        builder.Property(v => v.MontoPagado).HasColumnName("monto_pagado").HasPrecision(12, 2);
        builder.Property(v => v.Saldo).HasColumnName("saldo").HasPrecision(12, 2);
        builder.Property(v => v.EstadoPago).HasColumnName("estado_pago").HasMaxLength(10);
    }
}
