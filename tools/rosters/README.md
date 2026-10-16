# Original roster and selector adapter, offline

This original Python-standard-library exporter reads **explicit local source**
and writes ignored local JSON. It never downloads data, executes C/historical
tools, imports artwork, or installs a server/database. It targets the original
archive, not today's NFL.

## Inputs and reproducibility

`source_inputs.json` contains metadata only. Its exact bytes are pinned by the
exporter and C# reader. The selected archive identity is
`historicalsource/nflblitz2000` at
`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`, root tree
`a7e0352125d46075b7d98e87253ab89b9160a4c7`.

Provide the twenty-two listed files under `--source-root`, preserving their
relative paths, plus complete recursive `tree.json` metadata for that root tree.
Acquire authorized originals separately; the exporter has **no network mode**.
The local integration input is `.squad/.scratch/SourceData/source/`. Inherited
raw-cache files need not be byte-identical: the exporter checks exact lengths,
Git **blob** SHA-1 (`blob <length>\0<bytes>`), SHA-256, and tree membership before
parsing anything. It independently reconstructs every Git tree object's hash
from entries; a JSON `sha` label is insufficient.

Python 3.9+:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 tools/rosters/export_rosters.py \
  --source-root .squad/.scratch/SourceData/source \
  --output .squad/.scratch/SourceData/rosters.json
```

Output must be explicitly below ignored `.squad/.scratch/SourceData/` or
`tools/rosters/SourceData/`, cannot traverse symlinks, and cannot modify
`source-root`. JSON is deterministic, compact, ASCII UTF-8 with terminal LF.
`rosters.manifest.json` records its exact bytes/SHA-256 and catalog counts.
Original data and all model/texture source arrays stay ignored; no art arrays
are embedded in exporter code or runtime C#.

## Dataset contract: `original-team-rosters/v1`

- **31 teams × 16 original static records = 496**. Original header IDs,
  aliases, city/name/abbreviation/prefix, all five team attributes,
  `drone_base`, image symbols and every original static-player field are retained.
  Empty anonymous line-player names and the six literal zero player ratings
  remain as authored. They are not invented names or movement speeds.
- A stable player key is `spd_<source-prefix>:<two-digit-static-index>`.
  `source_static_id = team_id * 16 + index`; neither key is a shuffled actor ID.
  Seven active actor slots are selected separately; maximum human stations is four.
- Raw field expressions, enum symbols/values, announcer/name-image references
  and original binary32 scale are retained. Active `SC(1.0f)` is
  `1.0499999523162842` as a JSON double representing `1.05f`, **not 1.0**.
  Jersey fields are packed BCD: `0x54` displays **54**, not 84. Zero nibbles use
  number-atlas cell 9; nonzero nibbles use digit minus one.
- These structs contain **no** height, weight, static player flags,
  player-specific head selector, team RGB palette, or named roster captain.
  Explicit nulls mean absent original fields, not fabricated zeros. Team colors
  are authored H/A uniform atlases, not an invented recolor palette.
  `game_info.captain` is a **0/1 human teammate/station offset**, not a named NFL
  captain or a source-team ID; `pmisc.c:off_captain/update_stations` owns its
  runtime adjustments.
- `lineups` retains actual standard, kickoff, punt, field-goal and three return
  arrays plus play-symbol selectors. Source `returners[30]` has **no entry for
  team 30** despite 31 teams: selecting that return lineup fails explicitly.
  The source repeats `play_punt` in its test; it does not select a fake-punt
  roster. No silently corrected source branch is introduced.
- `selection.away_jersey` is the active `#if 1` Blitz 2000 matrix.
  The inactive Blitz 99 matrix is separately qualified and has different team
  ordering; do not apply it to active IDs. Collision lookup uses min/max team
  IDs. Without a collision **both sides use HA_HOME**; physical away side does
  not universally mean HA_AWAY.
- `tuning` retains original model/actor-role flags, speed constants,
  mode-indexed base speeds, 49-way direction table, surface-friction factors,
  difficulty triples, ladder and beat-all-team inputs. Speeds are original
  source units/tick, **not yards/second**. The library does not port physics,
  RNG, autoselect scheduling or player control.
- `actor_model_schema` reads all **32** actual body/count/held-ball/jumbo
  selectors from `player.c:models`, their fifteen source-joint-ordered limb
  references from `models/skeleton.h`, actual five material slots and **eight**
  jersey-overlay selectors. Normal `model266_limbs` is one choice, not a
  universal player. Jersey geometry uses matrix 1 and texture slot 5.
