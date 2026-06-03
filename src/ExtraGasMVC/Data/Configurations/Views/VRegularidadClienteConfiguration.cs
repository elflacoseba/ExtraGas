using ExtraGasMVC.Data.Entities.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations.Views;

public class VRegularidadClienteConfiguration : IEntityTypeConfiguration<VRegularidadCliente>
{
    public void Configure(EntityTypeBuilder<VRegularidadCliente> builder)
    {
        builder.ToView("v_regularidad_clientes");
        builder.HasNoKey();

        builder.Property(v => v.ClienteId).HasColumnName("cliente_id");
        builder.Property(v => v.Cliente).HasColumnName("cliente").HasMaxLength(201);
        builder.Property(v => v.TotalPedidos).HasColumnName("total_pedidos");
        builder.Property(v => v.UltimoPedido).HasColumnName("ultimo_pedido").HasColumnType("datetime");
        builder.Property(v => v.PrimerPedido).HasColumnName("primer_pedido").HasColumnType("datetime");
        builder.Property(v => v.DiasPromedioEntrePedidos).HasColumnName("dias_promedio_entre_pedidos").HasColumnType("double");
        builder.Property(v => v.TotalFacturado).HasColumnName("total_facturado").HasPrecision(42, 2);
        builder.Property(v => v.SaldoPendiente).HasColumnName("saldo_pendiente").HasPrecision(42, 2);
    }
}
