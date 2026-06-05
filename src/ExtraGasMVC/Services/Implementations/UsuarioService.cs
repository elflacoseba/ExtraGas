using AutoMapper;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExtraGasMVC.Services.Implementations;

public class UsuarioService : IUsuarioService
{
    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;

    public UsuarioService(ExtraGasDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
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

    public async Task<SearchResultDto<UsuarioDto>> SearchAsync(
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
            var q = busqueda.Trim().ToLower();
            query = query.Where(u =>
                u.Username.ToLower().Contains(q)
                || (u.Email != null && u.Email.ToLower().Contains(q)));
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

        return new SearchResultDto<UsuarioDto>
        {
            Items = dtos,
            Total = total,
            Pagina = pagina,
            Tamanio = tamanio
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

    public async Task<UsuarioDto?> ValidateAndLoadForAuthAsync(string username, string password, CancellationToken ct = default)
    {
        var usuario = await _context.Usuarios
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(u => u.Rol)
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        if (usuario is null) return null;
        if (usuario.DeletedAt is not null) return null;
        if (!usuario.Activo) return null;
        if (!BCrypt.Net.BCrypt.Verify(password, usuario.PasswordHash)) return null;

        usuario.UltimoLogin = DateTime.UtcNow;
        usuario.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return _mapper.Map<UsuarioDto>(usuario);
    }

    public async Task<UsuarioDto> CreateAsync(CreateUsuarioDto dto, ulong createdBy, CancellationToken ct = default)
    {
        var usuario = _mapper.Map<Usuario>(dto);
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

    public async Task<UsuarioDto> UpdateAsync(UpdateUsuarioDto dto, ulong updatedBy, CancellationToken ct = default)
    {
        var usuario = await _context.Usuarios
            .Include(u => u.Rol)
            .FirstOrDefaultAsync(u => u.Id == dto.Id, ct);

        if (usuario is null)
            throw new KeyNotFoundException($"Usuario con Id {dto.Id} no encontrado.");

        _mapper.Map(dto, usuario);
        usuario.UpdatedAt = DateTime.UtcNow;
        usuario.UpdatedBy = updatedBy;

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
        usuario.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return true;
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
        var auditUserIds = new HashSet<ulong>();

        foreach (var usuario in usuarios)
        {
            if (usuario.CreatedBy.HasValue) auditUserIds.Add(usuario.CreatedBy.Value);
            if (usuario.UpdatedBy.HasValue) auditUserIds.Add(usuario.UpdatedBy.Value);
        }

        var auditUsers = auditUserIds.Count > 0
            ? await _context.Usuarios
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(u => auditUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Username, ct)
            : new Dictionary<ulong, string>();

        var usuarioIds = dtos.Select(d => d.Id).ToList();
        var empleados = usuarioIds.Count > 0
            ? await _context.Empleados
                .AsNoTracking()
                .Where(e => e.UsuarioId.HasValue && usuarioIds.Contains(e.UsuarioId!.Value))
                .ToListAsync(ct)
            : new List<Empleado>();

        var empleadosDict = empleados.ToDictionary(e => e.UsuarioId!.Value);

        foreach (var dto in dtos)
        {
            var usuario = usuarios.First(u => u.Id == dto.Id);

            if (usuario.CreatedBy.HasValue && auditUsers.TryGetValue(usuario.CreatedBy.Value, out var creador))
                dto.CreadoPor = creador;

            if (usuario.UpdatedBy.HasValue && auditUsers.TryGetValue(usuario.UpdatedBy.Value, out var actualizador))
                dto.ActualizadoPor = actualizador;

            if (empleadosDict.TryGetValue(dto.Id, out var empleado))
            {
                dto.EmpleadoId = empleado.Id;
                dto.EmpleadoNombre = $"{empleado.Apellido}, {empleado.Nombre}";
            }
        }
    }
}
