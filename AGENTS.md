# AGENTS.md

Convenciones y datos verificados del repositorio **ExtraGas** para sesiones de OpenCode. Solo incluye lo que un agente no podría inferir trivialmente.

## Descripción del proyecto

Sistema de gestión para una empresa familiar de **venta de gas envasado (garrafas 10/15/45 kg), carbón (3/5/10/25 kg) y leña (bolsa 25 kg)**. Cubre clientes, pedidos, control individual de garrafas, cobros, proveedores, recepciones y pagos a proveedores. **No incluye facturación** (la realiza ARCA en su plataforma web).

## Stack

### Base de datos
- **MySQL 8.4 LTS** soportado como target (dev actual en homelab). MySQL 9.x sigue siendo compatible (sintaxis portable). Ver ADR #11 en `db/docs/DECISIONES.md`.
- **InnoDB** en todas las tablas, `utf8mb4` / `utf8mb4_unicode_ci`.
- **Time zone**: `America/Argentina/Buenos_Aires` (-03:00).

### Backend — ASP.NET Core MVC
- **.NET 10.0** (TFM: `net10.0`).
- **ASP.NET Core MVC** (`AddControllersWithViews`), routing por defecto `{controller=Home}/{action=Index}/{id?}`.
- **Entity Framework Core 9.0.16** con **Pomelo.EntityFrameworkCore.MySql 9.0.0** (driver MySQL nativo).
- **Sin autenticación/autorización** configurada aún — solo `UseAuthorization()` sin middleware de identidad.
- **AdminLTE 4** como template admin (CSS/JS en `wwwroot/lib/admin-lte/`), cargado vía npm (`package.json` con `admin-lte ^4.0.0`).
- **SweetAlert2** para diálogos modales (confirmaciones, mensajes), cargado vía npm (`package.json` con `sweetalert2 ^11.26.25`).
- **User Secrets** habilitado para configuración sensible (`appsettings.Development.json`).

### Capa de datos (EF Core)
- **DbContext**: `ExtraGasDbContext` en `Data/Context/`.
- **Entities**: clases POCO en `Data/Entities/` — una por tabla, incluyendo 10 vistas read-only en `Data/Entities/Views/`.
- **Configurations**: `IEntityTypeConfiguration<T>` en `Data/Configurations/` — una por entidad + una subcarpeta `Views/` para las vistas. Se aplican via `ApplyConfigurationsFromAssembly()`.
- **Un solo enum en la app**: `TipoLinea` (`ENTREGA`, `DEVOLUCION`, `VENTA`) en `Data/Entities/Enums/`.
- **No hay migraciones de EF Core** — el esquema se gestiona con SQL migraciones manuales en `db/migrations/`.

### Servicios de negocio
- Patrón **Interface + Implementation** en `Services/Interfaces/` y `Services/Implementations/`.
- Registrados como **Scoped** en `Program.cs`.
- Servicios actuales: `IClienteService`, `IEmpleadoService`, `IPedidoService`, `IProductoService`, `IProveedorService`, `IPagoService`, `IGarrafaService`, `IUsuarioService`.

### DTOs y Mapeo
- 8 DTOs en `DTOs/` (`ClienteDto`, `EmpleadoDto`, `GarrafaDto`, `PagoDto`, `PedidoDto`, `ProductoDto`, `ProveedorDto`, `UsuarioDto`).
- **AutoMapper** con perfil único `Mappings/MappingProfile.cs` para conversión Entity ↔ DTO.

### Controllers
- 12 controllers MVC en `Controllers/`:
  - `HomeController` — dashboard, errores
  - `AccountController` — login/logout (vistas básicas, sin Identity)
  - `BaseController` — base compartido (helpers de auditoría `CreatedBy`/`UpdatedBy`, `Json` camelCase, antiforgery)
  - `ClientesController`, `PedidosController`, `ProductosController`, `ProveedoresController`, `PagosController`, `RecepcionesController`, `GarrafasController`
  - `EmpleadosController`, `UsuariosController` — ABM completo
  - `Reportes` — vistas de reportes (pagos por forma, productos más vendidos, regularidad de clientes)

