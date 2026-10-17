# Contact272 production resource/presentation preflight

This suite checks the actual new production profile, pure factory and pose
consumer, not an inspector milestone. No Unity stubs, decoder replacement,
original source arrays or motion fixtures are committed. Generated artifacts
and all original/derived resources remain ignored and local.

## Asset-free SDK and caller checks

```sh
python3 -B -m unittest discover -s tools/animation/tests -p 'test_prepare_contact_profile.py' -q
python3 -B -m unittest discover -s tests/presentation/contact-runtime -p 'test_callers.py' -q
python3 -B tests/presentation/contact-runtime/run.py
python3 -B tests/presentation/contact-runtime/mutations.py
```

Production pure sources build separately as C#7.3/netstandard2.1; the actual
checks execute on the existing .NET10 SDK. Default tests inspect exact new and
historical keys/pins/counts, wrong-profile gates, mandatory SAFN identities and
current hit/puppet/attack/release interpolation seams. Twelve actual Unity
caller guards and seven in-memory caller mutations prove provider/bootstrap/
services/import wiring. Those source guards are not native execution.

The six default compiled source mutants independently break real profile
keys, manifest identity/count gates and contact interpolation. Each must
compile, then fail a named contract assertion, not an unrelated exception or
compiler error.

## Actual272 positive and compiled causal negatives

First prepare actual production272 and the new C1 impact catalog using the
explicit commands in `tools/animation/README.md`. Then supply every input:

```sh
ARGS=(
  --bundle "$ACTUAL_BUNDLE272"
  --base-bundle "$IMMUTABLE_BUNDLE157"
  --fixture "$ACTUAL_SOUND_SOURCE_KERNEL_FIXTURE"
  --impact-catalog "$NEW_C1_IMPACT_CATALOG"
  --old-impact-catalog "$IMMUTABLE_COMPLETE_IMPACT_CATALOG"
  --hud-root "$AUTHORIZED_COMPLETE_HUD_ROOT"
)
python3 -B tests/presentation/contact-runtime/run.py "${ARGS[@]}" \
  --output tests/presentation/contact-runtime/.artifacts/actual
python3 -B tests/presentation/contact-runtime/mutations.py "${ARGS[@]}"
```

The fixture is the unchanged actual69-original-input/443-prepared-file SoundVM
fixture, including real model/skeleton, immutable157, P1,31x16 rosters,
movement and exact1983-byte SAFN data. No fixture rewrite, guessed frame or
hidden Primary path is used. The published metadata-only registry is the
default `--registry`; an alternative must be supplied explicitly.

Actual checks load all272 clips/24,892 frames using the production reader,
compare every original157 frame and exact supplemental115 maps/hash identities,
retain R_SKIP's explicit converter-state qualification and reject missing/
corrupted new clips, actual old157 and a well-formed repinned wrong manifest.
They compare all original camera tables/shaker values and old416 source sites,
verify every new camera opcode/operand pair and all six synchronous sound
tuples, and reject the old impact generation.

The actual production factory is executed, verifying object identity for
Core/geometry and mandatory SAFN binding before the first original source
wake. A bounded real `tie1c_a_anim`/`tie1c_v_anim` interpreter fixture publishes
actual C1 puppet snapshots; current pose/root/facing and original reference
pivots are consumed by both render sampling and actual collision geometry.
Real unimplemented callback output remains `Unsupported` and is visibly
rejected, never called resolved silence. Complete P1/HUD/movement/bootstrap/
configuration preservation is checked with real prepared inputs.

Six additional compiled mutants remove SAFN binding, substitute zero geometry
pivots or a detached same-content library object, remove C1 impact qualification,
use the previous C1 root and revert the actual package to157. All twelve actual-mode mutants must fail named causal
assertions. These are narrow resource/presentation fixtures, not original
random/scheduler or full B1/M1/C1 caller qualification.

## Actual Unity references, no Editor process

Only after the accountable Core owner freezes source/API generation:

```sh
python3 -B tests/presentation/contact-runtime/api_compile.py \
  --unity-editor-data "$AUTHORIZED_UNITY_6000_3_24F1_EDITOR_DATA"
```

The existing compiler inventories **all** current runtime and **all separate**
Editor C# sources, compiles C#7.3 against actual Unity netstandard2.1/engine/
Editor/WebGL references with warnings-as-errors in five profiles, and verifies
source inventory/hash stability before and after. It does not launch an
Editor, import assets, access activation data, build a player or claim pixels.
API receipts and per-profile logs remain under this suite's `.artifacts/api`.

Old complete-match campaigns, parity16, native/browser/device acceptance,
helmet physics, speech/SPFX and DSP output remain outside this bounded
preflight. The existing live8080 build is not touched.
