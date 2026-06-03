using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class EmpleadoConfiguration : IEntityTypeConfiguration<Empleado>
{
    public void Configure(EntityTypeBuilder<Empleado> builder)
    {
        builder.ToTable("empleados");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.Nombre)
            .HasColumnName("nombre")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Apellido)
            .HasColumnName("apellido")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Dni)
            .HasColumnName("dni")
            .HasMaxLength(15);

        builder.Property(e => e.Cuil)
            .HasColumnName("cuil")
            .HasMaxLength(15);

        builder.Property(e => e.Telefono)
            .HasColumnName("telefono")
            .HasMaxLength(25);

        builder.Property(e => e.Email)
            .HasColumnName("email")
            .HasMaxLength(150);

        builder.Property(e => e.Calle)
            .HasColumnName("calle")
            .HasMaxLength(150);

        builder.Property(e => e.Numero)
            .HasColumnName("numero")
            .HasMaxLength(10);

        builder.Property(e => e.Piso)
            .HasColumnName("piso")
            .HasMaxLength(10);

        builder.Property(e => e.Depto)
            .HasColumnName("depto")
            .HasMaxLength(10);

        builder.Property(e => e.Ciudad)
            .HasColumnName("ciudad")
            .HasMaxLength(100);

        builder.Property(e => e.CodigoPostal)
            .HasColumnName("codigo_postal")
            .HasMaxLength(10);

        builder.Property(e => e.ProvinciaId).HasColumnName("provincia_id");
        builder.Property(e => e.FechaIngreso)
            .HasColumnName("fecha_ingreso")
            .HasColumnType("date");

        builder.Property(e => e.UsuarioId).HasColumnName("usuario_id");
        builder.Property(e => e.Activo)
            .HasColumnName("activo")
            .HasColumnType("tinyint(1)")
            .HasDefaultValue(true);

        builder.Property(e => e.Observaciones)
            .HasColumnName("observaciones")
            .HasColumnType("text");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAddOrUpdate()
            .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("datetime");

        // FKs
        builder.HasOne<Provincia>()
            .WithMany()
            .HasForeignKey(e => e.ProvinciaId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_empleados_provincia");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(e => e.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_empleados_usuario");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_empleados_created_by");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_empleados_updated_by");

        builder.HasIndex(e => e.Dni)
            .IsUnique()
            .HasDatabaseName("uq_empleados_dni");

        builder.HasIndex(e => new { e.Apellido, e.Nombre }).HasDatabaseName("idx_empleados_apellido");
        builder.HasIndex(e => e.UsuarioId).HasDatabaseName("idx_empleados_usuario");
        builder.HasIndex(e => e.DeletedAt).HasDatabaseName("idx_empleados_deleted_at");

        builder.HasQueryFilter(e => e.DeletedAt == null);
    }
}
