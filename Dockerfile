# syntax=docker/dockerfile:1
# AI Contribution Weight: [100%] AI Generated / [0%] Human Refactored

# ---- build stage: .NET SDK ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached between code changes.
COPY global.json Directory.Build.props ./
COPY src/ResourceHub.Api/ResourceHub.Api.csproj src/ResourceHub.Api/packages.lock.json src/ResourceHub.Api/
RUN dotnet restore src/ResourceHub.Api/ResourceHub.Api.csproj --locked-mode

COPY src/ src/
RUN dotnet publish src/ResourceHub.Api/ResourceHub.Api.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

# ---- runtime stage: ASP.NET Core runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

# Built-in non-root user in the .NET images.
USER $APP_UID

ENTRYPOINT ["dotnet", "ResourceHub.Api.dll"]
