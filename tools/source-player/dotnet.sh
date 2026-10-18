#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
runtime="$PWD/tools/source-player/.runtime"
mkdir -p "$runtime/tmp" "$runtime/cli"
if [[ -n "${SOURCE_PLAYER_DOTNET:-}" ]]; then
  executable="$SOURCE_PLAYER_DOTNET"
elif [[ -x "$runtime/dotnet/dotnet" ]]; then
  executable="$runtime/dotnet/dotnet"
else
  executable="$(command -v dotnet)"
fi
export TMPDIR="$runtime/tmp" DOTNET_CLI_HOME="$runtime/cli"
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true
if [[ "$executable" == "$runtime/dotnet/dotnet" ]]; then
  export DOTNET_ROOT="$runtime/dotnet" NUGET_PACKAGES="$runtime/nuget"
fi
exec "$executable" "$@"
