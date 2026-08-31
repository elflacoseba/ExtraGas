using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Implementations;
using ExtraGasMVC.Services.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de regresión para los bugs de integridad de datos descubiertos en
/// issue #145:
///   - <c>RecepcionService.LoadProductosByIdAsync</c> aceptaba productos con
///     <c>Activo = false</c>, dejándolos pasar al cuerpo de
///     <c>CreateAsync</c> y ensuciando el inventario.
///   - El path de validación pre-commit <c>ValidarItemsPreCommitAsync</c>
///     rechazaba con un mensaje que ya decía "no existe o está inactivo" pero
///     que no se cumplía porque el filtro SQL no excluía los inactivos.
///
/// Patrón: tests end-to-end contra <c>CreateAsync</c> (no Reflection) usando
/// EFC.InMemory. Cada test ejercita el camino público que el usuario
/// gatillaría desde el Controller — más robusto frente a refactors internos
/// que invocar métodos privados por reflection.
/// </summary>
public class RecepcionServiceTests
{
    private static (RecepcionService service, ExtraGasDbContext context) NewService(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            // El happy path del test abre transacción con InMemory → warning
            // que termina en excepción. Suprimirla explícitamente para que el
            // path real se ejercite sin ruido.
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new ExtraGasDbContext(options);
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
        // El flujo bajo prueba no llega a GetProductosActivosAsync (se ejecuta
        // antes de LoadProductosByIdAsync), pero el constructor exige el
        // interface. NotImplementedIProductoService lanza si algo lo invoca —
        // falla ruidosa si cambia el orden de validación.
        var productoService = new NotImplementedIProductoService();
        return (new RecepcionService(context, productoService), context);
    }

