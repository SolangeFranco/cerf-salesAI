# syntax=docker/dockerfile:1
# Build context is the repository root (see infrastructure/docker-compose.yml and CI).

# ---- Restore & build -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Restore first with only the project files so this layer is cached whenever
# only application code (not dependencies) changes — faster, reproducible rebuilds.
COPY apps/api/src/Vanta.Api/Vanta.Api.csproj apps/api/src/Vanta.Api/
RUN dotnet restore apps/api/src/Vanta.Api/Vanta.Api.csproj

COPY apps/api/src/Vanta.Api/ apps/api/src/Vanta.Api/
RUN dotnet publish apps/api/src/Vanta.Api/Vanta.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

# ---- Runtime ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final

# curl is required for the container HEALTHCHECK below; nothing else is added.
RUN apk add --no-cache curl krb5-libs \
    && addgroup -S vanta -g 5678 \
    && adduser -S vanta -G vanta -u 5678

WORKDIR /app
COPY --from=build --chown=vanta:vanta /app/publish .

USER vanta
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080

# Liveness only: does not depend on Postgres, matches /health/live semantics
# documented in docs/OBSERVABILITY.md.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Vanta.Api.dll"]
