# ODD — Garrafas PR #2: Concurrencia (T03 + T04 de #182)

Branch: `fix/garrafas-pr2-concurrencia`
PR target: `develop`
Issue: #182 (T03 + T04)

## Objetivo

Cerrar los items T03 y T04 del tracker del modulo Garrafas (#182): eliminar
el race condition entre la lectura del controller y la escritura del service
en `CambiarEstado` POST (T03), y agregar un token de concurrencia optimista
a la tabla `garrafas` (T04).

## Alcance

### Dentro (este PR)

- **T03 — Race en `CambiarEstado` POST**: el controller hace un read para
  poblar `ViewBag.Garrafa` y otro read implicito dentro de
  `GarrafaService.CambiarEstadoAsync` antes de la transaccion. Entre los dos
  roundtrips otro operador puede cambiar el estado, dejando al controller
  con un snapshot desfasado. Fix: el controller hace UN read, pasa el
  `EstadoGarrafaId` esperado al service, el service lo compara contra la fila
  recargada y rechaza si difieren antes de tocar SaveChanges.

- **T04 — Token de concurrencia en `garrafas`**: agregar columna
  `version BIGINT UNSIGNED NOT NULL DEFAULT 0`, configurada con
  `IsConcurrencyToken()` (equivalente a `[ConcurrencyCheck]` para nuestro
  caso), backfill a `1` en todas las filas existentes. El service
  incrementa `entity.Version = originalVersion + 1` antes de SaveChanges y
  atrapa `DbUpdateConcurrencyException` para traducirla a
  `InvalidOperationException` con mensaje claro.

### Fuera (PRs siguientes)

- T01, T02, T05 — ya cerrado en PR #183.
- T06-T10 (tests de lectura faltantes).
- T11-T18 (perf/UX/mantenibilidad).

## Restricciones

- Stack: ASP.NET Core MVC 10 + EF Core 9 + Pomelo MySQL.
- Sin migraciones de EF Core: esquema se gestiona con SQL en `db/migrations/`
  ordenadas alfabeticamente. Idempotencia con `information_schema` +
  `schema_migrations` (skip por checksum).
- Tests InMemory existentes: el factory `NewService(dbName, seedCatalogos: true)`
  del archivo `GarrafaServiceTests.cs` siembra catalogo y clientes.
- 28 fallas pre-existentes de `PedidoItemSoftDeleteIntegrationTests`
  requieren Docker/Testcontainers — no disponibles localmente, no tocar.
- Conventional commits, sin trailers `Co-Authored-By`.

## Plan de implementacion

1. Crear migration `20260920_175040_add_concurrency_version_garrafas.sql`
   con backfill idempotente a `version = 1`.
2. Modificar `src/ExtraGasMVC/Data/Entities/Garrafa.cs` — agregar
   `public ulong Version { get; set; }` con comentario.
3. Modificar `src/ExtraGasMVC/Data/Configurations/GarrafaConfiguration.cs` —
   registrar `version` con `.IsConcurrencyToken()`.
4. Modificar `src/ExtraGasMVC/Services/Implementations/GarrafaService.cs`:
   - `CreateAsync`: setea `entity.Version = 1` para que INSERT arranque
     con un valor conocido (DEFAULT 0 tambien funciona, pero setear
     explicito es consistente con el patron "manual increment").
   - `UpdateAsync`: snapshot pre-mapper de `Version`, incrementa a
     `originalVersion + 1`, atrapa `DbUpdateConcurrencyException` y la
     traduce a `InvalidOperationException` con mensaje claro.
   - `CambiarEstadoAsync`: nueva firma
     `(ulong id, ulong estadoOrigenEsperadoId, ...)`. Lee la entidad,
     compara `entity.EstadoGarrafaId` contra `estadoOrigenEsperadoId`;
     si difieren, lanza `InvalidOperationException` con mensaje claro
     ("la garrafa fue modificada por otro operador"). Incrementa
     `Version`, atrapa `DbUpdateConcurrencyException`.
   - `RegistrarMovimientoPorCanjeAsync`: tambien incrementa `Version`
     y maneja la excepcion (mismo patron).
5. Modificar `src/ExtraGasMVC/Services/Interfaces/IGarrafaService.cs`:
   actualizar firma de `CambiarEstadoAsync` con el nuevo parametro
   `estadoOrigenEsperadoId`.
6. Modificar `src/ExtraGasMVC/Controllers/GarrafasController.cs`:
   - `CambiarEstado` POST: pasa `garrafa.EstadoGarrafaId` (de la unica
     lectura del controller) como `estadoOrigenEsperadoId`.
   - Manejar `InvalidOperationException` para mensajes de concurrencia
     con TempData["Error"] en lugar de ModelState.
7. Tests nuevos en `tests/ExtraGasMVC.Tests/GarrafaServiceTests.cs`:
   - T03: CambiarEstadoAsync rechaza cuando el entity's EstadoGarrafaId
     no coincide con `estadoOrigenEsperadoId`.
   - T04-a: UpdateAsync con version stale lanza InvalidOperationException.
   - T04-b: UpdateAsync exitoso incrementa `version`.
   - T04-c: CambiarEstadoAsync exitoso incrementa `version`.
   - Triangulacion: Garrafa entity expone `Version` y configuration lo
     marca como concurrency token (paralelo a Robustez146_4).

## Archivos a tocar

| Archivo | Tipo de cambio |
|---------|----------------|
| `db/migrations/20260920_175040_add_concurrency_version_garrafas.sql` | nuevo |
| `src/ExtraGasMVC/Data/Entities/Garrafa.cs` | +1 prop |
| `src/ExtraGasMVC/Data/Configurations/GarrafaConfiguration.cs` | +5 lineas |
| `src/ExtraGasMVC/Services/Interfaces/IGarrafaService.cs` | firma |
| `src/ExtraGasMVC/Services/Implementations/GarrafaService.cs` | logica |
| `src/ExtraGasMVC/Controllers/GarrafasController.cs` | 1 controller method |
| `tests/ExtraGasMVC.Tests/GarrafaServiceTests.cs` | +5 tests |

## Decisiones

### BIGINT version vs BINARY(8) row_version

`BIGINT version` (manual increment) en lugar de `BINARY(8) row_version` +
trigger `RANDOM_BYTES(8)`. Razones:

1. **Mas simple** — no requiere trigger para incrementar.
2. **Testeable en InMemory sin trucos** — el InMemory provider respeta
   tokens de concurrencia, pero no simula triggers MySQL. Con BIGINT el
   test es directo: snapshot antes + SaveChanges + aserción sobre la
   excepcion.
3. **El task spec lo recomienda explicitamente**: "BIGINT que el service
   incrementa explicitamente antes de SaveChanges".

Trade-off: pierde el "auto-incrementa sin pensar" del BINARY(8) +
trigger, pero la explicitud del increment explicito es una ventaja
revisable en code review (mas visible que el side-effect del trigger).

### Service detecta el race, no el controller

El controller pasa el `EstadoGarrafaId` esperado al service. El service
hace FindAsync y compara. Esto:

1. Mantiene una sola lectura del lado del controller (T03 cumplido).
2. Centraliza la deteccion del race en la capa de negocio, donde estan
   las invariantes (matriz de transiciones, etc.).
3. Permite que `RegistrarMovimientoPorCanjeAsync` (canje) reciba el mismo
   trato si lo necesita en el futuro, sin duplicar la logica.

### Mensaje del concurrency conflict

`"La garrafa {codigo} fue modificada por otro operador mientras editabas.
Recargá la página y volvé a intentar."` — paralelo al mensaje de
ProductoService (issue #146.4).

## Criterios de aceptacion

- [ ] Migration idempotente, backfill a `version = 1` para todas las filas
      existentes.
- [ ] `Garrafa.Version` mapea a columna `version` y esta marcada como
      `IsConcurrencyToken`.
- [ ] `GarrafaService.UpdateAsync` con `Version` stale → `InvalidOperationException`
      con mensaje mencionando "otro operador" y nombre del codigo.
- [ ] `GarrafaService.UpdateAsync` exitoso incrementa `Version` en BD.
- [ ] `GarrafaService.CambiarEstadoAsync` con `estadoOrigenEsperadoId` distinto
      del actual → `InvalidOperationException` con mensaje claro.
- [ ] `GarrafaService.CambiarEstadoAsync` exitoso incrementa `Version`.
- [ ] Controller hace un solo read en CambiarEstado POST y pasa el snapshot
      al service.
- [ ] 5 tests nuevos pasan + 31 existentes siguen pasando.
- [ ] Suite completa: misma cantidad de pass/fail que `develop` antes del PR
      (las 28 fallas pre-existentes de `PedidoItemSoftDeleteIntegrationTests`
      permanecen identicas).
- [ ] Build sin errores nuevos.

## Riesgos residuales

- **Controller CambiarEstado POST** — si el read inicial falla
  (NotFound), no se popula ViewBag.Garrafa y el re-render del form falla.
  Mitigacion: el controller actual ya maneja `garrafa is null` antes del
  read secundario, retornando NotFound. Sin cambios funcionales.
- **Race window entre CambiarEstado GET y POST** — el read del GET
  alimenta ViewBag (incluido el dropdown de transiciones). Si el estado
  cambia entre el GET y el POST, el dropdown muestra opciones stale
  y el POST detecta el race. Mensaje al usuario: "el estado cambio".
  Es comportamiento aceptable: el operador recarga y ve el estado real.
- **Otros callers de `CambiarEstadoAsync`** — solo `GarrafasController`
  la invoca. `RegistrarMovimientoPorCanjeAsync` es un metodo distinto
  con su propia firma; no le cambia la interfaz.
- **InMemory vs MySQL**: la columna `version` tiene DEFAULT 0 en BD pero
  el backfill la pone en 1. El service incrementa a `original + 1`. En
  InMemory el default es 0 (no hay DEFAULT de BD). Tests deben setear
  `Version` explicitamente en el seed de garrafas, o aceptar que arranca
  en 0. Vamos a setear `Version = 1` en el create y los tests validan
  con el flujo completo (no necesitan inicializarlo manualmente).

## Rollback

- Drop de la columna `version` en `garrafas`.
- Revertir cambios en `GarrafaService`, `GarrafaConfiguration`,
  `Garrafa`, `IGarrafaService`, `GarrafasController`, tests.
- El rollback NO requiere rollback de la app si la columna se queda:
  EF funciona con columna extra en BD sin error (mapeo explicito).