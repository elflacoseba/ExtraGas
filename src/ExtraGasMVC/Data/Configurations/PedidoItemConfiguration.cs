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

        // Issue #17: soft-delete per AGENTS.md convention #6. La columna
        // existe en BD desde la migración 20260607_000003. HasQueryFilter
        // oculta DeletedAt != null de las queries por defecto (mismo patrón
        // que Pedido, Cliente, Producto, etc.). El índice acelera los lookups
        // que pasan por IgnoreQueryFilters() y mantiene simetría con el resto
        // del modelo.
        builder.Property(pi => pi.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("datetime");

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
        // MySQL partial index workaround: la BD genera una columna virtual
        // <c>unique_hash</c> que concatena
        // (pedido_id, producto_id, tipo_linea, COALESCE(deleted_at, '0')) y
        // tiene un UNIQUE INDEX. Cuando un item se soft-deletea su hash
        // cambia, así que se puede re-agregar el mismo (pedido, producto,
        // tipo_linea) sin violar la constraint. La enforcement defensiva
        // (chequeo + catch de DbUpdateException 1062) sigue viviendo en
        // PedidoService.AddItemAsync por si la BD y el EF se desincronizan.
        builder.HasIndex(pi => pi.PedidoId).HasDatabaseName("idx_pedido_items_pedido");
        builder.HasIndex(pi => pi.ProductoId).HasDatabaseName("idx_pedido_items_producto");
        builder.HasIndex(pi => pi.TipoLinea).HasDatabaseName("idx_pedido_items_tipo");
        builder.HasIndex(pi => pi.DeletedAt).HasDatabaseName("idx_pedido_items_deleted_at");

        // Issue #17: soft-delete query filter. Sin esto la BD no soft-deleta
        // realmente — los registros borrados volverían a aparecer en
        // GetItemsByPedidoAsync, RecalculateTotalsAsync y LoadItemsParaCanjeAsync.
        builder.HasQueryFilter(pi => pi.DeletedAt == null);
    }
}