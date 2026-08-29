using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.Services.Implementations;
using FluentAssertions;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de regresión del helper de unicidad de DNI en clientes.
/// Issue #105: tras un soft-delete, el DNI debe quedar libre para re-registro.
/// La verificación end-to-end contra la columna virtual <c>dni_unique</c> vive en
/// <c>db/scripts/verify_issue_105_clientes_dni_soft_delete.sql</c>; estos tests
/// cubren la pieza de lógica que la app ejecuta antes de llegar a la BD
/// (<see cref="ClienteService.IsDniUniqueAsync(string?, CancellationToken)"/>
/// delega en <see cref="ClienteService.DniEsUnicoSobre"/>).
/// </summary>
public class ClienteDniUnicidadSoftDeleteTests
{
    [Fact]
    public void DniEsUnicoSobre_ConDniVacio_DevuelveTrue()
    {
        var clientes = new List<Cliente>
        {
            new() { Id = 1, Dni = "12345678", DeletedAt = null },
        }.AsQueryable();

        ClienteService.DniEsUnicoSobre(clientes, "   ")
            .Should().BeTrue();
    }

    [Fact]
    public void DniEsUnicoSobre_SinClientes_DevuelveTrue()
    {
        var clientes = new List<Cliente>().AsQueryable();

        ClienteService.DniEsUnicoSobre(clientes, "12345678")
            .Should().BeTrue();
    }

    [Fact]
    public void DniEsUnicoSobre_SoloClienteActivoConMismoDni_DevuelveFalse()
    {
        var clientes = new List<Cliente>
        {
            new() { Id = 1, Dni = "12345678", DeletedAt = null },
            new() { Id = 2, Dni = "99999999", DeletedAt = null },
        }.AsQueryable();

        ClienteService.DniEsUnicoSobre(clientes, "12345678")
            .Should().BeFalse();
    }

    /// <summary>
    /// Caso central de la issue #105: la query ya viene con el QueryFilter global
    /// aplicado (DeletedAt == null), por lo que el cliente soft-deleted con el
    /// mismo DNI no aparece en la lista. El helper debe reportar "único" y permitir
    /// que la app llegue al INSERT — que en la BD pasa gracias a la columna virtual
    /// <c>dni_unique</c> (válidado en el script SQL de verificación).
    /// </summary>
    [Fact]
    public void DniEsUnicoSobre_ClienteSoftDeletedExcluidoPorQueryFilter_DevuelveTrue()
    {
        // Simula el resultado de _context.Clientes.AsNoTracking() con el QueryFilter
        // global activo: el soft-deleted NO aparece en la colección.
        var clientesVisibles = new List<Cliente>
        {
            new() { Id = 1, Dni = "11111111", DeletedAt = null },
            new() { Id = 2, Dni = "22222222", DeletedAt = null },
            // Id=99 con DNI 12345678 y DeletedAt != null NO está en la lista
            // porque el QueryFilter global lo excluyó antes de llegar al helper.
        }.AsQueryable();

        ClienteService.DniEsUnicoSobre(clientesVisibles, "12345678")
            .Should().BeTrue("el cliente soft-deleted con ese DNI fue filtrado por el QueryFilter global");
    }

    [Fact]
    public void DniEsUnicoSobre_ExcluyendoIdEnUpdate_NoCuentaAlMismoCliente()
    {
        // Simula _context.Clientes.AsNoTracking().Where(c => c.Id != excludeId).
        var clientes = new List<Cliente>
        {
            new() { Id = 5, Dni = "12345678", DeletedAt = null },
            new() { Id = 6, Dni = "12345678", DeletedAt = null },
        }.AsQueryable().Where(c => c.Id != 5);

        ClienteService.DniEsUnicoSobre(clientes, "12345678")
            .Should().BeFalse("queda otro cliente activo con ese DNI distinto del que se edita");
    }
}
