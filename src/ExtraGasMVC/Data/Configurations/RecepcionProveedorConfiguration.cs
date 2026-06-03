using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class RecepcionProveedorConfiguration : IEntityTypeConfiguration<RecepcionProveedor>
{
    public void Configure(EntityTypeBuilder<RecepcionProveedor> builder)
    {
        builder.ToTable("recepciones_proveedor", t => t.HasCheckConstraint("chk_recepciones_total", "`total` >= 0"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.Numero)
            .HasColumnName("numero")
            .HasMaxLength(20)
            .ValueGeneratedOnAdd();
        builder.Property(r => r.Numero).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.Property(r => r.Fecha)
            .HasColumnName("fecha")
            .HasColumnType("datetime")
            .IsRequired();

        builder.Property(r => r.ProveedorId).HasColumnName("proveedor_id");
        builder.Property(r => r.EmpleadoId).HasColumnName("empleado_id");

        builder.Property(r => r.NumeroFacturaProveedor)
            .HasColumnName("numero_factura_proveedor")
            .HasMaxLength(50);

        builder.Property(r => r.Subtotal)
            .HasColumnName("subtotal")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m);

        builder.Property(r => r.Descuento)
            .HasColumnName("descuento")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m);

        builder.Property(r => r.Total)
            .HasColumnName("total")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m);

        builder.Property(r => r.MontoPagado)
            .HasColumnName("monto_pagado")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m)
            .ValueGeneratedOnAddOrUpdate();
        builder.Property(r => r.MontoPagado).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.Property(r => r.Saldo)
            .HasColumnName("saldo")
            .HasPrecision(12, 2)
            .HasComputedColumnSql("`total` - `monto_pagado`", stored: true);

        builder.Property(r => r.Observaciones)
            .HasColumnName("observaciones")
            .HasColumnType("text");

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAddOrUpdate()
            .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by");
        builder.Property(r => r.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("datetime");

        builder.HasOne<Proveedor>()
            .WithMany()
            .HasForeignKey(r => r.ProveedorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_recepciones_proveedor");

        builder.HasOne<Empleado>()
            .WithMany()
            .HasForeignKey(r => r.EmpleadoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_recepciones_empleado");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(r => r.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_recepciones_created_by");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(r => r.UpdatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_recepciones_updated_by");

        builder.HasIndex(r => r.Numero)
            .IsUnique()
            .HasDatabaseName("idx_recepciones_numero");

        builder.HasIndex(r => new { r.ProveedorId, r.Fecha }).HasDatabaseName("idx_recepciones_proveedor");
        builder.HasIndex(r => r.Fecha).HasDatabaseName("idx_recepciones_fecha");
        builder.HasIndex(r => r.DeletedAt).HasDatabaseName("idx_recepciones_deleted_at");

        builder.HasQueryFilter(r => r.DeletedAt == null);
    }
}
