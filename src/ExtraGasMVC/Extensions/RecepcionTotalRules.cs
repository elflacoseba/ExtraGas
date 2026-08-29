namespace ExtraGasMVC.Extensions;

/// <summary>
/// Regla de cálculo del Total de una recepción de proveedor. Centralizada
/// para poder testear sin DbContext y reusar en Service y, eventualmente,
/// en UI (vía un endpoint de cálculo si fuera necesario).
///
/// <para>El Total es un campo derivado: <c>Subtotal - Descuento</c>. No es
/// input del operador. Sacarlo del input del form y calcularlo acá
/// garantiza la invariante contable incluso si alguien postea el form con
/// un valor arbitrario (que el Service ignora porque el DTO no lo trae).</para>
/// </summary>
public static class RecepcionTotalRules
{
    /// <summary>
    /// Calcula el Total de una recepción: <c>Subtotal - Descuento</c>.
    /// Rechaza valores negativos o un descuento mayor al subtotal (no tiene
    /// sentido económico y casi siempre indica un error de carga).
    /// </summary>
    /// <param name="subtotal">Suma de los items de la recepción (sin descuento).</param>
    /// <param name="descuento">Descuento global aplicado a la recepción.</param>
    /// <returns>Total resultante (siempre >= 0).</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Si <paramref name="subtotal"/> es negativo, <paramref name="descuento"/>
    /// es negativo, o el descuento supera al subtotal.
    /// </exception>
    public static decimal Calcular(decimal subtotal, decimal descuento)
    {
        if (subtotal < 0)
            throw new ArgumentOutOfRangeException(
                nameof(subtotal), subtotal, "El subtotal no puede ser negativo.");
        if (descuento < 0)
            throw new ArgumentOutOfRangeException(
                nameof(descuento), descuento, "El descuento no puede ser negativo.");
        if (descuento > subtotal)
            throw new ArgumentOutOfRangeException(
                nameof(descuento), descuento,
                $"El descuento ({descuento:0.00}) no puede ser mayor al subtotal ({subtotal:0.00}).");

        return subtotal - descuento;
    }
}
