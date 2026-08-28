using ExtraGasMVC.Configuration;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace ExtraGasMVC.Services.Implementations;

public class PasswordPolicyService : IPasswordPolicyService
{
    private readonly PasswordPolicyOptions _options;

    public PasswordPolicyService(IOptions<PasswordPolicyOptions> options)
    {
        _options = options.Value;
    }

    public PasswordPolicyResult Validate(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return PasswordPolicyResult.Fail("La contrasena es obligatoria.");

        var errors = new List<string>();

        if (_options.MinLength > 0 && password.Length < _options.MinLength)
            errors.Add($"La contrasena debe tener al menos {_options.MinLength} caracteres.");

        if (_options.RequireUppercase && !password.Any(char.IsUpper))
            errors.Add("La contrasena debe incluir al menos una letra mayuscula.");

        if (_options.RequireLowercase && !password.Any(char.IsLower))
            errors.Add("La contrasena debe incluir al menos una letra minuscula.");

        if (_options.RequireDigit && !password.Any(char.IsDigit))
            errors.Add("La contrasena debe incluir al menos un digito.");

        if (_options.RequireSpecialChar && !password.Any(c => !char.IsLetterOrDigit(c)))
            errors.Add("La contrasena debe incluir al menos un caracter especial.");

        if (_options.MaxConsecutiveChars > 0 && HasTooManyConsecutiveChars(password, _options.MaxConsecutiveChars))
            errors.Add($"La contrasena no puede tener mas de {_options.MaxConsecutiveChars} caracteres consecutivos repetidos.");

        return errors.Count == 0 ? PasswordPolicyResult.Ok() : PasswordPolicyResult.Fail(errors.ToArray());
    }

    private static bool HasTooManyConsecutiveChars(string password, int max)
    {
        var consecutive = 1;
        for (var i = 1; i < password.Length; i++)
        {
            if (password[i] == password[i - 1])
            {
                consecutive++;
                if (consecutive > max) return true;
            }
            else
            {
                consecutive = 1;
            }
        }
        return false;
    }
}
