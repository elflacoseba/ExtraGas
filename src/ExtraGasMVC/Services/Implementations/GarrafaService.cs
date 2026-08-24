using AutoMapper;
using ExtraGasMVC.Constants;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Data.Entities;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ExtraGasMVC.Services.Implementations;

public class GarrafaService : IGarrafaService
{
    private readonly ExtraGasDbContext _context;
    private readonly IMapper _mapper;

    public GarrafaService(ExtraGasDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<GarrafaDto?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        var garrafa = await _context.Garrafas
            .AsNoTracking()
            .Include(g => g.EstadoGarrafa)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

        return garrafa is null ? null : _mapper.Map<GarrafaDto>(garrafa);
    }

    public async Task<GarrafaDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
    {
        var garrafa = await _context.Garrafas
            .AsNoTracking()
            .Include(g => g.EstadoGarrafa)
            .FirstOrDefaultAsync(g => g.Codigo == codigo, ct);

        return garrafa is null ? null : _mapper.Map<GarrafaDto>(garrafa);
    }

    public async Task<IEnumerable<GarrafaDto>> GetAllAsync(CancellationToken ct = default)
    {
        var garrafas = await _context.Garrafas
            .AsNoTracking()
            .Include(g => g.EstadoGarrafa)
            .OrderBy(g => g.Codigo)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<GarrafaDto>>(garrafas);
    }

