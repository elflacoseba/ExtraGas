using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class RecepcionItemConfiguration : IEntityTypeConfiguration<RecepcionItem>
{
    public void Configure(EntityTypeBuilder<RecepcionItem> builder)
    {
        builder.ToTable("recepcion_items", t =>
        {
            t.HasCheckConstraint("chk_recepcion_items_cantidad", "`cantidad` > 0");
            t.HasCheckConstraint("chk_recepcion_items_precio", "`precio_unitario` >= 0");
        });

        builder.HasKey(ri => ri.Id);
        builder.Property(ri => ri.Id).HasColumnName("id");

        builder.Property(ri => ri.RecepcionId).HasColumnName("recepcion_id");
        builder.Property(ri => ri.ProductoId).HasColumnName("producto_id");

        builder.Property(ri => ri.Cantidad)
            .HasColumnName("cantidad")
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(ri => ri.PrecioUnitario)
            .HasColumnName("precio_unitario")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(ri => ri.Subtotal)
            .HasColumnName("subtotal")
            .HasPrecision(12, 2)
            .HasComputedColumnSql("`cantidad` * `precio_unitario`", stored: true);

        builder.Property(ri => ri.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(ri => ri.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAddOrUpdate()
            .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

        builder.HasOne<RecepcionProveedor>()
            .WithMany()
            .HasForeignKey(ri => ri.RecepcionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_recepcion_items_recepcion");

        builder.HasOne<Producto>()
            .WithMany()
            .HasForeignKey(ri => ri.ProductoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_recepcion_items_producto");

        builder.HasIndex(ri => ri.RecepcionId).HasDatabaseName("idx_recepcion_items_recepcion");
        builder.HasIndex(ri => ri.ProductoId).HasDatabaseName("idx_recepcion_items_producto");

    }
}
