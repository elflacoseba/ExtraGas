using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Data.Entities.Views;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Data.Context;

public class ExtraGasDbContext : DbContext
{
    public ExtraGasDbContext(DbContextOptions<ExtraGasDbContext> options) : base(options) { }

    // ============== Lookups ==============
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<TipoProducto> TiposProducto => Set<TipoProducto>();
    public DbSet<FormaPago> FormasPago => Set<FormaPago>();
    public DbSet<EstadoPedido> EstadosPedido => Set<EstadoPedido>();
    public DbSet<EstadoGarrafa> EstadosGarrafa => Set<EstadoGarrafa>();
    public DbSet<TipoMovimientoGarrafa> TiposMovimientoGarrafa => Set<TipoMovimientoGarrafa>();
    public DbSet<CanalVenta> CanalesVenta => Set<CanalVenta>();
    public DbSet<MedioContactoPedido> MediosContactoPedido => Set<MedioContactoPedido>();
    public DbSet<TipoContactoCliente> TiposContactoCliente => Set<TipoContactoCliente>();
    public DbSet<Provincia> Provincias => Set<Provincia>();
    public DbSet<Secuencia> Secuencias => Set<Secuencia>();

    // ============== Personas y seguridad ==============
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Empleado> Empleados => Set<Empleado>();
    public DbSet<AuditoriaLogin> AuditoriaLogins => Set<AuditoriaLogin>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<ClienteContacto> ClienteContactos => Set<ClienteContacto>();
    public DbSet<Proveedor> Proveedores => Set<Proveedor>();

    // ============== Productos y catálogo ==============
    public DbSet<Producto> Productos => Set<Producto>();

    // ============== Pedidos y pagos ==============
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<PedidoItem> PedidoItems => Set<PedidoItem>();
    public DbSet<Pago> Pagos => Set<Pago>();

    // ============== Recepciones y pagos a proveedor ==============
    public DbSet<RecepcionProveedor> RecepcionesProveedor => Set<RecepcionProveedor>();
    public DbSet<RecepcionItem> RecepcionItems => Set<RecepcionItem>();
    public DbSet<PagoProveedor> PagosProveedor => Set<PagoProveedor>();

    // ============== Garrafas ==============
    public DbSet<Garrafa> Garrafas => Set<Garrafa>();
    public DbSet<MovimientoGarrafa> MovimientosGarrafa => Set<MovimientoGarrafa>();

    // ============== Views (read-only) ==============
    public DbSet<VPedidoResumen> VPedidosResumen => Set<VPedidoResumen>();
    public DbSet<VProductoMasVendido> VProductosMasVendidos => Set<VProductoMasVendido>();
    public DbSet<VRegularidadCliente> VRegularidadClientes => Set<VRegularidadCliente>();
    public DbSet<VSaldoCliente> VSaldosClientes => Set<VSaldoCliente>();
    public DbSet<VStockGarrafa> VStockGarrafas => Set<VStockGarrafa>();
    public DbSet<VGarrafaEnCliente> VGarrafasEnClientes => Set<VGarrafaEnCliente>();
    public DbSet<VPagoPorFormaPago> VPagosPorFormaPago => Set<VPagoPorFormaPago>();
    public DbSet<VCuentaCorrienteCliente> VCuentaCorrienteClientes => Set<VCuentaCorrienteCliente>();
    public DbSet<VSaldoProveedor> VSaldosProveedores => Set<VSaldoProveedor>();
    public DbSet<VRecepcionResumen> VRecepcionesResumen => Set<VRecepcionResumen>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExtraGasDbContext).Assembly);
    }
}