### Vistas Razor
- Layout principal: `Views/Shared/_AdminLTELayout.cshtml` (AdminLTE).
- Layout alternativo: `Views/Shared/_AccountLayout.cshtml` (login).
- Partials compartidos: `_Sidebar.cshtml`, `_Navbar.cshtml`, `_Footer.cshtml`, `_Styles.cshtml`, `_Scripts.cshtml`, `_ContentHeader.cshtml`, `_StatusMessage.cshtml`.
- ViewModels en `Models/ViewModels/` para datos compuestos (dashboard, sidebar, navbar, breadcrumbs, paginación).

### Formateo
- Extensión `FormatExtensions.cs` en `Extensions/` — helpers para formato ARS (`ToArs`), fechas (`ToShortDate`, `ToShortDateTime`).

## Comandos clave

```bash
# MySQL
brew services start mysql              # iniciar servicio
mysqladmin -uroot ping                 # verificar estado
mysql -uroot                           # cliente root
mysql -uroot extragas < db/migrations/<archivo>.sql  # migración puntual

# BD — scripts
./db/scripts/install.sh                       # crear BD + migraciones + seed (idempotente, skip-by-checksum)
./db/scripts/setup_migrator_user.sh           # crear user extragas_migrator (idempotente, una vez como root)
./db/scripts/reset.sh                         # drop + recreate (solo dev)

# install.sh con migrator user (recomendado para homelab)
MYSQL_USER=root MYSQL_MIGRATOR_PASS='xxx' ./db/scripts/setup_migrator_user.sh   # una vez
MYSQL_MIGRATOR_USER=extragas_migrator MYSQL_MIGRATOR_PASS='xxx' ./db/scripts/install.sh

# .NET
dotnet restore                         # restore de paquetes
dotnet build                           # compilar
dotnet run --project src/ExtraGasMVC   # ejecutar (dev)
dotnet ef migrations add <Nombre> --project src/ExtraGasMVC  # nueva migración EF (si se usa)
```

Las credenciales por defecto en los scripts de BD son `root` sin password (instalación local de Homebrew). Ajustar antes de usar en cualquier otro entorno.

## Layout del repositorio

```
/
├── AGENTS.md                       este archivo
├── README.md                       descripción funcional del sistema
├── ExtraGasMVC.sln                 solución .NET (proyecto único)
├── package.json                    npm — dependencias admin-lte ^4.0.0 y sweetalert2 ^11.26.25
└── db/
    ├── migrations/                 SQL versionado, orden alfabético = orden de ejecución
    │   ├── 20260101_*_create_database.sql
    │   ├── 20260102_000001_*_lookup_tables.sql
    │   ├── 20260102_000002_*_personas_y_seguridad.sql
    │   ├── 20260102_000003_*_productos.sql
    │   ├── 20260102_000004_*_pedidos_y_pagos.sql
    │   ├── 20260102_000005_*_proveedores_y_recepciones.sql
    │   ├── 20260102_000006_*_garrafas.sql
    │   ├── 20260102_000007_*_triggers.sql
    │   ├── 20260102_000008_*_views.sql
    │   ├── 20260102_000009_*_seed_data.sql
    │   ├── 20260606_000001_*_add_unique_index_clientes_dni.sql
    │   ├── 20260607_000001_*_drop_pedidos_entregado.sql
    │   ├── 20260607_000002_*_add_motivo_cancelacion_pedidos.sql
    │   ├── 20260607_000002_*_pedido_items_soft_delete_and_unique.sql
    │   └── 20260608_000001_*_add_tipo_movimiento_cambio_estado.sql
    ├── seed/                       datos iniciales (provincias, productos, catálogos)
    ├── scripts/
    │   ├── install.sh              crea BD + aplica migraciones + seed
    │   └── reset.sh                drop + recreate (solo dev)
    └── docs/
        ├── ERD.mmd                 diagrama entidad-relación en Mermaid
        └── DECISIONES.md           decisiones de diseño y supuestos

src/ExtraGasMVC/                    proyecto ASP.NET Core MVC (.NET 10.0)
├── Program.cs                      entry point — DI, middleware, routing
├── appsettings.json                config (connection string en User Secrets)
├── Controllers/                    12 controllers MVC (ver abajo)
├── Data/
│   ├── Context/ExtraGasDbContext.cs  DbContext — 30+ DbSets, ApplyConfigurationsFromAssembly
│   ├── Entities/                   25 entidades POCO (una por tabla) + 10 vistas + Enums/
│   └── Configurations/             IEntityTypeConfiguration<T> por entidad + Views/
├── Services/
│   ├── Interfaces/                 8 interfaces de servicio de negocio
│   └── Implementations/            8 implementaciones (Scoped)
├── DTOs/                           8 DTOs (Cliente, Empleado, Garrafa, Pago, Pedido, Producto, Proveedor, Usuario)
├── Mappings/MappingProfile.cs      perfil AutoMapper — Entity ↔ DTO
├── Extensions/FormatExtensions.cs  helpers de formato ARS y fechas
├── Constants/                      constantes tipadas (ej. `PedidoEstados.cs`)
├── Models/ViewModels/              DTOs compuestos (Dashboard, Sidebar, Navbar, BreadcrumbItem, PagedResult)
├── Views/                          Razor views (ver abajo)
└── wwwroot/lib/admin-lte/          CSS + JS AdminLTE 4 (via npm)
```

