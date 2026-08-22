# syntax=docker/dockerfile:1

# ---- 1. Web assets: Tailwind stylesheet + model-viewer bundle -------------------------------
FROM node:22-alpine AS assets
WORKDIR /web
COPY src/MeshKit.Web/package.json src/MeshKit.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/MeshKit.Web/Styles ./Styles
COPY src/MeshKit.Web/Components ./Components
RUN npm run build

# ---- 2. Publish --------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Whole tree in one layer: restoring from csproj alone drops framework static web assets
# (_framework/*.js) from the publish output, which silently breaks Blazor in production.
COPY . .
COPY --from=assets /web/wwwroot/css ./src/MeshKit.Web/wwwroot/css
COPY --from=assets /web/wwwroot/js ./src/MeshKit.Web/wwwroot/js
RUN dotnet publish src/MeshKit.Web/MeshKit.Web.csproj -c Release -o /app/publish -p:SkipNpm=true

# ---- 3. Runtime --------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system --gid 1001 meshkit \
    && useradd --system --uid 1001 --gid meshkit --home /app --shell /usr/sbin/nologin meshkit \
    && mkdir -p /app/data /app/catalog \
    && chown -R meshkit:meshkit /app
WORKDIR /app
COPY --from=build --chown=meshkit:meshkit /app/publish .
USER meshkit

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ConnectionStrings__AppDb="Data Source=/app/data/meshkit.db" \
    DataProtection__KeysPath=/app/data/keys \
    MeshKit__Catalog__Path=/app/catalog

VOLUME ["/app/data", "/app/catalog"]
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "MeshKit.Web.dll"]
