# Build context is the repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against just the project graph first for layer caching.
COPY global.json Directory.Build.props ./
COPY src/Fogos.Domain/Fogos.Domain.csproj src/Fogos.Domain/
COPY src/Fogos.Infrastructure/Fogos.Infrastructure.csproj src/Fogos.Infrastructure/
COPY src/Fogos.Worker/Fogos.Worker.csproj src/Fogos.Worker/
RUN dotnet restore src/Fogos.Worker/Fogos.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Fogos.Worker/Fogos.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "Fogos.Worker.dll"]