**Controllers** (12): `Home`, `Account`, `Base` (compartido), `Clientes`, `Pedidos`, `Productos`, `Proveedores`, `Pagos`, `Recepciones`, `Garrafas`, `Empleados`, `Usuarios`, `Reportes` (vistas de reportes).

**Vistas** (carpetas): `Home/`, `Account/`, `Clientes/`, `Pedidos/`, `Productos/`, `Proveedores/`, `Pagos/`, `PagosProveedor/`, `Recepciones/`, `Garrafas/`, `Empleados/`, `Usuarios/`, `Reportes/`, `Shared/` (layouts y partials). **No se listan** las ~64 vistas individuales (CRUD por controller) ni las ~35 configuraciones EF — son convencionales y un agente puede encontrarlas por patrón.

## Convenciones de la BD

- **Tablas**: `snake_case` en **plural** (`clientes`, `pedidos`).
- **Columnas**: `snake_case`.
- **PK**: `id BIGINT UNSIGNED AUTO_INCREMENT` en toda tabla.
- **FK**: `<tabla_singular>_id` (ej. `cliente_id`).
- **Auditoría**: `created_at`, `updated_at`, `created_by`, `updated_by` en toda tabla principal.
- **Soft delete**: `deleted_at DATETIME NULL`; las vistas siempre filtran `WHERE deleted_at IS NULL`.
- **Montos**: `DECIMAL(12,2)` (ARS, sin multi-moneda).
- **Catálogos**: lookup tables, **nunca `ENUM`** en columnas (flexibilidad para agregar valores sin migración).
- **Numeración**: `PREFIX-YYYY-NNNNN` (`PED-2026-00001`, `REC-2026-00001`, `REC-PROV-2026-00001`, `PAG-PROV-2026-00001`). La genera un `BEFORE INSERT` trigger leyendo de la tabla `secuencias`.

## Decisiones de diseño que NO se deben romper

1. **Tracking individual de garrafas**: cada garrafa física tiene un `codigo` único y se rastrea individualmente. Stock se calcula agregando la tabla `garrafas` por `estado_garrafa_id`, **no** se mantiene un contador aparte.
2. **Modelo de canje**: en un mismo pedido puede haber `pedido_items` con `tipo_linea = 'ENTREGA'` (llena) y `tipo_linea = 'DEVOLUCION'` (vacía). El saldo a cobrar es la suma algebraica.
3. **Las garrafas específicas** que se entregan/devuelven se registran en `movimientos_garrafa` con FK a `pedido_id`. **La app debe crear siempre ambos registros** (`pedido_item` + `movimiento_garrafa`) en la misma transacción al confirmar una entrega o devolución.
4. **`monto_pagado` en `pedidos`** lo mantiene un trigger de `pagos`. No actualizar manualmente desde la app.
5. **No hay ENUM en columnas**: los catálogos viven en tablas (`estados_pedido`, `estados_garrafa`, `tipos_movimiento_garrafa`, `formas_pago`, `tipos_producto`, `canales_venta`, `medios_contacto_pedido`, `roles`, `provincias`).
6. **Soft delete en todo**: para borrar un registro usar `UPDATE ... SET deleted_at = NOW()`, no `DELETE`.

