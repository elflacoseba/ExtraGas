namespace ExtraGasMVC.Constants;

/// <summary>
/// Canonical <c>TempData</c> keys shared across controllers. Centralizing
/// them here avoids magic-string duplication (SonarQube csharpsquid:S1192)
/// and makes it harder to typo a key silently.
///
/// These are keys, not values — the string the user actually sees in the UI
/// still lives in the caller (each <c>TempData[key] = "..."</c> line).
/// Issue #136.
/// </summary>
public static class TempDataKeys
{
    /// <summary>Success/info banner shown via <c>_StatusMessage.cshtml</c>.</summary>
    public const string Success = "Success";

    /// <summary>Failure/error banner shown via <c>_StatusMessage.cshtml</c>.</summary>
    public const string Error = "Error";

    /// <summary>Neutral info banner (no semantic = success).</summary>
    public const string Info = "Info";

    /// <summary>Temporary password surfaced to the admin after a reset (consumed once).</summary>
    public const string TemporaryPassword = "TemporaryPassword";

    /// <summary>Username associated with the temporary password (consumed once).</summary>
    public const string TemporaryPasswordUsername = "TemporaryPasswordUsername";

    /// <summary>Default user-facing message when an entity is not found.</summary>
    public const string PedidoNotFoundMessage = "No se encontró el pedido.";
}
