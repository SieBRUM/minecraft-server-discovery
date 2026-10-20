# syntax=docker/dockerfile:1
# Scanner image (includes masscan). Build context: repository root.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/McDiscovery.Core/McDiscovery.Core.csproj src/McDiscovery.Core/
COPY src/McDiscovery.Protocol/McDiscovery.Protocol.csproj src/McDiscovery.Protocol/
COPY src/McDiscovery.Scanner/McDiscovery.Scanner.csproj src/McDiscovery.Scanner/
RUN dotnet restore src/McDiscovery.Scanner/McDiscovery.Scanner.csproj -a $TARGETARCH
COPY src/McDiscovery.Core/ src/McDiscovery.Core/
COPY src/McDiscovery.Protocol/ src/McDiscovery.Protocol/
COPY src/McDiscovery.Scanner/ src/McDiscovery.Scanner/
RUN dotnet publish src/McDiscovery.Scanner/McDiscovery.Scanner.csproj -c Release -a $TARGETARCH --no-restore -o /out

FROM mcr.microsoft.com/dotnet/runtime:10.0
# masscan needs raw sockets. Instead of running as root, the binary gets file capabilities; the container
# must be started with cap_add NET_RAW + NET_ADMIN (see docker-compose.yml) for them to take effect.
# libpcap is only a "recommends" of the masscan package, but masscan dlopen()s it at runtime.
RUN apt-get update \
    && apt-get install -y --no-install-recommends masscan libpcap0.8t64 libcap2-bin \
    && setcap cap_net_raw,cap_net_admin+eip /usr/bin/masscan \
    && apt-get purge -y libcap2-bin \
    && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data /config \
    && chown app:app /data
WORKDIR /app
COPY --from=build /out ./
USER app
ENV MCD_DB_PATH=/data/mcdiscovery.db \
    MCD_GEOIP_DIR=/data/geoip \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
ENTRYPOINT ["dotnet", "mcd-scanner.dll"]
CMD ["--help"]
