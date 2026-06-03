using ExtraGasMVC.Data.Entities.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations.Views;

public class VProductoMasVendidoConfiguration : IEntityTypeConfiguration<VProductoMasVendido>
{
    public void Configure(EntityTypeBuilder<VProductoMasVendido> builder)
    {
        builder.ToView("v_productos_mas_vendidos");
        builder.HasNoKey();

        builder.Property(v => v.Fecha).HasColumnName("fecha").HasColumnType("date");
        builder.Property(v => v.ProductoId).HasColumnName("producto_id");
        builder.Property(v => v.ProductoCodigo).HasColumnName("producto_codigo").HasMaxLength(30);
        builder.Property(v => v.ProductoNombre).HasColumnName("producto_nombre").HasMaxLength(150);
        builder.Property(v => v.TipoProducto).HasColumnName("tipo_producto").HasMaxLength(100);
        builder.Property(v => v.CantidadVendida).HasColumnName("cantidad_vendida").HasPrecision(32, 2);
        builder.Property(v => v.CantidadEntregada).HasColumnName("cantidad_entregada").HasPrecision(32, 2);
        builder.Property(v => v.CantidadDevuelta).HasColumnName("cantidad_devuelta").HasPrecision(32, 2);
        builder.Property(v => v.MontoTotal).HasColumnName("monto_total").HasPrecision(42, 2);
    }
}
