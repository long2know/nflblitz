#!/usr/bin/env python3
"""After NETSDK1147 only: install the supported pinned SDK/workload into this preview, never globally."""

import hashlib
import json
import os
from pathlib import Path
import subprocess
import tarfile
from urllib.request import urlopen


VERSION = "10.0.100"
HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[1]
RUNTIME = HERE / ".runtime"
METADATA = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json"


def main():
    for name in ("downloads", "tmp", "dotnet", "cli", "nuget"):
        (RUNTIME / name).mkdir(parents=True, exist_ok=True)
    sdk = RUNTIME / "dotnet"
    if not (sdk / "dotnet").exists():
        with urlopen(METADATA, timeout=60) as response:
            document = response.read(8 * 1024 * 1024)
        (RUNTIME / "downloads/releases.json").write_bytes(document)
        releases = json.loads(document)
        sdks = [item for release in releases["releases"] for item in release.get("sdks", [release.get("sdk", {})])
                if item.get("version") == VERSION]
        record = next(file for item in sdks for file in item["files"]
                      if file["rid"] == "linux-x64" and file["name"].endswith(".tar.gz"))
        if not record["url"].startswith(f"https://builds.dotnet.microsoft.com/dotnet/Sdk/{VERSION}/"):
            raise ValueError("The official pinned SDK download identity changed.")
        archive = RUNTIME / f"downloads/dotnet-sdk-{VERSION}-linux-x64.tar.gz"
        if not archive.exists() or hashlib.sha512(archive.read_bytes()).hexdigest().lower() != record["hash"].lower():
            with urlopen(record["url"], timeout=120) as response, archive.open("wb") as output:
                while chunk := response.read(1024 * 1024):
                    output.write(chunk)
        if hashlib.sha512(archive.read_bytes()).hexdigest().lower() != record["hash"].lower():
            raise ValueError("The official SDK SHA512 does not match.")
        with tarfile.open(archive, "r:gz") as package:
            package.extractall(sdk, filter="data")
    environment = dict(os.environ)
    environment.update(DOTNET_ROOT=str(sdk), DOTNET_CLI_HOME=str(RUNTIME / "cli"),
                       TMPDIR=str(RUNTIME / "tmp"), NUGET_PACKAGES=str(RUNTIME / "nuget"),
                       DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    subprocess.run([str(sdk / "dotnet"), "workload", "install", "wasm-tools",
                    "--skip-manifest-update", "--disable-parallel", "--verbosity", "minimal"],
                   cwd=PROJECT, env=environment, check=True)
    print(f"Supported SDK {VERSION} / wasm-tools installed only at tools/source-player/.runtime/dotnet.")


if __name__ == "__main__":
    main()
