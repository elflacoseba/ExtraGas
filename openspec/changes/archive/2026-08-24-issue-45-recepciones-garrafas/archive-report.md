# Archive Report: issue-45-recepciones-garrafas

## Change Summary

| Field | Value |
|-------|-------|
| Change | `issue-45-recepciones-garrafas` |
| Archived | `2026-08-24` |
| Artifact store mode | `hybrid` (OpenSpec + Engram) |
| Spec sync | New domain `recepcion-compra-garrafa` synced from `openspec/changes/issue-45-recepciones-garrafas/spec.md` |
| Issue | #45 closed by PR #74 (`Closes #45`) |
| Delivery | 2 chained PRs (stacked-to-main): #73 (backend) + #74 (UI) |
| Verdict | PASS (10/10 requirements, 5/5 acceptance criteria, 18 scenarios) |

## Final State

### Code
- PR #73 MERGED: backend (`IRecepcionService`, `RecepcionService`, DTOs, VM, controller refactor, DI). 548 LOC, over budget 400 by 10%.
- PR #74 MERGED: UI (`wwwroot/js/recepciones.js`, `Views/Recepciones/Create.cshtml`). 486 LOC, over budget 400 by 21%.
- develop HEAD: `09f6037`.

### Issue
- #45 cerrada el 2026-08-24 por `elflacoseba` (auto-close vía `Closes #45` en PR #74).

### Database
- Cero migraciones SQL nuevas en este change.
- `tipos_movimiento_garrafa` ya tenía `COMPRA` (seed data).
- FKs `garrafas.recepcion_id` y `movimientos_garrafa.recepcion_id` ya existían.

## Deviations Documented

1. **ReversarAsync append-only**: `RecepcionService.ReversarAsync` no soft-deleta `movimientos_garrafa` por convención del módulo (la tabla no tiene columna `deleted_at`). Soft delete solo en `recepciones_proveedor` y `garrafas`. Documentado en código y verify-report.
2. **Sin UI affordance para ReversarAsync**: el método existe en el servicio pero no hay botón en la UI para invocarlo. Está disponible vía DI para uso futuro. Documentado en verify-report.
3. **LOC sobre budget per-PR**: PR1 548 (+10%) y PR2 486 (+21%). La estimación del design subestimó validaciones GARRAFA, validación cliente espejo en JS, y modal resumen detallado. Documentado en PRs bodies.
4. **DefaultModelBinder prefix trap**: `Create.cshtml` usa inputs HTML plano (`name="X"`) en lugar de `asp-for` porque el controller firma `Create(CrearRecepcionDto input, ...)` y el prefijo del parámetro no matchea con el ViewModel. Documentado en PR2 body.
5. **CS8602 nullability warning nuevo**: en `Create.cshtml:62`. Harmless, no remediado.

## Files Synced

```
openspec/specs/recepcion-compra-garrafa/spec.md  # NEW — synced from change
openspec/changes/issue-45-recepciones-garrafas/archive-report.md  # NEW — this file
```

## Work-Unit Attempts Ledger

- PR1-backend (acquire: c19df51e..., settle: passed with budget exceeded 500 by 48 LOC)
- PR2-ui (acquire: 1962229f..., settle: passed with budget exceeded 400 by 86 LOC)
- sdd-verify (acquire: 797c057c..., settle: passed)
- sdd-archive (acquire: e129661d..., this run)

## Engram Topics

- `sdd/issue-45-recepciones-garrafas/explore` (3 obs)
- `sdd/issue-45-recepciones-garrafas/proposal` (1 obs)
- `sdd/issue-45-recepciones-garrafas/spec` (1 obs)
- `sdd/issue-45-recepciones-garrafas/design` (1 obs)
- `sdd/issue-45-recepciones-garrafas/tasks` (1 obs)
- `sdd/issue-45-recepciones-garrafas/apply-progress` (1 obs, 1923)
- `sdd/issue-45-recepciones-garrafas/verify-report` (1 obs, 1925)
- `sdd/issue-45-recepciones-garrafas/archive-report` (1 obs, this run)

## Recommendation for Next Steps

- Considerar PR de upgrade de AutoMapper 12.0.1 (vulnerabilidad NU1903 preexistente).
- Considerar agregar UI para `ReversarAsync` en futura issue.
- Considerar recalibrar budget per-work-unit a 600 LOC para cambios UI con validación cliente + modal resumen (patrón repetido en PR1 y PR2).
- Lección: estimaciones de LOC en `design.md` para UI suelen subestimar 20-30%. Agregar margen.
