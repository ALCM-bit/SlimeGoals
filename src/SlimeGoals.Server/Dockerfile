# O contexto de build é a raiz do repositório: docker build -f src/SlimeGoals.Server/Dockerfile .
# Imagens fixadas pelo digest (o Dependabot atualiza): uma tag pode passar a apontar para outra imagem.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /src

# Primeiro só o que a restauração precisa, para a camada de pacotes ficar em cache.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/SlimeGoals.Server/SlimeGoals.Server.csproj src/SlimeGoals.Server/
COPY src/SlimeGoals.Sync.Contracts/SlimeGoals.Sync.Contracts.csproj src/SlimeGoals.Sync.Contracts/
RUN dotnet restore src/SlimeGoals.Server/SlimeGoals.Server.csproj

COPY src/SlimeGoals.Server/ src/SlimeGoals.Server/
COPY src/SlimeGoals.Sync.Contracts/ src/SlimeGoals.Sync.Contracts/
RUN dotnet publish src/SlimeGoals.Server/SlimeGoals.Server.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:11.0-alpine@sha256:d39b72ad1534fa0faf52b488bffb0762df7ba269d120f0338713d4dda2a41bf5 AS runtime
WORKDIR /app
COPY --from=build /out .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "SlimeGoals.Server.dll"]
