using ExtraGasMVC.Data.Entities;

namespace ExtraGasMVC.Extensions;

/// <summary>
/// Reglas de edición de la entidad <see cref="Garrafa"/> centralizadas para
/// poder testear sin DbContext. Aplica la convención "Activo es estado de
/// soft-delete, no dato operacional" — análoga a los demás
/// <c>*EditRules</c> (Proveedor, Cliente, Empleado, Producto, Usuario).
///
/// <para>Garrafa tiene DOS flags que parecen superponerse pero son ortogonales:
/// <list type="bullet">
/// <item><c>Activo</c>: si la garrafa está dada de baja (soft-delete). Lo
/// cambia solo <c>Delete</c>.</item>
/// <item><c>estado_garrafa_id</c>: situación operacional (LLENA_DEPOSITO,
/// EN_CLIENTE, FUERA_SERVICIO, etc.). Lo cambia la acción dedicada
/// "Cambiar estado" con validación contra <c>GarrafaTransiciones</c>.</item>
/// </list>
/// Este helper solo preserva <c>Activo</c>. El estado operacional se
/// gestiona por otro camino.</para>
///
/// <para>Issue #114 (replicado en Garrafas): corrige el bug por el cual
/// <c>UpdateGarrafaDto.Activo</c> era editable desde el formulario de Edit,
/// permitiendo desactivar garrafas sin pasar por Delete y dejar el campo
/// inconsistente con el sistema de estados.</para>
/// </summary>
public static class GarrafaEditRules
{
    /// <summary>
    /// Restaura el flag <c>Activo</c> de la garrafa que Edit tiene prohibido
    /// modificar. Llamar DESPUÉS del AutoMapper, sobre la entity ya mergeada
    /// con el DTO.
    /// </summary>
    /// <param name="entity">Entity post-mapper con los valores del form aplicados.</param>
    /// <param name="activoOriginal">Valor de <c>Activo</c> leído de la BD antes del mapper.</param>
    public static void PreservarFlagsNoEditables(Garrafa entity, bool activoOriginal)
    {
        // REGLA DURA: el flag Activo solo cambia vía Delete. Si el operador
        // lo destilda en el form de Edit, la edición silenciosamente no lo
        // modifica — más amigable que un 400. Para dar de baja una garrafa
        // hay que pasar por la acción dedicada.
        entity.Activo = activoOriginal;
    }
}
