# syntax=docker/dockerfile:1.7
# Multi-stage build for Indexer service

ARG DOTNET_VERSION=8.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS restore
WORKDIR /src

# Copy solution and project files only (cache-friendly)
COPY docduck.sln ./
COPY src/Indexer/Indexer.csproj Indexer/
COPY src/Providers.Shared/Providers.Shared.csproj Providers.Shared/

RUN dotnet restore Indexer/Indexer.csproj --nologo

FROM restore AS build
# Copy the remaining source
COPY src/Indexer/ Indexer/
COPY src/Providers.Shared/ Providers.Shared/
WORKDIR /src/Indexer
RUN dotnet publish -c Release -o /app/publish --no-restore --nologo /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION} AS runtime
WORKDIR /app

# Create non-root user
RUN useradd -m -u 1001 appuser && chown -R appuser:appuser /app
USER appuser

COPY --from=build /app/publish .

# Labels for OCI metadata
LABEL org.opencontainers.image.source="https://github.com/abrahamberg/docduck" \
      org.opencontainers.image.title="docduck-indexer" \
      org.opencontainers.image.description="DocDuck indexing and ingestion service" \
      org.opencontainers.image.licenses="MIT"

ENTRYPOINT ["dotnet", "Indexer.dll"]
