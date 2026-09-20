# Garrafas PR4 — UX/perf/defensivo (T11 + T12 + T13)

> Issue #182 — T11, T12, T13. Cierra el arco de perf/UX/defensivo del módulo
> Garrafas antes de pasar a T14–T18.

## Goal

Resolver tres mejoras de mantenibilidad/UX sobre el módulo Garrafas:

1. **T11** — Paginar `GetEnClientesAsync` para que la vista no se ponga lenta
   con miles de garrafas en clientes.
2. **T12** — Reemplazar el hardcode `EstadoGarrafaId = 1` en
   `GarrafasController.Create` por un lookup del código `LLENA_DEPOSITO`.
3. **T13** — Agregar `[MaxLength]` a `Observaciones` en los DTOs de Garrafas
   (la columna es `TEXT` sin tope en BD).

## Alcance

### T11 — Paginación de `GetEnClientesAsync`

Cubre:

- Cambiar la firma de `IGarrafaService.GetEnClientesAsync` para devolver
  `PagedResult<VGarrafaEnCliente>` y aceptar `page` + `pageSize` (mismo
  patrón de normalización que `GetPagedAsync`).
- Actualizar el Controller `GarrafasController.EnClientes` para recibir
  `page` y `pageSize` desde el query string y exponerlos a la vista.
- Reescribir la vista `Views/Garrafas/EnClientes.cshtml` para usar el modelo
  `PagedResult<VGarrafaEnCliente>` y mostrar controles de paginación.
- Adaptar los 10 stubs de `IGarrafaService` en los tests que lo implementan
  a mano.
- Adaptar los 2 tests existentes (`GetEnClientesAsync_*_VistaEstaVacia...`) y
  agregar nuevos (paginación básica, normalización de page<1 y pageSize>100).

### T12 — Lookup de código en `Create` Controller

Cubre:

- Agregar `GetEstadoIdByCodigoAsync(string codigo)` a `IGarrafaService` y su
  implementación en `GarrafaService` (query AsNoTracking puntual).
- Reemplazar `EstadoGarrafaId = 1` por
  `EstadoGarrafaId = await _garrafaService.GetEstadoIdByCodigoAsync(GarrafaEstados.LlenaDeposito)`
  en `GarrafasController.Create` (GET).
- Tests del helper: happy path (codigo existente) y codigo inexistente
  (devuelve 0).

### T13 — `[MaxLength]` en `Observaciones`

Cubre:

- Agregar `[StringLength(500, ErrorMessage = "...")]` a `Observaciones` en
  `CreateGarrafaDto`, `UpdateGarrafaDto`, `CambiarEstadoGarrafaDto` y
  `GarrafaDto`. La columna `garrafas.observaciones` es `TEXT` (~64KB);
  500 caracteres es razonable para observaciones operativas de garrafas y
  consistente con la convención del proyecto (motivo_cancelacion en pedidos
  usa 500).
- Tests de validación (límite exacto + exceso).

## Restricciones

- **Code fixes grandes NO.** Cada tarea es un fix puntual. T11 NO reescribe
  `GetEnClientesAsync` desde cero — sólo agrega paginación. T12 NO refactorea
  el Controller completo. T13 NO migra la columna de BD a `VARCHAR(500)`.
- Tests con **EF Core InMemory** + `xUnit + FluentAssertions`, mismo factory
  `NewService(dbName, seedCatalogos: true)`.
- No introducir dependencias nuevas. Reusar `PagedResult<T>` existente.
- Mantener estilo del repo: español en comentarios, inglés en identificadores
  y mensajes de UI/strings de tests.
- Conventional commits SIN Co-Authored-By. Branch: `refactor/garrafas-pr4-ux-perf`.
- PR contra `develop`.

## Plan

1. Cargar skills relevantes (dotnet-backend-patterns, csharp-testing,
   work-unit-commits). ✓
2. Leer `GarrafaService.cs`, `IGarrafaService.cs`, `GarrafasController.cs`,
   `GarrafaDto.cs`, `Views/Garrafas/EnClientes.cshtml`,
   `Models/ViewModels/PagedResult.cs`, `Constants/GarrafaEstados.cs`,
   migraciones SQL. ✓
3. Crear ODD task doc. ✓
4. Crear branch `refactor/garrafas-pr4-ux-perf` desde `develop`. (en este PR)
5. **T11** — paginar `GetEnClientesAsync`:
   - Cambiar firma en `IGarrafaService.cs` y `GarrafaService.cs`.
   - Actualizar `GarrafasController.EnClientes`.
   - Reescribir `EnClientes.cshtml` con paginación.
   - Adaptar 10 stubs en tests.
   - Adaptar 2 tests existentes + agregar nuevos (~4 tests).
6. **T12** — lookup por código:
   - Agregar `GetEstadoIdByCodigoAsync` en service + interface.
   - Reemplazar hardcode en `GarrafasController.Create`.
   - Tests del helper (~2 tests).
7. **T13** — `[StringLength]`:
   - Agregar atributo a `Observaciones` en 4 DTOs.
   - Tests de validación (~2 tests).
