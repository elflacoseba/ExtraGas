# Decisiones de diseño

Documento de decisiones (ADR-style) y supuestos del sistema **ExtraGas**.

---

## 1. Tracking individual de garrafas

**Decisión:** cada garrafa física es un activo rastreable, identificado por `codigo` único.

**Por qué:** la descripción de requerimientos pide explícitamente "saber las que tienen los clientes" (cuáles, no cuántas) y "las que están aptas para seguir intercambiándose" (condición por unidad).

**Implicancia:** las consultas de stock se calculan con `COUNT(*)` sobre `garrafas` agrupado por `estado_garrafa_id`, **no** se mantiene una columna `stock_garrafas` aparte. Cualquier desnormalización se hace en vistas.

---

## 2. Modelo de canje (entrega/devolución simultánea)

**Decisión:** en un mismo pedido puede haber líneas de tipo `ENTREGA` (garrafa llena al cliente) y `DEVOLUCION` (garrafa vacía devuelta por el cliente). El saldo a cobrar es `SUM(ENTREGA) - SUM(DEVOLUCION) + SUM(VENTA)`.

**Por qué:** es el modelo de operación real de las distribuidoras de gas en Argentina. El cliente "trueca" garrafas y se cobra la diferencia. Modelarlo en una sola transacción simplifica el cobro y el seguimiento.

**Implicancia:** la app debe crear, en una sola transacción, los `pedido_items` Y los `movimientos_garrafa` correspondientes, referenciando cada garrafa física específica.

---

## 3. Soft delete universal

**Decisión:** todas las tablas principales tienen `deleted_at DATETIME NULL`. Las vistas filtran `WHERE deleted_at IS NULL`.

**Por qué:** preservar el histórico de pedidos, pagos y movimientos de garrafas es crítico para informes (regularidad, ventas por período, cuenta corriente histórica).

**Implicancia:** para "borrar" un registro, la app hace `UPDATE tabla SET deleted_at = NOW() WHERE id = ?`. Nunca `DELETE`.

---

## 4. Catálogos en lugar de ENUM

**Decisión:** todos los valores de catálogo (estados, tipos, formas de pago) viven en tablas lookup.

**Por qué:** agregar un nuevo valor (ej. nueva forma de pago "DÉBITO") no debe requerir una migración. Cambiar un ENUM en MySQL con `ALTER TABLE` es disruptivo.

**Excepción:** `pedido_items.tipo_linea` SÍ es `ENUM('ENTREGA','DEVOLUCION','VENTA')` porque es lógica de negocio inmutable, no un catálogo administrativo.

---

## 5. Numeración automática por trigger

**Decisión:** los correlativos (`PED-2026-00001`, `REC-2026-00001`, `REC-PROV-2026-00001`, `PAG-PROV-2026-00001`) los genera un `BEFORE INSERT` trigger leyendo e incrementando la tabla `secuencias`.

**Por qué:** garantiza atomicidad y consistencia, sin condiciones de carrera que un `MAX(id)+1` desde la app tendría.

**Implicancia:** la app no asigna el `numero`; deja que la BD lo haga. Si necesita conocerlo de antemano para mostrarlo en UI, debe hacer un `INSERT` y leer el `LAST_INSERT_ID()` (el trigger habrá completado antes).

---

## 6. `monto_pagado` mantenido por trigger

**Decisión:** `pedidos.monto_pagado` lo actualiza un trigger `AFTER INSERT/UPDATE/DELETE` sobre `pagos`. La app no escribe esta columna.

**Por qué:** una sola fuente de verdad. Si la app calcula el total, se puede desincronizar fácilmente al borrar o modificar un pago.

**Implicancia:** la app nunca hace `UPDATE pedidos SET monto_pagado = ?`. Confía en el trigger. El `saldo` es columna generada (`total - monto_pagado`).

---

## 7. Moneda única (ARS)

