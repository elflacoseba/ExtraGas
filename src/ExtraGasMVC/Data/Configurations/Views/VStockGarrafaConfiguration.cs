using ExtraGasMVC.Data.Entities.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations.Views;

public class VStockGarrafaConfiguration : IEntityTypeConfiguration<VStockGarrafa>
{
    public void Configure(EntityTypeBuilder<VStockGarrafa> builder)
    {
        builder.ToView("v_stock_garrafas");
        builder.HasNoKey();

        builder.Property(v => v.CapacidadKg)
            .HasColumnName("capacidad_kg")
            .HasColumnType("tinyint unsigned");

        builder.Property(v => v.EstadoGarrafaId).HasColumnName("estado_garrafa_id");
        builder.Property(v => v.EstadoCodigo).HasColumnName("estado_codigo").HasMaxLength(30);
        builder.Property(v => v.EstadoNombre).HasColumnName("estado_nombre").HasMaxLength(100);
        builder.Property(v => v.EstadoColor).HasColumnName("estado_color").HasMaxLength(7);
        builder.Property(v => v.Cantidad).HasColumnName("cantidad");
    }
}
