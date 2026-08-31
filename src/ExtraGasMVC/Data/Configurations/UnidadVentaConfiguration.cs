using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

/// <summary>
/// Configuración EF de <see cref="UnidadVenta"/>. Issue #147 slice 3 item 7.
/// Réplica del patrón de <see cref="TipoProductoConfiguration"/>: snake_case
/// en columnas, unique key en codigo, default + auto-update en timestamps.
/// El query filter de soft-delete es la única diferencia — tipos_producto
/// no lo tiene porque su uso es read-only y los seed values no se borran;
/// aquí se incluye para mantener consistencia con el resto del schema que
/// sí aplica soft-delete (Clientes, Productos, etc.).
/// </summary>
public class UnidadVentaConfiguration : IEntityTypeConfiguration<UnidadVenta>
{
    public void Configure(EntityTypeBuilder<UnidadVenta> builder)
    {
        builder.ToTable("unidades_venta");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");

        builder.Property(u => u.Codigo)
            .HasColumnName("codigo")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(u => u.Nombre)
            .HasColumnName("nombre")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(u => u.Activo)
            .HasColumnName("activo")
            .HasColumnType("tinyint(1)")
            .HasDefaultValue(true);

        builder.Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(u => u.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAddOrUpdate()
            .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

        builder.Property(u => u.CreatedBy).HasColumnName("created_by");
        builder.Property(u => u.UpdatedBy).HasColumnName("updated_by");

        builder.Property(u => u.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("datetime");

        builder.HasIndex(u => u.Codigo)
            .IsUnique()
            .HasDatabaseName("uk_unidades_venta_codigo");

        builder.HasIndex(u => new { u.Activo, u.DeletedAt })
            .HasDatabaseName("idx_unidades_venta_activo");

        builder.HasQueryFilter(u => u.DeletedAt == null);
    }
}
