# Release Checklist

**Corte:** 27 de septiembre de 2026
**Decisión actual:** NOT READY FOR PRODUCTION. No se ha ejecutado despliegue público.

## Verificación local del 27 de septiembre

- Compose levantado sin borrar volúmenes; API/Web/Postgres/Redis healthy y bridge activo.
- API `/health/live` y `/health/ready`: HTTP 200; `/login` y `/store/vanta-demo`: HTTP 200.
- API público: storefront `vanta-demo` devuelve un producto; referencia de checkout inválida devuelve 404.
- `PaymentsTests` + `PublicCommerceTests`: 15 passed, 0 failed.
- E2E final: **6 passed, 0 failed** seriales con fixtures por merchant/slug aislados; incluye transferencia manual y checkout público.
- Tests backend final: **279 passed, 0 failed, 0 skipped**.
- API/Web/DB local healthy tras reset y migraciones; usuario demo fijo retirado.
- Landing y dashboard limpiados de métricas, testimonios, precios y órdenes ficticios; KPIs salen del resumen tenant-scoped.
- Se reparó metadata de `AddProductSkuSequence`, que impedía crear la secuencia de ProductNumber en bases nuevas.

## Evidencia local más reciente

- API .NET: build Release correcto.
- Tests `tests/unit/Vanta.Api.UnitTests.csproj`: 279 passed, 0 failed, 0 skipped (28 Sep 2026).
- Web: typecheck y production build correctos.
- ESLint global con `--quiet`: cero errores; quedan warnings de React/TypeScript.
- `git diff --check`: verificado en el último cambio documental.
- CI publica imágenes inmutables por SHA tras los gates configurados; no realiza deploy automático a staging/producción.

Estos checks no validan credenciales externas, cloud, restore ni operación real.

## Gates obligatorios

- [x] Suite E2E determinista local verde con mocks y fixtures aislados.
- [x] KPIs del dashboard tenant-scoped o estado vacío; retirados KPIs/órdenes ficticios del frontend.
- [ ] Backup configurado y ejecutado; restauración exitosa en base aislada y RPO/RTO registrados.
- [ ] Entorno, proveedor cloud, costo mensual, dominio/TLS, PostgreSQL y gestión de secretos aprobados.
- [ ] JWT fuerte; WhatsApp Meta y AI configurados desde secret manager; OTLP externo y alertas comprobados.
- [ ] Procedimiento operativo de transferencia aprobado: quién valida el depósito, quién confirma, cómo soportar discrepancias y cómo se tramitan refunds fuera de VANTA.
- [ ] Pruebas manuales: datos bancarios por tenant, checkout público, WhatsApp, pago pendiente antes de confirmar, confirmación tenant-scoped después de verificar abono, idempotencia y expiración.
- [ ] Migración aplicada con release job aprobado; smoke test, rollback de imagen y plan de incidentes verificados.
- [ ] Revisión de privacidad/seguridad y logs sin secretos o PII innecesaria.

## Pago disponible

`manual-transfer` es el único provider registrado en Production. Mock sólo Development/Testing. Transferencias no son confirmadas automáticamente; el comprobante enviado por el cliente no cambia el estado. No hay refund bancario automático ni procesador online.

## Resultado

No liberar producción mientras algún gate obligatorio esté abierto. El cambio de estado de este checklist requiere evidencia y aprobación, no inferencia por un build exitoso.

Fuentes: [PRODUCTION_READINESS.md](PRODUCTION_READINESS.md), [SECURITY.md](SECURITY.md), [DEPLOYMENT_PRODUCTION.md](DEPLOYMENT_PRODUCTION.md).
