using ExtraGasMVC.Data.Entities;

namespace ExtraGasMVC.Extensions;

/// <summary>
/// Reglas de edición de la entidad <see cref="Usuario"/> centralizadas para
/// poder testear sin DbContext. Aplica la convención "doble flag acoplado"
/// del módulo usuarios — análoga a <see cref="ProveedorEditRules"/>,
/// <see cref="ClienteEditRules"/>, <see cref="EmpleadoEditRules"/> y
/// <see cref="ProductoEditRules"/>.
///
/// <para>Issue #114 (replicado en Usuarios): corrige el bug de integridad por
/// el cual <c>UpdateUsuarioDto.Activo</c> era editable desde el formulario
/// de Edit, permitiendo estados zombie (<c>Activo=false</c> con
/// <c>DeletedAt=null</c>).</para>
/// </summary>
public static class UsuarioEditRules
{
    /// <summary>
    /// Restaura los flags del usuario que Edit tiene prohibido modificar.
    /// Llamar DESPUÉS del AutoMapper, sobre la entity ya mergeada con el DTO.
    /// </summary>
    /// <param name="entity">Entity post-mapper con los valores del form aplicados.</param>
    /// <param name="activoOriginal">Valor de <c>Activo</c> leído de la BD antes del mapper.</param>
    public static void PreservarFlagsNoEditables(Usuario entity, bool activoOriginal)
    {
        // REGLA DURA: el flag Activo solo cambia vía Delete. Si el operador
        // lo destilda en el form de Edit, la edición silenciosamente no lo
        // modifica — más amigable que un 400. Para (des)activar un usuario
        // hay que pasar por la acción dedicada (que además bloquea la
        // auto-eliminación por seguridad).
        entity.Activo = activoOriginal;
    }
}
