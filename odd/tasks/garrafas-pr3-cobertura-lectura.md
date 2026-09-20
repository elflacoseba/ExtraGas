# Garrafas PR3 — Cobertura de lectura + micro-fixes defensivos

> Issue #182 — T06, T07, T08, T09, T10. Cierra el arco de tests faltantes sobre
> el módulo Garrafas antes de pasar a T11–T18 (perf/UX/mantenibilidad).

## Goal

Cubrir con tests unitarios los métodos de GarrafaService que aún no tienen
cobertura dedicada: los 7 Gets restantes (`GetPagedAsync`,
`GetTransicionesDisponiblesAsync`, `GetHistorialAsync`,
`GetMovimientosByPedidoAsync`, `GetStockAsync`, `GetEnClientesAsync`,
`GetEstadosAsync`), `RegistrarMovimientoPorCanjeAsync`, más los caminos
defensivos de `GetPagedAsync` (normalización + escape LIKE), estado terminal
como origen, y FK inválida (`ProveedorId`) en Create.

## Alcance

- **T06** — tests para los 7 métodos de lectura restantes.
- **T07** — tests dedicados a `RegistrarMovimientoPorCanjeAsync` (hoy cubierto
  indirectamente vía `PedidoCanjeIntegrationTests`).
- **T08** — code fix mínimo en `GetPagedAsync` para escapar `%`/`_` del input
  antes del `EF.Functions.Like`, + tests de normalización defensiva
  (page<1, pageSize>100, sortBy/direction desconocidos, escape LIKE).
- **T09** — tests para estado terminal como origen y self-transition
  (la matriz de `GarrafaTransiciones` ya los rechaza — sólo tests).
- **T10** — code fix pequeño en `CreateAsync` (y `UpdateAsync`) para validar
  `ProveedorId` no soft-deleted, mismo patrón que `ValidarClienteActivoAsync`
  introducido en PR #183. Los tests de `ClienteId` soft-deleted en Create /
  Update / CambiarEstado ya están cubiertos por PR #183.

## Restricciones

- Tests con **EF Core InMemory** y `xUnit + FluentAssertions`, mismo factory
  `NewService(dbName, seedCatalogos: true)` ya existente en
  `tests/ExtraGasMVC.Tests/GarrafaServiceTests.cs`.
- Para `T07` el seed debe extenderse con `TiposMovimientoGarrafa` adicionales
  (`ENTREGA_CLIENTE`, `DEVOLUCION_CLIENTE`).
- Para `T10` el seed debe extender con un `Proveedor` vivo (id=N) y uno
  soft-deleted (id=N+1) — mismo patrón que los clientes sembrados.
- **Code fixes grandes NO.** Sólo el escape LIKE (3 líneas) y la validación de
  `ProveedorId` (helper + 3 líneas de uso en Create y Update). Nada de
  reescribir `GetPagedAsync` ni tocar `UpdateAsync` más allá de la nueva
  validación.
- No introducir dependencias nuevas.
- Mantener estilo del repo: español en comentarios, inglés en identificadores
  y mensajes de UI/strings de tests.
- Conventional commits SIN Co-Authored-By.
- Branch: `test/garrafas-pr3-cobertura-lectura`. PR contra `develop`.

## Plan

1. Cargar skills relevantes (dotnet-backend-patterns, csharp-testing,
   work-unit-commits). ✅
2. Leer `GarrafaService.cs` y `GarrafaTransiciones.cs` para mapear lo que
   existe vs. lo que falta. ✅
3. Levantar branch y baseline de build.
4. Tests T06 (7 métodos de lectura sin cobertura).
5. Tests T07 (RegistrarMovimientoPorCanjeAsync — 4 tests cubriendo ENTREGA,
   DEVOLUCION, canje múltiple, pedidoId en movimiento).
6. T08: code fix de escape LIKE + 6 tests (page<1, pageSize>100, sort
   desconocido, dir inválida, LIKE `%`, LIKE `_`).
7. T09: 2 tests (FUERA_SERVICIO terminal como origen, self-transition).
8. T10: code fix para validar `ProveedorId` en Create y Update, + 1 test
   (CreateAsync con ProveedorId soft-deleted). Los tests de ClienteId ya
   están cubiertos por PR #183; no duplico.
9. Build + tests passing.
10. Rebase contra `origin/develop` (puede haberse movido).
11. Commit + push + PR contra `develop`.

## Criterios de aceptación

- `dotnet test tests/ExtraGasMVC.Tests/ExtraGasMVC.Tests.csproj --filter
  "FullyQualifiedName~GarrafaServiceTests"` → todos los tests pasan (existentes
  + nuevos).
