using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services;

namespace ExtraGasMVC.Services.Interfaces;

public interface IAuditoriaLoginService
{
    /// <summary>
    /// Registra un intento de login. Se llama una vez por intento,
    /// exista o no el usuario (usernameIntentado siempre se persiste).
    /// </summary>
    Task RecordAsync(
        string usernameIntentado,
        ulong? usuarioId,
        bool exito,
        LoginFailureReason motivoFallo,
        string? ipOrigen,
        string? userAgent,
        CancellationToken ct = default);

    Task<SearchResultDto<AuditoriaLoginListDto>> SearchAsync(
        string? busqueda,
        string? ip,
        bool soloFallidos,
        int pagina,
        int tamanio,
        CancellationToken ct = default);
}
