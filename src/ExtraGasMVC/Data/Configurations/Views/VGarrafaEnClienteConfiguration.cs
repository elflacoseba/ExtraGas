using ExtraGasMVC.Data.Entities.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations.Views;

public class VGarrafaEnClienteConfiguration : IEntityTypeConfiguration<VGarrafaEnCliente>
{
    public void Configure(EntityTypeBuilder<VGarrafaEnCliente> builder)
    {
        builder.ToView("v_garrafas_en_clientes");
        builder.HasNoKey();

        builder.Property(v => v.GarrafaId).HasColumnName("garrafa_id");
        builder.Property(v => v.Codigo).HasColumnName("codigo").HasMaxLength(50);
        builder.Property(v => v.CapacidadKg)
            .HasColumnName("capacidad_kg")
            .HasColumnType("tinyint unsigned");

        builder.Property(v => v.ClienteId).HasColumnName("cliente_id");
        builder.Property(v => v.Cliente).HasColumnName("cliente").HasMaxLength(201);
        builder.Property(v => v.FechaUltimoMovimiento).HasColumnName("fecha_ultimo_movimiento").HasColumnType("datetime");
        builder.Property(v => v.DiasEnCliente).HasColumnName("dias_en_cliente");
    }
}
