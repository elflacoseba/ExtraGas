using ExtraGasMVC.Data.Entities;

namespace ExtraGasMVC.Extensions;

/// <summary>
/// Reglas de edición de la entidad <see cref="Cliente"/> centralizadas para
/// poder testear sin DbContext. Aplica la convención "doble flag acoplado"
/// del módulo clientes — análoga a <see cref="ProveedorEditRules"/> — más la
/// preservación de <c>FechaAlta</c> como audit trail inmutable del alta.
///
/// <para>Issue #114: corrige el bug de integridad por el cual
/// <c>ClienteDtoBase.Activo</c> y <c>ClienteDtoBase.FechaAlta</c> eran
/// editables desde el formulario de Edit, permitiendo estados zombie
/// (<c>Activo=false</c> con <c>DeletedAt=null</c>) y retroceder la fecha
/// de alta a una fecha arbitraria.</para>
/// </summary>
public static class ClienteEditRules
{
    /// <summary>
    /// Restaura los campos del cliente que Edit tiene prohibido modificar.
    /// Llamar DESPUÉS del AutoMapper, sobre la entity ya mergeada con el DTO.
    /// </summary>
    /// <param name="entity">Entity post-mapper con los valores del form aplicados.</param>
    /// <param name="activoOriginal">Valor de <c>Activo</c> leído de la BD antes del mapper.</param>
    /// <param name="fechaAltaOriginal">Valor de <c>FechaAlta</c> leído de la BD antes del mapper.</param>
    public static void PreservarFlagsNoEditables(Cliente entity, bool activoOriginal, DateOnly fechaAltaOriginal)
    {
        // REGLA DURA: el flag Activo solo cambia vía Delete/Restore. Si el
        // operador lo destilda en el form de Edit, la edición silenciosamente
        // no lo modifica — más amigable que un 400. Para (des)activar un
        // cliente hay que pasar por las acciones dedicadas.
        entity.Activo = activoOriginal;

        // REGLA DURA: FechaAlta es audit trail del alta. No debe retrocederse
        // ni reescribirse desde el form. Se preserva el valor con el que se
        // dio de alta al cliente.
        entity.FechaAlta = fechaAltaOriginal;
    }
}
