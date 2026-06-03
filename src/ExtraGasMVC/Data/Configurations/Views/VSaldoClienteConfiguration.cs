using ExtraGasMVC.Data.Entities.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations.Views;

public class VSaldoClienteConfiguration : IEntityTypeConfiguration<VSaldoCliente>
{
    public void Configure(EntityTypeBuilder<VSaldoCliente> builder)
    {
        builder.ToView("v_saldo_clientes");
        builder.HasNoKey();

        builder.Property(v => v.ClienteId).HasColumnName("cliente_id");
        builder.Property(v => v.Cliente).HasColumnName("cliente").HasMaxLength(201);
        builder.Property(v => v.TelefonoPrincipal).HasColumnName("telefono_principal").HasMaxLength(25);
        builder.Property(v => v.PedidosPendientes).HasColumnName("pedidos_pendientes");
        builder.Property(v => v.SaldoTotal).HasColumnName("saldo_total").HasPrecision(42, 2);
    }
}
