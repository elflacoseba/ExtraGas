using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class FormaPagoConfiguration : IEntityTypeConfiguration<FormaPago>
{
    public void Configure(EntityTypeBuilder<FormaPago> builder)
    {
        builder.ToTable("formas_pago");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id");

        builder.Property(f => f.Codigo)
            .HasColumnName("codigo")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(f => f.Nombre)
            .HasColumnName("nombre")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(f => f.Descripcion)
            .HasColumnName("descripcion")
            .HasMaxLength(255);

        builder.Property(f => f.RequiereReferencia)
            .HasColumnName("requiere_referencia")
            .HasColumnType("tinyint(1)")
            .HasDefaultValue(false);

        builder.Property(f => f.Activo)
            .HasColumnName("activo")
            .HasColumnType("tinyint(1)")
            .HasDefaultValue(true);

        builder.Property(f => f.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(f => f.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAddOrUpdate()
            .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

        builder.HasIndex(f => f.Codigo)
            .IsUnique()
            .HasDatabaseName("uq_formas_pago_codigo");
    }
}
