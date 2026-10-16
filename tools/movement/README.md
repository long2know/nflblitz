# Runtime movement data preparation

This **offline** producer reads three hash-pinned public original source blobs
and exports only the tables consumed by the frozen pure M1 helpers. It does not
import assets, package Resources, load runtime data, execute original makefiles,
edit production helpers, or implement animation/Core/native/browser parity.

## Reproduce

From the repository root, using Python 3.10+, `gcc` or `clang`, and `gh` if a
bounded public input needs fetching:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 tools/movement/export.py --fetch-inputs
```

With an existing pinned M1 source cache, no network or copying is needed:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 tools/movement/export.py \
  --source-root tests/movement/.artifacts/original \
  --output tools/movement/.artifacts/source-movement-runtime.json
```

The first form reuses matching project-local caches before fetching missing
public blobs by exact Git SHA. A mismatched explicit `--source-root` is rejected,
not silently replaced. Output must remain in the owned ignored
`tools/movement/.artifacts/**` or `tests/movement/.artifacts/**` directories.

Outputs:

* `source-movement-runtime.json`: deterministic runtime payload.
* `source-movement-runtime.manifest.json`: byte/content hashes, all three source
  pins, recipe/producer hashes and exact original numeric-array selections.
* `source-movement-runtime-export/**`: ignored minimal C numeric initializer,
  compiled exporter and process scratch. No game functions or animation/header
  responses are compiled here.

The exporter compiles original declarations to obtain their actual binary32
initialization bits. Selector symbols are read in original array order,
including duplicates. No source numeric arrays or generated data are committed.
`recipe.json` pins the original revision/tree/blobs and expected dimensions;
`runtime.schema.json` describes the strict output.

## Typed consumption/resource binding

Schema: **`original-source-movement-runtime/v1`**.
The only `data` fields are:

| Field | Type and consumer |
| --- | --- |
| `Constants` | 23 source-name → `uint32` **IEEE754 binary32 bit patterns**. Reinterpret each bit pattern as `float`; construct `SourceMovementData`. |
| `Tables` | Thirteen name → ordered string arrays. Preserve every index and duplicate; supply as `IDictionary<string, IReadOnlyList<string>>` to `SourceMovementData`. `run_seqs`/`qb_seqs` contain sequence identities without `SEQ_`; other values are script symbols. |
| `Friction` | Seven binary32 bit patterns in original `pup_field` order; decode the selected factor for `SourceMovement.Integrate`. |
| `Defense` | Twelve handicap-indexed rows, **both** `Line[6]` and `Back[6]` as binary32 bits, plus integer `Base`, `Range`, `Plow`. Supply decoded rows and delays to `AssignDefenseSpeeds`, and actual selected `Plow` to the movement context. Do not transpose, collapse tables, deduplicate, average, or tune them. |

Bit decoding uses supported C# 7.3/.NET Standard 2.1 APIs:

```csharp
float DecodeSourceFloat(uint bits)
{
    return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
}
```

This is bit reinterpretation, **not** `(float)bits`. The future Runtime/UI owner
must validate schema, source revision/profile, blob identities and the trusted
manifest/payload SHA256 before constructing immutable helper input. Use P1's
actual `SourceDirectionMath`; directions/trig and RNG implementations are not
duplicated in this payload.

Suggested binding for the separately owned packager:

* Generated input: `tools/movement/.artifacts/source-movement-runtime.json`.
* Compiled logical resource: `SourceData/movement` with the exact payload bytes
  and an explicit trusted SHA256 from the generated manifest.
* Adapter output: `SourceMovementData`, decoded friction factors and the twelve
  typed defense rows. No network/source parsing is required at runtime.

This is a **binding handoff**, not a created Resources asset, loader, `.meta`,
Editor import or publication permission. Source/derived-data rights remain the
same explicit project constraint.

`Responses`, first-script-header state, fake `_animate_block`, pose/velocity
header responses and every animation fixture are absent and forbidden. The
test-only `tests/movement/.artifacts/source-movement-data.json` is **not** the
runtime resource. Actual scripts/clips/callbacks remain mandatory; packaging
this data does not turn the first-header oracle into a runtime animation VM.

## Coupled validation

The M1 SDK-only CI gate also runs this independent producer and requires exact
equality of all four fields against the actual-C oracle, including binary32
bits and selector order:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 tests/movement/run.py --fetch-inputs --sdk-netstandard21
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover \
  -s tests/movement -p 'test_publication.py' -q
```

SDK mode targets actual SDK `.NET Standard 2.1` references, has no Editor
requirement, and is explicitly **NOT Unity validated**. The full original-C
functional corpus and sixteen real-source implementation mutants still run.
The focused publication tests reject runtime header fixtures, bit/order/row
mutations, source hash mismatches, nonignored outputs and wrong profile labels.
They do not run the legacy/Core, native or browser suites.
