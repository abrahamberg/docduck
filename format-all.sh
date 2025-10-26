#!/bin/bash
set -e

echo "🔧 Formatting .NET code..."
dotnet format docduck.sln

echo "🔧 Formatting TypeScript code..."
cd src/web
npm run format
cd ../..

echo "✅ All code formatted successfully!"
