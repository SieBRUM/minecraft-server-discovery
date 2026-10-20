# syntax=docker/dockerfile:1
# API + website image. Build context: repository root.

# ---- Angular website ---------------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM node:24-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npx ng build

# ---- .NET API ----------------------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/McDiscovery.Core/McDiscovery.Core.csproj src/McDiscovery.Core/
COPY src/McDiscovery.Api/McDiscovery.Api.csproj src/McDiscovery.Api/
RUN dotnet restore src/McDiscovery.Api/McDiscovery.Api.csproj -a $TARGETARCH
COPY src/McDiscovery.Core/ src/McDiscovery.Core/
COPY src/McDiscovery.Api/ src/McDiscovery.Api/
RUN dotnet publish src/McDiscovery.Api/McDiscovery.Api.csproj -c Release -a $TARGETARCH --no-restore -o /out

# ---- Runtime -----------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out ./
COPY --from=web /web/dist/web/browser ./wwwroot
# /data is the persistent volume (SQLite database). Owned by the unprivileged "app" user of the base image.
RUN mkdir -p /data && chown app:app /data
USER app
# Listen on all interfaces inside the container (the default in appsettings.json is 127.0.0.1). Unprefixed
# environment variables override appsettings.json; ASPNETCORE_URLS would not. docker-compose.yml decides which
# host interface the port is published on.
ENV URLS=http://0.0.0.0:8080 \
    MCD_DB_PATH=/data/mcdiscovery.db \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080
ENTRYPOINT ["dotnet", "mcd-api.dll"]
