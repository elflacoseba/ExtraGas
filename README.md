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
- **.NET 10** (ASP.NET Core MVC)
- **EF Core 9** + **Pomelo 9** (database-first, sin migraciones EF)
- La BD es la fuente de verdad; los cambios de esquema van por SQL en `db/migrations/`

## Estructura de la solución

```
/
├── AGENTS.md                       instrucciones para agentes de IA
├── README.md                       este archivo
├── ExtraGasMVC.sln                 solución .NET
├── package.json                    npm — dependencia admin-lte ^4.0.0
├── skills-lock.json                control de versiones de skills instaladas
│
├── src/ExtraGasMVC/                aplicación web ASP.NET Core MVC
│   ├── Data/                       capa de acceso a datos (EF Core)
│   │   ├── Context/                ExtraGasDbContext (punto de entrada del ORM)
│   │   ├── Entities/               modelos POCO (25 tablas + 10 vistas + 1 enum)
│   │   └── Configurations/         Fluent API por entidad (35 archivos separados)
│   ├── Controllers/                controladores MVC (11 controllers)
│   ├── Services/                   lógica de negocio (8 interfaces + 8 implementaciones)
│   ├── DTOs/                       objetos de transferencia de datos (8 DTOs)
│   ├── Mappings/                   perfil AutoMapper (Entity ↔ DTO)
│   ├── Extensions/                 helpers de formato (ARS, fechas)
│   ├── Models/                     ViewModels para las vistas
│   ├── Views/                      plantillas Razor
│   ├── wwwroot/                    archivos estáticos (css, js, lib)
│   ├── Program.cs                  punto de entrada y registro de servicios
│   └── appsettings.json            configuración (connection string)
│
├── .agents/skills/                 skills de OpenCode
│   ├── database-designer/          análisis de esquema, migraciones, índices
│   ├── dotnet-backend-patterns/    patrones backend .NET, repository, EF Core
│   ├── dotnet-best-practices/      mejores prácticas .NET
│   └── pr-review-dotnet/           revisión de PRs para .NET
│
└── db/
    ├── migrations/                 SQL versionado (orden alfabético = orden de ejecución)
    ├── seed/                       datos iniciales (provincias, etc.)
    ├── scripts/                    install.sh, reset.sh
    └── docs/                       ERD.mmd, DECISIONES.md
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

## Skills de OpenCode

El proyecto incluye 4 skills instaladas (gestionadas en `skills-lock.json`):

| Skill | Propósito |
|-------|-----------|
| `database-designer` | Análisis de esquema, generación de migraciones, optimización de índices |
| `dotnet-backend-patterns` | Patrones de backend .NET, repository, EF Core, Dapper |
| `dotnet-best-practices` | Mejores prácticas generales de .NET/C# |
| `pr-review-dotnet` | Revisión integral de PRs para .NET, ASP.NET Core MVC, EF Core |
