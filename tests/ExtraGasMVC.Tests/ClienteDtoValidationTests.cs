using System.ComponentModel.DataAnnotations;
using ExtraGasMVC.DTOs;
using Xunit;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Tests de DataAnnotations sobre el campo <c>TelefonoPrincipal</c> del DTO
/// de Cliente. Cubren la regla agregada en la issue #117: el regex
/// <c>^[\d\s\-\+\(\)]{6,25}$</c> acepta solo dígitos, espacios, guiones,
/// signo <c>+</c> y paréntesis, con mínimo 6 caracteres. El objetivo es
/// fijar la regla y detectar regresiones si alguien vuelve a un regex
/// permisivo (el anterior <c>^[0-9 +()\-.]*$</c> aceptaba string vacío
/// o todo separadores — un teléfono inútil para llamar o mandar WhatsApp).
/// </summary>
public class ClienteDtoValidationTests
{
    // Issue #158 (CA1859): IReadOnlyList porque el caller nunca muta el
    // resultado de Validator.TryValidateObject — comunica la intención.
    private static IReadOnlyList<ValidationResult> Validar(object dto)
    {
        var ctx = new ValidationContext(dto);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, ctx, results, validateAllProperties: true);
        return results;
    }

    private static CreateClienteDto ClienteValido() => new()
    {
        Nombre = "Juan",
        Apellido = "Pérez",
        TelefonoPrincipal = "1144556677",
    };

    // ---------- CreateClienteDto: teléfonos válidos ----------

    [Theory]
    [InlineData("1144556677")]            // 10 dígitos, formato local
    [InlineData("123456")]                // 6 dígitos (mínimo permitido)
    [InlineData("+541144556677")]         // 13 chars, con + al inicio
    [InlineData("+54 11 4455-6677")]      // 15 chars, formato argentino completo
    [InlineData("(011) 4455-6677")]       // con paréntesis y espacios
    [InlineData("11 4455 6677")]          // con espacios internos
    [InlineData("1234567890123456789012345")] // 25 dígitos (máximo permitido)
    // El regex acepta whitespace en cualquier posición — esto es válido por la
    // spec del issue. Si en el futuro se quiere exigir "no arranca con espacio",
    // hay que cambiar el regex a algo como ^\d[...]{5,24}$ o similar.
    [InlineData(" 1144556677")]
    public void CreateClienteDto_TelefonoFormatoValido_NoTieneError(string telefono)
    {
        var dto = ClienteValido();
        dto.TelefonoPrincipal = telefono;

        var errors = Validar(dto);

        Assert.DoesNotContain(errors, e => e.MemberNames.Contains(nameof(CreateClienteDto.TelefonoPrincipal)));
    }

    // ---------- CreateClienteDto: teléfonos inválidos por regex ----------

    [Theory]
    [InlineData("12345")]                 // 5 dígitos (debajo del mínimo 6)
    [InlineData("12345678901234567890123456")] // 26 dígitos (sobre el máximo 25)
    [InlineData("abc123")]                // letras
    [InlineData("114455-66-77a")]         // letra al final
    [InlineData("11.4455.6677")]          // puntos NO permitidos (issue #117)
    [InlineData("11/4455-6677")]          // barra NO permitida
    [InlineData("*1144556677")]           // asterisco NO permitido
    public void CreateClienteDto_TelefonoFormatoInvalido_TieneErrorDeRegex(string telefono)
    {
        var dto = ClienteValido();
        dto.TelefonoPrincipal = telefono;

        var errors = Validar(dto);

        Assert.Contains(errors, e =>
            e.MemberNames.Contains(nameof(CreateClienteDto.TelefonoPrincipal))
            && e.ErrorMessage!.Contains("dígitos, espacios, guiones y signo +"));
    }

    // ---------- CreateClienteDto: requerido ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CreateClienteDto_TelefonoVacio_TieneErrorDeRequired(string? telefono)
    {
        var dto = ClienteValido();
        dto.TelefonoPrincipal = telefono!;

        var errors = Validar(dto);

        Assert.Contains(errors, e =>
            e.MemberNames.Contains(nameof(CreateClienteDto.TelefonoPrincipal))
            && e.ErrorMessage!.Contains("obligatorio"));
    }

    // ---------- UpdateClienteDto hereda la misma regla ----------

    [Fact]
    public void UpdateClienteDto_TelefonoFormatoInvalido_TieneErrorDeRegex()
    {
        // Create y Update comparten ClienteDtoBase → misma validación.
        // Si alguien rompe el atributo en la base, este test cubre la ruta Edit.
        var dto = new UpdateClienteDto
        {
            Id = 1,
            Nombre = "Juan",
            Apellido = "Pérez",
            TelefonoPrincipal = "abc",  // 3 chars + letras → falla regex y longitud
        };

        var errors = Validar(dto);

        Assert.Contains(errors, e =>
            e.MemberNames.Contains(nameof(UpdateClienteDto.TelefonoPrincipal))
            && e.ErrorMessage!.Contains("dígitos, espacios, guiones y signo +"));
    }

    [Fact]
    public void UpdateClienteDto_TelefonoArgentino_NoTieneError()
    {
        // Aceptación clave del issue #117: clientes existentes con formato
        // "+54 11 4455-6677" deben seguir siendo editables sin saltar validación.
        var dto = new UpdateClienteDto
        {
            Id = 1,
            Nombre = "Juan",
            Apellido = "Pérez",
            TelefonoPrincipal = "+54 11 4455-6677",
        };

        var errors = Validar(dto);

        Assert.DoesNotContain(errors, e => e.MemberNames.Contains(nameof(UpdateClienteDto.TelefonoPrincipal)));
    }
}