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

        // Pedido mappings
        CreateMap<Pedido, PedidoDto>().ReverseMap();
        CreateMap<CreatePedidoDto, Pedido>();
        CreateMap<UpdatePedidoDto, Pedido>();

        // Producto mappings
        CreateMap<Producto, ProductoDto>().ReverseMap();
        CreateMap<CreateProductoDto, Producto>();
        CreateMap<UpdateProductoDto, Producto>();

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
