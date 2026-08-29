using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class AuditoriaLoginService : IAuditoriaLoginService
{
    private readonly ExtraGasDbContext _context;

    public AuditoriaLoginService(ExtraGasDbContext context)
    {
        _context = context;
    }

    public async Task RecordAsync(
        string usernameIntentado,
        ulong? usuarioId,
        bool exito,
        LoginFailureReason motivoFallo,
        string? ipOrigen,
        string? userAgent,
        CancellationToken ct = default)
    {
        var registro = new AuditoriaLogin
        {
            UsernameIntentado = usernameIntentado,
            UsuarioId = usuarioId,
            Exito = exito,
            MotivoFallo = exito ? null : MapMotivo(motivoFallo),
            IpOrigen = ipOrigen,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow,
        };

        _context.AuditoriaLogins.Add(registro);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<SearchResultDto<AuditoriaLoginListDto>> SearchAsync(
        string? busqueda,
        string? ip,
        bool soloFallidos,
        int pagina,
        int tamanio,
        CancellationToken ct = default)
    {
        var query = _context.AuditoriaLogins
            .AsNoTracking()
            .Include(a => a.Usuario)
            .AsQueryable();

        if (soloFallidos)
            query = query.Where(a => !a.Exito);

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var q = busqueda.Trim();
            query = query.Where(a => a.UsernameIntentado.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(ip))
        {
            var ipFilter = ip.Trim();
            query = query.Where(a => a.IpOrigen == ipFilter);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((pagina - 1) * tamanio)
            .Take(tamanio)
            .ToListAsync(ct);

        var dtos = items.Select(a => new AuditoriaLoginListDto
        {
            Id = a.Id,
            UsernameIntentado = a.UsernameIntentado,
            UsuarioId = a.UsuarioId,
            UsuarioNombre = a.Usuario?.Username,
            Exito = a.Exito,
            MotivoFallo = a.MotivoFallo,
            MotivoFalloLegible = a.Exito ? "OK" : MapMotivoLegible(a.MotivoFallo),
            IpOrigen = a.IpOrigen,
            UserAgent = a.UserAgent,
            CreatedAt = a.CreatedAt,
        }).ToList();

        return new SearchResultDto<AuditoriaLoginListDto>
        {
            Items = dtos,
            Total = total,
            Pagina = pagina,
            Tamanio = tamanio,
        };
    }

    private static string? MapMotivo(LoginFailureReason motivo) => motivo switch
    {
        LoginFailureReason.None => null,
        LoginFailureReason.UserNotFound => "USER_NOT_FOUND",
        LoginFailureReason.UserInactive => "USER_INACTIVE",
        LoginFailureReason.UserDeleted => "USER_DELETED",
        LoginFailureReason.InvalidPassword => "INVALID_PASSWORD",
        LoginFailureReason.LockedOut => "LOCKED_OUT",
        _ => "UNKNOWN",
    };

    private static string MapMotivoLegible(string? motivoCodigo) => motivoCodigo switch
    {
        "USER_NOT_FOUND" => "Usuario inexistente",
        "USER_INACTIVE" => "Usuario inactivo",
        "USER_DELETED" => "Usuario eliminado",
        "INVALID_PASSWORD" => "Password incorrecta",
        "LOCKED_OUT" => "Cuenta bloqueada",
        _ => motivoCodigo ?? "Desconocido",
    };
}
