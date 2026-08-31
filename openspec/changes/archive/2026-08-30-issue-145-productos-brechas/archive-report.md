# Archive Report: issue-145-productos-brechas

```yaml
schema: gentle-ai.sdd-archive/v1
change: issue-145-productos-brechas
archived_at: 2026-08-30T00:00:00-03:00
archive_path: openspec/changes/archive/2026-08-30-issue-145-productos-brechas/
mode: hybrid
status: complete
```

## Final State

| Field | Value |
|-------|-------|
| Change | `issue-145-productos-brechas` |
| Archived | 2026-08-30 |
| Archive | `openspec/changes/archive/2026-08-30-issue-145-productos-brechas/` |
| Mode | hybrid (OpenSpec filesystem + Engram) |
| Status | **complete** |

## Final State Facts (per Final-State Authority)

> Explicit final-state facts from the orchestrator's launch prompt outrank intermediate snapshots. All `verify-report` intermediate snapshot claims are superseded.

| Fact | Value | Source |
|------|-------|--------|
| PRs | #148, #149, #150, #151 (OPEN, stacked) | Orchestrator prompt |
| Tests | **347** (278 pre-cambio → +69) | Orchestrator prompt |
| Lines | ~2620 across 4 slices | Orchestrator prompt |
| Acceptance criteria | **4/4** fulfilled | Orchestrator prompt |
| ADRs | #18 (append-only histórico precios) + #19 (invariante Activo) | Orchestrator prompt |
| Migration | Applied to homelab, checksum in `schema_migrations` | Orchestrator prompt |
| Stack head | `feat/issue-145-slice-4-integrity` base `develop` | Orchestrator prompt |
| verify-report slice 1 | PASS WITH WARNINGS (CRITICAL: 0, blockers: 0) | `verify-report.md` |
| verify-report slice 2 | PASS WITH WARNINGS (CRITICAL: 0, blockers: 0) | `verify-report-slice-2.md` |
| verify-report slice 3 | PASS WITH WARNINGS (CRITICAL: 0, blockers: 0) | `verify-report-slice-3.md` |
| verify-report slice 4 | PASS WITH WARNINGS (CRITICAL: 0, blockers: 0) | `verify-report-slice-4.md` |

All 4 verify reports have verdict `pass_with_warnings` with **zero CRITICAL issues**. The warnings are pre-existing architectural limits (no WebApplicationFactory, no bunit, no Sonar server-side in verify phase) consistent across all slices.

## Specs Synced to Main

| Domain | Action | Requirements |
|--------|--------|-------------|
| `productos` | **Created** (new capability) | 4 requirements: RestoreAsync, Activo invariant, price-history hook, MotivoCambioPrecio DTO |
| `producto-precio-historico` | **Created** (new capability) | 4 requirements: schema, append-only, hook writes, audit queries |
| `recepciones` | **Created** (new capability) | 2 requirements: dropdown excludes inactivos, pre-commit validation |
| `pedidos` | **Created** (new capability) | 1 requirement: validation at CONFIRMADO transition |

All 4 main specs were created by copying the delta specs since no prior spec existed for these domains.

## Archive Contents

```
openspec/changes/archive/2026-08-30-issue-145-productos-brechas/
├── proposal.md                   ✅ (92 lines)
├── design.md                    ✅ (132 lines)
├── tasks.md                     ✅ (59 lines, all tasks checked)
├── specs/
│   ├── productos/spec.md         ✅ (81 lines)
│   ├── producto-precio-historico/spec.md ✅ (65 lines)
│   ├── recepciones/spec.md      ✅ (31 lines)
│   └── pedidos/spec.md          ✅ (33 lines)
├── verify-report.md             ✅ (slice 1, PASS WITH WARNINGS)
├── verify-report-slice-2.md     ✅ (slice 2, PASS WITH WARNINGS)
├── verify-report-slice-3.md     ✅ (slice 3, PASS WITH WARNINGS)
└── verify-report-slice-4.md     ✅ (slice 4, PASS WITH WARNINGS)
```