**Decisión:** todos los montos son `DECIMAL(12,2)` en pesos argentinos. No se modela multi-moneda.

**Por qué:** la empresa opera 100% en Argentina y ARCA. No hay justificación para multi-moneda.

**Implicancia:** si en el futuro se requiere USD, agregar `moneda_id` y `tipo_cambio` impacta prácticamente todas las tablas de importes.

---

## 8. Time zone Argentina

**Decisión:** todas las marcas de tiempo se almacenan en `America/Argentina/Buenos_Aires` (-03:00). MySQL session `time_zone` se setea a `-03:00` en la migración inicial.

**Por qué:** evitar bugs de "una hora menos" al cruzar medianoche, particularmente para cierres diarios de caja.

**Implicancia:** la app debe setear `SET time_zone = '-03:00'` en cada conexión. Los reportes que cruzan medianoche con UTC podrían tener errores si se hacen desde otra zona.

---

## 9. Usuarios y empleados desacoplados

**Decisión:** `usuarios` y `empleados` son tablas separadas con FK opcional `empleados.usuario_id`.

**Por qué:** un usuario del sistema puede no ser un empleado (ej. un auditor externo, un familiar del dueño que solo consulta), y un empleado puede no tener usuario (ej. el dueño decide no usar el sistema).

**Implicancia:** los login de empleados van por `usuarios`, no por `empleados`.

---

## 10. Recepciones de proveedor y garrafas

**Decisión:** cuando se confirma una `recepcion_proveedor` que incluye garrafas, la app:
1. Crea los `recepcion_items`.
2. Para cada garrafa, crea un registro en `garrafas` con `estado_garrafa_id` (LLENA_DEPOSITO o VACIA_DEPOSITO según el producto) y un `movimientos_garrafa` tipo `COMPRA`.

**Por qué:** la unidad contable mínima para una garrafa es la unidad física, no la "docena". Comprar 10 garrafas de 10kg significa crear 10 registros en `garrafas`.

**Implicancia:** la transacción que confirma la recepción puede ser pesada (10 garrafas = 10 inserts + 10 movimientos). En la práctica se hace con `INSERT ... SELECT` y un solo `BEGIN/COMMIT`.

---

## 11. Pagos "a cuenta"

**Decisión:** un `pago` puede tener `pedido_id = NULL` (pago a cuenta del cliente). Se aplica luego al pedido más antiguo con saldo del mismo cliente.

**Por qué:** los clientes suelen dejar plata anticipada ("te dejo $5000 y voy juntando pedidos"). Modelar esto como un pago explícito simplifica la conciliación contra el sistema de ARCA.

**Implicancia:** el trigger `trg_pagos_ai` solo actualiza `pedidos.monto_pagado` si `pedido_id IS NOT NULL`. La aplicación de pagos a cuenta es lógica de la app (o un job).

---

## 12. No se incluye facturación

**Decisión:** el sistema no genera facturas. La facturación la realiza ARCA en su plataforma web.

**Por qué:** explícito en los requerimientos. Evita duplicar el esfuerzo fiscal y mantener sincronía con AFIP.

**Implicancia:** el sistema emite pedidos (PDF) y recibos de pago (PDF) con su numeración propia, pero no factura. La integración con ARCA queda fuera de alcance.

---

## 13. Sin login de clientes

**Decisión:** no hay portal de autogestión para clientes. Solo usuarios internos (dueño y empleados).

**Por qué:** el requerimiento no lo menciona, y el canal de venta es telefónico/WhatsApp/presencial, no web.

**Implicancia:** si en el futuro se agrega portal cliente, se necesitará una tabla `cliente_usuarios` separada.

---

## 14. Precios en producto, congelados en pedido_item

**Decisión:** `productos.precio_actual` es el precio vigente. Al crear un `pedido_item` se copia a `pedido_items.precio_unitario` (snapshot histórico).

**Por qué:** los informes de ventas pasadas deben poder reconstruir los precios cobrados, no los actuales.

