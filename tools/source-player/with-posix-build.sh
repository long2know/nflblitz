#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
if (( $# == 0 )) || [[ ! -x "${SOURCE_PLAYER_DOTNET:-}" ]]; then
  printf 'Usage: SOURCE_PLAYER_DOTNET=/existing/sdk/dotnet bash tools/source-player/with-posix-build.sh COMMAND ARGUMENTS...\n' >&2
  exit 2
fi
export SOURCE_PLAYER_PARENT_USER_NAMESPACE="$(readlink /proc/self/ns/user)"
export SOURCE_PLAYER_PARENT_MNT_NAMESPACE="$(readlink /proc/self/ns/mnt)"
unshare --user --map-current-user --keep-caps --mount --propagation private bash -c '
  set -euo pipefail
  project="$PWD"
  python3 -B - <<'"'"'PY'"'"'
import os
from pathlib import Path
import sys

root = Path.cwd()
sys.path.insert(0, str(root / "tools/source-player"))
from namespace_mount import main as mount

obj = root / "tools/source-player/obj"
metadata = {path.relative_to(obj): path.read_bytes() for path in obj.glob("*/*")
            if path.name == "project.assets.json" or
            path.name.endswith((".nuget.g.props", ".nuget.g.targets"))}
mount(["rbind", str(root), str(root)])
mount(["readonly", str(root), str(root)])
for name in (".runtime", "bin", "obj"):
    target = root / "tools/source-player" / name
    if not target.is_dir() or target.is_symlink():
        raise ValueError("An existing physical build output directory is required: " + str(target))
    mount(["private-tmpfs", str(target), "tmpfs", "mode=0700,size=1g"])
scratch = os.environ.get("SOURCE_PLAYER_POSIX_SCRATCH")
if scratch:
    scratch = Path(scratch)
    if (not scratch.is_absolute() or scratch.is_symlink() or not scratch.is_dir() or
            scratch.resolve() != scratch or not scratch.is_relative_to(root / ".squad/.scratch")):
        raise ValueError("POSIX scratch must be an existing physical project scratch directory.")
    mount(["private-tmpfs", str(scratch), "tmpfs", "mode=0700,size=4g"])
artifacts = os.environ.get("SOURCE_PLAYER_POSIX_TEST_ARTIFACTS")
if artifacts:
    artifacts = Path(artifacts)
    if (artifacts != root / "tests/movement-callers/.artifacts" or
            not artifacts.is_dir() or artifacts.is_symlink()):
        raise ValueError("Only the existing movement suite artifact directory may be overlaid.")
    mount(["private-tmpfs", str(artifacts), "tmpfs", "mode=0700,size=64m"])
for relative, raw in metadata.items():
    path = obj / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(raw)
sdk = str(Path(os.environ["SOURCE_PLAYER_DOTNET"]).resolve().parent)
mount(["bind", sdk, sdk])
mount(["readonly", sdk, sdk])
data = os.environ.get("SOURCE_PLAYER_DATA_ROOT")
if data:
    data = str(Path(data).resolve())
    mount(["bind", data, data])
    mount(["readonly", data, data])
PY
  cd /
  cd "$project"
  runtime="$PWD/tools/source-player/.runtime"
  mkdir -p "$runtime"/{tmp,home,cli,nuget}
  export HOME="$runtime/home" TMPDIR="$runtime/tmp" DOTNET_CLI_HOME="$runtime/cli"
  export NUGET_PACKAGES="$runtime/nuget" DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
  export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true DOTNET_CLI_USE_MSBUILD_SERVER=0
  export MSBUILDDISABLENODEREUSE=1 DOTNET_EnableDiagnostics=0
  exec setpriv --inh-caps=-all --ambient-caps=-all "$@"
' bash "$@"
