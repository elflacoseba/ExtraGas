using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using AutoMapper;
using ExtraGasMVC.Configuration;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Extensions;
using ExtraGasMVC.Models.ViewModels;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExtraGasMVC.Services.Implementations;

public class UsuarioService : IUsuarioService
{
    /// <summary>Vigencia del token de reset, en horas.</summary>
    private const int TokenLifetimeHours = 1;

    /// <summary>Costo BCrypt usado al setear la contrasena desde el flujo de reset.</summary>
    private const int ResetPasswordWorkFactor = 11;

    /// <summary>Mensaje generico: nunca expone detalle interno al usuario final.</summary>
    private const string GenericErrorMessage = "No se pudo procesar la solicitud. Intente nuevamente.";

    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;
    private readonly AuthLockoutOptions _lockout;
    private readonly IPasswordPolicyService _passwordPolicy;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<UsuarioService> _logger;

    public UsuarioService(
        ExtraGasDbContext context,
        IMapper mapper,
        IOptions<AuthLockoutOptions> lockout,
        IPasswordPolicyService passwordPolicy,
        IServiceScopeFactory scopeFactory,
        IOptions<EmailOptions> emailOptions,
        ILogger<UsuarioService> logger)
    {
        _context = context;
        _mapper = mapper;
        _lockout = lockout.Value;
        _passwordPolicy = passwordPolicy;
        _scopeFactory = scopeFactory;
        _emailOptions = emailOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Genera un token raw criptograficamente seguro (32 bytes) en base64url,
    /// 43 caracteres sin padding. El raw solo viaja por email: nunca se persiste.
    /// </summary>
    private static string GenerateRawToken()
    {
        var raw = RandomNumberGenerator.GetBytes(32);
        return Base64UrlTextEncoder.Encode(raw);
    }

    /// <summary>
    /// Calcula el SHA-256 hex (minusculas, 64 chars) del token raw.
    /// Es lo unico que se guarda en <c>password_reset_tokens.token_hash</c>.
    /// </summary>
    private static string HashToken(string rawToken)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(rawToken), hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Despacha un email sin bloquear la request. SMTP puede tardar segundos y
    /// un fallo de transporte no debe afectar la respuesta HTTP.
    /// Resuelve un <see cref="IEmailSender"/> desde un scope nuevo porque el scope
    /// de la request ya puede estar dispuesto cuando corre esta tarea.
    /// </summary>
    private void SendEmailFireAndForget(string to, string subject, string body)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                await sender.SendAsync(to, subject, body, ct: CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo el envio del email a {Recipient}", to);
            }
        });
    }

    public async Task<UsuarioDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        var usuario = await _context.Usuarios
            .AsNoTracking()
            .Include(u => u.Rol)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (usuario is null) return null;

        var dto = _mapper.Map<UsuarioDto>(usuario);
        await EnrichDtoAsync(dto, usuario, ct);
        return dto;
    }

    public async Task<PagedResult<UsuarioDto>> SearchAsync(
        string? busqueda, ulong? rolId, bool soloActivos,
        int pagina, int tamanio, CancellationToken ct = default)
    {
        var query = _context.Usuarios
            .AsNoTracking()
            .Include(u => u.Rol)
            .AsQueryable();

        if (soloActivos)
            query = query.Where(u => u.Activo);

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var q = busqueda.Trim();
            query = query.Where(u =>
                u.Username.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (u.Email != null && u.Email.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        if (rolId.HasValue)
            query = query.Where(u => u.RolId == rolId.Value);

        var total = await query.CountAsync(ct);

        var usuarios = await query
            .OrderBy(u => u.Username)
            .Skip((pagina - 1) * tamanio)
            .Take(tamanio)
            .ToListAsync(ct);

        var dtos = _mapper.Map<List<UsuarioDto>>(usuarios);
        await EnrichBatchAsync(dtos, usuarios, ct);

        return new PagedResult<UsuarioDto>
        {
            Items = dtos,
            Total = total,
            Page = pagina,
            PageSize = tamanio
        };
    }

    public async Task<List<RolDto>> GetRolesAsync(CancellationToken ct = default)
    {
        var roles = await _context.Roles
            .AsNoTracking()
            .OrderBy(r => r.Nombre)
            .ToListAsync(ct);

        return roles.Select(r => new RolDto
        {
            Id = r.Id,
            Nombre = r.Nombre,
            Codigo = r.Codigo
        }).ToList();
    }

    public async Task<List<EmpleadoSinUsuarioDto>> GetEmpleadosSinUsuarioAsync(CancellationToken ct = default)
    {
        return await _context.Empleados
            .AsNoTracking()
            .Where(e => e.UsuarioId == null && e.Activo)
            .OrderBy(e => e.Apellido)
            .ThenBy(e => e.Nombre)
            .Select(e => new EmpleadoSinUsuarioDto
            {
                Id = e.Id,
                NombreCompleto = e.Apellido + ", " + e.Nombre
            })
            .ToListAsync(ct);
    }

    public async Task<UsuarioDto?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        var usuario = await _context.Usuarios
            .AsNoTracking()
            .Include(u => u.Rol)
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        return usuario is null ? null : _mapper.Map<UsuarioDto>(usuario);
    }

    public async Task<LoginResult> ValidateAndLoadForAuthAsync(string username, string password, CancellationToken ct = default)
    {
        var usuario = await _context.Usuarios
            .IgnoreQueryFilters()
            .Include(u => u.Rol)
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        if (usuario is null)
            return LoginResult.Fail(attemptedUserId: null, LoginFailureReason.UserNotFound);

        // Precedencia: DeletedAt antes que Activo. Si el usuario esta soft-deleted
        // (independientemente de su Activo), reportamos UserDeleted para preservar
        // la fidelidad del historial aunque se restaure Activo=true a posteriori.
        if (usuario.DeletedAt is not null)
            return LoginResult.Fail(attemptedUserId: usuario.Id, LoginFailureReason.UserDeleted);

        if (!usuario.Activo)
            return LoginResult.Fail(attemptedUserId: usuario.Id, LoginFailureReason.UserInactive);

        // Lockout vigente: rechazar sin re-hashear (no delatar si la password era correcta).
        if (usuario.BloqueadoHasta is not null && usuario.BloqueadoHasta > DateTime.UtcNow)
            return LoginResult.Fail(attemptedUserId: usuario.Id, LoginFailureReason.LockedOut);

        if (!BCrypt.Net.BCrypt.Verify(password, usuario.PasswordHash))
        {
            await HandleFailedAttemptAsync(usuario, ct);
            return LoginResult.Fail(attemptedUserId: usuario.Id, LoginFailureReason.InvalidPassword);
        }

        // Éxito: resetear contador y lockout, actualizar último login.
        usuario.IntentosFallidos = 0;
        usuario.BloqueadoHasta = null;
        usuario.UltimoLogin = DateTime.UtcNow;
        usuario.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return LoginResult.Ok(_mapper.Map<UsuarioDto>(usuario));
    }

    private async Task HandleFailedAttemptAsync(Usuario usuario, CancellationToken ct)
    {
        // MaxFailedAttempts <= 0 desactiva el lockout (útil para tests o si se quiere apagar).
        if (_lockout.MaxFailedAttempts <= 0)
            return;

        usuario.IntentosFallidos++;

        if (usuario.IntentosFallidos >= _lockout.MaxFailedAttempts)
            usuario.BloqueadoHasta = DateTime.UtcNow.AddMinutes(_lockout.LockoutMinutes);

        usuario.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }

    public async Task<UsuarioDto> CreateAsync(CreateUsuarioDto dto, ulong? createdBy, CancellationToken ct = default)
    {
        var usuario = _mapper.Map<Usuario>(dto);
        // Issue #114: Activo no viene del DTO. Lo setea el Service en true
        // porque es estado, no dato de carga del operador.
        usuario.Activo = true;
        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
        usuario.CreatedAt = DateTime.UtcNow;
        usuario.UpdatedAt = DateTime.UtcNow;
        usuario.CreatedBy = createdBy;
        usuario.UpdatedBy = createdBy;

        _context.Usuarios.Add(usuario);
        await _context.SaveChangesAsync(ct);

        if (dto.EmpleadoId.HasValue)
        {
            var empleado = await _context.Empleados.FindAsync(new object[] { dto.EmpleadoId.Value }, ct);
            if (empleado is not null)
            {
                empleado.UsuarioId = usuario.Id;
                empleado.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
            }
        }

        return _mapper.Map<UsuarioDto>(usuario);
    }

    public async Task<UsuarioDto> UpdateAsync(UpdateUsuarioDto dto, ulong? updatedBy, CancellationToken ct = default)
    {
        var usuario = await _context.Usuarios
            .Include(u => u.Rol)
            .FirstOrDefaultAsync(u => u.Id == dto.Id, ct);

        if (usuario is null)
            throw new KeyNotFoundException($"Usuario con Id {dto.Id} no encontrado.");

        // Snapshot de Activo ANTES del AutoMapper: el formulario de Edit no
        // debe poder modificarlo. Si llega por bug del DTO, curl o form
        // antiguo en cache, lo restauramos silenciosamente.
        var activoOriginal = usuario.Activo;

        _mapper.Map(dto, usuario);
        usuario.UpdatedAt = DateTime.UtcNow;
        usuario.UpdatedBy = updatedBy;
        UsuarioEditRules.PreservarFlagsNoEditables(usuario, activoOriginal);

        await _context.SaveChangesAsync(ct);

        var result = _mapper.Map<UsuarioDto>(usuario);
        await EnrichDtoAsync(result, usuario, ct);
        return result;
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var usuario = await _context.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (usuario is null) return false;

        usuario.DeletedAt = DateTime.UtcNow;
        usuario.Activo = false;
        usuario.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return true;
    }

    public async Task<bool> ChangePasswordAsync(ulong id, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var usuario = await _context.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (usuario is null) return false;

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, usuario.PasswordHash))
            return false;

        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        usuario.DebeCambiarPassword = false;
        usuario.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return true;
    }

    public async Task ChangePasswordWithoutCurrentAsync(ulong id, string newPassword, CancellationToken ct = default)
    {
        var usuario = await _context.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (usuario is null)
            throw new KeyNotFoundException($"Usuario con Id {id} no encontrado.");

        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        usuario.DebeCambiarPassword = false;
        usuario.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }

    public async Task<string> ResetPasswordAsync(ulong id, ulong? updatedBy, CancellationToken ct = default)
    {
        var usuario = await _context.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (usuario is null)
            throw new KeyNotFoundException($"Usuario con Id {id} no encontrado.");

        var temporaryPassword = TemporaryPasswordGenerator.Generate(12);
        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword);
        usuario.DebeCambiarPassword = true;
        usuario.IntentosFallidos = 0;
        usuario.BloqueadoHasta = null;
        usuario.UpdatedAt = DateTime.UtcNow;
        usuario.UpdatedBy = updatedBy;
        await _context.SaveChangesAsync(ct);

        return temporaryPassword;
    }

    public async Task RequestPasswordResetAsync(string email, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return;

        var normalizedEmail = email.Trim();

        var usuario = await _context.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.Activo && u.DeletedAt == null, ct);

        // Anti-enumeracion: email desconocido, usuario inactivo/borrado o sin
        // email cargado => salimos en silencio, sin escribir ni enviar nada.
        if (usuario is null || string.IsNullOrWhiteSpace(usuario.Email))
            return;

        var rawToken = GenerateRawToken();
        var now = DateTime.UtcNow;

        var token = new PasswordResetToken
        {
            UsuarioId = usuario.Id,
            TokenHash = HashToken(rawToken),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            ExpiresAt = now.AddHours(TokenLifetimeHours),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = null,
            UpdatedBy = null
        };

        try
        {
            _context.PasswordResetTokens.Add(token);
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Incluye la colision de uk_token_hash (practicamente imposible en 2^256).
            _logger.LogError(ex, "No se pudo persistir el token de reset del usuario {UsuarioId}", usuario.Id);
            return;
        }

        var resetUrl = $"{_emailOptions.BaseUrl.TrimEnd('/')}/Account/ResetPassword?token={rawToken}";

        SendEmailFireAndForget(
            usuario.Email,
            EmailTemplates.ResetLinkSubject,
            EmailTemplates.ResetLink(usuario.Username, resetUrl));
    }

    public async Task<ConsumeResetTokenResult> ConsumePasswordResetTokenAsync(string rawToken, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return new ConsumeResetTokenResult(ConsumeResetTokenOutcome.InvalidToken, "Enlace inválido.");

        var policy = _passwordPolicy.Validate(newPassword);
        if (!policy.IsValid)
            return new ConsumeResetTokenResult(ConsumeResetTokenOutcome.WeakPassword, string.Join(" ", policy.Errors));

        var tokenHash = HashToken(rawToken);
        var now = DateTime.UtcNow;

        try
        {
            // Uso unico: el gate es el rows-affected del UPDATE atomico, no un SELECT
            // previo. InnoDB serializa los intentos concurrentes via uk_token_hash.
            var rowsAffected = await _context.PasswordResetTokens
                .IgnoreQueryFilters()
                .Where(rt => rt.TokenHash == tokenHash
                          && rt.UsedAt == null
                          && rt.ExpiresAt > now
                          && rt.DeletedAt == null)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(rt => rt.UsedAt, (DateTime?)now)
                        .SetProperty(rt => rt.UpdatedAt, now),
                    ct);

            if (rowsAffected == 0)
                return await DiagnoseFailedConsumeAsync(tokenHash, now, ct);

            var usuarioId = await _context.PasswordResetTokens
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(rt => rt.TokenHash == tokenHash)
                .Select(rt => rt.UsuarioId)
                .FirstOrDefaultAsync(ct);

            var usuario = await _context.Usuarios
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == usuarioId, ct);

            if (usuario is null)
            {
                _logger.LogError("Token de reset consumido pero el usuario {UsuarioId} no existe.", usuarioId);
                return new ConsumeResetTokenResult(ConsumeResetTokenOutcome.UnknownError, GenericErrorMessage);
            }

            usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: ResetPasswordWorkFactor);
            usuario.DebeCambiarPassword = false;
            usuario.UpdatedAt = now;
            await _context.SaveChangesAsync(ct);

            if (!string.IsNullOrWhiteSpace(usuario.Email))
            {
                SendEmailFireAndForget(
                    usuario.Email,
                    EmailTemplates.PasswordChangedSubject,
                    EmailTemplates.PasswordChanged(usuario.Username));
            }

            return new ConsumeResetTokenResult(ConsumeResetTokenOutcome.Success);
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException)
        {
            _logger.LogError(ex, "Error de base de datos al consumir un token de reset.");
            return new ConsumeResetTokenResult(ConsumeResetTokenOutcome.UnknownError, GenericErrorMessage);
        }
    }

    /// <summary>
    /// Distingue por que el UPDATE atomico no afecto filas: token inexistente,
    /// ya consumido o vencido. Solo corre en el camino de error.
    /// </summary>
    private async Task<ConsumeResetTokenResult> DiagnoseFailedConsumeAsync(string tokenHash, DateTime now, CancellationToken ct)
    {
        var token = await _context.PasswordResetTokens
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

        if (token is null)
            return new ConsumeResetTokenResult(ConsumeResetTokenOutcome.InvalidToken, "Enlace inválido.");

        if (token.UsedAt is not null)
            return new ConsumeResetTokenResult(ConsumeResetTokenOutcome.AlreadyUsed, "Este enlace ya fue utilizado.");

        if (token.ExpiresAt <= now)
            return new ConsumeResetTokenResult(ConsumeResetTokenOutcome.ExpiredToken, "El enlace ha expirado. Solicitá uno nuevo.");

        return new ConsumeResetTokenResult(ConsumeResetTokenOutcome.UnknownError, GenericErrorMessage);
    }

    private async Task EnrichDtoAsync(UsuarioDto dto, Usuario usuario, CancellationToken ct)
    {
        if (usuario.CreatedBy.HasValue)
        {
            var creador = await _context.Usuarios
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == usuario.CreatedBy.Value, ct);
            dto.CreadoPor = creador?.Username;
        }

        if (usuario.UpdatedBy.HasValue)
        {
            var actualizador = await _context.Usuarios
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == usuario.UpdatedBy.Value, ct);
            dto.ActualizadoPor = actualizador?.Username;
        }

        var empleado = await _context.Empleados
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UsuarioId == dto.Id, ct);

        if (empleado is not null)
        {
            dto.EmpleadoId = empleado.Id;
            dto.EmpleadoNombre = $"{empleado.Apellido}, {empleado.Nombre}";
        }
    }

    private async Task EnrichBatchAsync(List<UsuarioDto> dtos, List<Usuario> usuarios, CancellationToken ct)
    {
        var auditUsers = await LoadAuditUsersAsync(usuarios, ct);
        var empleadosByUsuarioId = await LoadEmpleadosByUsuarioIdAsync(dtos, ct);

        foreach (var dto in dtos)
        {
            var usuario = usuarios.First(u => u.Id == dto.Id);
            AplicarAudit(dto, usuario, auditUsers);
            AplicarEmpleado(dto, empleadosByUsuarioId);
        }
    }

    /// <summary>
    /// Recolecta los IDs de CreatedBy/UpdatedBy de todos los usuarios y
    /// devuelve un diccionario Id → Username para resolver auditores en
    /// una sola query. Devuelve diccionario vacío si no hay IDs.
    /// </summary>
    private async Task<Dictionary<ulong, string>> LoadAuditUsersAsync(
        IEnumerable<Usuario> usuarios, CancellationToken ct)
    {
        var auditUserIds = new HashSet<ulong>();
        foreach (var usuario in usuarios)
        {
            if (usuario.CreatedBy.HasValue) auditUserIds.Add(usuario.CreatedBy.Value);
            if (usuario.UpdatedBy.HasValue) auditUserIds.Add(usuario.UpdatedBy.Value);
        }

        if (auditUserIds.Count == 0) return new Dictionary<ulong, string>();

        return await _context.Usuarios
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(u => auditUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);
    }

    /// <summary>
    /// Devuelve los empleados vinculados a cada Usuario en un diccionario
    /// UsuarioId → Empleado. Diccionario vacío si ningún usuario tiene
    /// empleado vinculado.
    /// </summary>
    private async Task<Dictionary<ulong, Empleado>> LoadEmpleadosByUsuarioIdAsync(
        IEnumerable<UsuarioDto> dtos, CancellationToken ct)
    {
        var usuarioIds = dtos.Select(d => d.Id).ToList();
        if (usuarioIds.Count == 0) return new Dictionary<ulong, Empleado>();

        var empleados = await _context.Empleados
            .AsNoTracking()
            .Where(e => e.UsuarioId.HasValue && usuarioIds.Contains(e.UsuarioId!.Value))
            .ToListAsync(ct);

        return empleados.ToDictionary(e => e.UsuarioId!.Value);
    }

    /// <summary>
    /// Copia el username del auditor (CreatedBy / UpdatedBy) en el DTO si
    /// el auditor existe. Sin excepción si el auditor fue soft-deleted.
    /// </summary>
    private static void AplicarAudit(
        UsuarioDto dto, Usuario usuario, Dictionary<ulong, string> auditUsers)
    {
        if (usuario.CreatedBy.HasValue && auditUsers.TryGetValue(usuario.CreatedBy.Value, out var creador))
            dto.CreadoPor = creador;

        if (usuario.UpdatedBy.HasValue && auditUsers.TryGetValue(usuario.UpdatedBy.Value, out var actualizador))
            dto.ActualizadoPor = actualizador;
    }

    /// <summary>
    /// Copia el EmpleadoId y el nombre formateado del empleado vinculado
    /// al Usuario. Sin excepción si el usuario no tiene empleado.
    /// </summary>
    private static void AplicarEmpleado(
        UsuarioDto dto, Dictionary<ulong, Empleado> empleadosByUsuarioId)
    {
        if (!empleadosByUsuarioId.TryGetValue(dto.Id, out var empleado)) return;

        dto.EmpleadoId = empleado.Id;
        dto.EmpleadoNombre = $"{empleado.Apellido}, {empleado.Nombre}";
    }
}