**Implicancia:** la app no lee `productos.precio_actual` al facturar; lee ese valor al crear el item y lo congela.

---

## 15. Auditoría: `created_at`, `updated_at`, `created_by`, `updated_by`

**Decisión:** toda tabla principal lleva las cuatro columnas de auditoría, con `created_by` y `updated_by` referenciando a `usuarios`.

**Por qué:** saber quién cargó qué es crítico en sistemas con 2+ operadores. Un FK a `usuarios` (no a `empleados`) porque un usuario no empleado también puede cargar datos.

**Implicancia:** la app debe pasar el `usuario_id` de la sesión en cada `INSERT`/`UPDATE`. No hay usuario "sistema" anónimo.

---

## 16. Máquina de estados de garrafa — matriz de transiciones

**Decisión:** las transiciones válidas entre estados de garrafa cuando se hacen manualmente desde la UI (`GarrafaService.CambiarEstadoAsync`) están definidas en una matriz hard-coded en C# (`Services/GarrafaTransiciones.cs`), no en una tabla lookup.

**Por qué:**

1. Las transiciones describen restricciones del dominio físico (una garrafa dañada no se puede entregar, una retirada del sistema no vuelve a estar disponible). Son un contrato invariante, no un catálogo administrativo.
2. Al estar en C# viajan con el código del servicio, las versiones y el PR — un cambio se revisa, no se aplica por una corrida SQL.
3. El compilador detecta referencias inválidas a estados. La matriz es trivialmente testeable sin necesidad de fixtures de BD.
4. El catálogo `estados_garrafa` (los **estados mismos**) sigue siendo una tabla en BD para mantener el principio #4. Sólo las transiciones, que son lógica de negocio, son código.

**Documentación operativa:** la matriz vigente, las notas operativas (estado terminal, flujos de negocio que la esquivan, implicancias para la UI), las validaciones, las integraciones con Pedidos/Recepciones y el ciclo de vida completo están documentados en [`db/docs/GARRAFAS.md`](./GARRAFAS.md). Este ADR queda con la **decisión y su porqué**; el detalle de implementación vive en el documento del módulo.

---

## 11. MySQL 8.4 LTS como target soportado

**Decisión:** el proyecto soporta y se valida contra MySQL 8.4 LTS (además de 9.x). La versión de runtime del homelab es 8.4.11.

**Por qué:** 8.4 es el branch LTS estable de MySQL Community; 9.x es el de innovación. Para un sistema de gestión interno que va a correr años sin migrar de versión, LTS es la elección correcta. La sintaxis SQL usada en el proyecto (CTEs, `JSON_TABLE`, CHECK constraints, generated columns, ENUM, triggers) está disponible desde 8.0, así que la portabilidad está garantizada.

**Implicancia:**
- Las migraciones se prueban contra 8.4. Si se agrega SQL específico de 9.x, hay que documentarlo acá.
- El archivo `AGENTS.md` menciona "MySQL 9.6" porque esa era la versión original del dev local. **No implica que 9.x esté roto**, solo refleja la versión del entorno de desarrollo.
- Para homelab/dev: grants adicionales requeridos por 8.x con binlog activo (`SYSTEM_VARIABLES_ADMIN`, `log_bin_trust_function_creators=1`) — ver [`db/scripts/install.sh`](../../db/scripts/install.sh).

---

## 12. `pedido_items.unique_hash` como columna generada VIRTUAL (no STORED)

**Decisión:** la columna `unique_hash` usada para prevenir duplicados de `(pedido_id, producto_id, tipo_linea)` entre items activos se implementa como `GENERATED ALWAYS AS (...) VIRTUAL`, no `STORED`.

