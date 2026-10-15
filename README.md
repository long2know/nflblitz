# nflblitz

A Unity-oriented C# reimplementation of a bounded NFL Blitz rules slice, targeting
browser play and container hosting. The engine-independent kernel, contracts and
headless tests are implemented; a real Unity/WebGL/browser result is still pending.
This is **not yet a verified browser-playable game or a full-fidelity recreation**.

## Run the headless tests

Prerequisites: Python 3 and .NET SDK 10.0.100. From the repository root:

```sh
python3 tests/run-core.py
# Conventional runner, without the extra inventory/source-hash guard:
dotnet test tests/Blitz.Core.Tests/Blitz.Core.Tests.csproj --configuration Release
```

All **156 core cases passed, zero skipped**, on October 15, 2026. The test project
links the actual production sources and compiles them as C# 7.3; this does not
certify Unity's scripting profile. Results/caches stay ignored. Browser harness
files are preparatory, require the later build/hosting increment and a real Unity
artifact, and are not evidence of a running game.

The library entry point is `Blitz.Gameplay.GameSimulation(int seed = 1)`, exposing
read-only state/events, per-tick commands and restart. There is no player executable
in the core increment. See [the API/integration contract](docs/architecture.md),
[source evidence and explicit nonparity](docs/source-parity.md),
[test commands/coverage](tests/README.md) and [pending browser smoke](tests/browser/README.md).

## Goals and constraints

- Full behavioral fidelity is the goal, not a guarantee. Progress must be supported
  by source-backed scenarios, reproducible regression tests and observed results.
- User-provided source reference: https://github.com/historicalsource/nflblitz2000.
  Archive completeness, dependencies, source/asset licensing and redistribution
  permissions are **unverified**. Public availability does not establish rights.
- Build/hosting reference: https://github.com/long2know/mahjong-unity.
  Studying its toolchain does not establish a successful build of this project.
- Unity profile/build validation is separate from the C# 7.3 headless suite.
  Modern standalone .NET services and persistence are conditional on actual
  requirements; no React, backend API or database is assumed.
- Unity browser builds and container gameplay remain unverified.
  Any build-license reuse requires supported entitlement/activation; inject secrets
  outside Git and never copy actual license/key files.

## Team

See [the roster](.squad/team.md), [ownership and handoffs](.squad/routing.md),
[governance](.squad/governance.md) and [accepted decisions](.squad/decisions.md).
Unity owns gameplay; Unity Systems owns presentation/runtime and shared Unity
assets/settings; Frontend owns browser/CI/container integration; Quality owns
parity evidence and browser performance. Backend work starts only when justified.

The current authorization permits focused completed-increment PRs, not one giant
game release. Unity presentation/project changes remain a separate increment
gated on actual build and browser proof. No original assets or services are added.
Ralph is inactive; heartbeat is manual-only.
The installed label workflows may update labels/comments after publication when
their documented events occur; they are not proof of game/build CI.

Runtime Squad sessions, mutable memory, local identity state and editor/session
dumps are ignored. Installer templates, skills, nonsecret configuration and
transaction-backed casting identity are intended source files. Review the exact
publish set before staging; future branches must start with `stlong/`.

## Configuration checks

With the installed Squad CLI available, run:

```sh
node .squad/scripts/validate-team.mjs
```

After an authorized roster/charter/routing change, regenerate only the capability
block with `node .squad/scripts/validate-team.mjs --write-capabilities`. This uses
the installed SDK renderer with a registry-backed specialist view: CLI 1.0.0's
default roster parser otherwise advertises Rai and Fact Checker as specialists.
It retains all ten roster entries and both coordinator canaries. Restart the
Copilot session after generated instructions change. Do not run broad upgrades
to regenerate this block without reviewing their effects.
