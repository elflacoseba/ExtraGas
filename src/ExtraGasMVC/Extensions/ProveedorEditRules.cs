using ExtraGasMVC.Data.Entities;

namespace ExtraGasMVC.Extensions;

/// <summary>
/// Reglas de edición de la entidad <see cref="Proveedor"/> centralizadas para
/// poder testear sin DbContext. Hoy aplica una sola regla: la convención
/// "doble flag acoplado" del módulo proveedores, donde <c>Activo</c> solo
/// puede cambiar vía <c>Delete</c>, no vía <c>Edit</c>. Edit debe preservar
/// el valor de BD para que el estado operacional no se desincronice.
/// Si en el futuro se agregan más flags protegidos, este es el lugar.
/// </summary>
public static class ProveedorEditRules
{
    /// <summary>
    /// Restaura los flags del proveedor que Edit tiene prohibido modificar.
    /// Llamar DESPUÉS del AutoMapper, sobre la entity ya mergeada con el DTO.
    /// </summary>
    /// <param name="entity">Entity post-mapper con los valores del form aplicados.</param>
    /// <param name="activoOriginal">Valor de <c>Activo</c> leído de la BD antes del mapper.</param>
    public static void PreservarFlagsNoEditables(Proveedor entity, bool activoOriginal)
    {
        // REGLA DURA: el flag Activo solo cambia vía Delete. Si el operador
        // lo destilda en el form de Edit, la edición silenciosamente no lo
        // modifica. Para desactivar un proveedor hay que pasar por Delete.
        entity.Activo = activoOriginal;
    }
}