**Por qué:** la migración original usaba `STORED` pero falla con `ERROR 1215: Cannot add foreign key constraint` en MySQL 8.4 cuando la tabla tiene FKs. Es un bug conocido del path de validación de InnoDB para `STORED generated columns` en ALTER TABLE — el error es engañoso (no hay FK involucrada en el ALTER). `VIRTUAL` toma otro code path y funciona. MySQL 8.0.16+ permite `UNIQUE INDEX` sobre columnas `VIRTUAL` generadas, así que el contrato original se preserva.

**Implicancia:**
- Funcionalmente equivalente: el `UNIQUE INDEX uk_pedido_items_pedido_producto_tipo` sobre `unique_hash` sigue funcionando.
- Trade-off: `VIRTUAL` se recalcula en cada lectura en lugar de almacenarse. Para esta tabla (volumen bajo-medio, lecturas frecuentes) el costo es despreciable.
- Si en el futuro queremos `STORED` (por ejemplo para índices secundarios sobre el hash), hay que aplicar el ALTER antes de crear las FKs de `pedido_items` — workaround: tabla sin FKs, ADD COLUMN STORED, luego ADD CONSTRAINT.

---

## 13. Estrategia de idempotencia para migraciones incrementales

**Decisión:** las migraciones incrementales (las posteriores a la creación inicial del schema) deben ser idempotentes — re-ejecutables sin fallar contra una BD que ya las tiene aplicadas. La idempotencia se garantiza en **dos capas**:

1. **Capa SQL (dentro de cada `.sql`)**: cada migración es auto-suficiente y no falla al re-ejecutarse.
2. **Capa runner (`db/scripts/install.sh`)**: mantiene una tabla `schema_migrations` con `filename` (PK) + `checksum` (SHA256 del contenido). Si el archivo está registrado y su checksum no cambió, se skipea sin ejecutar.

**Por qué dos capas:**
- La capa SQL protege contra re-ejecuciones accidentales (defensa en profundidad).
- La capa runner evita ejecutar el archivo completo cuando no hace falta, y — más importante — **detecta drift**: si alguien edita una migración ya aplicada, el checksum no coincide y `install.sh` aborta con error explícito en lugar de re-ejecutar algo que no es lo que se aplicó originalmente.

