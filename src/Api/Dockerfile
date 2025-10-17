FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files, restore dependencies
COPY docduck.sln ./
COPY src/Api/Api.csproj Api/
COPY src/Providers.Shared/Providers.Shared.csproj Providers.Shared/
RUN dotnet restore Api/Api.csproj

# Copy source and build
COPY src/Api/ Api/
COPY src/Providers.Shared/ Providers.Shared/
RUN dotnet publish Api/Api.csproj -c Release -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
WORKDIR /app

# Create non-root user
RUN addgroup -g 1000 appuser && \
    adduser -D -u 1000 -G appuser appuser

# Copy published app
COPY --from=build /app/publish .

# Switch to non-root user
USER appuser

# Expose port
EXPOSE 8080

# Set ASP.NET Core to listen on port 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Api.dll"]
