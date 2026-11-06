#!/usr/bin/env bash
# Run CSharpier over backend/ inside the .NET 10 SDK container (host has no SDK).
# Usage: scripts/csharpier.sh format .     # rewrite
#        scripts/csharpier.sh check .      # fail if anything is unformatted
#
# Deliberately not scripts/dotnet.sh: the tool has to be restored before it can
# run, and doing both in one container start keeps this fast enough for the
# pre-commit hook. CSharpier parses syntax and never loads MSBuild, so unlike
# `dotnet format` it does not restore into backend/obj and cannot collide with
# the dev container's dotnet watch (see CLAUDE.md).
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
  mcr.microsoft.com/dotnet/sdk:11.0 \
  sh -c 'dotnet tool restore >/dev/null && exec dotnet csharpier "$@"' -- "$@"
