using ExtraGasMVC.DTOs;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de regresión estructurales: Garantizan que el contrato del DTO no
/// vuelve a exponer <c>Activo</c> como propiedad editable. Si alguien lo
/// vuelve a poner en CreateProveedorDto o UpdateProveedorDto, los tests
/// fallan en compilación.
///
/// <para>El patrón es análogo al de Cliente, Empleado, Producto y Usuario
/// (todos del issue #114). Proveedor es el último del recorrido porque ya
/// tenía el helper <see cref="Extensions.ProveedorEditRules"/> y la defensa
/// en UI, pero el DTO seguía editable — un agujero de contrato que un POST
/// hand-crafted podría explotar.</para>
/// </summary>
public class ProveedorDtoContratoTests
{
    [Fact]
    public void UpdateProveedorDto_NoExponeActivo()
    {
        Assert.Null(typeof(UpdateProveedorDto).GetProperty("Activo"));
    }

    [Fact]
    public void CreateProveedorDto_NoExponeActivo()
    {
        Assert.Null(typeof(CreateProveedorDto).GetProperty("Activo"));
    }

    [Fact]
    public void ProveedorDto_SiExponeActivo_ParaDisplay()
    {
        // El DTO de salida sigue exponiéndolo porque Details/Index lo
        // necesitan para mostrar el badge de estado.
        Assert.NotNull(typeof(ProveedorDto).GetProperty("Activo"));
    }
}
