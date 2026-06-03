using ExtraGasMVC.Data.Entities.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraGasMVC.Data.Configurations.Views;

public class VSaldoProveedorConfiguration : IEntityTypeConfiguration<VSaldoProveedor>
{
    public void Configure(EntityTypeBuilder<VSaldoProveedor> builder)
    {
        builder.ToView("v_saldo_proveedores");
        builder.HasNoKey();

        builder.Property(v => v.ProveedorId).HasColumnName("proveedor_id");
        builder.Property(v => v.RazonSocial).HasColumnName("razon_social").HasMaxLength(150);
        builder.Property(v => v.Cuit).HasColumnName("cuit").HasMaxLength(15);
        builder.Property(v => v.RecepcionesPendientes).HasColumnName("recepciones_pendientes");
        builder.Property(v => v.SaldoTotal).HasColumnName("saldo_total").HasPrecision(42, 2);
    }
}
