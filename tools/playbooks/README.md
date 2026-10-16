# Source playbook conversion

This stdlib-only Python producer makes the pure C# playbook reproducible from a
clean checkout. It reads an **explicit, read-only source root**, checks the
original Git-blob hashes in `recipe.json`, and writes functional data to an
**explicit ignored directory**. It does not fetch an archive, compile/evaluate C,
launch Unity, import artwork, or modify the provided source.

The pinned profile is `historicalsource/nflblitz2000` revision
`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`, normal release at 57 source ticks per
second, with `USE_OLD_QB=FALSE`. This is a selected archive profile, not a claim
about every edition or complete match parity.

## Reproduce

Provide the exact original files listed under `inputs` in `recipe.json`:

```sh
python3 tools/playbooks/convert.py \
  --source-root /path/to/the/pinned/nflblitz2000 \
  --output tools/playbooks/.artifacts/converted
```

The output contains:

- `catalog.tsv`: actual functional play, formation, route, direction and handicap
  fields accepted by `Blitz.Gameplay.SourcePlaybook.LoadConverted(TextReader)`.
- `manifest.json`: source identities, input blob hashes, producer/recipe hashes,
  inventory counts, and the catalog SHA-256. Its public metadata schema is
  `manifest.schema.json`. It deliberately does not embed original tables.

The producer allows output only **below** `tools/playbooks/.artifacts/` or
`.squad/.scratch/`, never inside the input source root. The former is protected
by this directory's `.gitignore`; the latter is already ignored by the project.
No converted catalog or original source array belongs in Git.

A previously cached `playsel.c` can be the wrong blob even when the filename
looks correct. Missing, changed, newline-normalized, or incorrect input bytes
fail **before any output is created**. To use separately obtained verified
files, provide the explicit overrides; these do not bypass hash verification:

```sh
python3 tools/playbooks/convert.py \
  --source-root /path/to/read-only/source-cache \
  --playsel /path/to/verified/playsel.c \
  --trigs /path/to/verified/TRIGS.H \
  --output .squad/.scratch/reproduced-playbook
```

The producer needs no ignored scratch script, recursive GitHub tree download,
compiler, Python package, or Unity installation. Preserve the original blob
bytes rather than asking the producer to repair or normalize inputs.

## Runtime integration

The caller owns loading/storage and integration:

```csharp
SourcePlaybook playbook;
using (var reader = System.IO.File.OpenText(catalogPath))
    playbook = SourcePlaybook.LoadConverted(reader);

// Existing SourceMatchFlow supplies the branch, ordered RNG requests and index.
SourcePlayDefinition selected = playbook.ResolveCpuPlay(resolvedCpuDecision);
```

The actual pinned inventory is 88 plays, 29 active formations, 140 active route
programs, 34 twelve-level handicap tables, 257 quarter-sine values, and 49 stick
directions. Seven weighted CPU pools retain **226 entries including repeats**;
six singleton families complete the thirteen CPU families. `recipe.json`
records their original symbols and counts, not their original arrays.

Do not deduplicate, sort, or select again after `SourceMatchFlow.CpuPickPlay`.
Two pool ordinals can resolve to the same original play symbol and still be
different selection identities. Preserve the original symbol, page/slot when
provided, family and weighted ordinal in `SourcePlaySelection` and match state.
Formation and route identities are not the four legacy `PlayId` values.

Positions and velocities in these pure helpers are centered **source units**
and source units **per source tick**. Formation offsets are not public-contract
yards. The interpreter returns original desired sticks/buttons, script names,
jobs, targets and ordered cross-defender effects; the caller applies movement,
animation, possession and actor/process ordering.

## `source-playbook/v1` records

UTF-8, tab-delimited, invariant numeric fields; semicolon-separated formation
slots and comma-separated numeric/identity lists. Records are emitted in
dependency order:

| Record | Columns after the record tag |
|---|---|
| `source-playbook/v1` | source revision; profile identity |
| `trig` | trig blob; player blob; 257 binary32 values; 49 integer directions |
| `handicap` | table identity; blob; source line; twelve values |
| `formation` | identity; blob; source line; seven `X,Z,script,mode,control` slots |
| `route` | identity; blob; source line; original opcode/parameter words |
| `play` | identity; display name; blob; source line; side; flags; formation; seven route identities |
| `pool` | family; original array/singleton identity; blob; source line; ordered play identities |

Every instruction is parsed with its original arity. Unknown opcodes,
truncated parameters, missing references, wrong inventories or unsupported
functional expressions are errors, not generic straight-line routes.
The constant parser is an arithmetic whitelist, not `eval`; it preserves C
integer division and binary32-versus-double literal operations, including the
one-ULP distinction between `(390.0f/300.0f)*6.0f` and literal `7.8f`.

## Tests and evidence boundary

```sh
python3 -m unittest discover -s tools/playbooks -p 'test_*.py' -v
```

These permanent producer tests use clearly named **synthetic** source snippets,
not original play arrays. They verify ordered duplicates, identity/role/opcode
records, provenance, reproducibility, input/output guards, source hash failures,
missing references and C numeric semantics. They do not claim original gameplay
parity. The bounded native-C comparison receipts are handed to Quality
separately; permanent gameplay/oracle tests remain Quality-owned.

Platform RNG is **not supplied by this converter**. The pinned Goose wrapper
uses `random() % val` (zero bound consumes no platform value), while raw C
`rand()` is a separate kind. The linked target `/video/lib/libc.a` PRNG bodies
are absent from the verified archive trees. Use explicit `ISourceRandom`
provenance and recorded draws or an explicitly unverified injected provider;
never silently substitute .NET, host libc, a guessed BSD algorithm or wall time.
Original scheduler/input/audio RNG ordering and equal-key target `qsort` order
remain independent verification requirements. Caller, Unity and browser proof
are still required for all gameplay parity claims.
