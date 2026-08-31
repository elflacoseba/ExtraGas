namespace ExtraGasMVC.Exceptions;

/// <summary>
/// Excepción que el Service arroja cuando una entrada del operador es
/// rechazable en términos de <em>validez</em> — no por invariantes internas
/// (<c>InvalidOperationException</c>) ni por ausencia del recurso
/// (<c>KeyNotFoundException</c>). El Controller la traduce a un error
/// 400 con el mensaje intacto, devolviendo al usuario la causa real del
/// rechazo en lugar de un 500 por FK constraint violada en MySQL.
///
/// <para>Issue #146: las validaciones que la issue pide (TipoProductoId
/// inexistente, Codigo duplicado, GARRAFA sin capacidad) caen en esta
/// categoría. Antes se propagaban como errores opacos del DB engine porque
/// la app no validaba en el borde.</para>
/// </summary>
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }

    /// <summary>
    /// Construye la excepción preservando la inner exception. Útil cuando se
    /// detecta un fallo no-recuperable (ej. <c>DbUpdateConcurrencyException</c>)
    /// y se quiere traducir a un error de validación sin perder el stack
    /// trace original en logs. Issue #158 (S2139).
    /// </summary>
    public ValidationException(string message, Exception innerException) : base(message, innerException) { }
}