- `dotnet build src/ExtraGasMVC/ExtraGasMVC.csproj` → 0 errors, 0 warnings
  nuevos.
- Tests nuevos cuentan: ~28–32 tests (detalle abajo).
- Code fixes: 2 micro-fixes (escape LIKE + validar ProveedorId). Total de
  cambios en código de producción ≤ ~20 líneas.
- PR abierto contra `develop`. URL devuelta al final.

## Archivos afectados

| Archivo | Tipo de cambio |
|---|---|
| `src/ExtraGasMVC/Services/Implementations/GarrafaService.cs` | Code fix T08 (escape LIKE) + T10 (validar ProveedorId en Create/Update) |
| `tests/ExtraGasMVC.Tests/GarrafaServiceTests.cs` | Ampliar seed (Proveedor + TiposMovimientoGarrafa para canje) + ~28 tests nuevos |

## Decisiones

### Code fix de escape LIKE (T08)

La línea actual es:
```csharp
var pattern = $"%{codigo.Trim()}%";
```

Fix mínimo (3 líneas):
```csharp
var trimmed = codigo.Trim();
// Escape % _ \ para que EF.Functions.Like no los interprete como wildcards.
// Tercer argumento = caracter de escape, soportado por MySQL via Pomelo
// y por InMemory en EF Core 9.
var sanitized = trimmed
    .Replace(@"\", @"\\")
    .Replace("%", @"\%")
    .Replace("_", @"\_");
var pattern = $"%{sanitized}%";
query = query.Where(g => EF.Functions.Like(g.Codigo, pattern, @"\"));
```

Justificación: en producción MySQL un usuario buscando el código `GAR-50%`
recibe TODAS las garrafas (el `%` se interpreta como wildcard). Con el fix, el
`%` se escapa a `\%` y se trata como literal.

### Code fix para ProveedorId (T10)

Helper `ValidarProveedorActivoAsync(ulong? proveedorId, ct)` análogo a
`ValidarClienteActivoAsync`. Se invoca en:
- `CreateAsync` después de `ValidarClienteActivoAsync`.
- `UpdateAsync` después de `ValidarClienteActivoAsync`.

NO se invoca en `CambiarEstadoAsync` porque ese flujo no toca `ProveedorId`
(sólo `ClienteId` y `EstadoGarrafaId`).

### Tests existentes reutilizados

- `NewService(dbName, seedCatalogos: true)` se mantiene igual; sólo se
  extiende `SeedCatalogos` para incluir un Proveedor vivo y uno soft-deleted,
  y los `TiposMovimientoGarrafa` que necesita `RegistrarMovimientoPorCanjeAsync`.

### Tests pre-existentes no afectados

`PedidoItemSoftDeleteIntegrationTests` requiere Docker/Testcontainers y ya
estaba fallando antes de este PR. No lo toco.

## Riesgos / notas

- El comportamiento del `EF.Functions.Like` con escape character puede
  diferir sutilmente entre MySQL y InMemory. Los tests verifican el
  comportamiento observable (input con `%`/`_` no devuelve filas extra),
  no la traducción interna.
- La view `v_stock_garrafas` no se popula en InMemory (las views no existen
  en InMemory). Los tests de `GetStockAsync` y `GetEnClientesAsync` sólo
  verifican que el método no tira y devuelve enumerable vacío, alineado con
  la naturaleza de las views en InMemory.

## Work-unit commit

- Branch: `test/garrafas-pr3-cobertura-lectura`
- Commit único cubriendo tests + micro-fixes:
  `test(garrafas): cover read paths + defensive GetPagedAsync + ProveedorId validation (#182 T06-T10)`
- Conventional commits, sin Co-Authored-By.

## Discovery log

- `GarrafaTransiciones.Matriz` ya rechaza FUERA_SERVICIO como origen (terminal)
  y self-transitions → T09 = sólo tests.
- `ValidarClienteActivoAsync` ya está invocado en Create, Update, CambiarEstado
  → T10 cubre sólo ProveedorId (no estaba validado en ningún path).
- `ProveedorId` está en `CreateGarrafaDto` y `UpdateGarrafaDto` pero
  `GarrafaService.Create/Update` no lo validan → confirmación de code fix.
- `RegistrarMovimientoPorCanjeAsync` ya tiene:
  - Validación de transición contra la matriz.
  - Búsqueda de TipoMovimientoGarrafa por código.
  - Seteo de `Garrafa.ClienteId`, `MovimientoGarrafa.ClienteId`, `MovimientoGarrafa.PedidoId`.
  - Trigger trg_mov_garrafa_ai actualiza estado_garrafa_id (en MySQL; en
    InMemory no aplica, así que los tests verifican lo que la app escribe).
