# HUD/cabinet regression checks

The model compiles **actual production C# 7.3 / .NET Standard 2.1**, warnings as
errors. It uses all current Contracts, the existing immutable roster JSON reader,
production input/menu/bridge code and the actual HUD composer, not fake Unity
APIs or substitute DTOs.

Synthetic-only checks require no original assets:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 tests/interface/run.py
```

The complete local original-data gate is explicit:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 tests/interface/run.py \
  --bundle PATH_TO_COMPLETE_INTERFACE_OUTPUT \
  --source-root PATH_TO_PINNED_NFL_ARCHIVE \
  --tools-root PATH_TO_PINNED_MIDWAYVIDEO_ARCHIVE \
  --movement tools/movement/.artifacts/source-movement-runtime.json \
  --mutations \
  --unity-managed "$UNITY_EDITOR_DATA/Managed/UnityEngine"
```

Prepare inputs with the permanent `tools/interface/prepare.py` and
`tools/movement/export.py` producers. Raw originals, converted data, receipts and
isolated build copies remain ignored. No `/tmp`, Editor, browser, service or
hosting operation is performed.

## What is asserted

- Compiler-evaluated unchanged original Goose raw49/digital tables, original
  player headings and two-history switch scan expressions, with exact Git pins.
  This is a functional C seam with a synthetic register tape, not GPIO/MIPS
  execution.
- Simultaneous ABC, all ordered press/release samples, station IDs versus
  configuration/controller/captain identity, held-edge draining and focus
  rearm without advancing/resetting the match.
- Every catch-up host step forwards both source events and presentation cues;
  existing legacy command constructor/control behavior remains intact.
- Actual packed clock/PAT/kick/period/result DTOs, including OT3 and intentional
  classifier no-ops; no legacy-text/rules inference.
- Original variable-width glyph metrics, signed anchors, missing-glyph/texture
  errors, original play symbols/page/slot/flip and the shipping exact menu switch.
- Actual original font/sprite/plane geometry at desktop/widescreen/portrait and
  DPR 1/2/3, including the old 360×740 DPR2 clock/team overlap condition.
- Runtime-safe M1 payload provenance and exact binary32 reinterpretation,
  selector order/duplicates and both immutable 12×6 defense matrices.

Thirteen isolated mutants change the actual production source copies: Hall
decode, press/release loss, captain shortcut, buffer-as-CSS layout, font
centering, legacy clock substitution, AB priority, visual-only flip, last-event
only transport, phantom inference, actual score glyph overlap and numeric
movement-bit casting. Each must compile and fail its named offending-field
assertion; compile failures are **not** accepted mutation evidence.

The baseline must equal a final baseline, and all live production hashes must
remain unchanged. Logs and `result.json` are retained in `.artifacts`; isolated
source/build trees are removed. The optional engine adapter check references the
**actual installed Unity assemblies**, with separate `UNITY_EDITOR` and
`UNITY_WEBGL` defines; it does not start Unity or certify the full native runtime.

The complete October 16 checkpoint passed **16,215 assertions**, including
**14,618 actual-data assertions**, **13 rejected production mutants**, nine
converter tests and four JavaScript interop tests. A fresh preparation reproduced
all four bundle files byte-for-byte. Receipts, not these counts alone, identify
the tested production inputs.

## Still separate

The original authored atlas is not a claim of missing Composit/Texus-generated
pixel equivalence. Complete source process/libc RNG, random-select/custom/powerup
flow, manual kick/punt meter state and decoded audio are not closed by these
checks. Neither are Core/M1 caller behavior, source-camera raw-mode integration,
native resource import, a fresh WebGL candidate, complete-match browser evidence,
or the existing RTX3070 ~60 drawn-FPS qualification. Those require the owner-led
integration/native/browser handoff; no all-sixteen/full-game parity is claimed.
