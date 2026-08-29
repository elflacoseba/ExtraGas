using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class ClienteConfiguration : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> builder)
    {
        builder.ToTable("clientes");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.Codigo)
            .HasColumnName("codigo")
            .HasMaxLength(20);

        builder.Property(c => c.Nombre)
            .HasColumnName("nombre")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.Apellido)
            .HasColumnName("apellido")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.Dni)
            .HasColumnName("dni")
            .HasMaxLength(15);

        builder.Property(c => c.CuitCuil)
            .HasColumnName("cuit_cuil")
            .HasMaxLength(15);

        builder.Property(c => c.TelefonoPrincipal)
            .HasColumnName("telefono_principal")
            .HasMaxLength(25)
            .IsRequired();

        builder.Property(c => c.TelefonoSecundario)
            .HasColumnName("telefono_secundario")
            .HasMaxLength(25);

        builder.Property(c => c.Email)
            .HasColumnName("email")
            .HasMaxLength(150);

        builder.Property(c => c.Calle).HasColumnName("calle").HasMaxLength(150);
        builder.Property(c => c.Numero).HasColumnName("numero").HasMaxLength(10);
        builder.Property(c => c.Piso).HasColumnName("piso").HasMaxLength(10);
        builder.Property(c => c.Depto).HasColumnName("depto").HasMaxLength(10);
        builder.Property(c => c.Ciudad).HasColumnName("ciudad").HasMaxLength(100);
        builder.Property(c => c.CodigoPostal).HasColumnName("codigo_postal").HasMaxLength(10);
        builder.Property(c => c.ProvinciaId).HasColumnName("provincia_id");

        builder.Property(c => c.Referencias).HasColumnName("referencias").HasColumnType("text");
        builder.Property(c => c.Observaciones).HasColumnName("observaciones").HasColumnType("text");

        builder.Property(c => c.FechaAlta)
            .HasColumnName("fecha_alta")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(c => c.Activo)
            .HasColumnName("activo")
            .HasColumnType("tinyint(1)")
            .HasDefaultValue(true);

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(c => c.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAddOrUpdate()
            .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

        builder.Property(c => c.CreatedBy).HasColumnName("created_by");
        builder.Property(c => c.UpdatedBy).HasColumnName("updated_by");
        builder.Property(c => c.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("datetime");

        // FKs
        builder.HasOne<Provincia>()
            .WithMany()
            .HasForeignKey(c => c.ProvinciaId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clientes_provincia");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(c => c.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clientes_created_by");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(c => c.UpdatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clientes_updated_by");

        builder.HasIndex(c => new { c.Apellido, c.Nombre }).HasDatabaseName("idx_clientes_apellido");
        builder.HasIndex(c => c.TelefonoPrincipal).HasDatabaseName("idx_clientes_telefono");
        // Issue #105: la unicidad del DNI entre clientes ACTIVOS la gestiona una columna
        // VIRTUAL `dni_unique` + UNIQUE INDEX en BD (migración 20260829_000001). EF no modela
        // columnas generadas VIRTUAL; el HasIndex siguiente documenta la intención
        // (índice sobre dni filtrado por soft-delete) sin pretender replicar la semántica
        // — el índice real es idx_clientes_dni_unique y vive solo a nivel BD.
        builder.HasIndex(c => c.Dni)
            .HasDatabaseName("idx_clientes_dni_lookup")
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(c => c.Codigo).HasDatabaseName("idx_clientes_codigo");
        builder.HasIndex(c => c.DeletedAt).HasDatabaseName("idx_clientes_deleted_at");

        builder.HasQueryFilter(c => c.DeletedAt == null);
    }
}
