# Source team/impact presentation checks

These checks compile the **production** pure helpers and Contracts with C#7.3.
They do not provide Unity stubs, copied gameplay, fake source provenance, or a
replacement animation decoder.

```sh
python3 -B tests/presentation/run.py
```

The default run uses the existing Backend-owned original synthetic roster
generator read-only, plus synthetic number/camera/timing/guard cases.

## Actual authorized originals

Prepare the inputs with `tools/assets/README.md`, then:

```sh
python3 -B tests/presentation/run.py \
  --actual .squad/.scratch/source-presentation/prepared-teams-all31 \
  --cinematics .squad/.scratch/source-presentation/prepared-cinematics-v2/impact-presentation.json \
  --source-root .squad/.scratch/source-presentation/source

python3 -B tests/presentation/mutate.py \
  --actual .squad/.scratch/source-presentation/prepared-teams-all31 \
  --cinematics .squad/.scratch/source-presentation/prepared-cinematics-v2/impact-presentation.json
```

The actual run covers:

- All31x31 pairings, both designated home sides, actual roster skin/scales,
  original home/home versus conflict selection, all32 model bit combinations,
  actual player IDs and BCD digit/number-geometry selection.
- All86 actual emitted texture identities and real byte mutations.
- Every actual selected body/held/jumbo limb and20 custom heads; original
  topology, UV seams and all one-/two-digit source number surfaces.
- Seven actual CSI tables,25 weighted entries, explicit no-selection, source
  opcode identity, ordered callback/camera playback, source shaker carrier
  semantics, independent RNG draws, dust wait/joint/pre-snap behavior and exact
  return-to-normal timing.
- 15,242 executing source camera ticks compared against **unchanged** original
  `cambot.c` zoom/spline/rail/Bezier/acceleration routines and original tables,
  including position, focus, distance, angles and all six velocity components.
  The host functional oracle uses libm rather than MIPS fast-trig; it is not a
  complete scheduler, PRNG, physics or target-bit-parity claim.

Critical mutants change actual isolated production files, not helper fixtures.
They cover matching/physical-away uniforms, skin, flipped held ball, decimal BCD,
duplicated number digits, extra camera ticks, spline stretch, tick0 acceleration,
raw-mode-versus-role, phantom flags, carrier guard, shaker decay, dust wait and
duplicate camera consumption. The live
source tree is hash-protected and isolated mutant files are removed.

## Native Unity proof

After the explicit import, under the exclusive selected-project Editor lease:

```text
-executeMethod Blitz.Editor.SourceTeamChecks.VerifyFromCommandLine
-teamAssetRoot <absolute prepared-teams-all31>
```

The native check reloads the real Resources asset and examines every persisted
mesh/UV/index/material, every original texture RGBA byte, all BCD surfaces and
fourteen actual roster-bound rigs. A real matching-opponent texture mutation must
fail the actual generated-asset validator. The final117 manifest and actual
`fall_04`/`csi_rs_suplex` cue are required.

When an actual graphics device is present, it renders two TGA screenshots:
ordinary opposing teams and the original fall04 rail camera. It checks actual
framebuffer samples where the two authored jersey atlases differ, for **both**
uniform IDs; actor count/HUD labels alone do not pass. With a Null graphics
device, the receipt explicitly says there is no image proof. The fixture is
clearly labeled deterministic presentation inspection, not Core gameplay or an
original animation-VM/contact trace.

Reports/images stay in the selected ignored project's `Build/`. TGA output avoids
requiring an additional image-conversion Unity module in the main project.

## Integrated acceptance still required

The coordinator must wire actual source home/head/draw flags and ordered VM/contact
cues through the gameplay and interface owners, then freeze the complete Core/
movement/UI/rendering candidate. Root Editor/WebGL build, visible browser proof
of both original uniform IDs plus an eligible impact event, exact build/source
identities and actual NVIDIA GL draw cadence are separate required acceptance.
Do not promote a world-only or presentation-only inspection as the game.

All sixteen full-parity rows remain required; these checks alone do not close D1,
V1, F1 or whole-game parity.
