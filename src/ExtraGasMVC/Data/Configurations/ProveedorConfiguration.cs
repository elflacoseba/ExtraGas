using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class ProveedorConfiguration : IEntityTypeConfiguration<Proveedor>
{
    public void Configure(EntityTypeBuilder<Proveedor> builder)
    {
        builder.ToTable("proveedores", t => t.HasCheckConstraint("chk_proveedores_cuit", "`cuit` REGEXP '^[0-9]{11}$'"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.Codigo)
            .HasColumnName("codigo")
            .HasMaxLength(20);

        builder.Property(p => p.RazonSocial)
            .HasColumnName("razon_social")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(p => p.NombreFantasia)
            .HasColumnName("nombre_fantasia")
            .HasMaxLength(150);

        builder.Property(p => p.Cuit)
            .HasColumnName("cuit")
            .HasMaxLength(15)
            .IsRequired();

        builder.Property(p => p.TelefonoPrincipal).HasColumnName("telefono_principal").HasMaxLength(25);
        builder.Property(p => p.TelefonoSecundario).HasColumnName("telefono_secundario").HasMaxLength(25);
        builder.Property(p => p.Email).HasColumnName("email").HasMaxLength(150);
        builder.Property(p => p.Calle).HasColumnName("calle").HasMaxLength(150);
        builder.Property(p => p.Numero).HasColumnName("numero").HasMaxLength(10);
        builder.Property(p => p.Piso).HasColumnName("piso").HasMaxLength(10);
        builder.Property(p => p.Depto).HasColumnName("depto").HasMaxLength(10);
        builder.Property(p => p.Ciudad).HasColumnName("ciudad").HasMaxLength(100);
        builder.Property(p => p.CodigoPostal).HasColumnName("codigo_postal").HasMaxLength(10);
        builder.Property(p => p.ProvinciaId).HasColumnName("provincia_id");

        builder.Property(p => p.Referencias).HasColumnName("referencias").HasColumnType("text");
        builder.Property(p => p.ContactoNombre).HasColumnName("contacto_nombre").HasMaxLength(150);
        builder.Property(p => p.ContactoTelefono).HasColumnName("contacto_telefono").HasMaxLength(25);
        builder.Property(p => p.ContactoEmail).HasColumnName("contacto_email").HasMaxLength(150);
        builder.Property(p => p.Observaciones).HasColumnName("observaciones").HasColumnType("text");

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

        // FKs
        builder.HasOne<Provincia>()
            .WithMany()
            .HasForeignKey(p => p.ProvinciaId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_proveedores_provincia");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(p => p.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_proveedores_created_by");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(p => p.UpdatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_proveedores_updated_by");

        builder.HasIndex(p => p.Cuit)
            .IsUnique()
            .HasDatabaseName("uq_proveedores_cuit");

        builder.HasIndex(p => p.RazonSocial).HasDatabaseName("idx_proveedores_razon_social");
        builder.HasIndex(p => p.Codigo).HasDatabaseName("idx_proveedores_codigo");
        builder.HasIndex(p => p.DeletedAt).HasDatabaseName("idx_proveedores_deleted_at");

        builder.HasQueryFilter(p => p.DeletedAt == null);
    }
}