8. Build + tests passing.
9. Pre-push rebase contra `origin/develop`.
10. Commit + push + PR contra `develop`.

## Criterios de aceptación

- `dotnet build src/ExtraGasMVC/ExtraGasMVC.csproj` → 0 errors, 0 warnings
  nuevos.
- `dotnet test tests/ExtraGasMVC.Tests/ExtraGasMVC.Tests.csproj --filter
  "FullyQualifiedName~GarrafaServiceTests"` → todos los tests pasan
  (existentes + nuevos).
- `dotnet test tests/ExtraGasMVC.Tests/ExtraGasMVC.Tests.csproj` → 0 tests
  rotos en otras suites (algunos stubs pueden romperse si no se adaptan).
- PR abierto contra `develop`. URL devuelta al final.

## Archivos afectados

| Archivo | Tipo de cambio |
|---|---|
| `src/ExtraGasMVC/Services/Interfaces/IGarrafaService.cs` | T11: firma de `GetEnClientesAsync`; T12: nueva firma `GetEstadoIdByCodigoAsync` |
| `src/ExtraGasMVC/Services/Implementations/GarrafaService.cs` | T11: impl paginada; T12: impl lookup |
| `src/ExtraGasMVC/Controllers/GarrafasController.cs` | T11: pasar page/pageSize; T12: reemplazar hardcode |
| `src/ExtraGasMVC/Views/Garrafas/EnClientes.cshtml` | T11: modelo `PagedResult` + controles de paginación |
| `src/ExtraGasMVC/DTOs/GarrafaDto.cs` | T13: `[StringLength(500)]` en `Observaciones` (4 DTOs) |
| `tests/ExtraGasMVC.Tests/GarrafaServiceTests.cs` | T11: adaptar 2 + ~4 nuevos; T12: ~2 nuevos; T13: ~2 nuevos |
| `tests/ExtraGasMVC.Tests/PedidoServiceSearchTests.cs` | T11: stub |
| `tests/ExtraGasMVC.Tests/PedidosControllerIndexTests.cs` | T11: stub |
| `tests/ExtraGasMVC.Tests/PedidoServiceItemEstadoTests.cs` | T11: stub |
| `tests/ExtraGasMVC.Tests/ControllersActivoViewBagTests.cs` | T11: stub |
| `tests/ExtraGasMVC.Tests/PedidoServiceCambiarEstadoTests.cs` | T11: stub |
| `tests/ExtraGasMVC.Tests/PedidosControllerCommandTests.cs` | T11: stub |
| `tests/ExtraGasMVC.Tests/ProductoActivoRaceIntegrationTests.cs` | T11: stub |
| `tests/ExtraGasMVC.Tests/PedidoServiceProductoActivoTests.cs` | T11: stub |
| `tests/ExtraGasMVC.Tests/PedidosControllerPendientesTests.cs` | T11: stub |

## Decisiones

### T11 — Single signature vs overload

Decisión: **cambiar la firma existente** a `Task<PagedResult<VGarrafaEnCliente>>
GetEnClientesAsync(ulong? clienteId, int page, int pageSize, ct)`.

Justificación: el método es consumido por UNA acción del Controller
(`EnClientes`). No hay callers externos (el resto son stubs en tests).
Un overload dejaría dos métodos divergentes sin beneficio real — sólo
complica el contrato y duplica cobertura de tests. El cliente siempre va a
querer paginar; si no quiere paginar, `PagedResult` con `PageSize > 100`
sigue siendo eficiente. Para mantener compatibilidad con el caller
`PedidoService` que hoy NO llama `GetEnClientesAsync` (sólo lo implementa en
el stub), el cambio de firma es interno al módulo.

Alternativas descartadas:

- (a) Overload: rechaza. Sólo agrega un método redundante.
- (b) Default `pageSize=20` en la firma, default sin params en el Controller:
  acepta. Es lo que se hace.

### T11 — Default `pageSize`

Decisión: **`pageSize = 20` por default** (consistente con `GetPagedAsync`),
con cap `100` aplicado en el service. En el Controller el default viene del
binding (`page = 1, pageSize = 20`) y se pasa explícito.

### T11 — Sort whitelist

Decisión: **NO agregar sort whitelist en T11.** El método filtra por
`clienteId` y la vista renderiza columnas fijas (código, capacidad, cliente,
último mov, días en cliente). No hay sorting clickeable esperado en esta
vista. Si surge, se agrega en otro PR siguiendo el mismo patrón de
`BuildOrderedQueryable` que ya está en `GetPagedAsync`.

### T11 — Vista

Decisión: **reusar el patrón de paginación de `Index.cshtml`** (mismas clases
Bootstrap, misma estructura `card-footer` con `pagination-sm`). El modelo
pasa de `IEnumerable<VGarrafaEnCliente>` a
`PagedResult<VGarrafaEnCliente>`. La query string preserva `clienteId` y
agrega `page`.

### T12 — Helper nuevo en service

Decisión: **agregar `GetEstadoIdByCodigoAsync(string codigo)`** al service.

