using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

/// <summary>
/// Configuración EF para <see cref="AuditLogEntry"/> (issue #147 slice 2).
/// Mapea a la tabla <c>audit_log</c> creada por la migración
/// <c>20260901_000001_create_audit_log.sql</c>.
///
/// <para>La tabla es append-only: NO se configura ningún FK ni navegación
/// (ni a la entidad auditada ni a <c>usuarios</c>). La auditoría debe
/// sobrevivir bajas de ambas. La convención del resto del proyecto
/// (mapeo manual snake_case) se aplica acá también.</para>
/// </summary>
public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.Entidad)
            .HasColumnName("entidad")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.RegistroId).HasColumnName("registro_id");

        builder.Property(e => e.Campo)
            .HasColumnName("campo")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.ValorAnterior)
            .HasColumnName("valor_anterior")
            .HasColumnType("text");

        builder.Property(e => e.ValorNuevo)
            .HasColumnName("valor_nuevo")
            .HasColumnType("text");

        builder.Property(e => e.UserId).HasColumnName("user_id");

        builder.Property(e => e.ChangedAt)
            .HasColumnName("changed_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // Índices: los crea la migración SQL; los reproducimos acá para que
        // EF los incluya en el modelo (necesario para EnsureCreated y para
        // el cache de metadatos de las queries).
        builder.HasIndex(e => new { e.Entidad, e.RegistroId, e.ChangedAt })
            .HasDatabaseName("idx_audit_entidad_registro");
        builder.HasIndex(e => e.ChangedAt)
            .HasDatabaseName("idx_audit_changed_at");
    }
}
