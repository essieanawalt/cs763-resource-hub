# syntax=docker/dockerfile:1
# Tool: Claude Code (Claude Opus 5.5)
# Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
# Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
# AI Contribution: Two-stage build of this PA3 copy (locked restore, Release publish) running as the image's non-root user (~100%)
# Human Modifications: None yet
# Verification: docker compose build/up via ComposeStackTests (RESOURCEHUB_COMPOSE_TESTS=1)
# Confidence: High

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

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080

COPY --from=build /app/publish .

# Built-in non-root user in the .NET images (CWE-250).
USER $APP_UID

ENTRYPOINT ["dotnet", "ResourceHub.Api.dll"]
