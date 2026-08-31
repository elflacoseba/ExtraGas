using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="PedidoEstadoHistorico"/>.
/// Tabla append-only: sin soft-delete (no <c>HasQueryFilter</c>), sin
/// <c>UpdatedAt</c>. Los FKs usan <c>RESTRICT</c> porque no queremos
/// perder histórico si se intenta borrar un pedido, estado o usuario
/// referenciado.
///
/// El índice <c>idx_peh_pedido_created</c> cubre la query más frecuente:
/// "SELECT ... WHERE pedido_id = ? ORDER BY created_at DESC" que alimenta
/// la timeline de Details y el endpoint <c>/Pedidos/{id}/historial-estados</c>.
/// </summary>
public class PedidoEstadoHistoricoConfiguration : IEntityTypeConfiguration<PedidoEstadoHistorico>
{
    public void Configure(EntityTypeBuilder<PedidoEstadoHistorico> builder)
    {
        builder.ToTable("pedido_estados_historico");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).HasColumnName("id");

        builder.Property(h => h.PedidoId).HasColumnName("pedido_id");
        builder.Property(h => h.EstadoAnteriorId).HasColumnName("estado_anterior_id");
        builder.Property(h => h.EstadoNuevoId).HasColumnName("estado_nuevo_id");
        builder.Property(h => h.MotivoCancelacion)
            .HasColumnName("motivo_cancelacion")
            .HasMaxLength(500);
        builder.Property(h => h.UsuarioId).HasColumnName("usuario_id");

        builder.Property(h => h.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(h => h.Pedido)
            .WithMany()
            .HasForeignKey(h => h.PedidoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_peh_pedido");

        builder.HasOne(h => h.EstadoAnterior)
            .WithMany()
            .HasForeignKey(h => h.EstadoAnteriorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_peh_estado_anterior");

        builder.HasOne(h => h.EstadoNuevo)
            .WithMany()
            .HasForeignKey(h => h.EstadoNuevoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_peh_estado_nuevo");

        builder.HasOne(h => h.Usuario)
            .WithMany()
            .HasForeignKey(h => h.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_peh_usuario");

        builder.HasIndex(h => new { h.PedidoId, h.CreatedAt })
            .HasDatabaseName("idx_peh_pedido_created");
    }
}