Justificación:

- Cumple la firma estándar (Queryable async + cancellation token).
- Reusable por otros módulos si en el futuro hay otro Controller que
  necesite resolver código → id (ej. al cambiar de estado con un código
  legible en vez del id numérico).
- El Controller no necesita conocer detalles del DbContext — separation of
  concerns.

Alternativa descartada: hacer el lookup inline en el Controller usando
`ViewBag.Estados`. Eso obliga a filtrar en memoria una lista pequeña pero
mezcla la responsabilidad del Controller y no es testeable de forma aislada.

### T13 — Límite

Decisión: **`[StringLength(500)]`**.

Justificación:

- La columna `garrafas.observaciones` es `TEXT` (~64KB) — necesitamos un
  tope defensivo pero razonable para evitar abuso.
- 500 caracteres coincide con la convención del proyecto para campos de
  observaciones libres de longitud media: `motivo_cancelacion` en pedidos
  usa `VARCHAR(500)` (migración 20260607_000002).
- No propongo migrar la columna a `VARCHAR(500)` porque eso es scope creep
  (issue #182 está limitado a T11–T13). Si en el futuro se quiere aplicar
  el límite en BD, se hace en un PR aparte siguiendo la guía de
  `mysql` skill.

### T13 — Attribute choice

Decisión: **`[StringLength(N)]` en lugar de `[MaxLength(N)]`**.

Justificación:

- El proyecto usa `[MaxLength]` mayormente en `Data/Configurations/*.cs`
  (a nivel EF Core, configuración de BD) y `[StringLength]` en los DTOs
  (`ProveedorDto`, `PedidoDto`, etc.).
- `StringLength` permite incluir `{1}` en el `ErrorMessage` para mostrar el
  límite exacto en el mensaje al usuario.
- Mantener consistencia con el resto de los DTOs.

### T13 — DTOs read-only (`GarrafaDto`)

Decisión: **también agregar `[StringLength]` en `GarrafaDto.Observaciones`**.

Justificación: `GarrafaDto` se usa en respuestas del Controller (Details,
Index paginado, etc.). El atributo no tiene efecto en runtime (es
informativo para el mapping de EF y para herramientas de Swagger) pero
mantiene la coherencia con los DTOs de input. El entity configuration de
`GarrafaConfiguration` ya tiene `.HasMaxLength(500)` (?) — verificar.

## Work-unit commits

- Branch: `refactor/garrafas-pr4-ux-perf`
- 4 commits independientes (uno por trabajo + docs):
  1. `refactor(garrafas): paginar GetEnClientesAsync (#182 T11)`
  2. `fix(garrafas): reemplazar hardcode de EstadoGarrafaId en Create (#182 T12)`
  3. `feat(garrafas): agregar MaxLength a Observaciones en DTOs (#182 T13)`
  4. `chore(docs): add ODD task doc for garrafas PR #4`
- Conventional commits, sin Co-Authored-By.

## Riesgos / notas

- El cambio de firma de `GetEnClientesAsync` rompe los 10 stubs en tests.
  Es mecánico (mismo patrón) pero requiere atención para no olvidar
  ninguno — `grep GetEnClientesAsync` antes de cerrar.
- Las views SQL no se populan en InMemory (`v_garrafas_en_clientes` no
  existe en InMemory). Los tests de paginación de `GetEnClientesAsync`
  verifican el comportamiento observable (normalización de page/pageSize,
  count + skip/take en SQL contra el view), pero los asserts serán sobre
  enumerable vacío (consistente con el patrón actual de tests #185).
- La columna `TEXT` acepta hasta ~64KB. Si se quiere hacer cumplir el
  límite también en BD, eso requiere una migración adicional — fuera de
  scope.

## Discovery log

- `GarrafaService.GetPagedAsync` ya tiene la normalización
  `page<1 → 1`, `pageSize>100 → 100`, `pageSize<1 → 20` que se reusa en
  T11. Mismo lugar donde se mete la lógica.
- `PagedResult<T>` ya existe en `Models/ViewModels/PagedResult.cs` con
  `Items`, `Page`, `PageSize`, `Total`, `TotalPages`, `HasPrevious`,
  `HasNext`. Se reusa sin cambios.
- `Views/Garrafas/Index.cshtml` ya tiene el patrón de paginación
  completo (`card-footer` con `pagination-sm`, ventana centrada de 5
  botones). Se reusa como template para `EnClientes.cshtml`.
- `GarrafasController.Create` (GET) ya carga `ViewBag.Estados` con la
  lista completa. El lookup `LLENA_DEPOSITO` se hace desde esa lista para
  no agregar una query extra. (Revisión: mejor el helper del service para
  testearlo aisladamente — confirmado.)
- `Constants/GarrafaEstados.LlenaDeposito = "LLENA_DEPOSITO"` es la
  constante canónica. Se usa en el Controller en lugar del string literal.
- Los DTOs de Garrafas usan `[Display(Name = "...")]` y
  `[StringLength(N, ErrorMessage = "...")]` en el resto del repo. Mismo
  patrón.
