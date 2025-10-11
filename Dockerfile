# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files and restore dependencies
COPY Indexer/Indexer.csproj Indexer/
RUN dotnet restore Indexer/Indexer.csproj

# Copy source and build
COPY Indexer/ Indexer/
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