- The separate secret catalog has **20** head bindings and **21** original
  name/game-code selectors, preserving aliases and padded names. These are
  station-selected head replacements, not extra teams/stat rosters. The
  selected static roster declarations contain no additional secret stat roster.
  `cap.c` is custom-play creation, not a created-player stat database.
- Every asset has an explicit source path/Git blob or an explicit missing
  status. `missing_asset_ids` must exactly match those unresolved bindings.
  Uniform slots 2–5 all bind the **same** team H/A LOD atlas; skin/race selects
  skin files rather than an RGB tint. Raw custom-head runtime filename case is
  preserved; stable asset IDs are case-normalized. This exports references,
  **not** reconstructed art or a substitute texture fallback.

The reader supports only the bounded C subset needed for these pinned arrays:
comments, active conditionals, object/function macros, enum constants,
positional initializers, integer/float arithmetic and symbol/string references.
It is not a general C compiler. Unsupported expressions/designators, conflicting
or missing references, changed field layouts, invalid counts and content-pin
failures are clear errors. No Python `eval` or C execution is used.

## Pure C# API

Production file:
`Assets/Scripts/AssetImport/SourceTeamRosterLibrary.cs`.
It is warning-free C# 7.3 / .NET Standard 2.1, has no Unity, filesystem, network,
JSON package, service, or DI-framework dependency, and implements Lead's
`Blitz.Contracts.ISourceTeamRoster`.

```csharp
SourceTeamRosterLibrary library =
    SourceTeamRosterLibrary.Read(bundledJsonBytes, expectedBundledSha256);
ISourceTeamRoster shared = library;
SourceTeamRoster team = shared.GetTeam(5);
SourceRosterProfile profile = shared.GetPlayer("spd_bea:03");
SourceRosterPlayer full = library.GetPlayerData("spd_bea:03");
SourceTeamSelection active =
    library.Lineups.Select(5, "play_field_goal", true);
SourceRosterUniform uniform =
    library.GetUniform(5, SourceUniformVariant.Home, full.Skin);
SourceRosterModel model = library.ActorModels.SelectModel(full.BodySize);
SourceRosterJersey number = library.ActorModels.SelectJersey(full);
```

Supply `expectedBundledSha256` from a **trusted pinned build manifest**, not by
hashing untrusted input immediately before calling `Read`. Known source
revision/tree/input-manifest/catalog pins and an explicit dataset content pin
are mandatory. The reader rejects unknown/duplicate/missing JSON members,
invalid enum mappings, counts, BCD/scale mismatches, missing references,
nonfinite values, invalid UTF-8, excessive nesting and unpaired surrogates.

`Teams/GetTeam/GetPlayer` expose the minimal shared immutable contract.
`TeamData/GetTeamData/GetPlayerData` expose full typed source fields;
`Assets/MissingAssets/Uniforms/ActorModels/Secrets/Tuning` expose renderer/core
inputs. All arrays, nested tables and dictionaries are detached and read-only.
The caller byte array may be changed after `Read` without changing the catalog.
Systems must bundle selected game data into Resources/compiled TextAssets;
WebGL must never fetch the archive at runtime.

## Original tests

No archive data/art is committed as fixtures. Synthetic rosters, models and
textures are generated from loops and fictional identifiers. Public schema
symbols and provenance hashes are metadata, not copied art arrays.

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tools/rosters/tests -q
PYTHONDONTWRITEBYTECODE=1 python3 tools/rosters/tests/fixtures.py
TMPDIR=$PWD/.squad/.scratch/SourceData dotnet run \
  --project tools/rosters/checks/SourceRoster.Checks.csproj \
  --configuration Release -p:NuGetAudit=false -- \
  tools/rosters/SourceData/synthetic-rosters.json \
  .squad/.scratch/SourceData/rosters.json
```

Omit the final actual-data argument for source-free synthetic checks.
The runner targets .NET 10 only as a test host. Its library project compiles
the **actual production C# plus actual Contracts files** with C# 7.3,
`netstandard2.1` and warnings as errors. No testing package is required.
Assertions inspect real output fields, immutable collections and re-pinned
malformed dataset mutations, not just command status.

## Integration boundary

This delivers a local data adapter, **not** full D1 browser/game parity.
Core still must inject source rosters/stats/tuning into actor/physics/P1/HUD
selection. Systems owns metadata, Resources bundling, all body/number/head
conversion and renderer selection. The actual catalog explicitly reports
missing skin/head WMS files, Browns/Titans H/A LOD atlases, generated
`models/head.h` / `models/helm.h`, and the popped-head limb; authored TGAs do
not silently satisfy missing runtime-WMS identities. Native/browser verification
and complete secret/custom gameflow behavior remain separate owner work.
