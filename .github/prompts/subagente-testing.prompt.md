---
mode: agent
---

Usa un subagente para analizar y diseñar tests de negocio para la tarea actual.

El subagente debe:
- localizar los tests existentes
- revisar herramientas de testing del proyecto
- indicar qué casos deben cubrirse
- no modificar primero la implementación
- devolver una lista priorizada de casos:
  - éxito
  - error
  - límite
  - idempotencia
  - concurrencia
  - seguridad y tenant

Luego, si te lo pido, implementa solo esos tests y la corrección mínima necesaria.