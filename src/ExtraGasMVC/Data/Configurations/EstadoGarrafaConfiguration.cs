using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class EstadoGarrafaConfiguration : IEntityTypeConfiguration<EstadoGarrafa>
{
    public void Configure(EntityTypeBuilder<EstadoGarrafa> builder)
    {
        builder.ToTable("estados_garrafa");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.Codigo)
            .HasColumnName("codigo")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.Nombre)
            .HasColumnName("nombre")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Descripcion)
            .HasColumnName("descripcion")
            .HasMaxLength(255);

        builder.Property(e => e.EsDisponibleParaVenta)
            .HasColumnName("es_disponible_para_venta")
            .HasColumnType("tinyint(1)")
            .HasDefaultValue(false);

        builder.Property(e => e.RequiereCliente)
            .HasColumnName("requiere_cliente")
            .HasColumnType("tinyint(1)")
            .HasDefaultValue(false);

        builder.Property(e => e.Color)
            .HasColumnName("color")
            .HasMaxLength(7);

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAddOrUpdate()
            .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

        builder.HasIndex(e => e.Codigo)
            .IsUnique()
            .HasDatabaseName("uq_estados_garrafa_codigo");
    }
}
