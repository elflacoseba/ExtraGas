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
    }
}
