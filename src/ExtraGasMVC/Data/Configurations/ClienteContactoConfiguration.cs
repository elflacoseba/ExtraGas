using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class ClienteContactoConfiguration : IEntityTypeConfiguration<ClienteContacto>
{
    public void Configure(EntityTypeBuilder<ClienteContacto> builder)
    {
        builder.ToTable("cliente_contactos");

        builder.HasKey(cc => cc.Id);
        builder.Property(cc => cc.Id).HasColumnName("id");

        builder.Property(cc => cc.ClienteId).HasColumnName("cliente_id");
        builder.Property(cc => cc.TipoContactoId).HasColumnName("tipo_contacto_id");
        builder.Property(cc => cc.Valor)
            .HasColumnName("valor")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(cc => cc.EsPrincipal)
            .HasColumnName("es_principal")
            .HasColumnType("tinyint(1)")
            .HasDefaultValue(false);

        builder.Property(cc => cc.Observaciones)
            .HasColumnName("observaciones")
            .HasMaxLength(255);

        builder.Property(cc => cc.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(cc => cc.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAddOrUpdate()
            .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(cc => cc.ClienteId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_cliente_contactos_cliente");

        builder.HasOne<TipoContactoCliente>()
            .WithMany()
            .HasForeignKey(cc => cc.TipoContactoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_cliente_contactos_tipo");

        builder.HasIndex(cc => cc.ClienteId).HasDatabaseName("idx_cliente_contactos_cliente");
        builder.HasIndex(cc => cc.TipoContactoId).HasDatabaseName("idx_cliente_contactos_tipo");
    }
}
