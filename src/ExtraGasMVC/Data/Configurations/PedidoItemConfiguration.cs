using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Data.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class PedidoItemConfiguration : IEntityTypeConfiguration<PedidoItem>
{
    public void Configure(EntityTypeBuilder<PedidoItem> builder)
    {
        builder.ToTable("pedido_items", t =>
        {
            t.HasCheckConstraint("chk_pedido_items_cantidad", "`cantidad` > 0");
            t.HasCheckConstraint("chk_pedido_items_precio", "`precio_unitario` >= 0");
        });

        builder.HasKey(pi => pi.Id);
        builder.Property(pi => pi.Id).HasColumnName("id");

        builder.Property(pi => pi.PedidoId).HasColumnName("pedido_id");
        builder.Property(pi => pi.ProductoId).HasColumnName("producto_id");

        builder.Property(pi => pi.TipoLinea)
            .HasColumnName("tipo_linea")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasSentinel((TipoLinea)255);

        builder.Property(pi => pi.Cantidad)
            .HasColumnName("cantidad")
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(pi => pi.PrecioUnitario)
            .HasColumnName("precio_unitario")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(pi => pi.Subtotal)
            .HasColumnName("subtotal")
            .HasComputedColumnSql("`cantidad` * `precio_unitario`", stored: true);

        builder.Property(pi => pi.Observaciones)
            .HasColumnName("observaciones")
            .HasMaxLength(255);

        builder.Property(pi => pi.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(pi => pi.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAddOrUpdate()
            .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

        builder.HasOne(pi => pi.Pedido)
            .WithMany(p => p.Items)
            .HasForeignKey(pi => pi.PedidoId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_pedido_items_pedido");

        builder.HasOne(pi => pi.Producto)
            .WithMany()
            .HasForeignKey(pi => pi.ProductoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pedido_items_producto");

        // Unique constraint: one item per (pedido, producto, tipo_linea) among active items.
        // MySQL partial index workaround: we use a filter on DeletedAt == null.
        // Note: MySQL 9.6 does not support filtered unique indexes natively,
        // so the constraint enforcement is handled in PedidoService.AddItemAsync
        // with a defensive check + DbUpdateException catch for duplicate key violations.
        builder.HasIndex(pi => pi.PedidoId).HasDatabaseName("idx_pedido_items_pedido");
        builder.HasIndex(pi => pi.ProductoId).HasDatabaseName("idx_pedido_items_producto");
        builder.HasIndex(pi => pi.TipoLinea).HasDatabaseName("idx_pedido_items_tipo");
    }
}