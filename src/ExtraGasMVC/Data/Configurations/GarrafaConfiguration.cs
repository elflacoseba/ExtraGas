using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class GarrafaConfiguration : IEntityTypeConfiguration<Garrafa>
{
    public void Configure(EntityTypeBuilder<Garrafa> builder)
    {
        builder.ToTable("garrafas", t => t.HasCheckConstraint("chk_garrafas_capacidad", "`capacidad_kg` IN (10, 15, 45)"));

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id");

        builder.Property(g => g.Codigo)
            .HasColumnName("codigo")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(g => g.CapacidadKg)
            .HasColumnName("capacidad_kg")
            .HasColumnType("tinyint unsigned")
            .IsRequired();

        builder.Property(g => g.ProveedorId).HasColumnName("proveedor_id");
        builder.Property(g => g.RecepcionId).HasColumnName("recepcion_id");

        builder.Property(g => g.FechaCompra)
            .HasColumnName("fecha_compra")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(g => g.EstadoGarrafaId).HasColumnName("estado_garrafa_id");
        builder.Property(g => g.ClienteId).HasColumnName("cliente_id");

        builder.Property(g => g.Activo)
            .HasColumnName("activo")
            .HasColumnType("tinyint(1)")
            .HasDefaultValue(true);

        builder.Property(g => g.FechaUltimoMovimiento)
            .HasColumnName("fecha_ultimo_movimiento")
            .HasColumnType("datetime")
            .ValueGeneratedOnAddOrUpdate();
        builder.Property(g => g.FechaUltimoMovimiento).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.Property(g => g.Observaciones)
            .HasColumnName("observaciones")
            .HasColumnType("text");

        builder.Property(g => g.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(g => g.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAddOrUpdate()
            .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

        builder.Property(g => g.CreatedBy).HasColumnName("created_by");
        builder.Property(g => g.UpdatedBy).HasColumnName("updated_by");
        builder.Property(g => g.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("datetime");

        builder.HasOne(g => g.Proveedor)
            .WithMany()
            .HasForeignKey(g => g.ProveedorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_garrafas_proveedor");

        builder.HasOne<RecepcionProveedor>()
            .WithMany()
            .HasForeignKey(g => g.RecepcionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_garrafas_recepcion");

        builder.HasOne(g => g.EstadoGarrafa)
            .WithMany()
            .HasForeignKey(g => g.EstadoGarrafaId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_garrafas_estado");

        builder.HasOne(g => g.Cliente)
            .WithMany()
            .HasForeignKey(g => g.ClienteId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_garrafas_cliente");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(g => g.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_garrafas_created_by");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(g => g.UpdatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_garrafas_updated_by");

        builder.HasIndex(g => g.Codigo)
            .IsUnique()
            .HasDatabaseName("uq_garrafas_codigo");

        builder.HasIndex(g => g.EstadoGarrafaId).HasDatabaseName("idx_garrafas_estado");
        builder.HasIndex(g => g.ClienteId).HasDatabaseName("idx_garrafas_cliente");
        builder.HasIndex(g => g.CapacidadKg).HasDatabaseName("idx_garrafas_capacidad");
        builder.HasIndex(g => g.RecepcionId).HasDatabaseName("idx_garrafas_recepcion");
        builder.HasIndex(g => g.DeletedAt).HasDatabaseName("idx_garrafas_deleted_at");

        builder.HasQueryFilter(g => g.DeletedAt == null);
    }
}
