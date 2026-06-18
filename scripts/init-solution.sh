#!/usr/bin/env bash
# Creates the stashr solution and adds all projects. Idempotent-ish: safe to re-run
# after `rm stashr.sln`. Run from the repo root on a machine with the .NET 8 SDK.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet new sln -n stashr --force

# src projects (added as they are created)
for proj in \
  src/Stashr.Core/Stashr.Core.csproj \
  src/Stashr.Crypto/Stashr.Crypto.csproj \
  src/Stashr.Storage.InMemory/Stashr.Storage.InMemory.csproj \
  src/Stashr.Storage.Postgres/Stashr.Storage.Postgres.csproj \
  src/Stashr.Engine/Stashr.Engine.csproj \
  src/Stashr.Engines.Database/Stashr.Engines.Database.csproj \
  src/Stashr.Server/Stashr.Server.csproj \
  src/Stashr.Client/Stashr.Client.csproj \
  src/Stashr.Configuration/Stashr.Configuration.csproj \
  src/Stashr.Cli/Stashr.Cli.csproj \
  src/Stashr.Ui/Stashr.Ui.csproj \
  src/Stashr.Configuration.Legacy/Stashr.Configuration.Legacy.csproj \
  samples/Stashr.Sample/Stashr.Sample.csproj \
  ; do
  [ -f "$proj" ] && dotnet sln add "$proj"
done

# test projects
for proj in \
  test/Stashr.Crypto.Tests/Stashr.Crypto.Tests.csproj \
  test/Stashr.Storage.Tests/Stashr.Storage.Tests.csproj \
  test/Stashr.Engine.Tests/Stashr.Engine.Tests.csproj \
  test/Stashr.Architecture.Tests/Stashr.Architecture.Tests.csproj \
  test/Stashr.Storage.Postgres.Tests/Stashr.Storage.Postgres.Tests.csproj \
  test/Stashr.Engines.Database.Tests/Stashr.Engines.Database.Tests.csproj \
  ; do
  [ -f "$proj" ] && dotnet sln add "$proj"
done

echo "Solution assembled. Run: dotnet build"