    /// <summary>
    /// Siembra los catálogos mínimos + un empleado + un proveedor para
    /// que <see cref="RecepcionService.CreateAsync"/> supere las
    /// pre-validaciones que anteceden a LoadProductosByIdAsync.
    /// </summary>
    private static async Task<(ulong empleadoId, ulong proveedorId)> SeedCatalogosAsync(ExtraGasDbContext context)
    {
        var now = DateTime.UtcNow;

        var tipoProducto = new TipoProducto { Codigo = "GAS", Nombre = "Gas" };
        context.TiposProducto.Add(tipoProducto);

        var llenaDeposito = new EstadoGarrafa
        {
            Codigo = "LLENA_DEPOSITO",
            Nombre = "Llena en depósito",
            EsDisponibleParaVenta = true,
            RequiereCliente = false,
        };
        context.EstadosGarrafa.Add(llenaDeposito);

        var tipoCompra = new TipoMovimientoGarrafa { Codigo = "COMPRA", Nombre = "Compra" };
        context.TiposMovimientoGarrafa.Add(tipoCompra);

        var proveedor = new Proveedor
        {
            RazonSocial = "Distribuidora Test",
            Cuit = "20-12345678-9",
            Activo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.Proveedores.Add(proveedor);

        var empleado = new Empleado
        {
            Nombre = "Juan",
            Apellido = "Operador",
            UsuarioId = 1,
            Activo = true,
            FechaIngreso = DateOnly.FromDateTime(now),
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.Empleados.Add(empleado);

        await context.SaveChangesAsync();
        return (empleado.Id, proveedor.Id);
    }

    /// <summary>
    /// Crea una CrearRecepcionDto mínima con un solo item (carbón/leña para
    /// evitar tracking de garrafas — el bug bajo prueba está antes de ese
    /// branch). Subtotal = precio * cantidad, descuento = 0.
    /// </summary>
    private static CrearRecepcionDto NewDto(
        ulong productoId, ulong proveedorId, decimal cantidad = 1m, decimal precioUnitario = 1000m)
    {
        return new CrearRecepcionDto
        {
            Fecha = DateTime.UtcNow,
            ProveedorId = proveedorId,
            Subtotal = cantidad * precioUnitario,
            Descuento = 0m,
            Items = new List<CrearRecepcionItemDto>
            {
                new()
                {
                    ProductoId = productoId,
                    Cantidad = cantidad,
                    PrecioUnitario = precioUnitario,
                },
            },
        };
    }

    // ====================================================================
    // Issue #145 Slice 4: integridad RecepcionService filtro && p.Activo
    // ====================================================================

    [Fact]
    public async Task CreateAsync_ProductoConActivoFalse_RechazaConInvalidOperationException()
    {
        // Issue #145 — Tarea 4.1/4.2 RED: el filtro SQL faltante
        // (&& p.Activo) permitía que un producto desactivado llegara al
        // dictionary de LoadProductosByIdAsync, evitando que
        // ValidarItemsPreCommitAsync detectara la inconsistencia. El test
        // verifica el comportamiento observable: el usuario recibe un
        // InvalidOperationException claro y nada persiste.
        var (service, context) = NewService(nameof(CreateAsync_ProductoConActivoFalse_RechazaConInvalidOperationException));
        var (_, proveedorId) = await SeedCatalogosAsync(context);

        // Seed: 3 productos activos + 1 con Activo=false (el malo).
        var ahora = DateTime.UtcNow;
        var activos = Enumerable.Range(1, 3).Select(i => new Producto
        {
            Codigo = $"GAS-ACT-{i}",
            Nombre = $"Garrafa Activa {i}",
            TipoProductoId = 1,
            UnidadVenta = "UNIDAD",
            PrecioActual = 1500m,
            ManejaGarrafaIndividual = false,
            Activo = true,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        }).ToList();
        var inactivo = new Producto
        {
            Codigo = "GAS-INACTIVO",
            Nombre = "Producto Desactivado",
            TipoProductoId = 1,
            UnidadVenta = "UNIDAD",
            PrecioActual = 1500m,
            ManejaGarrafaIndividual = false,
            Activo = false, // <-- la condición bajo prueba
            CreatedAt = ahora,
            UpdatedAt = ahora,
        };
        context.Productos.AddRange(activos);
        context.Productos.Add(inactivo);
        await context.SaveChangesAsync();

        // Submit con el item apuntando al producto desactivado.
        var dto = NewDto(inactivo.Id, proveedorId);

        var act = async () => await service.CreateAsync(dto, usuarioId: 1);

        // El throw debe mencionar el id (o nombre) del producto desactivado
        // — el comentario línea 148 de RecepcionService dice textualmente
        // "no existe o está inactivo". Filtramos por id para ser robustos.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{inactivo.Id}*");

        // La recepción NO se persistió.
        context.ChangeTracker.Clear();
        (await context.RecepcionesProveedor.CountAsync()).Should().Be(0,
            "el rechazo debe ocurrir ANTES de la transacción");
    }

    [Fact]
    public async Task CreateAsync_ProductoSoftDeleted_RechazaConInvalidOperationException()
    {
        // El QueryFilter global oculta soft-deleted, así que este caso
        // funciona como una doble validación: el filtro global los excluye
        // del dictionary (porque DeletedAt != null), y entonces
        // ValidarItemsPreCommitAsync dispara el mismo throw. El test
        // verifica el comportamiento simétrico al producto desactivado.
        var (service, context) = NewService(nameof(CreateAsync_ProductoSoftDeleted_RechazaConInvalidOperationException));
        var (_, proveedorId) = await SeedCatalogosAsync(context);

        var ahora = DateTime.UtcNow;
        var softDeleted = new Producto
        {
            Codigo = "GAS-DELETED",
            Nombre = "Producto Borrado",
            TipoProductoId = 1,
            UnidadVenta = "UNIDAD",
            PrecioActual = 1500m,
            ManejaGarrafaIndividual = false,
            Activo = false,
            CreatedAt = ahora,
            UpdatedAt = ahora,
            DeletedAt = ahora, // <-- la condición bajo prueba
        };
        context.Productos.Add(softDeleted);
        await context.SaveChangesAsync();

        var dto = NewDto(softDeleted.Id, proveedorId);

        var act = async () => await service.CreateAsync(dto, usuarioId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{softDeleted.Id}*");
    }

    [Fact]
    public async Task CreateAsync_TodosProductosActivos_NoRechazaPorProducto()
    {
        // Tarea 4.1 — happy path / triangulación: con todos los productos
        // activos, la validación pre-commit NO rechaza por producto. El test
        // espera que CreateAsync NO tire por motivo de producto (puede tirar
        // por motivos posteriores — ej. faltan catalogos — pero esos ya
        // están seedeados en SeedCatalogosAsync).
        var (service, context) = NewService(nameof(CreateAsync_TodosProductosActivos_NoRechazaPorProducto));
        var (_, proveedorId) = await SeedCatalogosAsync(context);

        var ahora = DateTime.UtcNow;
        var producto = new Producto
        {
            Codigo = "GAS-OK",
            Nombre = "Garrafa OK",
            TipoProductoId = 1,
            UnidadVenta = "UNIDAD",
            PrecioActual = 1500m,
            ManejaGarrafaIndividual = false, // carbón/leña: no entra al branch de garrafas
            Activo = true,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        };
        context.Productos.Add(producto);
        await context.SaveChangesAsync();

        var dto = NewDto(producto.Id, proveedorId);

        var act = async () => await service.CreateAsync(dto, usuarioId: 1);

        await act.Should().NotThrowAsync(
            "ningún producto está desactivado ni soft-deleted; no hay razón para rechazar");
    }

    /// <summary>
    /// Stub de <see cref="IProductoService"/> que lanza si algo lo invoca
    /// durante el flujo bajo prueba. Los tests de esta clase NO ejercitan
    /// <c>GetProductosActivosAsync</c>, así que cualquier invocación es una
    /// falla ruidosa (señal de refactor accidental).
    /// </summary>
    private sealed class NotImplementedIProductoService : IProductoService
    {
        public Task<ProductoDto?> GetByIdAsync(ulong id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetActivosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoDto>> GetByTipoAsync(ulong tipoProductoId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<TipoProductoDto>> GetTiposProductoAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto> CreateAsync(CreateProductoDto producto, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProductoDto> UpdateAsync(UpdateProductoDto producto, ulong? usuarioId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(ulong id, ulong? updatedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ProductoDto>> GetPagedAsync(string? busqueda, bool soloActivos, int page, int pageSize, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
