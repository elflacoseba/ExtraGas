using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("usuarios");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");

        builder.Property(u => u.Username)
            .HasColumnName("username")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(150);

        builder.Property(u => u.RolId).HasColumnName("rol_id");
        builder.Property(u => u.Activo)
            .HasColumnName("activo")
            .HasColumnType("tinyint(1)")
            .HasDefaultValue(true);

        builder.Property(u => u.UltimoLogin)
            .HasColumnName("ultimo_login")
            .HasColumnType("datetime");

        builder.Property(u => u.IntentosFallidos)
            .HasColumnName("intentos_fallidos")
            .HasColumnType("smallint unsigned")
            .HasDefaultValue(0);

        builder.Property(u => u.BloqueadoHasta)
            .HasColumnName("bloqueado_hasta")
            .HasColumnType("datetime");

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

        // FK a roles
        builder.HasOne(u => u.Rol)
            .WithMany()
            .HasForeignKey(u => u.RolId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_usuarios_rol");

        // Self-referencing FK created_by / updated_by
        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(u => u.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_usuarios_created_by");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(u => u.UpdatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_usuarios_updated_by");

        builder.HasIndex(u => u.Username)
            .IsUnique()
            .HasDatabaseName("uq_usuarios_username");

        builder.HasIndex(u => u.RolId).HasDatabaseName("idx_usuarios_rol");
        builder.HasIndex(u => u.DeletedAt).HasDatabaseName("idx_usuarios_deleted_at");

        builder.HasQueryFilter(u => u.DeletedAt == null);
    }
}
