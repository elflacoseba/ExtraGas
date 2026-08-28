using ExtraGasMVC.Configuration;
using ExtraGasMVC.Services.Implementations;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExtraGasMVC.Tests;

public class PasswordPolicyServiceTests
{
    private static PasswordPolicyService CreateService(PasswordPolicyOptions? options = null)
    {
        options ??= new PasswordPolicyOptions();
        return new PasswordPolicyService(Options.Create(options));
    }

    // ---------- Defaults (MinLength=8, Upper+Lower+Digit, no special, MaxConsecutive=4) ----------

    [Fact]
    public void Validate_NullOrEmpty_Fails()
    {
        var svc = CreateService();

        Assert.False(svc.Validate(null).IsValid);
        Assert.False(svc.Validate("").IsValid);
    }

    [Fact]
    public void Validate_ValidPassword_Passes()
    {
        var svc = CreateService();

        Assert.True(svc.Validate("Abcdef1!").IsValid);
        Assert.True(svc.Validate("Passw0rdOK").IsValid);
    }

    [Fact]
    public void Validate_TooShort_Fails()
    {
        var svc = CreateService();

        var result = svc.Validate("Ab1");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("al menos 8"));
    }

    [Fact]
    public void Validate_MissingUppercase_Fails()
    {
        var svc = CreateService();

        var result = svc.Validate("abcdefgh1");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("mayuscula"));
    }

    [Fact]
    public void Validate_MissingLowercase_Fails()
    {
        var svc = CreateService();

        var result = svc.Validate("ABCDEFGH1");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("minuscula"));
    }

    [Fact]
    public void Validate_MissingDigit_Fails()
    {
        var svc = CreateService();

        var result = svc.Validate("Abcdefgh");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("digito"));
    }

    [Fact]
    public void Validate_MultipleViolations_ReportedAsMultiple()
    {
        var svc = CreateService();

        var result = svc.Validate("abc");
        Assert.False(result.IsValid);
        // Al menos longitud + mayuscula + digito
        Assert.True(result.Errors.Count >= 3, $"Expected >=3 errors, got {result.Errors.Count}: {string.Join("; ", result.Errors)}");
    }

    [Fact]
    public void Validate_ConsecutiveRepeated_AboveLimit_Fails()
    {
        var svc = CreateService(new PasswordPolicyOptions { MaxConsecutiveChars = 4 });

        var result = svc.Validate("Abcde11111!"); // 5 unos consecutivos
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("consecutivos"));
    }

    [Fact]
    public void Validate_ConsecutiveRepeated_AtLimit_Passes()
    {
        var svc = CreateService(new PasswordPolicyOptions { MaxConsecutiveChars = 4 });

        var result = svc.Validate("Abcde1111!"); // 4 unos consecutivos: dentro del limite
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MaxConsecutiveZero_DisablesRule()
    {
        var svc = CreateService(new PasswordPolicyOptions { MaxConsecutiveChars = 0 });

        var result = svc.Validate("Ab111111111111111111111111111cdefgh"); // muchos repetidos, regla desactivada
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MinLengthZero_DisablesRule()
    {
        var svc = CreateService(new PasswordPolicyOptions { MinLength = 0 });

        // "ab1" tiene 3 chars (menor a default 8, pero MinLength=0 la desactiva),
        // falta mayuscula. Tiene minuscula y digito, asi que falla SOLO por mayuscula.
        var result = svc.Validate("ab1");
        Assert.False(result.IsValid);
        Assert.DoesNotContain(result.Errors, e => e.Contains("caracteres")); // mensaje de longitud deshabilitada
        Assert.Contains(result.Errors, e => e.Contains("mayuscula"));
    }

    [Fact]
    public void Validate_RequireSpecialChar_True_FailsWithoutSymbol()
    {
        var svc = CreateService(new PasswordPolicyOptions { RequireSpecialChar = true });

        var result = svc.Validate("Abcdefg1");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("especial"));
    }

    [Fact]
    public void Validate_RequireSpecialChar_True_PassesWithSymbol()
    {
        var svc = CreateService(new PasswordPolicyOptions { RequireSpecialChar = true });

        var result = svc.Validate("Abcdefg1!");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AllRulesOff_PassesAnythingNonEmpty()
    {
        var svc = CreateService(new PasswordPolicyOptions
        {
            MinLength = 0,
            RequireUppercase = false,
            RequireLowercase = false,
            RequireDigit = false,
            RequireSpecialChar = false,
            MaxConsecutiveChars = 0,
        });

        Assert.True(svc.Validate("x").IsValid);
        Assert.True(svc.Validate("anything goes here").IsValid);
    }
}