**Capa SQL — cómo se logra:**
- **ADD/DROP COLUMN**: MySQL 8.x no soporta `IF [NOT] EXISTS` para columnas en `ALTER TABLE`. Se usa el patrón `information_schema` + `PREPARE`/`EXECUTE` para ejecutar condicionalmente.
- **INSERT en lookup**: `INSERT IGNORE` (descarta solo el error de duplicate-key, no otros errores).
- **CREATE/DROP VIEW, CREATE TRIGGER**: ya idempotentes con `IF NOT EXISTS` / `DROP IF EXISTS`.
- **CREATE TABLE**: ya idempotente con `IF NOT EXISTS`.
- **CREATE INDEX UNIQUE**: requiere `DROP INDEX` previo si el índice ya existe con el mismo nombre (ver migración 20260606_000001).
- **Status SELECT final**: cuando el archivo imprime un "OK" al terminar, debe ser **condicional** sobre el estado real (ver migración `20260607_000001_drop_pedidos_entregado.sql` post-PR #88). Un SELECT incondicional contradice los mensajes del IF-guard y confunde al debug.

**Capa runner — cómo funciona `install.sh`:**
1. **Bootstrap defensivo**: `CREATE TABLE IF NOT EXISTS schema_migrations (...)` antes del loop. Cubre el caso de BD recién creada (todavía no se aplicó la migración que crea la tabla).
2. **Para cada `.sql`**: calcula SHA256 del contenido, consulta `SELECT checksum FROM schema_migrations WHERE filename = ?`.
   - **Fila no existe** → ejecuta el archivo y hace `INSERT INTO schema_migrations`.
   - **Fila existe, checksum coincide** → skip con mensaje "already applied".
   - **Fila existe, checksum distinto** → aborta con error explícito: `migration modified after application; restore the file or write a new migration`.
3. Las migraciones existentes (las que ya tienen guards de `information_schema`) no se tocan — siguen siendo defensa en profundidad.

**Implicancia:**
- Las migraciones nuevas deben seguir el patrón de guards SQL. Code review debe verificar idempotencia antes de aceptar una migración nueva.
- **No editar migraciones ya aplicadas.** Si se necesita cambiar comportamiento, escribir una migración nueva. Esta restricción la enforce el checksum check de `install.sh`.
- La tabla `schema_migrations` vive en la BD `extragas`. No confundir con catálogos de dominio (estados, formas de pago, etc.).

---

## 14. Autenticacion propia (sin ASP.NET Core Identity) — lockout, politica configurable, auditoria y recuperacion admin-assisted

**Contexto:** el sistema necesitaba endurecer la autenticacion (lockout por intentos fallidos, politica de password configurable, auditoria de logins, recuperacion admin-assisted) pero sin migrar a ASP.NET Core Identity por costo de adopcion y dependencia externa.

**Decisiones tomadas:**

1. **Mensaje generico anti-user-enumeration**: el login devuelve el mismo texto ("Usuario o contrasena invalidos") para `UserNotFound`, `UserDeleted` e `InvalidPassword`. Solo `LockedOut` y `UserInactive` se diferencian porque el usuario legitimo necesita saber por que no entra. Esto evita que un atacante use el endpoint para enumerar usernames validos.

2. **Lockout sin re-hashear**: cuando una cuenta esta bloqueada (`bloqueado_hasta > NOW()`), `ValidateAndLoadForAuthAsync` rechaza antes de invocar `BCrypt.Verify`. Esto previene timing-attack: no se puede deducir si la password era correcta a partir del tiempo de respuesta.

3. **`LoginResult` + `LoginFailureReason` como contrato**: el servicio no devuelve `UsuarioDto? null` sino un record `LoginResult(User, FailureReason, AttemptedUserId)`. Esto permite propagar el motivo de fallo a la UI (para mensajes especificos) y a la auditoria (para `motivo_fallo`). `AttemptedUserId` lleva el id del usuario conocido aun cuando el login fallo por inactivo/eliminado/lockout/password — clave para vincular los intentos fallidos al usuario real en `auditoria_logins`.

4. **Auditoria desacoplada del login**: `RecordAsync` se invoca dentro de un `try/catch` que loguea con `ILogger` y no propaga. Si la tabla `auditoria_logins` tiene un problema (constraint, deadlock), el login sigue funcionando. La trazabilidad se prefiere sobre garantia absoluta de persistencia; un follow-up podria migrar a outbox.

5. **PasswordPolicy + AuthLockout via `IOptions<>`**: ambas opciones se bindean al arranque. Cambios requieren reinicio. Out-of-scope la migracion a `IOptionsMonitor<>`.

6. **`TemporaryPasswordGenerator` criptografico**: usa `RandomNumberGenerator.GetInt32` (no `Random`), garantiza lower/upper/digit/symbol en las primeras 4 posiciones y aplica Fisher-Yates al final. Resultado: passwords no predecibles que cumplen la policy por construccion.

7. **ForwardedHeaders opcional**: detras de un reverse proxy, `HttpContext.Connection.RemoteIpAddress` ya viene reescrito por el middleware `UseForwardedHeaders` cuando hay `KnownProxies`/`KnownNetworks` configurados. Sin config, ASP.NET falla a "closed" (no reescribe), evitando IP spoofing. Configurar `ForwardedHeaders` en appsettings por entorno.

8. **TempData una-vez para password temporal**: el POST de `ResetPassword` asigna `TempData["TemporaryPassword"]` y redirige a `Edit`. El GET de `Edit` usa `TempData.Peek` + `TempData.Remove` para asegurar que la password se muestre exactamente una vez (no reaparece en refresh). El modelo de cookie-based TempData del ASP.NET Core hace esto seguro: el `Remove` actualiza la cookie, el siguiente request no la ve.

**Implicancia:**
- No introducir ASP.NET Core Identity sin antes evaluar estas primitivas — la mayoria del valor de Identity (lockout, policy, hash) ya esta cubierta por esta capa.
- Los archivos `Configuration/AuthLockoutOptions.cs` y `Configuration/PasswordPolicyOptions.cs` son la unica fuente de verdad para esos tunables. Cambiar defaults requiere migracion conceptual, no de datos.
- Tests automatizados de `PasswordPolicyService.Validate`, `UsuarioService.HandleFailedAttemptAsync` y `TemporaryPasswordGenerator.Generate` quedan pendientes como deuda explicita.

---

## 15. Unicidad de `clientes.dni` solo entre activos (columna VIRTUAL `dni_unique`)

**Contexto:** el índice `idx_clientes_dni` era `UNIQUE` sobre la tabla completa (migración `20260606_000001`), sin contemplar soft-delete. Esto provocaba que, tras dar de baja a un cliente con DNI X, cualquier intento de crear otro cliente con el mismo DNI fallara con `ER_DUP_ENTRY (1062)` aunque la app lo permitiera (porque `IsDniUniqueAsync` filtra por `deleted_at IS NULL` vía el QueryFilter global). Issue #105.

**Decisión:** reemplazar el `UNIQUE INDEX idx_clientes_dni` por una columna VIRTUAL `dni_unique` + `UNIQUE INDEX idx_clientes_dni_unique` sobre esa columna. La columna se calcula como `CASE WHEN deleted_at IS NULL THEN dni ELSE NULL END`. Migración `20260829_000001_clientes_dni_unique_soft_delete.sql`.

**Por qué:**
- MySQL no soporta índices parciales nativos (`UNIQUE WHERE deleted_at IS NULL`). La columna VIRTUAL es el workaround documentado en el ADR #12 (mismo patrón aplicado a `pedido_items.unique_hash`).
- MySQL trata múltiples `NULL` como distintos en `UNIQUE INDEX`, por lo que el índice permite N soft-deleted con el mismo DNI (todos con `dni_unique = NULL`) y exige exactamente 1 activo por DNI (`dni_unique = dni`).
- DDL puro: la app no necesita saber de la columna. EF no la modela; la `Configuration` usa `HasIndex(...).HasFilter("deleted_at IS NULL")` solo como documentación de la intención (el filtro real lo implementa la columna VIRTUAL, no EF).
- Conserva la garantía a nivel BD y la trazabilidad histórica del DNI borrado (no se vacía la columna `dni`).

**Implicancia:**
- Cualquier futuro índice único sobre columnas con soft-delete en este proyecto debe seguir el mismo patrón (columna VIRTUAL + UNIQUE INDEX), no `UNIQUE` directo sobre la tabla.
- El `HasFilter` en la Configuration de EF es **metadato**, no se traduce a SQL en MySQL — no genera ni altera el índice real. El índice real es `idx_clientes_dni_unique` sobre `dni_unique`, creado por la migración.
- La verificación end-to-end (INSERT → soft-delete → INSERT con mismo DNI → OK) requiere MySQL real; los tests automatizados cubren la lógica del service (`IsDniUniqueAsync` filtra por QueryFilter).

---

## Supuestos explícitos (no validados con el usuario)

1. No hay delivery con zonas / tarifas distintas. Un pedido = una dirección.
2. No hay lista de precios por cliente. Todos pagan lo mismo.
3. No hay descuentos automáticos por volumen / fidelidad.
4. No hay comisiones ni manejo de empleados externos / fleteros.
5. No hay integración con balanza; las cantidades se cargan manualmente.
6. No hay sincronización con sistema de ARCA; los datos fiscales se manejan afuera.
7. No hay manejo de devoluciones de producto (solo devolución de garrafas vacías por canje).
8. El dueño actúa como ADMIN; los 2 empleados como OPERADOR.
