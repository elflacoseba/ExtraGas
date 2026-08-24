using ExtraGasMVC.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations;

public class MovimientoGarrafaConfiguration : IEntityTypeConfiguration<MovimientoGarrafa>
{
    public void Configure(EntityTypeBuilder<MovimientoGarrafa> builder)
    {
        builder.ToTable("movimientos_garrafa");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.GarrafaId).HasColumnName("garrafa_id");

        builder.Property(m => m.Fecha)
            .HasColumnName("fecha")
            .HasColumnType("datetime")
            .IsRequired();

        builder.Property(m => m.TipoMovimientoId).HasColumnName("tipo_movimiento_id");
        builder.Property(m => m.PedidoId).HasColumnName("pedido_id");
        builder.Property(m => m.RecepcionId).HasColumnName("recepcion_id");
        builder.Property(m => m.ClienteId).HasColumnName("cliente_id");
        builder.Property(m => m.EstadoOrigenId).HasColumnName("estado_origen_id");
        builder.Property(m => m.EstadoDestinoId).HasColumnName("estado_destino_id");
        builder.Property(m => m.EmpleadoId).HasColumnName("empleado_id");

        builder.Property(m => m.Observaciones)
            .HasColumnName("observaciones")
            .HasColumnType("text");

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetime")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(m => m.CreatedBy).HasColumnName("created_by");

        builder.HasOne(m => m.Garrafa)
            .WithMany()
            .HasForeignKey(m => m.GarrafaId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_mov_garrafa_garrafa");

        builder.HasOne(m => m.TipoMovimiento)
            .WithMany()
            .HasForeignKey(m => m.TipoMovimientoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_mov_garrafa_tipo");

        builder.HasOne(m => m.Pedido)
            .WithMany()
            .HasForeignKey(m => m.PedidoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_mov_garrafa_pedido");

        builder.HasOne(m => m.Recepcion)
            .WithMany()
            .HasForeignKey(m => m.RecepcionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_mov_garrafa_recepcion");

        builder.HasOne(m => m.Cliente)
            .WithMany()
            .HasForeignKey(m => m.ClienteId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_mov_garrafa_cliente");

        builder.HasOne(m => m.EstadoOrigen)
            .WithMany()
            .HasForeignKey(m => m.EstadoOrigenId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_mov_garrafa_estado_origen");

        builder.HasOne(m => m.EstadoDestino)
            .WithMany()
            .HasForeignKey(m => m.EstadoDestinoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_mov_garrafa_estado_destino");

        builder.HasOne(m => m.Empleado)
            .WithMany()
            .HasForeignKey(m => m.EmpleadoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_mov_garrafa_empleado");

        builder.HasOne(m => m.CreatedByUsuario)
            .WithMany()
            .HasForeignKey(m => m.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_mov_garrafa_created_by");

        builder.HasIndex(m => new { m.GarrafaId, m.Fecha }).HasDatabaseName("idx_mov_garrafa_garrafa");
        builder.HasIndex(m => m.Fecha).HasDatabaseName("idx_mov_garrafa_fecha");
        builder.HasIndex(m => m.PedidoId).HasDatabaseName("idx_mov_garrafa_pedido");
        builder.HasIndex(m => m.RecepcionId).HasDatabaseName("idx_mov_garrafa_recepcion");
        builder.HasIndex(m => m.TipoMovimientoId).HasDatabaseName("idx_mov_garrafa_tipo");
    }
}