    public async Task<IEnumerable<GarrafaDto>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default)
    {
        var garrafas = await _context.Garrafas
            .AsNoTracking()
            .Include(g => g.EstadoGarrafa)
            .Where(g => g.ClienteId == clienteId)
            .OrderBy(g => g.Codigo)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<GarrafaDto>>(garrafas);
    }

    public async Task<IEnumerable<GarrafaDto>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default)
    {
        var garrafas = await _context.Garrafas
            .AsNoTracking()
            .Include(g => g.EstadoGarrafa)
            .Where(g => g.EstadoGarrafaId == estadoId)
            .OrderBy(g => g.Codigo)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<GarrafaDto>>(garrafas);
    }

    public async Task<IEnumerable<EstadoGarrafaDto>> GetEstadosAsync(CancellationToken ct = default)
    {
        var estados = await _context.EstadosGarrafa
            .AsNoTracking()
            .OrderBy(e => e.Nombre)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<EstadoGarrafaDto>>(estados);
    }

    public async Task<GarrafaDto> CreateAsync(CreateGarrafaDto garrafaDto, CancellationToken ct = default)
    {
        if (await _context.Garrafas.AnyAsync(g => g.Codigo == garrafaDto.Codigo, ct))
            throw new InvalidOperationException($"Ya existe una garrafa con el código {garrafaDto.Codigo}.");

        var garrafa = _mapper.Map<Garrafa>(garrafaDto);
        garrafa.CreatedAt = DateTime.UtcNow;
        garrafa.UpdatedAt = DateTime.UtcNow;

        _context.Garrafas.Add(garrafa);
        await SaveOrThrowDuplicateAsync(garrafaDto.Codigo, ct);

        return _mapper.Map<GarrafaDto>(garrafa);
    }

    public async Task<GarrafaDto> UpdateAsync(UpdateGarrafaDto garrafaDto, CancellationToken ct = default)
    {
        var garrafa = await _context.Garrafas.FindAsync(new object[] { garrafaDto.Id }, ct);
        if (garrafa == null)
            throw new KeyNotFoundException($"Garrafa con Id {garrafaDto.Id} no encontrada.");

        if (await _context.Garrafas.AnyAsync(g => g.Codigo == garrafaDto.Codigo && g.Id != garrafaDto.Id, ct))
            throw new InvalidOperationException($"Ya existe una garrafa con el código {garrafaDto.Codigo}.");

        _mapper.Map(garrafaDto, garrafa);
        garrafa.UpdatedAt = DateTime.UtcNow;

        await SaveOrThrowDuplicateAsync(garrafaDto.Codigo, ct);

        return _mapper.Map<GarrafaDto>(garrafa);
    }

    public async Task<bool> CambiarEstadoAsync(ulong id, CambiarEstadoGarrafaDto dto, CancellationToken ct = default)
    {
        var garrafa = await _context.Garrafas.FindAsync(new object[] { id }, ct);
        if (garrafa == null)
            return false;

        // Cargar ambos extremos de la transición (origen y destino) en una sola
        // consulta para poder validar contra GarrafaTransiciones y contra las
        // reglas del catálogo (requiere_cliente, etc.).
        var extremos = await _context.EstadosGarrafa
            .AsNoTracking()
            .Where(e => e.Id == garrafa.EstadoGarrafaId || e.Id == dto.NuevoEstadoId)
            .Select(e => new { e.Id, e.Codigo, e.RequiereCliente, e.Nombre })
            .ToListAsync(ct);

        var origen = extremos.FirstOrDefault(e => e.Id == garrafa.EstadoGarrafaId);
        var destino = extremos.FirstOrDefault(e => e.Id == dto.NuevoEstadoId);

        if (origen is null)
            throw new InvalidOperationException(
                $"El estado actual de la garrafa (id={garrafa.EstadoGarrafaId}) no existe en el catálogo estados_garrafa.");

        if (destino is null)
            throw new InvalidOperationException(
                $"El estado destino solicitado (id={dto.NuevoEstadoId}) no existe en el catálogo estados_garrafa.");

        // Issue #40: validar la transición contra la matriz de estados.
        if (!GarrafaTransiciones.EsValida(origen.Codigo, destino.Codigo))
        {
            throw new InvalidOperationException(
                $"Transición inválida: {origen.Nombre} ({origen.Codigo}) → {destino.Nombre} ({destino.Codigo}). " +
                $"Consulte la matriz de transiciones válidas en la documentación del módulo Garrafas.");
        }

        // Si el estado destino exige un cliente (p.ej. EN_CLIENTE) el DTO debe traerlo.
        // El trigger trg_garrafas_bi_validate sólo cubre INSERT — para los cambios
        // de estado hechos por CAMBIO_ESTADO la validación la hace la app.
        if (destino.RequiereCliente && !dto.ClienteId.HasValue)
        {
            throw new InvalidOperationException(
                $"El estado {destino.Nombre} requiere seleccionar un cliente.");
        }

        var tipoCambioEstadoId = await _context.TiposMovimientoGarrafa
            .AsNoTracking()
            .Where(t => t.Codigo == "CAMBIO_ESTADO")
            .Select(t => t.Id)
            .FirstOrDefaultAsync(ct);

        if (tipoCambioEstadoId == 0)
            throw new InvalidOperationException("No se encontró el tipo de movimiento CAMBIO_ESTADO en la base de datos.");

        var estadoOrigen = garrafa.EstadoGarrafaId;

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        try
        {
            // NOTA: estado_garrafa_id y fecha_ultimo_movimiento NO se actualizan acá.
            // El trigger trg_mov_garrafa_ai los setea automáticamente desde el
            // movimiento (estado_destino_id y fecha) para mantener una sola fuente
            // de verdad. La app solo actualiza los campos que el trigger no toca.
            garrafa.ClienteId = dto.ClienteId;
            garrafa.UpdatedAt = DateTime.UtcNow;

            var movimiento = new MovimientoGarrafa
            {
                GarrafaId = garrafa.Id,
                Fecha = DateTime.UtcNow,
                TipoMovimientoId = tipoCambioEstadoId,
                ClienteId = dto.ClienteId,
                EstadoOrigenId = estadoOrigen,
                EstadoDestinoId = dto.NuevoEstadoId,
                Observaciones = dto.Observaciones
            };

            _context.MovimientosGarrafa.Add(movimiento);

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return true;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IEnumerable<EstadoGarrafaDto>> GetTransicionesDisponiblesAsync(ulong garrafaId, CancellationToken ct = default)
    {
        // Solo necesitamos el código del estado actual para consultar la matriz;
        // hacerlo con un join manual evita tener que añadir una navigation
        // property a Garrafa (la entidad lo mantiene plano por convención).
        var estadoCodigo = await (
            from g in _context.Garrafas.AsNoTracking()
            join e in _context.EstadosGarrafa.AsNoTracking()
                on g.EstadoGarrafaId equals e.Id
            where g.Id == garrafaId
            select e.Codigo
        ).FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(estadoCodigo))
            return Array.Empty<EstadoGarrafaDto>();

        var codigosPermitidos = GarrafaTransiciones.DestinosPermitidos(estadoCodigo);
        if (codigosPermitidos.Count == 0)
            return Array.Empty<EstadoGarrafaDto>();

        // Filtra el catálogo por los códigos permitidos y mantiene el orden
        // alfabético que usa GetEstadosAsync para que la UI sea consistente.
        var destinos = await _context.EstadosGarrafa
            .AsNoTracking()
            .Where(e => codigosPermitidos.Contains(e.Codigo))
            .OrderBy(e => e.Nombre)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<EstadoGarrafaDto>>(destinos);
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var garrafa = await _context.Garrafas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.Id == id, ct);

        if (garrafa == null)
            return false;

        var codigosBloqueados = new[] { "EN_CLIENTE", "EN_TRANSITO" };
        var estadoCodigo = await _context.EstadosGarrafa
            .AsNoTracking()
            .Where(e => e.Id == garrafa.EstadoGarrafaId)
            .Select(e => e.Codigo)
            .FirstOrDefaultAsync(ct);

        if (estadoCodigo != null && codigosBloqueados.Contains(estadoCodigo))
            throw new InvalidOperationException(
                $"No se puede eliminar una garrafa en estado {estadoCodigo}. Primero cambie su estado.");

        garrafa.DeletedAt = DateTime.UtcNow;
        garrafa.Activo = false;
        garrafa.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return true;
    }

    public async Task<IEnumerable<MovimientoGarrafaDto>> GetHistorialAsync(ulong garrafaId, CancellationToken ct = default)
    {
        // Primero verificamos que la garrafa exista (incluso soft-deleted) para
        // devolver 404 coherente desde el controller. Si no existe, enumerable vacío.
        var garrafaExiste = await _context.Garrafas
            .IgnoreQueryFilters()
            .AnyAsync(g => g.Id == garrafaId, ct);

        if (!garrafaExiste)
            return Array.Empty<MovimientoGarrafaDto>();

        // Joins manuales a las tablas de lookup para traer los nombres legibles.
        // Sigue el mismo patrón que GetTransicionesDisponiblesAsync (no hay navigation
        // properties confiables en la entidad, así que se hace el join a mano).
        // Pero como en este caso sí agregamos navigation properties a MovimientoGarrafa,
        // usamos Include para mantener la consistencia con GetByIdAsync/GetAllAsync.
        var movimientos = await _context.MovimientosGarrafa
            .AsNoTracking()
            .Include(m => m.TipoMovimiento)
            .Include(m => m.EstadoOrigen)
            .Include(m => m.EstadoDestino)
            .Include(m => m.Empleado)
            .Where(m => m.GarrafaId == garrafaId)
            .OrderByDescending(m => m.Fecha)
            .ThenByDescending(m => m.Id)
            .ToListAsync(ct);

        return _mapper.Map<IEnumerable<MovimientoGarrafaDto>>(movimientos);
    }

    private async Task SaveOrThrowDuplicateAsync(string codigo, CancellationToken ct)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException dbex) when (dbex.InnerException is MySqlException my && my.Number == 1062)
        {
            throw new InvalidOperationException($"Ya existe una garrafa con el código {codigo}.");
        }
    }
}
