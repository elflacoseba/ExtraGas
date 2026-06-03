# AGENTS.md

Convenciones y datos verificados del repositorio **ExtraGas** para sesiones de OpenCode. Solo incluye lo que un agente no podría inferir trivialmente.

## Descripción del proyecto

Sistema de gestión para una empresa familiar de **venta de gas envasado (garrafas 10/15/45 kg), carbón (3/5/10/25 kg) y leña (bolsa 25 kg)**. Cubre clientes, pedidos, control individual de garrafas, cobros, proveedores, recepciones y pagos a proveedores. **No incluye facturación** (la realiza ARCA en su plataforma web).

## Stack

- **MySQL 9.6.0** (Homebrew, servicio `homebrew.mxcl.mysql`) — único motor en uso.
- **InnoDB** en todas las tablas, `utf8mb4` / `utf8mb4_unicode_ci`.
- **Time zone**: `America/Argentina/Buenos_Aires` (-03:00).
- **Sin ORM, sin framework backend** todavía — la BD es la primera capa entregada.

## Comandos clave

```bash
# Iniciar el servicio MySQL (si está caído)
brew services start mysql

# Verificar estado
mysqladmin -uroot ping

# Cliente root
mysql -uroot

# Crear la BD + correr todas las migraciones + cargar seed
./db/scripts/install.sh

# Reset completo (drop + recreate) — solo desarrollo
./db/scripts/reset.sh

# Aplicar una migración puntual
mysql -uroot extragas < db/migrations/<archivo>.sql
```

Las credenciales por defecto en los scripts son `root` sin password (instalación local de Homebrew). Ajustar antes de usar en cualquier otro entorno.

## Layout del repositorio

```
/
├── AGENTS.md                       este archivo
├── README.md                       descripción funcional del sistema
└── db/
    ├── migrations/                 SQL versionado, orden alfabético = orden de ejecución
    │   ├── 20260101_*_create_database.sql
    │   ├── 20260102_000001_*_lookup_tables.sql
    │   ├── 20260102_000002_*_personas_y_seguridad.sql
    │   ├── 20260102_000003_*_productos.sql
    │   ├── 20260102_000004_*_pedidos_y_pagos.sql
    │   ├── 20260102_000005_*_garrafas.sql
    │   ├── 20260102_000006_*_proveedores_y_recepciones.sql
    │   ├── 20260102_000007_*_triggers.sql
    │   ├── 20260102_000008_*_views.sql
    │   └── 20260102_000009_*_seed_data.sql
    ├── seed/                       datos iniciales (provincias, productos, catálogos)
    ├── scripts/
    │   ├── install.sh              crea BD + aplica migraciones + seed
    │   └── reset.sh                drop + recreate (solo dev)
    └── docs/
        ├── ERD.mmd                 diagrama entidad-relación en Mermaid
        └── DECISIONES.md           decisiones de diseño y supuestos
```

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

- MySQL 9.6 está instalado vía Homebrew pero el servicio **no arranca automáticamente** después de reiniciar la Mac. Si `mysqladmin -uroot ping` falla, correr `brew services start mysql`.
- El socket queda en `/tmp/mysql.sock` (no en `/var/mysql/`). Si una app cliente se queja, exportar `TMPDIR=/tmp`.
- `mysql_upgrade` no es necesario: BD recién creada en 9.6.
- `install.sh` es **idempotente** (no borra datos). Para empezar de cero en dev, usar `db/scripts/reset.sh`, que sí hace `DROP DATABASE` y vuelve a correr todo.
- La migración de seed (`20260102_000009_seed_data.sql`) inlina las 24 provincias argentinas. El archivo `db/seed/provincias_argentina.sql` se conserva como referencia documental.
- Los triggers usan `SIGNAL SQLSTATE` para validar; si la app recibe un error, traducirlo desde la capa de UI.

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

## Recursos

- Skill `database-designer` en `.agents/skills/database-designer/` — usar para optimizaciones, índices y migraciones futuras.
- Diagrama ER: `db/docs/ERD.mmd` (Mermaid).
- Decisiones y supuestos: `db/docs/DECISIONES.md`.