All tasks checked in `tasks.md` (chore commit `0c187e2` closed the bookkeeping gap flagged in verify-report-slice-3 WARNING #1).

## Source of Truth Updated

The following main specs now reflect the new behavior:

| Main Spec Path | What Changed |
|---------------|-------------|
| `openspec/specs/productos/spec.md` | RestoreAsync + Activo invariant + price-history hook + MotivoCambioPrecio |
| `openspec/specs/producto-precio-historico/spec.md` | New append-only audit table |
| `openspec/specs/recepciones/spec.md` | Activo filter in dropdown + pre-commit validation |
| `openspec/specs/pedidos/spec.md` | Activo validation at CONFIRMADO transition |

## Acceptance Criteria

| # | Criterion | Verified | Evidence |
|---|-----------|----------|----------|
| 1 | `RestoreAsync` + Controller action + botón UI + tests | ✅ | verify-report-slice-2.md |
| 2 | `RecepcionService` filtra `&& p.Activo` + regression test | ✅ | verify-report-slice-4.md |
| 3 | `PedidoService` valida `Activo` al confirmar + mensaje claro | ✅ | verify-report-slice-4.md |
| 4 | `producto_precios_historico` tabla + hook + ≥1 test | ✅ | verify-report.md + verify-report-slice-3.md |

**4/4 — Issue #145 complete.**

## Technical Details

### PR Stack
- `feat/issue-145-slice-4-integrity` ← HEAD (stacked on `develop`)
- `feat/issue-145-slice-3-price-history` ← PR #150
- `feat/issue-145-slice-2-producto-restore` ← PR #149
- `feat/issue-145-slice-1-db-foundation` ← PR #148

### Tests
- **347/347** total (full suite)
- +69 net new tests across 4 slices
- Coverage on new code: 100% line + 100% branch (verified per-slice in verify reports)

### ADRs
- **ADR #18** — Histórico append-only de precios (`db/docs/DECISIONES.md`)
- **ADR #19** — Invariante `producto.Activo ⇒ visible en dropdowns de Pedidos/Recepciones`

### Known Warnings (non-blocking, accepted by design)
1. No WebApplicationFactory for 403 middleware tests (pre-existing repo limit)
2. No bunit for Razor view assertions (pre-existing repo limit)
3. Audit SQL queries not unit-tested (trivial SQL over indexed append-only table)
4. Phase 3 tasks.md checkboxes not ticked by apply (fixed in chore commit `0c187e2`)
5. SDD artifact files were untracked in git (fixed by `git add` before archive commit)

## SDD Cycle Summary

| Phase | Duration | Notes |
|-------|----------|-------|
| sdd-propose | Slice 1 | 4 data-integrity gaps identified |
| sdd-spec | 4 deltas | productos, producto-precio-historico, recepciones, pedidos |
| sdd-design | Full design doc | 9 architecture decisions |
| sdd-tasks | 4 phases | ~940 forecast → ~2620 actual (4 slices) |
| sdd-apply | 4 slices | Stacked PRs #148–#151 |
| sdd-verify | 4 reports | All PASS WITH WARNINGS, 0 CRITICAL |
| sdd-archive | 2026-08-30 | Change closed |

## Next Steps

1. **Merge PR stack**: Review and merge #148 → #149 → #150 → #151 in sequence
2. **SonarQube server-side**: Run `scripts/sonar-analyze.sh` against `feat/issue-145-slice-4-integrity` to confirm Quality Gate (`new_coverage ≥ 65%`) before merge
3. **Smoke test on homelab**: After merge, run `SELECT * FROM producto_precios_historico ORDER BY changed_at DESC LIMIT 5` to verify the price-history table is being written to

---

*Archive report generated by `sdd-archive` on 2026-08-30. Artifact store: hybrid (OpenSpec + Engram).*
