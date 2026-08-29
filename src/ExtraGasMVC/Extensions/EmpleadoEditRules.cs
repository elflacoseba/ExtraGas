using ExtraGasMVC.Data.Entities;

namespace ExtraGasMVC.Extensions;

/// <summary>
/// Reglas de edición de la entidad <see cref="Empleado"/> centralizadas para
/// poder testear sin DbContext. Aplica la convención "doble flag acoplado"
/// del módulo empleados — análoga a <see cref="ProveedorEditRules"/> y
/// <see cref="ClienteEditRules"/>.
///
/// <para>A diferencia del helper de Cliente, este NO preserva <c>FechaIngreso</c>:
/// es un dato de negocio del empleado (lo carga el operador y puede corregirlo),
/// no audit trail. Solo <c>Activo</c> es estado.</para>
///
/// <para>Issue #114 (replicado en Empleados): corrige el bug de integridad por
/// el cual <c>EmpleadoDtoBase.Activo</c> era editable desde el formulario de
/// Edit, permitiendo estados zombie (<c>Activo=false</c> con
/// <c>DeletedAt=null</c>).</para>
/// </summary>
public static class EmpleadoEditRules
{
    /// <summary>
    /// Restaura los flags del empleado que Edit tiene prohibido modificar.
    /// Llamar DESPUÉS del AutoMapper, sobre la entity ya mergeada con el DTO.
    /// </summary>
    /// <param name="entity">Entity post-mapper con los valores del form aplicados.</param>
    /// <param name="activoOriginal">Valor de <c>Activo</c> leído de la BD antes del mapper.</param>
    public static void PreservarFlagsNoEditables(Empleado entity, bool activoOriginal)
    {
        // REGLA DURA: el flag Activo solo cambia vía Delete. Si el operador
        // lo destilda en el form de Edit, la edición silenciosamente no lo
        // modifica — más amigable que un 400. Para (des)activar un empleado
        // hay que pasar por la acción dedicada.
        entity.Activo = activoOriginal;
    }
}
