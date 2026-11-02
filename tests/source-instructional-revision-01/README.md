# Independent Q1/Q2 integration revision

Owner: **Lead**, not either rejected artifact's original author or its reviewer.
This directory is new. All earlier component harnesses, fixtures and validation
outputs are read-only. **Nothing here has been compiled or executed.**

## Q1: actual HUD asset and concrete adapter

`SourceHudAsset.TextureAtlas(id)` first resolves the actual runtime texture, then
returns its authored atlas definition. `interface-white` and `interface-chroma`
keep the `interface` dimensions/UV coordinate system without adding manifest
entries. The instruction adapter uses that same API for constructor admission,
plate UVs and glyph UVs. White texture selection is not replaced by a base texture.
Ordinary manifest identities retain their original definitions; unknown runtime
identities still fail closed.

`SourceInstructionHudResourcesRegression.cs` uses the **actual**
`SourceHudAsset` and **actual** `SourceInstructionHudResources`, not an
`ISourceInstructionResources` fixture or Unity API substitute. Its `Run` entry
constructs the real adapter and checks these five named cases:

1. `ActualAssetAliasesRetainBaseAtlasAndManifest`
2. `BasePlateUsesActualGeneratedWhite`
3. `ChromaPlateUsesBaseAtlasUvs`
4. `WhitePlateUsesBaseAtlasUvs`
5. `MissingAtlasBindingsRemainFailClosed`

The assertions inspect real cached texture identities/dimensions, original image
dimensions/anchors, base-atlas UVs, absence of fabricated manifest aliases, and
unknown-texture rejection. Texture names/identity are not pixel-readback evidence.
This does not test native sprite layout, text evaluation, rendering or sound.
No formatter, raster callback or sprite-layout method is invoked by these cases.

### Required genuine inputs — not supplied or queried here

The parent must admit a real compatible Unity execution context and the complete
genuine API/framework reference closure before compiling or invoking this harness.
An ordinary .NET process plus reference DLLs alone cannot execute Unity's native
`ScriptableObject`/`Texture2D`/`Material` internals.

Call the following entry **inside that genuine context**, on its supported thread:

```csharp
Blitz.RevisionTests.SourceInstructionHudResourcesRegression.Run(
    actualHud, actualRaster, originalFormats, nativeFormatter,
    actualFonts, actualColors, nativeSpriteLayout);
```

- `actualHud`: the runtime's genuine loaded original `SourceHudAsset`. Its existing
  `Load` checks remain authoritative: the prepared manifest hash, all exact original
  texture bytes, and shader `Hidden/Blitz/SourceHud`. No synthetic manifest, private
  field injection, uninitialized objects or asset/resource acquisition is allowed.
- `originalFormats`: all 28 admitted original slots; materialized once by the test.
- Other arguments: the explicit genuine bindings already required by the
  production constructor, with every native font/color mapped. These tests only
  validate their constructor presence; they do not establish native semantics.

There is deliberately no fake bootstrap, Editor/version lookup, NUnit dependency,
package restore, reflection, Game construction or guessed executable Unity argv.
The new proposal freezes the current 135 production inputs plus this one test
source (136 total for compile-only qualification); genuine references and an
admitted invocation entry are still missing. Two proposed mutations act on copies
of the real adapter: restore the manifest-only admission gate; restore direct
plate-texture manifest indexing. Neither mutation or baseline has been run.

## Q2: exact ordinary discovery and count gate

`tests/playbooks/run.py` now expects **15** runner tests, not 14. No producer count
or historical pin is changed. The existing pure inventory test is extended rather
than adding another discoverable method:

- Actually collect the same `test_*.py` suite without running collected bodies or
  setup; require the explicit 15 test identities, including the inventory test.
- AST-match the ordinary caller's discovery command and its immediately following
  literal `require_unit_tests(..., 15)` admission; never invoke `main`.
- Exercise the existing output gate against 15-test success, wrong counts, skips,
  nonzero/crash exits, duplicate summaries and status-only output.

The unchanged historical tests remain collected but unexecuted by the proposed
single selector. Their `snapshot()` and immutable source-byte checks are not
patched, mocked or bypassed. Their independent earlier historical mismatches remain
open; this revision does not claim the ordinary historical runner reaches its
test-admission step.

Proposed **only after fresh parent admission**, from `WT/tests/playbooks`:

```text
/usr/bin/python3.12 -E -S -B -m unittest -v test_runner.PlaybookCurrentBuildInventory.test_contract_roster_project_links_and_profile_without_historical_certification
```

It should run exactly **one** test and collect, but not execute, the exact 15-test
ordinary inventory. Imports are the existing `test_runner`, `support`, `oracle`
and dynamic `run.py` module. No custom setup, filesystem writes, subprocesses,
Git calls, compiler or managed runtime calls occur in this selected test.
The parent, not the test, may capture complete stdout/stderr into the fresh
proposal output root. The stale-count negative control must modify only a copied
real `run.py`, not the working tree or historical pins.

## Qualification

Authoring performed source/JSON/AST parsing, exact hashing and inverse-preservation
inspection only. No test/import/discovery, compiler, managed harness, Unity, Game,
browser, native/provider execution or tool/reference probing occurred. The receipts
and fresh, unexecuted validation proposal are in
`B/metadata/source-instruction-runtime-integration-01/revision-01`.
Earlier passes are evidence for their historical snapshots, not this revision.
Whole100, genuine Unity135 integration, native resources/providers and whole-game
fidelity remain **OPEN**.
