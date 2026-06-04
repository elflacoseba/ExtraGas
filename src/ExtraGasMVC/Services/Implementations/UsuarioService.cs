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
        await EnrichAuditAsync(dto, usuario, ct);
        await EnrichEmpleadoAsync(dto, ct);
        return dto;
    }

    public async Task<IEnumerable<UsuarioDto>> GetAllAsync(CancellationToken ct = default)
    {
        var usuarios = await _context.Usuarios
            .AsNoTracking()
            .Include(u => u.Rol)
            .OrderBy(u => u.Username)
            .ToListAsync(ct);

        var dtos = _mapper.Map<IEnumerable<UsuarioDto>>(usuarios).ToList();
        foreach (var dto in dtos)
        {
            var usuario = usuarios.First(u => u.Id == dto.Id);
            await EnrichAuditAsync(dto, usuario, ct);
            await EnrichEmpleadoAsync(dto, ct);
        }
        return dtos;
    }

    public async Task<IEnumerable<UsuarioDto>> GetActivosAsync(CancellationToken ct = default)
    {
        var usuarios = await _context.Usuarios
            .AsNoTracking()
            .Include(u => u.Rol)
            .Where(u => u.Activo)
            .OrderBy(u => u.Username)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<UsuarioDto>>(usuarios);
    }

    public async Task<UsuarioDto?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        var usuario = await _context.Usuarios
            .AsNoTracking()
            .Include(u => u.Rol)
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        return usuario is null ? null : _mapper.Map<UsuarioDto>(usuario);
    }

    public async Task<UsuarioDto?> GetByUsernameForAuthAsync(string username, CancellationToken ct = default)
    {
        var usuario = await _context.Usuarios
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(u => u.Rol)
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        return usuario is null ? null : _mapper.Map<UsuarioDto>(usuario);
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

        if (!dto.Activo && usuario.DeletedAt is null)
            usuario.DeletedAt = DateTime.UtcNow;
        else if (dto.Activo && usuario.DeletedAt is not null)
            usuario.DeletedAt = null;

        await _context.SaveChangesAsync(ct);

        var result = _mapper.Map<UsuarioDto>(usuario);
        await EnrichAuditAsync(result, usuario, ct);
        await EnrichEmpleadoAsync(result, ct);
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

    public async Task<bool> ValidateLoginAsync(string username, string password, CancellationToken ct = default)
    {
        var usuario = await _context.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        if (usuario is null) return false;
        if (usuario.DeletedAt is not null) return false;
        if (!usuario.Activo) return false;
        if (!BCrypt.Net.BCrypt.Verify(password, usuario.PasswordHash)) return false;

        usuario.UltimoLogin = DateTime.UtcNow;
        usuario.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return true;
    }

    private async Task EnrichAuditAsync(UsuarioDto dto, Usuario usuario, CancellationToken ct)
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
    }

    private async Task EnrichEmpleadoAsync(UsuarioDto dto, CancellationToken ct)
    {
        var empleado = await _context.Empleados
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UsuarioId == dto.Id, ct);

        if (empleado is not null)
        {
            dto.EmpleadoId = empleado.Id;
            dto.EmpleadoNombre = $"{empleado.Apellido}, {empleado.Nombre}";
        }
    }
}
