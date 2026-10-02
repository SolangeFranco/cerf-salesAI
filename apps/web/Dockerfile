# syntax=docker/dockerfile:1
# Build context is the repository root (see infrastructure/docker-compose.yml and CI).

# ---- Dependencies -----------------------------------------------------------
FROM node:20-alpine AS deps
WORKDIR /app
COPY apps/web/package.json apps/web/package-lock.json ./
RUN npm ci

# ---- Build --------------------------------------------------------------
FROM node:20-alpine AS build
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
COPY --from=deps /app/node_modules ./node_modules
COPY apps/web/ .
RUN npm run build

# ---- Runtime ------------------------------------------------------------
FROM node:20-alpine AS final
RUN addgroup -S vanta -g 5678 && adduser -S vanta -G vanta -u 5678

WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0

# Next.js "standalone" output only includes the production node_modules subset
# actually required at runtime — this is what keeps the final image small.
COPY --from=build --chown=vanta:vanta /app/public ./public
COPY --from=build --chown=vanta:vanta /app/.next/standalone ./
COPY --from=build --chown=vanta:vanta /app/.next/static ./.next/static

USER vanta
EXPOSE 3000

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD wget -q -O- http://127.0.0.1:3000/login || exit 1

CMD ["node", "server.js"]
