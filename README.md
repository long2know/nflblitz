# nflblitz

A planned Unity-based C# reimplementation of NFL Blitz, targeting browser play and
container hosting. This repository currently contains the approved Squad team
configuration and project context, not a playable game or a working build pipeline.

## Goals and constraints

- Full behavioral fidelity is the goal, not a guarantee. Progress must be supported
  by source-backed scenarios, reproducible regression tests and observed results.
- User-provided source reference: https://github.com/historicalsource/nflblitz2000.
  Archive completeness, dependencies, source/asset licensing and redistribution
  permissions are **unverified**. Public availability does not establish rights.
- Build/hosting reference for later study: https://github.com/long2know/mahjong-unity.
  Its configuration has not been validated or imported into this project.
- Select a supported Unity version and its compatible C# scripting profile before
  implementation. Modern standalone .NET services and persistence are conditional
  on actual requirements; no React, backend API or database is assumed.
- Unity browser builds and container serving are targets, not current capabilities.
  Any build-license reuse requires supported entitlement/activation; inject secrets
  outside Git and never copy actual license/key files.

## Team

See [the roster](.squad/team.md), [ownership and handoffs](.squad/routing.md),
[governance](.squad/governance.md) and [accepted decisions](.squad/decisions.md).
Unity owns gameplay; Unity Systems owns presentation/runtime and shared Unity
assets/settings; Frontend owns browser/CI/container integration; Quality owns
parity evidence and browser performance. Backend work starts only when justified.

Setup preparation does not implement gameplay, import/convert assets, scaffold
Docker game builds or create services. Ralph is inactive; heartbeat is manual-only.
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
