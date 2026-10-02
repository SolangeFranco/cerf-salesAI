# Despliegue de staging gratuito sin tarjeta

**Arquitectura:** Vercel Hobby para Next.js/BFF, Supabase Free para PostgreSQL + pgvector y Render Free para API .NET + worker.

Este entorno acepta exclusivamente datos sintéticos. Render Free duerme tras 15 minutos sin tráfico; el primer request puede tardar cerca de un minuto y el worker no procesa jobs mientras la API está dormida. No es producción.

## Estado

- GitHub Actions publica correctamente desde `main`.
- Supabase `vanta-staging` fue creado y `vector` está habilitado.
- `render.yaml` define la API gratuita.
- Faltan crear el Blueprint de Render y el proyecto Vercel.

## 1. Crear la API en Render

1. Abrir <https://dashboard.render.com/register> en incógnito.
2. Continuar con GitHub usando únicamente `RomVeron`.
3. Autorizar acceso al repositorio privado `RomVeron/salesAgentAI`.
4. En Render, elegir **New > Blueprint**.
5. Seleccionar `salesAgentAI`, rama `main`, archivo `render.yaml`.
6. Confirmar que crea sólo un Web Service llamado `vanta-staging-api`, región Virginia y plan Free.

Render solicitará tres valores secretos:

- `ConnectionStrings__Default`: connection string Npgsql del Session pooler de Supabase.
- `Commerce__BaseUrl`: usar temporalmente `https://example.invalid`.
- `Cors__AllowedOrigins__0`: usar temporalmente `https://example.invalid`.

Formato Npgsql esperado:

```text
Host=<pooler-host>;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<secret>;SSL Mode=Require;Trust Server Certificate=true;Maximum Pool Size=10
```

No pegar esos valores en chat, GitHub o archivos del repositorio. Render genera automáticamente `Jwt__SigningKey` y `WhatsApp__MockWebhookSecret`.

El primer deploy construye el Dockerfile, aplica migraciones EF y arranca API/worker. Esperar estado **Live** y copiar la URL, por ejemplo:

```text
https://vanta-staging-api.onrender.com
```

Validar:

```sh
curl -fsS https://<render-host>/health/live
curl -fsS https://<render-host>/health/ready
```

## 2. Crear el frontend en Vercel

1. Abrir <https://vercel.com/new> en incógnito.
2. Continuar con GitHub como `RomVeron`.
3. Importar `RomVeron/salesAgentAI`.
4. Configurar **Root Directory** como `apps/web`.
5. Mantener framework Next.js y plan Hobby.
6. Añadir variables para Production y Preview:

```text
API_BASE_URL=https://<render-host>
NEXT_PUBLIC_ENABLE_STAGING_DEMO=true
```

7. Desplegar y guardar la URL final `https://<project>.vercel.app`.

`API_BASE_URL` es server-only. El JWT permanece en cookie httpOnly. El flag público sólo muestra la opción de canal sintético; el secreto del webhook permanece en Render.

## 3. Completar URLs en Render

En Render, abrir `vanta-staging-api > Environment` y reemplazar:

```text
Commerce__BaseUrl=https://<project>.vercel.app
Cors__AllowedOrigins__0=https://<project>.vercel.app
```

Guardar y redeployar. Volver a comprobar `/health/ready`.

## 4. Smoke test

Desde Vercel:

1. Registrar un merchant sintético.
2. Elegir **Continuar con canal de prueba**.
3. Completar nombre de tienda y tarifas de delivery.
4. Crear producto, variante y stock.
5. Revisar dashboard y estados vacíos.
6. Crear pedido y transferencia manual.
7. Confirmar que el pago queda pendiente hasta confirmación autenticada.

Meta Cloud API y el bridge QR no se prueban en esta variante gratuita. El canal sintético existe sólo para onboarding y validación funcional.

## 5. Actualizaciones y rollback

Render usa `autoDeployTrigger: checksPass`: sólo despliega un commit después de que GitHub Actions pasa. Vercel despliega `main` automáticamente.

Para rollback:

- Render: **Deploys > Rollback** a uno de los builds retenidos.
- Vercel: **Deployments > Promote to Production** sobre el deploy anterior.
- No revertir una migración destructiva sin backup y procedimiento específico.

## 6. Límites

- Vercel Hobby: personal/no comercial.
- Supabase Free: 500 MB, puede pausarse y no incluye backups productivos.
- Render Free: 512 MB, 0.1 CPU, duerme tras 15 minutos, filesystem efímero y sin one-off jobs.
- El worker de la API se detiene mientras Render duerme.
- No hay sesiones linked-device persistentes.
- Sin SLA, restore drill, OTLP externo ni credenciales reales.

Para producción, usar compute siempre activo, backup/restore probado, WhatsApp Meta real, observabilidad y los gates de [PRODUCTION_READINESS.md](PRODUCTION_READINESS.md).
