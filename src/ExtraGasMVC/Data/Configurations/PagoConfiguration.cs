using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class PagoConfiguration : IEntityTypeConfiguration<Pago>
{
    public void Configure(EntityTypeBuilder<Pago> builder)
    {
        builder.ToTable("pagos", t => t.HasCheckConstraint("chk_pagos_monto", "`monto` > 0"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.NumeroRecibo)
            .HasColumnName("numero_recibo")
            .HasMaxLength(20)
            .ValueGeneratedOnAdd();
        builder.Property(p => p.NumeroRecibo).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.Property(p => p.Fecha)
            .HasColumnName("fecha")
            .HasColumnType("datetime")
            .IsRequired();

        builder.Property(p => p.ClienteId).HasColumnName("cliente_id");
        builder.Property(p => p.PedidoId).HasColumnName("pedido_id");
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

        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(p => p.ClienteId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pagos_cliente");

        builder.HasOne<Pedido>()
            .WithMany()
            .HasForeignKey(p => p.PedidoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pagos_pedido");

        builder.HasOne<FormaPago>()
            .WithMany()
            .HasForeignKey(p => p.FormaPagoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pagos_forma");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(p => p.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pagos_created_by");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(p => p.UpdatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pagos_updated_by");

        builder.HasIndex(p => p.NumeroRecibo)
            .IsUnique()
            .HasDatabaseName("idx_pagos_numero");

        builder.HasIndex(p => new { p.ClienteId, p.Fecha }).HasDatabaseName("idx_pagos_cliente");
        builder.HasIndex(p => p.PedidoId).HasDatabaseName("idx_pagos_pedido");
        builder.HasIndex(p => new { p.FormaPagoId, p.Fecha }).HasDatabaseName("idx_pagos_forma");
        builder.HasIndex(p => p.Fecha).HasDatabaseName("idx_pagos_fecha");
        builder.HasIndex(p => p.DeletedAt).HasDatabaseName("idx_pagos_deleted_at");

        builder.HasQueryFilter(p => p.DeletedAt == null);
    }
}
