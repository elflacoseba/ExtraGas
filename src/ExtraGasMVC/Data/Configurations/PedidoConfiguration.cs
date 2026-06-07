using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class PedidoConfiguration : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> builder)
    {
        builder.ToTable("pedidos", t =>
        {
            t.HasCheckConstraint("chk_pedidos_total", "`total` >= 0");
            t.HasCheckConstraint("chk_pedidos_monto_pagado", "`monto_pagado` >= 0");
        });

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

        builder.Property(p => p.FechaEntrega)
            .HasColumnName("fecha_entrega")
            .HasColumnType("datetime");

        builder.Property(p => p.ClienteId).HasColumnName("cliente_id");
        builder.Property(p => p.EmpleadoId).HasColumnName("empleado_id");
        builder.Property(p => p.EstadoPedidoId).HasColumnName("estado_pedido_id");
        builder.Property(p => p.CanalVentaId).HasColumnName("canal_venta_id");
        builder.Property(p => p.MedioContactoId).HasColumnName("medio_contacto_id");

        builder.Property(p => p.Subtotal)
            .HasColumnName("subtotal")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m);

        builder.Property(p => p.Descuento)
            .HasColumnName("descuento")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m);

        builder.Property(p => p.Total)
            .HasColumnName("total")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m);

        builder.Property(p => p.MontoPagado)
            .HasColumnName("monto_pagado")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m)
            .ValueGeneratedOnAddOrUpdate();
        builder.Property(p => p.MontoPagado).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.Property(p => p.Saldo)
            .HasColumnName("saldo")
            .HasPrecision(12, 2)
            .HasComputedColumnSql("`total` - `monto_pagado`", stored: true);

        builder.Property(p => p.Observaciones)
            .HasColumnName("observaciones")
            .HasColumnType("text");

        builder.Property(p => p.DireccionEntrega)
            .HasColumnName("direccion_entrega")
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

        // FKs
        builder.HasOne(p => p.Cliente)
            .WithMany()
            .HasForeignKey(p => p.ClienteId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pedidos_cliente");

        builder.HasOne(p => p.Empleado)
            .WithMany()
            .HasForeignKey(p => p.EmpleadoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pedidos_empleado");

        builder.HasOne(p => p.EstadoPedido)
            .WithMany()
            .HasForeignKey(p => p.EstadoPedidoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pedidos_estado");

        builder.HasOne(p => p.CanalVenta)
            .WithMany()
            .HasForeignKey(p => p.CanalVentaId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pedidos_canal");

        builder.HasOne(p => p.MedioContactoPedido)
            .WithMany()
            .HasForeignKey(p => p.MedioContactoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pedidos_medio_contacto");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(p => p.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pedidos_created_by");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(p => p.UpdatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pedidos_updated_by");

        builder.HasIndex(p => p.Numero)
            .IsUnique()
            .HasDatabaseName("idx_pedidos_numero");

        builder.HasIndex(p => new { p.ClienteId, p.Fecha }).HasDatabaseName("idx_pedidos_cliente");
        builder.HasIndex(p => p.Fecha).HasDatabaseName("idx_pedidos_fecha");
        builder.HasIndex(p => p.EstadoPedidoId).HasDatabaseName("idx_pedidos_estado");
        builder.HasIndex(p => new { p.EmpleadoId, p.Fecha }).HasDatabaseName("idx_pedidos_empleado");
        builder.HasIndex(p => p.DeletedAt).HasDatabaseName("idx_pedidos_deleted_at");

        builder.HasQueryFilter(p => p.DeletedAt == null);
    }
}
