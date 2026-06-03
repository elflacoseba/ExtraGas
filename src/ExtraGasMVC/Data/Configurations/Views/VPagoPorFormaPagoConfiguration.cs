using ExtraGasMVC.Data.Entities.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations.Views;

public class VPagoPorFormaPagoConfiguration : IEntityTypeConfiguration<VPagoPorFormaPago>
{
    public void Configure(EntityTypeBuilder<VPagoPorFormaPago> builder)
    {
        builder.ToView("v_pagos_por_forma_pago");
        builder.HasNoKey();

        builder.Property(v => v.Fecha).HasColumnName("fecha").HasColumnType("date");
        builder.Property(v => v.FormaPagoCodigo).HasColumnName("forma_pago_codigo").HasMaxLength(30);
        builder.Property(v => v.FormaPagoNombre).HasColumnName("forma_pago_nombre").HasMaxLength(100);
        builder.Property(v => v.CantidadPagos).HasColumnName("cantidad_pagos");
        builder.Property(v => v.MontoTotal).HasColumnName("monto_total").HasPrecision(42, 2);
    }
}