## Gotchas

- El entorno actual apunta a un **homelab** (`192.168.0.216`) con MySQL 8.4.11, no a un server local. El cliente MySQL (`brew install mysql-client`) está en keg-only y requiere agregar `/opt/homebrew/opt/mysql-client/bin` al PATH.
- Si `mysqladmin ping` (sin env vars) tira "Can't connect to local MySQL server through socket", es porque no pasaste `MYSQL_HOST` — el default es `localhost`. Exportá `MYSQL_HOST=tu_server` antes de correr el script.
- El user `extragas` en el homelab es el user de la app. NO debe tener `SYSTEM_VARIABLES_ADMIN` (privilegio global) — para instalar/migrar se usa un user separado `extragas_migrator` con privilegios completos sobre `extragas.*` + `SYSTEM_VARIABLES_ADMIN`. Crear/rotar con `./db/scripts/setup_migrator_user.sh` (idempotente, requiere MYSQL_USER=root). El binlog de MySQL 8.x con replicación exige `SET GLOBAL log_bin_trust_function_creators = 1` para crear triggers — eso lo hace el migrator user (con SYSTEM_VARIABLES_ADMIN), no la app.
- Si el cliente está en Apple Silicon y Homebrew no está, instalalo con `brew install mysql-client` (sin server).
- El socket queda en `/tmp/mysql.sock` (no en `/var/mysql/`). Si una app cliente se queja, exportar `TMPDIR=/tmp`.
- `install.sh` es **idempotente** sobre la estructura inicial (CREATE TABLE/VIEW/TRIGGER con IF NOT EXISTS), sobre las migraciones incrementales (patrón `information_schema` + `PREPARE`/`EXECUTE` o `INSERT IGNORE`), y mantiene además una tabla `schema_migrations` (filename PK + checksum SHA256) que skipea migraciones ya aplicadas y detecta drift (checksum cambió en archivo ya aplicado → abort con error explícito). Ver ADR #13 en `db/docs/DECISIONES.md`.
- La migración de seed (`20260102_000009_seed_data.sql`) inlina las 24 provincias argentinas. El archivo `db/seed/provincias_argentina.sql` se conserva como referencia documental.
- Los triggers usan `SIGNAL SQLSTATE` para validar; si la app recibe un error, traducirlo desde la capa de UI.
- En passwords de BD, evitá caracteres que bash expanda (`$`, `*`, `!`, espacios). Si los necesitás, exportá con comillas simples: `MYSQL_PASS='Pa$$W0rd'`.

## Consultas frecuentes (smoke test)

```sql
USE extragas;

-- Stock de garrafas por estado y capacidad
SELECT capacidad_kg, eg.codigo AS estado, COUNT(*) AS cantidad
FROM garrafas g
JOIN estados_garrafa eg ON eg.id = g.estado_garrafa_id
WHERE g.deleted_at IS NULL
GROUP BY capacidad_kg, estado
ORDER BY capacidad_kg, estado;

-- Garrafas en poder de un cliente
SELECT g.codigo, g.capacidad_kg, c.apellido, c.nombre
FROM garrafas g
JOIN clientes c ON c.id = g.cliente_id
JOIN estados_garrafa eg ON eg.id = g.estado_garrafa_id
WHERE eg.codigo = 'EN_CLIENTE' AND g.deleted_at IS NULL;

-- Pedidos pendientes de cobro
SELECT * FROM v_pedidos_resumen WHERE saldo > 0 ORDER BY fecha DESC;

-- Productos más vendidos (últimos 30 días)
SELECT * FROM v_productos_mas_vendidos
WHERE fecha >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
ORDER BY cantidad_vendida DESC;

-- Regularidad de pedidos por cliente
SELECT * FROM v_regularidad_clientes ORDER BY dias_promedio_entre_pedidos ASC;
```

