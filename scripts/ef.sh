#!/usr/bin/env bash
# Run dotnet-ef inside a .NET 10 SDK container that has the tool preinstalled.
# Usage: scripts/ef.sh <ef args...>   (working dir: backend/)
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
docker build -q -t invoicing-dev-sdk -f "${REPO_ROOT}/backend/dev-sdk.Dockerfile" "${REPO_ROOT}/backend"
exec docker run --rm \
  -u "$(id -u):$(id -g)" \
  -e HOME=/tmp/home \
  -e NUGET_PACKAGES=/nuget \
  -v "${REPO_ROOT}/backend:/src" \
  -v invoicing-nuget:/nuget \
  -w /src \
  invoicing-dev-sdk dotnet ef "$@"
