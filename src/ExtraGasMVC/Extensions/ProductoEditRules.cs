using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Exceptions;

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
///
/// <para>Issue #146.3: agrupamos también la validación GARRAFA ⇒ CapacidadKg
/// > 0 acá — antes explotaba tarde en RecepcionService.ValidarCodigosGarrafaAsync.
/// Ahora se rechaza en el Service con un mensaje claro. Mismo caso de
/// "validar en el borde, no en la BD".</para>
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

    /// <summary>
    /// Issue #146.3: regla de negocio GARRAFA ⇒ CapacidadKg &gt; 0. Aplicar
    /// ANTES de SaveChanges en Create y Update. Si el operador marca
    /// <c>ManejaGarrafaIndividual=true</c> sin capacidad (o con capacidad
    /// cero/negativa), se rechaza con <see cref="ValidationException"/>.
    ///
    /// <para>Por qué se valida acá y no en
    /// <see cref="ProductoEditRules"/>-como-attribute: Reglas de validación
    /// que dependen de cross-field (<c>ManejaGarrafaIndividual</c> + <c>CapacidadKg</c>)
    /// no son expresables con DataAnnotations estándar — necesitamos comparar
    /// dos campos y DataAnnotations solo ve uno por anotación. La regla
    /// tampoco calza en la entity porque el Service ya la valida sobre el
    /// DTO antes del Map (no queremos seguir con la entity hasta validar).</para>
    /// </summary>
    public static void ValidarGarrafaCapacidad(CreateProductoDto dto)
        => ValidarGarrafaCapacidadInternal(dto.ManejaGarrafaIndividual, dto.CapacidadKg);

    /// <summary>
    /// Sobrecarga para <see cref="UpdateProductoDto"/>. Misma lógica que
    /// el overload de Create — se llaman desde distintas operaciones del
    /// Service pero la regla es la misma.
    /// </summary>
    public static void ValidarGarrafaCapacidad(UpdateProductoDto dto)
        => ValidarGarrafaCapacidadInternal(dto.ManejaGarrafaIndividual, dto.CapacidadKg);

    private static void ValidarGarrafaCapacidadInternal(bool manejaGarrafaIndividual, decimal? capacidadKg)
    {
        // `is null or <= 0m` cubre ambos casos: null (no setearon CapacidadKg)
        // y <= 0 (setearon 0 explícito, o negativo). DataAnnotations no
        // llegan acá: [Range(0.01, ...)] en el DTO rechaza <= 0, pero NO
        // rechaza null (es nullable) y el operador que marca "maneja
        // garrafa individual" puede olvidarse del campo. La regla conjunta
        // es lo que nos importa.
        if (manejaGarrafaIndividual && (capacidadKg is null or <= 0m))
            throw new ValidationException("Productos GARRAFA requieren capacidad_kg > 0.");
    }
}
