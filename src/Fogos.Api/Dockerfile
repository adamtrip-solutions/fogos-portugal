# Build context is the repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against just the project graph first for layer caching.
COPY global.json Directory.Build.props ./
COPY src/Fogos.Domain/Fogos.Domain.csproj src/Fogos.Domain/
COPY src/Fogos.Infrastructure/Fogos.Infrastructure.csproj src/Fogos.Infrastructure/
COPY src/Fogos.Api/Fogos.Api.csproj src/Fogos.Api/
RUN dotnet restore src/Fogos.Api/Fogos.Api.csproj

COPY src/ src/
RUN dotnet publish src/Fogos.Api/Fogos.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "Fogos.Api.dll"]
