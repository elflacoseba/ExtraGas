using System;

namespace ExtraGasMVC.Services.Exceptions;

/// <summary>
/// Se lanza cuando se intenta operar (Update, etc.) sobre un cliente que
/// está soft-deleted en la BD. Distinct de <see cref="KeyNotFoundException"/>
/// (cliente nunca existió o ya fue purgado) para que el Controller pueda
/// mostrar al operador el mensaje correcto: "debe restaurarlo primero" en
/// lugar de "no encontrado".
///
/// <para>Issue #108: antes se confundía con <see cref="KeyNotFoundException"/>
/// porque <c>UpdateAsync</c> usaba <c>FindAsync</c> (que respeta el
/// QueryFilter global) y devolvía <c>null</c> tanto para inexistentes como
/// para soft-deleted. Ahora se distingue con <c>IgnoreQueryFilters()</c> +
/// check de <c>DeletedAt</c>.</para>
/// </summary>
public class ClienteSoftDeletedException : InvalidOperationException
{
    public ulong ClienteId { get; }

    public ClienteSoftDeletedException(ulong clienteId)
        : base("No se puede editar un cliente eliminado; debe restaurarlo primero.")
    {
        ClienteId = clienteId;
    }
}
