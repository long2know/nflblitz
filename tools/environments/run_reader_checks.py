#!/usr/bin/env python3
"""Run an explicit asset-free synthetic or strict original-bundle reader profile.

Both compile the actual pure production reader as C# 7.3, without opening Unity.
Synthetic-only results are not original-asset, native, or browser proof.
"""

import argparse
import os
from pathlib import Path
import subprocess
import sys
import uuid

import prepare_environment as env


def parse_arguments(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    profile = parser.add_mutually_exclusive_group(required=True)
    profile.add_argument("--synthetic-only", action="store_true",
                         help="Asset-free CI: execute only the original toy checks against the production reader.")
    profile.add_argument("--bundle-root", type=Path,
                         help="Strict actual-bundle profile; missing/corrupt original data fails, never falls back.")
    parser.add_argument("--reader-source", type=Path, help="Explicit isolated production-code mutation; default is the actual production reader.")
    return parser.parse_args(argv)


def main(argv=None):
    args = parse_arguments(argv)
    if args.synthetic_only:
        checks_arguments = ["--synthetic-only"]
    else:
        root = args.bundle_root.absolute()
        try:
            manifest = env.read_file(root, "environment.json", 1024 * 1024)
        except (env.EnvironmentError, OSError) as error:
            print("Actual source environment reader profile rejected: " + str(error), file=sys.stderr)
            return 1
        checks_arguments = [str(root), env.digest(manifest)]
    project = Path(__file__).absolute().parents[2]
    output = project / ".squad/.scratch" / ("environment-reader-" + uuid.uuid4().hex)
    files = [
        "Assets/Scripts/AssetImport/SourceEnvironmentData.cs",
        "Assets/Scripts/AssetImport/AuthorizedAssetFiles.cs",
        "Assets/Scripts/AssetImport/GeneratedPlayerData.cs",
        "tools/environments/ReaderChecks.cs",
    ]
    includes = "".join('<Compile Include="' + str(args.reader_source.absolute() if i == 0 and args.reader_source else project / file)
                       + '" />' for i, file in enumerate(files))
    env.write_bundle(project, output, {
        "ReaderChecks.csproj": ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                               '<TargetFramework>net10.0</TargetFramework><LangVersion>7.3</LangVersion>'
                               '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
                               '<ImplicitUsings>disable</ImplicitUsings><Nullable>disable</Nullable>'
                               '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>').encode(),
        "NuGet.Config": b'<configuration><packageSources><clear /></packageSources></configuration>',
    })
    environment = dict(os.environ, TMPDIR=str(project / ".squad/.scratch"),
                       MSBUILDTEMPPATH=str(project / ".squad/.scratch"), DOTNET_NOLOGO="1")
    csproj = output / "ReaderChecks.csproj"
    build = subprocess.run(["dotnet", "build", str(csproj), "--no-restore", "-v:q"],
                           capture_output=True, text=True, env=environment)
    if build.returncode and "NETSDK1004" in build.stdout + build.stderr:
        subprocess.run(["dotnet", "restore", str(csproj), "--configfile", str(output / "NuGet.Config"), "-v:q"],
                       check=True, env=environment)
        build = subprocess.run(["dotnet", "build", str(csproj), "--no-restore", "-v:q"],
                               capture_output=True, text=True, env=environment)
    if build.returncode:
        print(build.stdout + build.stderr, file=sys.stderr)
        return build.returncode
    return subprocess.run(["dotnet", str(output / "bin/Debug/net10.0/ReaderChecks.dll"),
                           *checks_arguments], env=environment).returncode


if __name__ == "__main__":
    raise SystemExit(main())
