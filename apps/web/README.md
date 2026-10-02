# VANTA Web

Aplicación Next.js 16 / React 19 para el dashboard del comercio, storefront público, checkout y BFF hacia la API .NET. Las rutas protegidas no sustituyen autorización: el API valida JWT y tenant en cada operación.

## Desarrollo local

Desde `apps/web`:

```sh
npm ci
npm run dev
```

Configura `API_BASE_URL` para apuntar a la API; con Docker Compose se define como `http://api:8080`. El servidor local queda en <http://localhost:3000>.

## Scripts

```sh
npm run lint
npm run typecheck
npm run build
npm run test:e2e
```

Los E2E necesitan una API, base y datos de prueba disponibles según `playwright.config.ts`. La imagen Docker usa salida standalone; este README no autoriza un deploy en Vercel ni en otro proveedor.

Consulta [../../docs/API.md](../../docs/API.md), [../../docs/CI_CD.md](../../docs/CI_CD.md) y [../../docs/README.md](../../docs/README.md) para contratos y estado del proyecto.
