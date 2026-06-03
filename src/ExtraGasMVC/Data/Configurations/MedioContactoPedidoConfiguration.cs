using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class MedioContactoPedidoConfiguration : IEntityTypeConfiguration<MedioContactoPedido>
{
    public void Configure(EntityTypeBuilder<MedioContactoPedido> builder)
    {
        builder.ToTable("medios_contacto_pedido");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.Codigo)
            .HasColumnName("codigo")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(m => m.Nombre)
            .HasColumnName("nombre")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.Descripcion)
            .HasColumnName("descripcion")
            .HasMaxLength(255);

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(m => m.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAddOrUpdate()
            .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

        builder.HasIndex(m => m.Codigo)
            .IsUnique()
            .HasDatabaseName("uq_medios_contacto_pedido_codigo");
    }
}
