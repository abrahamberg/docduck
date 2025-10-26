# Makefile for DocDuck project

.PHONY: help format format-dotnet format-web format-check test build clean

help:
	@echo "DocDuck Project Commands:"
	@echo "  make format        - Format all code (.NET + TypeScript)"
	@echo "  make format-dotnet - Format only .NET code"
	@echo "  make format-web    - Format only TypeScript code"
	@echo "  make format-check  - Check formatting without changes"
	@echo "  make test          - Run all tests"
	@echo "  make build         - Build all projects"
	@echo "  make clean         - Clean build artifacts"

format:
	@echo "🔧 Formatting all code..."
	@dotnet format docduck.sln
	@cd src/web && npm run format
	@echo "✅ All code formatted successfully!"

format-dotnet:
	@echo "🔧 Formatting .NET code..."
	@dotnet format docduck.sln
	@echo "✅ .NET code formatted!"

format-web:
	@echo "🔧 Formatting TypeScript code..."
	@cd src/web && npm run format
	@echo "✅ TypeScript code formatted!"

format-check:
	@echo "🔍 Checking code formatting..."
	@dotnet format docduck.sln --verify-no-changes
	@cd src/web && npm run format:check
	@echo "✅ All code is properly formatted!"

test:
	@echo "🧪 Running all tests..."
	@dotnet test
	@cd src/web && npm test
	@echo "✅ All tests passed!"

build:
	@echo "🏗️  Building all projects..."
	@dotnet build docduck.sln --configuration Release
	@cd src/web && npm run build
	@echo "✅ Build complete!"

clean:
	@echo "🧹 Cleaning build artifacts..."
	@dotnet clean docduck.sln
	@rm -rf src/web/dist src/web/node_modules/.vite
	@echo "✅ Clean complete!"
