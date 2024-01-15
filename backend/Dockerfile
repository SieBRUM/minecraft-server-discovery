# See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.
# Change the ENV variable on line 6 to your own!

FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app
ENV MINECRAFT_DISCOVERY_DB server=mysql-container;user=root;password=my-secret-password;database=minecraftdiscoverytool
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["MinecraftServerDiscoveryApi/MinecraftServerDiscoveryApi.csproj", "MinecraftServerDiscoveryApi/"]
RUN dotnet restore "MinecraftServerDiscoveryApi/MinecraftServerDiscoveryApi.csproj"
COPY . .
WORKDIR "/src/MinecraftServerDiscoveryApi"
RUN dotnet build "MinecraftServerDiscoveryApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MinecraftServerDiscoveryApi.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MinecraftServerDiscoveryApi.dll"]
