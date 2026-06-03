# ExtraGas

Sistema de gestión para una empresa familiar de venta de **gas envasado**, **carbón** y **leña** para hogares.

## Productos

| Producto | Capacidades |
|----------|-------------|
| Gas envasado (garrafa) | 10, 15 y 45 kg |
| Carbón (bolsa) | 3, 5, 10 y 25 kg |
| Leña (bolsa) | 25 kg |

## Alcance funcional

- Gestión de clientes, empleados y proveedores
- Recepción de pedidos (teléfono, WhatsApp, presencial)
- Control **individual** de garrafas (trazabilidad por unidad física, con estados: llena, vacía, en cliente, dañada, fuera de servicio)
- Stock en depósito y cuenta corriente de garrafas por cliente
- Cobros (efectivo, transferencia) con recibos PDF
- Pagos a proveedores
- Informes: pedidos por cliente, productos más vendidos, regularidad de pedidos, saldos

**No incluye facturación** — la realiza ARCA en su plataforma web.

## Stack técnico

- **MySQL 9.6.0** (InnoDB, `utf8mb4` / `utf8mb4_unicode_ci`)
- Time zone: `America/Argentina/Buenos_Aires`
- Sin ORM ni framework backend (esta primera entrega es la base de datos)

## Estructura

```
/
├── AGENTS.md               instrucciones para agentes de IA
├── README.md               este archivo
└── db/
    ├── migrations/         SQL versionado (orden alfabético = orden de ejecución)
    ├── seed/               datos iniciales (provincias, etc.)
    ├── scripts/            install.sh, reset.sh
    └── docs/               ERD.mmd, DECISIONES.md
```

## Comandos rápidos

```bash
# Levantar MySQL (si no está corriendo)
brew services start mysql

# Crear la BD, correr migraciones y cargar seed
./db/scripts/install.sh

# Entrar a la consola
mysql -uroot extragas
```

## Documentación

- [AGENTS.md](./AGENTS.md) — convenciones y gotchas para sesiones de OpenCode
- [db/docs/ERD.mmd](./db/docs/ERD.mmd) — diagrama entidad-relación
- [db/docs/DECISIONES.md](./db/docs/DECISIONES.md) — decisiones de diseño
