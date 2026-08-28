using System.Security.Cryptography;

namespace ExtraGasMVC.Services;

/// <summary>
/// Generador de passwords temporales aleatorios para el flujo de
/// reseteo admin-assisted. Usa RandomNumberGenerator (criptografico)
/// para que el resultado no sea predecible.
/// </summary>
public static class TemporaryPasswordGenerator
{
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%&*+-=?";
    private const string AllChars = Lowercase + Uppercase + Digits + Symbols;

    public static string Generate(int length = 12)
    {
        if (length < 8)
            throw new ArgumentException("La password temporal debe tener al menos 8 caracteres.", nameof(length));

        var password = new char[length];

        // Garantizar al menos un caracter de cada set (lower/upper/digit/symbol)
        password[0] = Lowercase[RandomNumberGenerator.GetInt32(Lowercase.Length)];
        password[1] = Uppercase[RandomNumberGenerator.GetInt32(Uppercase.Length)];
        password[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        password[3] = Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)];

        for (var i = 4; i < length; i++)
            password[i] = AllChars[RandomNumberGenerator.GetInt32(AllChars.Length)];

        // Mezclar (Fisher-Yates) para que las primeras 4 posiciones no sean siempre el mismo set.
        for (var i = length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        return new string(password);
    }
}
