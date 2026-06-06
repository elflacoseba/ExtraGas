using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class ProductoConfiguration : IEntityTypeConfiguration<Producto>
{
    public void Configure(EntityTypeBuilder<Producto> builder)
    {
        builder.ToTable("productos", t => t.HasCheckConstraint("chk_productos_precio", "`precio_actual` >= 0"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.Codigo)
            .HasColumnName("codigo")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.Nombre)
            .HasColumnName("nombre")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(p => p.Descripcion)
            .HasColumnName("descripcion")
            .HasMaxLength(255);

        builder.Property(p => p.TipoProductoId).HasColumnName("tipo_producto_id");

        builder.Property(p => p.CapacidadKg)
            .HasColumnName("capacidad_kg")
            .HasPrecision(8, 2);

        builder.Property(p => p.UnidadVenta)
            .HasColumnName("unidad_venta")
            .HasMaxLength(20)
            .HasDefaultValue("UNIDAD")
            .IsRequired();

        builder.Property(p => p.PrecioActual)
            .HasColumnName("precio_actual")
            .HasPrecision(12, 2)
            .HasDefaultValue(0m);

        builder.Property(p => p.ManejaGarrafaIndividual)
            .HasColumnName("maneja_garrafa_individual")
            .HasColumnType("tinyint(1)")
            .HasDefaultValue(false);

        builder.Property(p => p.Activo)
            .HasColumnName("activo")
            .HasColumnType("tinyint(1)")
            .HasDefaultValue(true);

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

        builder.HasOne(p => p.TipoProducto)
            .WithMany()
            .HasForeignKey(p => p.TipoProductoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_productos_tipo");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(p => p.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_productos_created_by");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(p => p.UpdatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_productos_updated_by");

        builder.HasIndex(p => p.Codigo)
            .IsUnique()
            .HasDatabaseName("uq_productos_codigo");

        builder.HasIndex(p => p.TipoProductoId).HasDatabaseName("idx_productos_tipo");
        builder.HasIndex(p => new { p.Codigo, p.Nombre }).HasDatabaseName("idx_productos_codigo_nombre");
        builder.HasIndex(p => p.DeletedAt).HasDatabaseName("idx_productos_deleted_at");

        builder.HasQueryFilter(p => p.DeletedAt == null);
    }
}
