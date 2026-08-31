using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="ProductoPrecioHistorico"/>.
/// Tabla append-only: sin soft-delete (no <c>HasQueryFilter</c>), sin
/// <c>UpdatedAt</c>. Los FKs usan <c>RESTRICT</c> porque no queremos perder
/// histórico si se intenta borrar un producto o usuario referenciado.
///
/// El índice <c>idx_pph_producto_changed</c> cubre el caso de uso más
/// frecuente: SELECT ... ORDER BY changed_at DESC LIMIT 1 por producto_id.
/// </summary>
public class ProductoPrecioHistoricoConfiguration : IEntityTypeConfiguration<ProductoPrecioHistorico>
{
    public void Configure(EntityTypeBuilder<ProductoPrecioHistorico> builder)
    {
        builder.ToTable("producto_precios_historico");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.ProductoId).HasColumnName("producto_id");

        builder.Property(p => p.PrecioAnterior)
            .HasColumnName("precio_anterior")
            .HasPrecision(12, 2);

        builder.Property(p => p.PrecioNuevo)
            .HasColumnName("precio_nuevo")
            .HasPrecision(12, 2);

        builder.Property(p => p.MotivoCambioPrecio)
            .HasColumnName("motivo_cambio_precio")
            .HasMaxLength(255);

        builder.Property(p => p.ChangedBy).HasColumnName("changed_by");

        builder.Property(p => p.ChangedAt)
            .HasColumnName("changed_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(p => p.Producto)
            .WithMany()
            .HasForeignKey(p => p.ProductoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pph_producto");

        builder.HasOne(p => p.ChangedByUsuario)
            .WithMany()
            .HasForeignKey(p => p.ChangedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pph_changed_by");

        builder.HasIndex(p => new { p.ProductoId, p.ChangedAt })
            .HasDatabaseName("idx_pph_producto_changed");
    }
}
