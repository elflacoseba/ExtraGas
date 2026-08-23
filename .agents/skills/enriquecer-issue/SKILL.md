---
name: enriquecer-issue
description: "Trigger: enriquecer issue, mejorar issue, completar issue, enrich issue, agregar contexto a issue. Enriquece una issue de GitHub existente con contexto del codebase, criterios de aceptación y detalles técnicos para eliminar ambigüedades."
license: Apache-2.0
metadata:
  author: "elflacoseba"
  version: "1.0"
---

# Activation Contract

Activar cuando el usuario pide enriquecer, mejorar o completar una issue de GitHub **existente**. No usar para crear issues nuevas — para eso existe `issue-creation`.

# Hard Rules

- Leer siempre la issue actual antes de modificarla. Nunca sobreescribir información existente que sea correcta.
- Usar `codegraph_explore` para identificar componentes, archivos y dependencias afectadas antes de escribir detalles técnicos. No inferir sin evidencia del codebase.
- Presentar el enriquecimiento propuesto al usuario para aprobación. Nunca modificar la issue sin confirmación explícita.
- Si el `issue_number` no está claro, preguntarlo.
- Usar `github_issue_read` (method: `get`) para leer y `github_issue_write` (method: `update`) para modificar.

# Decision Gates

| Escenario | Acción |
|-----------|--------|
| Issue es un bug | Verificar: pasos para reproducir, comportamiento esperado vs actual, logs, entorno |
| Issue es un feature request | Verificar: problema que resuelve, solución propuesta, scope, criterios de aceptación |
| Faltan detalles de implementación | Explorar codebase con `codegraph_explore` para identificar archivos y componentes |
| Issue ya tiene toda la info | Informar que está completa, no modificar |
| Hay múltiples issues candidatas | Pedir al usuario que confirme cuál enriquecer |

# Execution Steps

1. Leer la issue con `github_issue_read(method: "get", issue_number: N)`.
2. Clasificar el tipo de issue (bug, feature, tarea) y detectar qué falta. Cargar `assets/plantilla-enriquecimiento.md` como guía de secciones esperadas.
3. Si faltan detalles técnicos, explorar el codebase con `codegraph_explore` usando términos del título y descripción de la issue.
4. Ensamblar el cuerpo enriquecido como markdown estructurado, preservando intacto el contenido original y agregando las secciones nuevas al final bajo `## 🔍 Contexto Técnico (agregado)`.
5. Mostrar el resultado al usuario y pedir confirmación.
6. Actualizar la issue con `github_issue_write(method: "update", body: "<nuevo cuerpo>")`.

# Output Contract

Al finalizar, confirmar:
- Issue enriquecida (número y URL).
- Secciones agregadas.
- Archivos o componentes identificados en el codebase.

# References

- `assets/plantilla-enriquecimiento.md` — secciones esperadas según tipo de issue.
