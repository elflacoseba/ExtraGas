using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class TipoMovimientoGarrafaConfiguration : IEntityTypeConfiguration<TipoMovimientoGarrafa>
{
    public void Configure(EntityTypeBuilder<TipoMovimientoGarrafa> builder)
    {
        builder.ToTable("tipos_movimiento_garrafa");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.Codigo)
            .HasColumnName("codigo")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(t => t.Nombre)
            .HasColumnName("nombre")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.Descripcion)
            .HasColumnName("descripcion")
            .HasMaxLength(255);

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAddOrUpdate()
            .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

        builder.HasIndex(t => t.Codigo)
            .IsUnique()
            .HasDatabaseName("uq_tipos_movimiento_garrafa_codigo");
    }
}