## Proceso de trabajo proporcional

La metodología debe proteger la calidad sin introducir burocracia innecesaria.

**No todas las tareas requieren el mismo nivel de análisis, documentación, testing ni validación.** La complejidad de la tarea determina el proceso, no las herramientas disponibles en el repositorio.

Antes de implementar, clasificá internamente la tarea como **TRIVIAL, PEQUEÑA, MEDIANA o GRANDE**.

### TRIVIAL

Cambio localizado, sin impacto en arquitectura, contratos, persistencia ni reglas de negocio. Fácilmente reversible y con bajo riesgo de regresión.

Ejemplos: cambiar texto de un botón, reordenar columnas en una vista, modificar una clase CSS, corregir un typo en una vista Razor, ajustar un mensaje al usuario, cambiar el label de un campo.

**Proceso:** entender → modificar → verificar. No crear artefactos SDD/OpenSpec, no correr la suite completa, no leer documentación extensa. Inspección puntual y validación mínima razonable.

### PEQUEÑA

Afecta varias piezas relacionadas (2+ archivos), puede tocar una capa o interacción pequeña entre capas, no introduce arquitectura nueva ni decisiones técnicas importantes.

Ejemplos: agregar un filtro o búsqueda a un listado existente, agregar paginación a una pantalla, agregar una acción a un Controller, modificar un Service existente, corregir un bug que requiere cambios coordinados en pocas clases.

**Proceso:** analizar brevemente → implementar → validar. Sin SDD completo ni artefactos burocráticos. Implementación directa y tests solo cuando aporten valor.

### MEDIANA

Afecta varias capas (Controller → Service → DbContext → Vista), modifica contratos públicos (DTOs, ViewModels), modifica persistencia sin cambio arquitectónico importante, requiere varias decisiones de implementación y tiene riesgo moderado de regresión.

Ejemplos: crear un caso de uso que atraviesa Controller/Service/DbContext, agregar una consulta con joins o agregaciones, modificar una funcionalidad que afecta varias vistas, agregar exportación de datos.

**Proceso:** analizar impacto → revisar `db/docs/DECISIONES.md` si afecta decisiones técnicas, persistencia o reglas de negocio → planificar brevemente → implementar → probar → validar. SDD puede usarse si aporta valor, pero no es obligatorio.

### GRANDE

Introduce un nuevo módulo significativo, modifica arquitectura, modifica decisiones técnicas importantes, introduce funcionalidad transversal de negocio, modifica significativamente persistencia, modifica seguridad o autenticación, afecta múltiples módulos, tiene alto riesgo de regresión y requiere múltiples decisiones de diseño.

Ejemplos: implementar un módulo completo (pagos, proveedores), cambiar la arquitectura de tracking de garrafas, introducir autenticación real con Identity, agregar integración con ARCA, refactorizar la capa de servicios a un patrón diferente.

**Proceso:** OpenSpec/SDD completo (exploration → proposal → design → tasks → apply → verify → archive). Documentar en `db/docs/DECISIONES.md` las decisiones que deban quedar vigentes.

### Regla de proporcionalidad

El nivel de proceso depende de:

- Complejidad y riesgo del cambio.
- Cantidad de capas y archivos afectados.
- Impacto sobre contratos, persistencia, seguridad y reglas de negocio.
- Reversibilidad.

**No medir la complejidad solo por líneas modificadas.** Un cambio de 5 líneas en una regla de negocio puede ser GRANDE. Un cambio de 100 líneas localizado en una vista puede seguir siendo PEQUEÑO.

Ante la duda entre dos niveles, elegir el inferior cuando el cambio sea localizado, reversible y de bajo riesgo. Si durante la implementación se descubre mayor complejidad, elevar el nivel.

### Mínimo contexto

Para TRIVIAL y PEQUEÑA: solo inspeccionar archivos necesarios. No recorrer todo el repositorio, no leer todos los artefactos SDD, no ejecutar comandos costosos innecesariamente.

El contexto también tiene costo: el objetivo es suficiente información para hacer bien el cambio, no máxima información leída.

### Cambio mínimo

