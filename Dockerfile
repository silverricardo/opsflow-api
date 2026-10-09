# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the layer is cached until package references change.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/OpsFlow.Domain/OpsFlow.Domain.csproj src/OpsFlow.Domain/
COPY src/OpsFlow.Api/OpsFlow.Api.csproj src/OpsFlow.Api/
RUN dotnet restore src/OpsFlow.Api/OpsFlow.Api.csproj

COPY src/ src/
RUN dotnet publish src/OpsFlow.Api/OpsFlow.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- runtime ----
# Chiseled: no shell or package manager, non-root by default. "extra" includes ICU, which SqlClient needs.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "OpsFlow.Api.dll"]
