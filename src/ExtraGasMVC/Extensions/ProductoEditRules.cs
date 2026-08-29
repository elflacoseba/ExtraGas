using ExtraGasMVC.Data.Entities;

namespace ExtraGasMVC.Extensions;

/// <summary>
/// Reglas de edición de la entidad <see cref="Producto"/> centralizadas para
/// poder testear sin DbContext. Aplica la convención "doble flag acoplado"
/// del módulo productos — análoga a <see cref="ProveedorEditRules"/>,
/// <see cref="ClienteEditRules"/> y <see cref="EmpleadoEditRules"/>.
///
/// <para>A diferencia de Cliente, este NO preserva una fecha: Producto no
/// tiene campo fecha de alta (se usa <c>CreatedAt</c> a nivel EF/BD).
/// <c>ManejaGarrafaIndividual</c> sí es editable — es config de negocio.</para>
///
/// <para>Issue #114 (replicado en Productos): corrige el bug de integridad
/// por el cual <c>UpdateProductoDto.Activo</c> era editable desde el
/// formulario de Edit, permitiendo estados zombie
/// (<c>Activo=false</c> con <c>DeletedAt=null</c>).</para>
/// </summary>
public static class ProductoEditRules
{
    /// <summary>
    /// Restaura los flags del producto que Edit tiene prohibido modificar.
    /// Llamar DESPUÉS del AutoMapper, sobre la entity ya mergeada con el DTO.
    /// </summary>
    /// <param name="entity">Entity post-mapper con los valores del form aplicados.</param>
    /// <param name="activoOriginal">Valor de <c>Activo</c> leído de la BD antes del mapper.</param>
    public static void PreservarFlagsNoEditables(Producto entity, bool activoOriginal)
    {
        // REGLA DURA: el flag Activo solo cambia vía Delete. Si el operador
        // lo destilda en el form de Edit, la edición silenciosamente no lo
        // modifica — más amigable que un 400. Para (des)activar un producto
        // hay que pasar por la acción dedicada.
        entity.Activo = activoOriginal;
    }
}
