using AutoMapper;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Data.Entities.Views;
using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        ConfigureCliente();
        ConfigurePedido();
        ConfigurePedidoItem();
        ConfigureLookups();
        ConfigureProducto();
        ConfigureTipoProducto();
        ConfigureProveedor();
        ConfigurePago();
        ConfigureGarrafa();
        ConfigureMovimientoGarrafa();
        ConfigureUsuario();
        ConfigureProvincia();
        ConfigureEmpleado();
        ConfigureVSaldoCliente();
    }

    // Issue #109: mapping de la vista v_saldo_clientes para evitar N+1 en
    // CuentasCorrientes. AutoMapper proyecta las 5 columnas 1:1 (mismo nombre).
    private void ConfigureVSaldoCliente()
    {
        CreateMap<VSaldoCliente, VSaldoClienteDto>().ReverseMap();
    }

    private void ConfigureCliente()
    {
        CreateMap<Cliente, ClienteDto>().ReverseMap();
        CreateMap<CreateClienteDto, Cliente>();
        CreateMap<UpdateClienteDto, Cliente>();
        CreateMap<ClienteDto, UpdateClienteDto>();
    }

    private void ConfigurePedido()
    {
        CreateMap<Pedido, PedidoDto>()
            .ForMember(d => d.ClienteNombre, o => o.MapFrom(s => NombreCompletoCliente(s)))
            .ForMember(d => d.EmpleadoNombre, o => o.MapFrom(s => NombreCompletoEmpleado(s)))
            .ForMember(d => d.EstadoNombre, o => o.MapFrom(s => NombreEstado(s)))
            .ForMember(d => d.EstadoCodigo, o => o.MapFrom(s => CodigoEstado(s)))
            .ForMember(d => d.EstadoColor, o => o.MapFrom(s => ColorEstado(s)))
            .ForMember(d => d.CanalNombre, o => o.MapFrom(s => NombreCanal(s)))
            .ForMember(d => d.MedioContactoNombre, o => o.MapFrom(s => NombreMedioContacto(s)))
            .ForMember(d => d.Items, o => o.MapFrom(s => s.Items));
        CreateMap<CreatePedidoDto, Pedido>();
        CreateMap<UpdatePedidoDto, Pedido>();
    }

    private static string? NombreCompletoCliente(Pedido s)
        => s.Cliente != null ? s.Cliente.Apellido + ", " + s.Cliente.Nombre : null;

    private static string? NombreEstado(Pedido s)
        => s.EstadoPedido != null ? s.EstadoPedido.Nombre : null;

    private static string? CodigoEstado(Pedido s)
        => s.EstadoPedido != null ? s.EstadoPedido.Codigo : null;

    private static string? ColorEstado(Pedido s)
        => s.EstadoPedido != null ? s.EstadoPedido.Color : null;

    private static string? NombreCanal(Pedido s)
        => s.CanalVenta != null ? s.CanalVenta.Nombre : null;

    private static string? NombreMedioContacto(Pedido s)
        => s.MedioContactoPedido != null ? s.MedioContactoPedido.Nombre : null;

    private void ConfigurePedidoItem()
    {
        CreateMap<PedidoItem, PedidoItemDto>()
            .ForMember(d => d.ProductoNombre, o => o.MapFrom(s =>
                s.Producto != null ? s.Producto.Nombre : null))
            .ForMember(d => d.ProductoCodigo, o => o.MapFrom(s =>
                s.Producto != null ? s.Producto.Codigo : null))
            .ForMember(d => d.ManejaGarrafaIndividual, o => o.MapFrom(s =>
                s.Producto != null && s.Producto.ManejaGarrafaIndividual))
            .ForMember(d => d.CapacidadKg, o => o.MapFrom(s =>
                s.Producto != null ? s.Producto.CapacidadKg : (decimal?)null))
            .ForMember(d => d.TipoLinea, o => o.MapFrom(s => s.TipoLinea.ToString()));
        CreateMap<CreatePedidoItemDto, PedidoItem>()
            .ForMember(d => d.TipoLinea, o => o.MapFrom(s =>
                Enum.Parse<ExtraGasMVC.Data.Entities.Enums.TipoLinea>(s.TipoLinea)));
        CreateMap<UpdatePedidoItemDto, PedidoItem>()
            .ForMember(d => d.TipoLinea, o => o.MapFrom(s =>
                Enum.Parse<ExtraGasMVC.Data.Entities.Enums.TipoLinea>(s.TipoLinea)));
    }

    private void ConfigureLookups()
    {
        CreateMap<EstadoPedido, EstadoPedidoDto>().ReverseMap();
        CreateMap<CanalVenta, CanalVentaDto>().ReverseMap();
        CreateMap<MedioContactoPedido, MedioContactoPedidoDto>().ReverseMap();
        CreateMap<EstadoGarrafa, EstadoGarrafaDto>().ReverseMap();
    }

    private void ConfigureProducto()
    {
        CreateMap<Producto, ProductoDto>()
            .ForMember(d => d.TipoProductoNombre, o => o.MapFrom(s =>
                s.TipoProducto != null ? s.TipoProducto.Nombre : null))
            .ReverseMap();
        CreateMap<CreateProductoDto, Producto>();
        CreateMap<UpdateProductoDto, Producto>();
    }

    private void ConfigureTipoProducto()
    {
        CreateMap<TipoProducto, TipoProductoDto>().ReverseMap();
    }

    private void ConfigureProveedor()
    {
        CreateMap<Proveedor, ProveedorDto>().ReverseMap();
        CreateMap<CreateProveedorDto, Proveedor>();
        CreateMap<UpdateProveedorDto, Proveedor>();
        CreateMap<ProveedorDto, UpdateProveedorDto>();
    }

    private void ConfigurePago()
    {
        CreateMap<Pago, PagoDto>().ReverseMap();
        CreateMap<CreatePagoDto, Pago>();
        CreateMap<UpdatePagoDto, Pago>();
    }

    private void ConfigureGarrafa()
    {
        CreateMap<Garrafa, GarrafaDto>()
            .ForMember(d => d.EstadoCodigo, o => o.MapFrom(s =>
                s.EstadoGarrafa != null ? s.EstadoGarrafa.Codigo : null))
            .ForMember(d => d.EstadoNombre, o => o.MapFrom(s =>
                s.EstadoGarrafa != null ? s.EstadoGarrafa.Nombre : null))
            .ForMember(d => d.EstadoColor, o => o.MapFrom(s =>
                s.EstadoGarrafa != null ? s.EstadoGarrafa.Color : null))
            .ForMember(d => d.ClienteNombre, o => o.MapFrom(s =>
                s.Cliente != null ? s.Cliente.Apellido + ", " + s.Cliente.Nombre : null))
            .ForMember(d => d.ProveedorNombre, o => o.MapFrom(s =>
                s.Proveedor != null ? s.Proveedor.RazonSocial : null))
            .ReverseMap();
        CreateMap<CreateGarrafaDto, Garrafa>();
        CreateMap<UpdateGarrafaDto, Garrafa>();
    }

    private void ConfigureMovimientoGarrafa()
    {
        CreateMap<MovimientoGarrafa, MovimientoGarrafaDto>()
            .ForMember(d => d.TipoMovimientoCodigo, o => o.MapFrom(s => CodigoTipoMovimiento(s)))
            .ForMember(d => d.TipoMovimientoNombre, o => o.MapFrom(s => NombreTipoMovimiento(s)))
            .ForMember(d => d.EstadoOrigenCodigo, o => o.MapFrom(s => CodigoEstadoOrigen(s)))
            .ForMember(d => d.EstadoOrigenNombre, o => o.MapFrom(s => NombreEstadoOrigen(s)))
            .ForMember(d => d.EstadoDestinoCodigo, o => o.MapFrom(s => CodigoEstadoDestino(s)))
            .ForMember(d => d.EstadoDestinoNombre, o => o.MapFrom(s => NombreEstadoDestino(s)))
            .ForMember(d => d.EmpleadoNombreCompleto, o => o.MapFrom(s => NombreCompletoEmpleado(s)))
            .ForMember(d => d.GarrafaCodigo, o => o.MapFrom(s => CodigoGarrafa(s)));
    }

    private static string? CodigoTipoMovimiento(MovimientoGarrafa s)
        => s.TipoMovimiento != null ? s.TipoMovimiento.Codigo : null;

    private static string? NombreTipoMovimiento(MovimientoGarrafa s)
        => s.TipoMovimiento != null ? s.TipoMovimiento.Nombre : null;

    private static string? CodigoEstadoOrigen(MovimientoGarrafa s)
        => s.EstadoOrigen != null ? s.EstadoOrigen.Codigo : null;

    private static string? NombreEstadoOrigen(MovimientoGarrafa s)
        => s.EstadoOrigen != null ? s.EstadoOrigen.Nombre : null;

    private static string? CodigoEstadoDestino(MovimientoGarrafa s)
        => s.EstadoDestino != null ? s.EstadoDestino.Codigo : null;

    private static string? NombreEstadoDestino(MovimientoGarrafa s)
        => s.EstadoDestino != null ? s.EstadoDestino.Nombre : null;

    private static string? CodigoGarrafa(MovimientoGarrafa s)
        => s.Garrafa != null ? s.Garrafa.Codigo : null;

    // Sobrecargas de NombreCompletoEmpleado agrupadas (SonarQube csharpsquid:S4136).
    // Antes estaban dispersas con ~130 lineas entre una y otra (issue #136).
    private static string? NombreCompletoEmpleado(Pedido s)
        => s.Empleado != null ? s.Empleado.Apellido + ", " + s.Empleado.Nombre : null;

    private static string? NombreCompletoEmpleado(MovimientoGarrafa s)
        => s.Empleado != null ? s.Empleado.Apellido + ", " + s.Empleado.Nombre : null;

    private void ConfigureUsuario()
    {
        CreateMap<Usuario, UsuarioDto>()
            .ForMember(d => d.RolCodigo, o => o.MapFrom(s => s.Rol != null ? s.Rol.Codigo : null))
            .ForMember(d => d.RolNombre, o => o.MapFrom(s => s.Rol != null ? s.Rol.Nombre : null));
        CreateMap<CreateUsuarioDto, Usuario>()
            .ForMember(d => d.PasswordHash, o => o.Ignore());
        CreateMap<UpdateUsuarioDto, Usuario>();
    }

    private void ConfigureProvincia()
    {
        CreateMap<Provincia, ProvinciaDto>();
    }

    private void ConfigureEmpleado()
    {
        CreateMap<Empleado, EmpleadoDto>().ReverseMap();
        CreateMap<CreateEmpleadoDto, Empleado>();
        CreateMap<UpdateEmpleadoDto, Empleado>();
        CreateMap<EmpleadoDto, UpdateEmpleadoDto>();
    }
}
