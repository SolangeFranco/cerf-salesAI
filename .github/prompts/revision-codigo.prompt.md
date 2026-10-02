---
mode: agent
---

Haz una revisión de código de los cambios actuales.

Busca específicamente:
- problemas de aislamiento por tenant
- errores de idempotencia
- condiciones de carrera
- migraciones peligrosas
- falta de tests
- incompatibilidad con APIs existentes
- secretos expuestos
- lógica que descarte pagos o reservas sin validación

No modifiques código en esta revisión. Devuelve una lista priorizada de hallazgos con nivel de severidad y sugerencias de corrección.