using System.Text;

namespace ExtraGasMVC.Extensions;

/// <summary>
/// Helpers de normalización de strings de identidad/contacto del cliente y de
/// códigos de producto. Garantizan que valores equivalentes por formato (con/sin
/// espacios, con/sin separadores, mayúsculas/minúsculas) se almacenen,
/// validen y busquen de forma canónica.
/// Issue #113 (DNI / Teléfono) + issue #147 item 6 (TrimAndUpper).
/// </summary>
public static class StringNormalizer
{
    /// <summary>
    /// Normaliza un DNI: trim + remueve espacios, puntos y guiones.
    /// Devuelve solo dígitos. null/vacío/whitespace → null.
    /// </summary>
    public static string? NormalizarDni(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var trimmed = input.Trim();
        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (ch == ' ' || ch == '.' || ch == '-') continue;
            sb.Append(ch);
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>
    /// Normaliza un teléfono: trim + remueve espacios, guiones, paréntesis y
    /// puntos. Conserva un '+' inicial (prefijo internacional) si estaba
    /// presente, porque es semánticamente distinto tener código de país o no.
    /// Devuelve solo '+' (si aplica) y dígitos. null/vacío/whitespace → null.
    /// </summary>
    public static string? NormalizarTelefono(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var trimmed = input.Trim();
        var sb = new StringBuilder(trimmed.Length);
        var tieneMas = trimmed[0] == '+';
        if (tieneMas) sb.Append('+');

        foreach (var ch in trimmed)
        {
            // Se descartan separadores comunes y el '+' (ya agregado arriba si era inicial).
            if (ch == ' ' || ch == '-' || ch == '(' || ch == ')' || ch == '.' || ch == '+') continue;
            sb.Append(ch);
        }

        // Si solo quedó el '+' (o nada), devolvemos null. Si quedó '+' + dígitos,
        // el código de país es parte de la identidad canónica.
        var longitudMinima = tieneMas ? 1 : 0;
        if (sb.Length <= longitudMinima) return null;
        return sb.ToString();
    }

    /// <summary>
    /// Normaliza un código de producto: trim + uppercase invariante.
    /// Devuelve <see cref="string.Empty"/> para null/empty/whitespace (no null),
    /// porque <c>Producto.Codigo</c> es NOT NULL en BD. Diverge deliberadamente
    /// de <see cref="NormalizarDni"/>/<see cref="NormalizarTelefono"/> que
    /// devuelven null en entradas vacías — el dominio del código es "no hay
    /// código canónico" en lugar de "código ausente".
    /// Issue #147 item 6: garantiza que <c>" gas-10 "</c>, <c>"GAS-10"</c> y
    /// <c>" gas-10 "</c> colapsan al mismo valor canónico, cubriendo el índice
    /// único <c>uq_productos_codigo</c>.
    /// </summary>
    public static string TrimAndUpper(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return input.Trim().ToUpperInvariant();
    }
}