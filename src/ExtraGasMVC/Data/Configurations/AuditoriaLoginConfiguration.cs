using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class AuditoriaLoginConfiguration : IEntityTypeConfiguration<AuditoriaLogin>
{
    public void Configure(EntityTypeBuilder<AuditoriaLogin> builder)
    {
        builder.ToTable("auditoria_logins");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.UsernameIntentado)
            .HasColumnName("username_intentado")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.UsuarioId).HasColumnName("usuario_id");
        builder.Property(a => a.Exito)
            .HasColumnName("exito")
            .HasColumnType("tinyint(1)");

        builder.Property(a => a.MotivoFallo)
            .HasColumnName("motivo_fallo")
            .HasMaxLength(20);

        builder.Property(a => a.IpOrigen)
            .HasColumnName("ip_origen")
            .HasMaxLength(45);

        builder.Property(a => a.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(255);

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(a => a.Usuario)
            .WithMany()
            .HasForeignKey(a => a.UsuarioId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_auditoria_logins_usuario");

        builder.HasIndex(a => a.CreatedAt).HasDatabaseName("idx_auditoria_logins_created_at");
        builder.HasIndex(a => a.UsuarioId).HasDatabaseName("idx_auditoria_logins_usuario_id");
        builder.HasIndex(a => a.IpOrigen).HasDatabaseName("idx_auditoria_logins_ip_origen");
    }
}
