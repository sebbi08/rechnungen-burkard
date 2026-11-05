#!/usr/bin/env bash
# Run dotnet inside the official .NET 10 SDK container (host has no SDK).
# Usage: scripts/dotnet.sh <dotnet args...>   (working dir: backend/)
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
exec docker run --rm \
  -u "$(id -u):$(id -g)" \
  -e HOME=/tmp/home \
  -e DOTNET_CLI_HOME=/tmp/home \
  -e NUGET_PACKAGES=/nuget \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -v "${REPO_ROOT}/backend:/src" \
  -v invoicing-nuget:/nuget \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:11.0 dotnet "$@"
