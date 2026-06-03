using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class SecuenciaConfiguration : IEntityTypeConfiguration<Secuencia>
{
    public void Configure(EntityTypeBuilder<Secuencia> builder)
    {
        builder.ToTable("secuencias");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.Nombre)
            .HasColumnName("nombre")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.Prefijo)
            .HasColumnName("prefijo")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.Anio)
            .HasColumnName("anio")
            .HasColumnType("smallint unsigned")
            .IsRequired();

        builder.Property(s => s.UltimoValor)
            .HasColumnName("ultimo_valor")
            .HasColumnType("int unsigned")
            .HasDefaultValue(0u)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAddOrUpdate()
            .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

        builder.HasIndex(s => new { s.Nombre, s.Anio })
            .IsUnique()
            .HasDatabaseName("uq_secuencias_nombre_anio");
    }
}
