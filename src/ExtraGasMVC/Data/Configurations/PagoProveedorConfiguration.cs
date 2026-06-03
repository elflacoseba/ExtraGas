using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class PagoProveedorConfiguration : IEntityTypeConfiguration<PagoProveedor>
{
    public void Configure(EntityTypeBuilder<PagoProveedor> builder)
    {
        builder.ToTable("pagos_proveedor", t => t.HasCheckConstraint("chk_pagos_proveedor_monto", "`monto` > 0"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.Numero)
            .HasColumnName("numero")
            .HasMaxLength(20)
            .ValueGeneratedOnAdd();
        builder.Property(p => p.Numero).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.Property(p => p.Fecha)
            .HasColumnName("fecha")
            .HasColumnType("datetime")
            .IsRequired();

        builder.Property(p => p.ProveedorId).HasColumnName("proveedor_id");
        builder.Property(p => p.RecepcionId).HasColumnName("recepcion_id");
        builder.Property(p => p.FormaPagoId).HasColumnName("forma_pago_id");

        builder.Property(p => p.Monto)
            .HasColumnName("monto")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(p => p.Referencia)
            .HasColumnName("referencia")
            .HasMaxLength(100);

        builder.Property(p => p.Observaciones)
            .HasColumnName("observaciones")
            .HasMaxLength(255);

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAddOrUpdate()
            .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");
        builder.Property(p => p.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("datetime");

        builder.HasOne<Proveedor>()
            .WithMany()
            .HasForeignKey(p => p.ProveedorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pagos_proveedor_proveedor");

        builder.HasOne<RecepcionProveedor>()
            .WithMany()
            .HasForeignKey(p => p.RecepcionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pagos_proveedor_recepcion");

        builder.HasOne<FormaPago>()
            .WithMany()
            .HasForeignKey(p => p.FormaPagoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pagos_proveedor_forma");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(p => p.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pagos_proveedor_created_by");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(p => p.UpdatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pagos_proveedor_updated_by");

        builder.HasIndex(p => p.Numero)
            .IsUnique()
            .HasDatabaseName("idx_pagos_proveedor_numero");

        builder.HasIndex(p => new { p.ProveedorId, p.Fecha }).HasDatabaseName("idx_pagos_proveedor_proveedor");
        builder.HasIndex(p => p.RecepcionId).HasDatabaseName("idx_pagos_proveedor_recepcion");
        builder.HasIndex(p => new { p.FormaPagoId, p.Fecha }).HasDatabaseName("idx_pagos_proveedor_forma");
        builder.HasIndex(p => p.Fecha).HasDatabaseName("idx_pagos_proveedor_fecha");
        builder.HasIndex(p => p.DeletedAt).HasDatabaseName("idx_pagos_proveedor_deleted_at");

        builder.HasQueryFilter(p => p.DeletedAt == null);
    }
}
