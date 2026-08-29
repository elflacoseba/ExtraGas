using System.ComponentModel.DataAnnotations;

namespace ExtraGasMVC.Extensions;

/// <summary>
/// Validación de CUIT argentino: combina verificación de formato (11 dígitos
/// numéricos) y dígito verificador módulo 11, según el algoritmo que usa AFIP
/// para la clave única de identificación tributaria.
/// </summary>
public static class CuitValidator
{
    /// <summary>
    /// Multiplicadores del algoritmo módulo 11 aplicados, en orden, a los
    /// primeros 10 dígitos del CUIT (de izquierda a derecha).
    /// </summary>
    private static readonly int[] Multiplicadores = { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };

    /// <summary>
    /// Calcula el dígito verificador (posición 11) de un CUIT sin DV.
    /// </summary>
    /// <param name="cuitSinDv">CUIT de exactamente 10 dígitos numéricos, sin guiones.</param>
    /// <returns>Dígito verificador (0-9).</returns>
    /// <exception cref="ArgumentException">Si la entrada no tiene 10 dígitos.</exception>
    public static int CalcularDigitoVerificador(string cuitSinDv)
    {
        if (string.IsNullOrEmpty(cuitSinDv) || cuitSinDv.Length != 10)
            throw new ArgumentException("El CUIT sin DV debe tener exactamente 10 dígitos.", nameof(cuitSinDv));

        int suma = 0;
        for (int i = 0; i < 10; i++)
        {
            int digito = cuitSinDv[i] - '0';
            if (digito < 0 || digito > 9)
                throw new ArgumentException("El CUIT sin DV debe contener solo dígitos numéricos.", nameof(cuitSinDv));
            suma += digito * Multiplicadores[i];
        }

        int resto = suma % 11;
        return resto switch
        {
            0 => 0,
            // AFIP define que para prefijo 20/23/24/25/26/27 con resto 1,
            // el dígito verificador es 9 (es la convención más usada).
            1 => 9,
            _ => 11 - resto
        };
    }

    /// <summary>
    /// Valida un CUIT completo (11 dígitos con DV incluido, sin guiones ni espacios).
    /// </summary>
    /// <returns><c>true</c> cuando el formato y el dígito verificador son correctos.</returns>
    public static bool EsValido(string? cuit)
    {
        if (string.IsNullOrWhiteSpace(cuit)) return false;
        if (cuit.Length != 11) return false;

        for (int i = 0; i < 11; i++)
        {
            int digito = cuit[i] - '0';
            if (digito < 0 || digito > 9) return false;
        }

        int dv = cuit[10] - '0';
        int dvCalculado = CalcularDigitoVerificador(cuit.Substring(0, 10));
        return dv == dvCalculado;
    }

    /// <summary>
    /// Genera un CUIT válido a partir de un prefijo (2 dígitos) y un DNI
    /// (hasta 8 dígitos). Pensado solo para tests y seeds; no para producción
    /// (en producción el CUIT lo emite AFIP).
    /// </summary>
    public static string Generar(int prefijo, long dni)
    {
        var prefijoStr = prefijo.ToString("D2");
        var dniStr = dni.ToString("D8");
        var sinDv = prefijoStr + dniStr;
        var dv = CalcularDigitoVerificador(sinDv);
        return $"{sinDv}{dv}";
    }
}

/// <summary>
/// Atributo de validación para ASP.NET MVC / DataAnnotations: aplica el
/// algoritmo del CUIT argentino (formato + dígito verificador módulo 11).
/// Si la cadena tiene un formato inválido, sobrescribe el mensaje con uno
/// específico de "11 dígitos numéricos"; si el formato es correcto pero el
/// DV está mal, lo sobrescribe con uno específico de "dígito verificador".
/// El caso null/empty lo maneja <see cref="RequiredAttribute"/> por separado.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class CuitAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        // El atributo NO se encarga del null/empty: RequiredAttribute es el
        // responsable de obligar la presencia. Aquí solo validamos CUITs
        // efectivamente presentes.
        if (value is null) return true;
        if (value is not string s) return false;
        if (string.IsNullOrWhiteSpace(s)) return true;

        if (s.Length != 11 || !s.All(char.IsDigit))
        {
            ErrorMessage = "El CUIT debe contener 11 dígitos numéricos.";
            return false;
        }

        int dv = s[10] - '0';
        int dvCalculado = CuitValidator.CalcularDigitoVerificador(s.Substring(0, 10));
        if (dv != dvCalculado)
        {
            ErrorMessage = "El CUIT es inválido. Verifique el dígito verificador.";
            return false;
        }

        return true;
    }
}
