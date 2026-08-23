# Plantilla de Enriquecimiento

Usar como checklist al enriquecer una issue. No todo aplica a todos los tipos.

## Secciones requeridas según tipo

### Bug Report

| Sección | Obligatoria | Descripción |
|---------|:-----------:|-------------|
| Descripción del bug | Sí | Qué falla, desde cuándo |
| Pasos para reproducir | Sí | Numerados, específicos |
| Comportamiento esperado | Sí | Qué debería ocurrir |
| Comportamiento actual | Sí | Qué ocurre en realidad |
| Logs / errores | No | Stack traces, mensajes |
| Entorno | No | OS, navegador, versión, DB |

### Feature Request

| Sección | Obligatoria | Descripción |
|---------|:-----------:|-------------|
| Problema a resolver | Sí | Por qué es necesario, quién lo necesita |
| Solución propuesta | Sí | Cómo debería funcionar |
| Criterios de aceptación | Sí | Lista verificable de condiciones de done |
| Scope (qué NO incluye) | No | Límites explícitos |
| Alternativas consideradas | No | Qué más se evaluó |

### Tarea técnica / Mejora

| Sección | Obligatoria | Descripción |
|---------|:-----------:|-------------|
| Motivación | Sí | Por qué se hace |
| Qué se va a hacer | Sí | Descripción concreta |
| Criterios de aceptación | Sí | Condiciones verificables |
| Archivos / componentes | Sí | Dónde se trabaja |

## Secciones de Contexto Técnico (agregadas por enriquecimiento)

Estas secciones se agregan bajo `## 🔍 Contexto Técnico (agregado)` al final del cuerpo:

```markdown
## 🔍 Contexto Técnico (agregado)

### Componentes afectados
- `ruta/al/archivo.cs` — qué hace y por qué es relevante
- `ruta/al/componente` — rol en la arquitectura

### Dependencias
- Qué otros módulos, servicios o tablas interactúan con esto

### Consideraciones técnicas
- Restricciones de arquitectura relevantes
- Patrones a seguir (ej. Clean Architecture, CQRS)
- Decisiones técnicas previas que aplican (ver `docs/decisiones-implementacion.md`)

### Plan de pruebas sugerido
- Tipos de tests necesarios (unitarios, integración, etc.)
- Escenarios críticos a cubrir
```

## Regla de preservación

El cuerpo original de la issue se mantiene intacto. Todo el contenido nuevo va exclusivamente dentro del bloque `## 🔍 Contexto Técnico (agregado)`. Si ese bloque ya existe, se actualiza sin tocar el resto.
