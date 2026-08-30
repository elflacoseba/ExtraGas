using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de unidad del DbSet <see cref="ProductoPrecioHistorico"/> contra
/// EFC.InMemory. Verifican que el entity POCO + la
/// <see cref="Data.Configurations.ProductoPrecioHistoricoConfiguration"/> están
/// registrados en el modelo de EF y que la inserción/lectura funciona con el
/// estado append-only esperado (sin soft-delete, sin UpdatedAt).
///
/// Slice 1 (#145): DB foundation. La tabla real se valida con Testcontainers en
/// <see cref="ProductoPrecioHistoricoIntegrationTests"/>. Estos tests cubren la
/// superficie de la entity POCO que la app va a usar desde
/// <c>ProductoService.UpdateAsync</c> (Slice 3).
/// </summary>
public class ProductoPrecioHistoricoEntityTests
{
    private static ExtraGasDbContext NewContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExtraGasDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new ExtraGasDbContext(options);
    }

    [Fact]
    public async Task DbSet_ProductoPreciosHistorico_ExpuestoEnExtraGasDbContext()
    {
        // El DbSet debe existir como propiedad pública en el DbContext para
        // que el Service pueda inyectar filas nuevas con _context.ProductoPreciosHistorico.Add(...).
        using var context = NewContext(nameof(DbSet_ProductoPreciosHistorico_ExpuestoEnExtraGasDbContext));
        var tipo = typeof(ExtraGasDbContext);
        var prop = tipo.GetProperty("ProductoPreciosHistorico");
        prop.Should().NotBeNull("ProductoPreciosHistorico DbSet debe estar declarado en ExtraGasDbContext");
        prop!.PropertyType.Should().Be<DbSet<ProductoPrecioHistorico>>(
            "la propiedad debe tiparse como DbSet<ProductoPrecioHistorico>");
    }

    [Fact]
    public async Task AddAsync_PersisteFilaYReleeaConMismasPropiedades()
    {
        // El hook de Slice 3 va a hacer exactamente esto: agregar una fila con
        // los valores del cambio y leerla de vuelta para auditoría.
        using var context = NewContext(nameof(AddAsync_PersisteFilaYReleeaConMismasPropiedades));

        var ahora = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        var fila = new ProductoPrecioHistorico
        {
            ProductoId = 42,
            PrecioAnterior = 1000m,
            PrecioNuevo = 1200m,
            MotivoCambioPrecio = "Ajuste por inflacion",
            ChangedBy = 1,
            ChangedAt = ahora,
        };

        context.ProductoPreciosHistorico.Add(fila);
        await context.SaveChangesAsync();

        var leida = await context.ProductoPreciosHistorico
            .AsNoTracking()
            .FirstAsync(p => p.ProductoId == 42);
        leida.PrecioAnterior.Should().Be(1000m);
        leida.PrecioNuevo.Should().Be(1200m);
        leida.MotivoCambioPrecio.Should().Be("Ajuste por inflacion");
        leida.ChangedBy.Should().Be(1);
        leida.ChangedAt.Should().Be(ahora);
    }

    [Fact]
    public async Task AddAsync_MotivoCambioPrecioNullYChangedByNull_SonValidos()
    {
        // El spec permite motivo NULL (operator no escribió motivo) y changed_by
        // NULL (cambio del sistema, no del operador). Estos dos casos deben
        // persistir sin lanzar.
        using var context = NewContext(nameof(AddAsync_MotivoCambioPrecioNullYChangedByNull_SonValidos));

        context.ProductoPreciosHistorico.Add(new ProductoPrecioHistorico
        {
            ProductoId = 7,
            PrecioAnterior = 500m,
            PrecioNuevo = 600m,
            MotivoCambioPrecio = null,
            ChangedBy = null,
            ChangedAt = DateTime.UtcNow,
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        var filas = await context.ProductoPreciosHistorico.AsNoTracking().ToListAsync();
        filas.Should().HaveCount(1);
        filas[0].MotivoCambioPrecio.Should().BeNull();
        filas[0].ChangedBy.Should().BeNull();
    }

    [Fact]
    public void Entity_NoExponeDeletedAtNiUpdatedAt_AppendOnly()
    {
        // El spec exige append-only: sin soft-delete, sin updated_at. Lo
        // verificamos a nivel POCO para que un refactor futuro no agregue esas
        // propiedades por accidente.
        var tipo = typeof(ProductoPrecioHistorico);
        tipo.GetProperty("DeletedAt").Should().BeNull(
            "ProductoPrecioHistorico es append-only y NO debe tener DeletedAt");
        tipo.GetProperty("UpdatedAt").Should().BeNull(
            "ProductoPrecioHistorico es append-only y NO debe tener UpdatedAt");
        tipo.GetProperty("UpdatedBy").Should().BeNull(
            "append-only: no hay Update — solo ChangedBy al insertar");
    }
}
