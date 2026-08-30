using System.Text;

namespace ExtraGasMVC.Extensions;

/// <summary>
/// Helpers de normalización de strings de identidad/contacto del cliente.
/// Garantizan que valores equivalentes por formato (con/sin espacios, con/sin
/// separadores) se almacenen, validen y busquen de forma canónica.
/// Issue #113.
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
            // '+' se descarta acá: el '+' inicial ya se agregó arriba explícitamente;
            // cualquier '+' en otra posición es ruido de tipeo (el código de país
            // solo es semánticamente válido al inicio) y lo removemos.
            if (ch == ' ' || ch == '-' || ch == '(' || ch == ')' || ch == '.' || ch == '+') continue;
            sb.Append(ch);
        }

        return sb.Length == (tieneMas ? 1 : 0) ? null : sb.ToString();
    }
}