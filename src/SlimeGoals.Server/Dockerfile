# O contexto de build é a raiz do repositório: docker build -f src/SlimeGoals.Server/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primeiro só o que a restauração precisa, para a camada de pacotes ficar em cache.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/SlimeGoals.Server/SlimeGoals.Server.csproj src/SlimeGoals.Server/
COPY src/SlimeGoals.Sync.Contracts/SlimeGoals.Sync.Contracts.csproj src/SlimeGoals.Sync.Contracts/
RUN dotnet restore src/SlimeGoals.Server/SlimeGoals.Server.csproj

COPY src/SlimeGoals.Server/ src/SlimeGoals.Server/
COPY src/SlimeGoals.Sync.Contracts/ src/SlimeGoals.Sync.Contracts/
RUN dotnet publish src/SlimeGoals.Server/SlimeGoals.Server.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /out .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "SlimeGoals.Server.dll"]
