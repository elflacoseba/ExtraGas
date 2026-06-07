using AutoMapper;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;

namespace ExtraGasMVC.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // Cliente mappings
        CreateMap<Cliente, ClienteDto>().ReverseMap();
        CreateMap<CreateClienteDto, Cliente>();
        CreateMap<UpdateClienteDto, Cliente>();
        CreateMap<ClienteDto, UpdateClienteDto>();

        // Pedido mappings
        CreateMap<Pedido, PedidoDto>()
            .ForMember(d => d.ClienteNombre, o => o.MapFrom(s =>
                s.Cliente != null ? s.Cliente.Apellido + ", " + s.Cliente.Nombre : null))
            .ForMember(d => d.EmpleadoNombre, o => o.MapFrom(s =>
                s.Empleado != null ? s.Empleado.Apellido + ", " + s.Empleado.Nombre : null))
            .ForMember(d => d.EstadoNombre, o => o.MapFrom(s =>
                s.EstadoPedido != null ? s.EstadoPedido.Nombre : null))
            .ForMember(d => d.EstadoCodigo, o => o.MapFrom(s =>
                s.EstadoPedido != null ? s.EstadoPedido.Codigo : null))
            .ForMember(d => d.EstadoColor, o => o.MapFrom(s =>
                s.EstadoPedido != null ? s.EstadoPedido.Color : null))
            .ForMember(d => d.CanalNombre, o => o.MapFrom(s =>
                s.CanalVenta != null ? s.CanalVenta.Nombre : null))
            .ForMember(d => d.MedioContactoNombre, o => o.MapFrom(s =>
                s.MedioContactoPedido != null ? s.MedioContactoPedido.Nombre : null))
            .ForMember(d => d.Items, o => o.MapFrom(s => s.Items));
        CreateMap<CreatePedidoDto, Pedido>();
        CreateMap<UpdatePedidoDto, Pedido>();

        // PedidoItem mappings
        CreateMap<PedidoItem, PedidoItemDto>()
            .ForMember(d => d.ProductoNombre, o => o.MapFrom(s =>
                s.Producto != null ? s.Producto.Nombre : null))
            .ForMember(d => d.ProductoCodigo, o => o.MapFrom(s =>
                s.Producto != null ? s.Producto.Codigo : null))
            .ForMember(d => d.TipoLinea, o => o.MapFrom(s => s.TipoLinea.ToString()));
        CreateMap<CreatePedidoItemDto, PedidoItem>()
            .ForMember(d => d.TipoLinea, o => o.MapFrom(s =>
                Enum.Parse<ExtraGasMVC.Data.Entities.Enums.TipoLinea>(s.TipoLinea)));
        CreateMap<UpdatePedidoItemDto, PedidoItem>()
            .ForMember(d => d.TipoLinea, o => o.MapFrom(s =>
                Enum.Parse<ExtraGasMVC.Data.Entities.Enums.TipoLinea>(s.TipoLinea)));

        // Lookup mappings
        CreateMap<EstadoPedido, EstadoPedidoDto>().ReverseMap();
        CreateMap<CanalVenta, CanalVentaDto>().ReverseMap();
        CreateMap<MedioContactoPedido, MedioContactoPedidoDto>().ReverseMap();

        // Producto mappings
        CreateMap<Producto, ProductoDto>()
            .ForMember(d => d.TipoProductoNombre, o => o.MapFrom(s => s.TipoProducto != null ? s.TipoProducto.Nombre : null))
            .ReverseMap();
        CreateMap<CreateProductoDto, Producto>();
        CreateMap<UpdateProductoDto, Producto>();

        // TipoProducto mappings
        CreateMap<TipoProducto, TipoProductoDto>().ReverseMap();

        // Proveedor mappings
        CreateMap<Proveedor, ProveedorDto>().ReverseMap();
        CreateMap<CreateProveedorDto, Proveedor>();
        CreateMap<UpdateProveedorDto, Proveedor>();

        // Pago mappings
        CreateMap<Pago, PagoDto>().ReverseMap();
        CreateMap<CreatePagoDto, Pago>();
        CreateMap<UpdatePagoDto, Pago>();

        // Garrafa mappings
        CreateMap<Garrafa, GarrafaDto>().ReverseMap();
        CreateMap<CreateGarrafaDto, Garrafa>();
        CreateMap<UpdateGarrafaDto, Garrafa>();

        // Usuario mappings
        CreateMap<Usuario, UsuarioDto>()
            .ForMember(d => d.RolCodigo, o => o.MapFrom(s => s.Rol != null ? s.Rol.Codigo : null))
            .ForMember(d => d.RolNombre, o => o.MapFrom(s => s.Rol != null ? s.Rol.Nombre : null));
        CreateMap<CreateUsuarioDto, Usuario>()
            .ForMember(d => d.PasswordHash, o => o.Ignore());
        CreateMap<UpdateUsuarioDto, Usuario>();

        // Provincia mappings
        CreateMap<Provincia, ProvinciaDto>();

        // Empleado mappings
        CreateMap<Empleado, EmpleadoDto>().ReverseMap();
        CreateMap<CreateEmpleadoDto, Empleado>();
        CreateMap<UpdateEmpleadoDto, Empleado>();
        CreateMap<EmpleadoDto, UpdateEmpleadoDto>();
    }
}
