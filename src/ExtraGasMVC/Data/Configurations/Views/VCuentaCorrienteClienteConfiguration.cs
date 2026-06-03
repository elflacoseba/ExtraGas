using ExtraGasMVC.Data.Entities.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations.Views;

public class VCuentaCorrienteClienteConfiguration : IEntityTypeConfiguration<VCuentaCorrienteCliente>
{
    public void Configure(EntityTypeBuilder<VCuentaCorrienteCliente> builder)
    {
        builder.ToView("v_cuenta_corriente_cliente");
        builder.HasNoKey();

        builder.Property(v => v.ClienteId).HasColumnName("cliente_id");
        builder.Property(v => v.Cliente).HasColumnName("cliente").HasMaxLength(201);
        builder.Property(v => v.PedidoId).HasColumnName("pedido_id");
        builder.Property(v => v.Comprobante).HasColumnName("comprobante").HasMaxLength(20);
        builder.Property(v => v.Fecha).HasColumnName("fecha").HasColumnType("datetime");
        builder.Property(v => v.TipoMovimiento).HasColumnName("tipo_movimiento").HasMaxLength(7);
        builder.Property(v => v.Debe).HasColumnName("debe").HasPrecision(12, 2);
        builder.Property(v => v.Haber).HasColumnName("haber").HasPrecision(12, 2);
        builder.Property(v => v.Observaciones).HasColumnName("observaciones").HasColumnType("text");
    }
}