En TRIVIAL y PEQUEÑA: modificar solo lo necesario. No refactorizar código no relacionado, no reorganizar archivos, no cambiar nombres por estética, no "mejorar" código fuera del alcance, no introducir abstracciones nuevas si una modificación directa alcanza.

Una mejora no relacionada puede mencionarse al usuario, pero no implementarse automáticamente.

### No sobre-ingeniería

No crear una solución más compleja que el problema. Preferir modificar implementaciones existentes antes que introducir nuevas abstracciones. Reutilizar Services, DTOs y Controllers existentes. No crear interfaces únicamente por preferencia arquitectónica abstracta.

La arquitectura protege el sistema; no convierte cada cambio en una ceremonia.

### Validación proporcional

| Tamaño | Validación |
|---|---|
| TRIVIAL | Solo lo directamente relacionado. Cambio CSS o textual → sin tests nuevos. Cambio de Razor sin lógica → validar compilación si corresponde. |
| PEQUEÑA | Build de la parte afectada + tests directamente relacionados cuando aporten valor. |
| MEDIANA | Build + tests de las capas afectadas; tests de integración si se modifica persistencia. |
| GRANDE | Validación completa correspondiente al alcance del cambio. |

No ejecutar comandos costosos solo por costumbre.

### Resumen rápido

| Tamaño | Flujo |
|---|---|
| TRIVIAL | entender → modificar → verificar |
| PEQUEÑA | analizar → implementar → validar |
| MEDIANA | analizar → planificar → implementar → probar → validar |
| GRANDE | OpenSpec/SDD completo |

## SonarQube (issue #134)

El plugin `opencode-sonarqube` usa el **scanner Java** estándar, que NO integra con MSBuild ni levanta `coverage.opencover.xml` → reporta `new_coverage: 0%` aunque los tests corran.

**Para cobertura real y Quality Gate usable:**

```bash
# Una vez (si no está): dotnet tool install -g dotnet-sonarscanner
export SONAR_TOKEN="squ_..."      # generar en el server (User > Security > Generate Tokens)
./scripts/sonar-analyze.sh        # flujo begin/build/test/end con MSBuild integration
```

El script `scripts/sonar-analyze.sh` envuelve `dotnet-sonarscanner 11.x`, mueve `sonar-project.properties` durante el flujo (el scanner .NET se queja si está en la raíz), y deja todo el coverage en `tests/.../TestResults/*/coverage.opencover.xml`.

`sonarqube({ action: "analyze" })` sigue siendo válido para **validación rápida sin coverage** (typos, code smells en código nuevo). NO mezclar ambos flujos en la misma rama — el segundo `analyze` resetea las métricas del server.

## Recursos

- Skill `database-designer` en `.agents/skills/database-designer/` — usar para optimizaciones, índices y migraciones futuras.
- Skill `dotnet-backend-patterns` en `.agents/skills/dotnet-backend-patterns/` — patrones de backend .NET, repository, EF Core, Dapper.
- Skill `dotnet-best-practices` en `.agents/skills/dotnet-best-practices/` — mejores prácticas generales de .NET.
- Skill `github-issues` en `.agents/skills/github-issues/` — creación, actualización y gestión de GitHub issues.
- Skill `pr-review-dotnet` en `.agents/skills/pr-review-dotnet/` — revisión integral de PRs para .NET/ASP.NET Core MVC/EF Core.
- Skill `mysql` en `.agents/skills/mysql/` — schema MySQL/InnoDB, índices, tuning de queries, transacciones.
- Skill `enriquecer-issue` en `.agents/skills/enriquecer-issue/` — enriquecer issues existentes con contexto del codebase (NO para crear nuevas; para eso está `issue-creation`).
- Skill `caveman` en `.agents/skills/caveman/` — modo de comunicación ultra-comprimido (ahorra ~65% tokens). Actívalo con "caveman mode" o `/caveman full`.
- Diagrama ER: `db/docs/ERD.mmd` (Mermaid).
- Decisiones y supuestos: `db/docs/DECISIONES.md`.
- Skills lock: `skills-lock.json` — control de versiones de skills instaladas (8 skills registradas).
