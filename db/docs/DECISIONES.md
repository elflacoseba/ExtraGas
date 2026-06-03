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

## Supuestos explícitos (no validados con el usuario)

1. No hay delivery con zonas / tarifas distintas. Un pedido = una dirección.
2. No hay lista de precios por cliente. Todos pagan lo mismo.
3. No hay descuentos automáticos por volumen / fidelidad.
4. No hay comisiones ni manejo de empleados externos / fleteros.
5. No hay integración con balanza; las cantidades se cargan manualmente.
6. No hay sincronización con sistema de ARCA; los datos fiscales se manejan afuera.
7. No hay manejo de devoluciones de producto (solo devolución de garrafas vacías por canje).
8. El dueño actúa como ADMIN; los 2 empleados como OPERADOR.
