# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only solution and project files first for layer caching
COPY docduck.sln ./
COPY Indexer/Indexer.csproj Indexer/
COPY Providers.Shared/Providers.Shared.csproj Providers.Shared/

# Restore dependencies (no source yet to maximize cache reuse)
RUN dotnet restore Indexer/Indexer.csproj

# Now copy the full source (relying on .dockerignore to keep context lean)
COPY . .

# Ensure the expected directories exist (defensive; will fail early if missing)
RUN test -d Providers.Shared && test -d Indexer

WORKDIR /src/Indexer
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN useradd -m -u 1001 appuser && chown -R appuser:appuser /app
USER appuser

# Copy published app
COPY --from=build --chown=appuser:appuser /app/publish .

# Health check (optional, can be removed if not needed)
HEALTHCHECK NONE

ENTRYPOINT ["dotnet", "Indexer.dll"]
