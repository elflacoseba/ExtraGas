using ExtraGasMVC.Data.Entities;

namespace ExtraGasMVC.Extensions;

/// <summary>
/// Reglas de edición de la entidad <see cref="Cliente"/> centralizadas para
/// poder testear sin DbContext. Preserva <c>FechaAlta</c> como audit trail
/// inmutable del alta.
///
/// <para>Issue #114: corrige el bug de integridad por el cual
/// <c>ClienteDtoBase.FechaAlta</c> era editable desde el formulario de Edit,
/// permitiendo retroceder la fecha de alta a una fecha arbitraria.</para>
///
/// <para>Issue #115: el flag <c>Activo</c> desapareció de la entity y del DTO
/// (se deriva de <c>DeletedAt == null</c>). Esta clase ya no preserva nada
/// relacionado con <c>Activo</c> — no hay forma de editarlo desde el form
/// porque no existe como propiedad escribible.</para>
/// </summary>
public static class ClienteEditRules
{
    /// <summary>
    /// Restaura la fecha de alta que Edit tiene prohibido modificar.
    /// Llamar DESPUÉS del AutoMapper, sobre la entity ya mergeada con el DTO.
    /// </summary>
    /// <param name="entity">Entity post-mapper con los valores del form aplicados.</param>
    /// <param name="fechaAltaOriginal">Valor de <c>FechaAlta</c> leído de la BD antes del mapper.</param>
    public static void PreservarFechaAlta(Cliente entity, DateOnly fechaAltaOriginal)
    {
        // REGLA DURA: FechaAlta es audit trail del alta. No debe retrocederse
        // ni reescribirse desde el form. Se preserva el valor con el que se
        // dio de alta al cliente.
        entity.FechaAlta = fechaAltaOriginal;
    }
}