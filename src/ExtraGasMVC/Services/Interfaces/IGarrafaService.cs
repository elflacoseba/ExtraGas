using ExtraGasMVC.Data.Entities.Views;
using ExtraGasMVC.DTOs;
using ExtraGasMVC.Models.ViewModels;

namespace ExtraGasMVC.Services.Interfaces;

public interface IGarrafaService
{
    Task<GarrafaDto?> GetByIdAsync(ulong id, CancellationToken ct = default);
    Task<GarrafaDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default);
    Task<IEnumerable<GarrafaDto>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<GarrafaDto>> GetByClienteAsync(ulong clienteId, CancellationToken ct = default);
    Task<IEnumerable<GarrafaDto>> GetByEstadoAsync(ulong estadoId, CancellationToken ct = default);

    /// <summary>
    /// Devuelve una página de garrafas filtrando en SQL (no en memoria) y
    /// contando el total de filas que satisfacen los filtros para alimentar
    /// los controles de paginación. Usado por <c>GarrafasController.Index</c>
    /// (issue #52). Los filtros <paramref name="codigo"/> y
    /// <paramref name="capacidad"/> son opcionales; cuando ambos son null,
    /// equivale a contar todas las garrafas activas (soft-deleted excluidas
    /// por el filtro global del DbContext).
    /// </summary>
    /// <param name="page">Número de página 1-based. Valores &lt; 1 se
    /// normalizan a 1.</param>
    /// <param name="pageSize">Tamaño de página. Default 20. Se aplica un
    /// tope máximo de 100 para evitar queries enormes accidentales.</param>
    /// <param name="sortBy">
    /// Campo por el que se ordena la página. Valores reconocidos:
    /// <c>codigo</c> (default), <c>capacidad</c>, <c>estado</c>,
    /// <c>cliente</c>, <c>fechacompra</c>, <c>ultimomov</c>. Cualquier otro
    /// valor cae al default (issue #53).
    /// </param>
    /// <param name="sortDir">
    /// <c>asc</c> o <c>desc</c>. Cualquier otro valor cae a <c>asc</c>.
    /// El ordenamiento por <c>cliente</c> usa apellido + nombre como
    /// desempate; todos los campos llevan <c>Id</c> como tiebreaker para
    /// que la paginación sea estable entre requests.
    /// </param>
    Task<PagedResult<GarrafaDto>> GetPagedAsync(
        string? codigo,
        byte? capacidad,
        int page = 1,
        int pageSize = 20,
        string sortBy = "codigo",
        string sortDir = "asc",
        CancellationToken ct = default);
    Task<IEnumerable<EstadoGarrafaDto>> GetEstadosAsync(CancellationToken ct = default);
    Task<GarrafaDto> CreateAsync(CreateGarrafaDto garrafa, ulong? usuarioId, CancellationToken ct = default);
    Task<GarrafaDto> UpdateAsync(UpdateGarrafaDto garrafa, ulong? usuarioId, CancellationToken ct = default);
    /// <summary>
    /// Cambia el estado operacional de la garrafa, registrando un
    /// <c>movimiento_garrafa</c> con tipo <c>CAMBIO_ESTADO</c>. La transicion
    /// se valida contra <see cref="GarrafaTransiciones"/>.
    /// </summary>
    /// <param name="estadoOrigenEsperadoId">
    /// <c>EstadoGarrafaId</c> que el caller (controller) leyo antes de
    /// invocar el service. Si difiere del valor actual en BD (otro operador
    /// cambio la garrafa en el medio), se rechaza con
    /// <see cref="InvalidOperationException"/> y no se ejecuta ninguna
    /// escritura. Issue #182 T03.
    /// </param>
    Task<bool> CambiarEstadoAsync(ulong id, ulong estadoOrigenEsperadoId, CambiarEstadoGarrafaDto dto, ulong? currentUserId = null, CancellationToken ct = default);
    /// <summary>
    /// Soft-delete de la garrafa: setea <c>deleted_at</c>, baja <c>activo</c> y
    /// actualiza <c>updated_at</c>/<c>updated_by</c>. Bloquea garrafas en estado
    /// EN_CLIENTE o EN_TRANSITO para preservar la trazabilidad de movimientos
    /// de canje (ver <c>DECISIONES.md</c>, regla de tracking individual).
    /// </summary>
    /// <param name="updatedBy">
    /// <c>UserId</c> que ejecuta la baja. Queda registrado en <c>updated_by</c>
    /// para auditoría. Coincide con la firma de <see cref="IClienteService.DeleteAsync"/>.
    /// </param>
    Task<bool> DeleteAsync(ulong id, ulong? updatedBy, CancellationToken ct = default);

    /// <summary>
    /// Returns the catalog rows for the destination states that the given
    /// garrafa is allowed to transition to. Used by the UI to filter the
    /// state dropdown shown on the "Cambiar estado" view.
    /// </summary>
    /// <returns>
    /// Empty enumerable when the garrafa doesn't exist, when its current
    /// state has no outgoing transitions (terminal state), or when the
    /// current state code is not present in the transition matrix.
    /// </returns>
    Task<IEnumerable<EstadoGarrafaDto>> GetTransicionesDisponiblesAsync(ulong garrafaId, CancellationToken ct = default);

    /// <summary>
    /// Devuelve todos los movimientos registrados para una garrafa específica,
    /// ordenados por fecha descendente. Cada movimiento trae los nombres
    /// legibles del tipo, los estados origen/destino y el empleado.
    /// Devuelve enumerable vacío si la garrafa no existe o no tiene movimientos.
    /// </summary>
    Task<IEnumerable<MovimientoGarrafaDto>> GetHistorialAsync(ulong garrafaId, CancellationToken ct = default);

    /// <summary>
    /// Devuelve los movimientos de garrafa vinculados a un pedido, ordenados
    /// por id ascendente. Usado por la vista Details para mostrar la
    /// trazabilidad del canje (issue #44).
    /// </summary>
    Task<IEnumerable<MovimientoGarrafaDto>> GetMovimientosByPedidoAsync(ulong pedidoId, CancellationToken ct = default);

    /// <summary>
    /// Registra un movimiento de canje (ENTREGA_CLIENTE / DEVOLUCION_CLIENTE)
    /// para una garrafa física, dejando que el trigger de BD actualice
    /// <c>estado_garrafa_id</c> y <c>fecha_ultimo_movimiento</c>. La app solo
    /// setea <c>garrafa.cliente_id</c>. NO abre transacción propia: depende de
    /// la transacción ambiente de <c>PedidoService.RegistrarCanjePedidoAsync</c>.
    /// </summary>
    /// <param name="tipoMovimientoCodigo">
    /// <c>ENTREGA_CLIENTE</c> o <c>DEVOLUCION_CLIENTE</c>. Determina el estado
    /// destino esperado (EN_CLIENTE / LLENA_DEPOSITO) y se persiste en la fila
    /// de <c>movimientos_garrafa</c>.
    /// </param>
    /// <param name="clienteId">
    /// <c>pedido.cliente_id</c> para ENTREGA, <c>null</c> para DEVOLUCION.
    /// Se aplica a <c>garrafas.cliente_id</c> y al campo
    /// <c>movimientos_garrafa.cliente_id</c>.
    /// </param>
    Task RegistrarMovimientoPorCanjeAsync(
        ulong garrafaId,
        ulong estadoDestinoId,
        ulong? clienteId,
        ulong pedidoId,
        string tipoMovimientoCodigo,
        ulong? usuarioId,
        CancellationToken ct = default);

    /// <summary>
    /// Devuelve el stock agrupado por capacidad y estado, leyendo directamente
    /// de la vista <c>v_stock_garrafas</c> para evitar el agrupamiento manual
    /// que antes se hacía en memoria en el Controller (issue #51). Cada fila
    /// ya trae los nombres y colores del catálogo <c>estados_garrafa</c>,
    /// por lo que la UI puede renderizar badges sin joins adicionales.
    /// </summary>
    Task<IEnumerable<VStockGarrafa>> GetStockAsync(CancellationToken ct = default);

    /// <summary>
    /// Devuelve una página de garrafas en poder de clientes, leyendo de la
    /// vista <c>v_garrafas_en_clientes</c> (issue #51). La vista ya filtra
    /// por estado <c>EN_CLIENTE</c> y calcula <c>dias_en_cliente</c>.
    /// Cuando <paramref name="clienteId"/> es null devuelve todas; cuando
    /// se especifica, filtra a ese cliente. El total devuelto en
    /// <see cref="PagedResult{T}.Total"/> refleja el conteo SIN paginar
    /// para que la UI muestre los controles correctos.
    /// Issue #182 T11.
    /// </summary>
    /// <param name="page">Número de página 1-based. Valores &lt; 1 se
    /// normalizan a 1 (mismo patrón que <see cref="GetPagedAsync"/>).</param>
    /// <param name="pageSize">Tamaño de página. Default 20. Tope máximo de
    /// 100 para evitar queries enormes accidentales.</param>
    Task<PagedResult<VGarrafaEnCliente>> GetEnClientesAsync(
        ulong? clienteId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Resuelve el <c>Id</c> numérico de un estado de garrafa a partir de su
    /// código canónico (ej. <c>LLENA_DEPOSITO</c>). Devuelve <c>0</c> cuando
    /// el código no existe en el catálogo <c>estados_garrafa</c>. Usado por
    /// el Controller para asignar el estado inicial del alta sin depender del
    /// Id físico (que puede cambiar si el seed se reordena). Issue #182 T12.
    /// </summary>
    Task<ulong> GetEstadoIdByCodigoAsync(string codigo, CancellationToken ct = default);
}
