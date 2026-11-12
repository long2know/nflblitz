# Explicit source instruction presentation — implementation, execution held

**New draft, not compiled or executed. Whole100 remains OPEN.** The parent's
earlier six-source program validation and 86-source Core compile do not cover
this implementation. No Game/Unity/browser/pixel/native-resource qualification
is asserted here. Quality owns independent final material review.

## Actual production path

`BlitzRuntime.BindSourceInstructionPresentation` registers one concrete
`SourceInstructionRuntime`, using the runtime's actual `GameSimulation`,
`SourceHudAsset`, and `SourceSoundVmTransport`. Its `SourceInstructionGameBinding`
calls the existing `GameSimulation.InvokeSourceInstructions` directly. A caller
must then explicitly call:

```csharp
runtime.InvokeSourceInstructions(actualAllAbcdMask, actualProcessIds, actualLiveInputs);
```

Binding does **not** invoke a program. No Awake, Update, restart, phase, flag,
configured-button schema, or initial-QB annotation admits one. Unbound and
non-invoked profiles create no instruction presentation or sounds. Existing
services/profile selectors, constructors, input schemas, scenes and prefabs were
not changed.

`SourceInstructionConsumer` gives the actual gameplay invocation its retained
`SourceInstructionPresentation` as `ISourceInstructionEffects`. Effects create
owned returned plate/string/flasher objects; property writes and vertex
generation remain separate. The four `pmsg` slots hold the actual string
instances, not copies. Rules writes slots 0 and 1; subsequent pages overwrite
only slot 0. Deleted slot-1 identity remains retained until reset/reentry, but is
not passed to later title flashers.

The existing `FixedStepBridge.AfterSimulationStep` subscription calls the
runtime consumer after each simulation step (including failure paths). The
current source step increments its source tick at most once per simulation
step. Duplicate unchanged source ticks do not advance presentation processes.
The consumer also synchronizes inside effects, at the admitted instruction
process's actual `LastWakeTick`. It forwards the real current-tick
`SuspendMask`, `ResumeMask`, and `ResumeAllCall` records, in order, to its owned
portable flasher host. **Core host records clear every tick:** this is not a
poll of an invented cumulative journal. A local host epoch maps birth/wake
observations back to actual supplied source ticks; no Unity deltaTime, tsec
initializer, global frame-rate replacement, or native Goose interleaving is
inferred.

On repaint, the existing `SourceHudRenderer.OnGUI` calls
`BlitzRuntime.DrawSourceInstructions`. `SourceInstructionHudRenderer` consumes
the retained draw frame, creates existing `SourceHudPrimitive` /
`SourceHudVertex` values, and calls the original HUD's factored
`DrawPrimitives` GL/material/texture consumer. Legacy HUD drawing still uses
that same implementation. Plate-first/text-after creation order is retained.
No generic SpriteMessage or GUI-font fallback is used.

The existing simulation restart notification resets both the real gameplay
epoch and the consumer; it does not re-invoke instructions. Runtime destruction
disposes owned objects/processes. Binding or execution failures are explicit
typed errors; failures retire owned resources and never manufacture flag 2.
The global runtime instruction state is exposed by
`runtime.SourceInstructions.IdiotBoxesShown`; it is not used as an operation
counter or automatic admission guard.

## Required external native/resource bindings

`SourceInstructionHudResources` is concrete and uses existing actual HUD data,
images, font glyph metrics, atlas textures, `SourceHudText`, and the original
GL drawing path. Its constructor nevertheless requires **all** of:

- actual raster dimensions **and** evaluated native half-expression values,
  as a live callback; plate conversion truncates only after float half plus
  source offset, while text retains the half-expression;
- all 28 original format slots, including original escape/control strings,
  native `oprintf` line locators, the actual native second-argument integer and
  its symbols; no original copy or numeric SDK enum is invented;
- a native format interpreter. `SourceInstructionTextProgram` passes the
  original format and integer unchanged to it. Its evaluated runs determine
  alignment/font/color/offsets. The original second argument is **not** silently
  treated as an independent renderer alignment. Unevaluated controls fail;
- the actual returned `ostring.state.color`, separately from selected global
  color and per-character run colors. The retained handle and text flasher use
  this actual returned color, including changes made by native format controls;
- all four actual font/atlas bindings and all three actual native colors;
- the actual plate and white/chroma atlas identities and a native sprite-layout
  binding using the resolved image metrics. Missing fonts, image data, texture
  data, rectangle/layout, or glyph geometry fail rather than drawing substitutes.

The actual existing `ISourceInstructionInputs` must still supply live opaque
switch words, selection-box flag, tsec and both low-res reads per page, plus the
explicit nonzero mask and declared PIDs. No absent SDK participant/provider,
inactive-station policy, or source native caller phase has been reconstructed.

`flash_text_proc` was authenticated separately from the pinned native `Game.c`
(see `native-acquisitions.json`). The retained process captures original state
colors, returns sleep(7), writes WHITE to **both string state and character
color**, returns sleep(7), then restores both, repeatedly. Owned shared string
identities are mutated. The portable host supplies suspension/sleep semantics;
native process interleaving and physical sleep units remain unqualified.

`flash_obj_proc` only forwards the actual plate and `(flashes, delay)` to SDK
`flash_obj_white`. That helper body is unavailable here. Consequently a
non-null `ISourceInstructionObjectFlashBinding` is mandatory, receives the exact
owned return and all original arguments, and supplies real continuation
sleep/white/chroma/completion steps. The consumer owns and draws those mutations.
There is **no built-in guessed object-flash cadence**. Missing/unsupported helper
bindings fail explicitly. Plate deletion retires remaining owned flasher state
for consumer safety; it is not evidence of SDK auto-kill behavior.

`ISourceInstructionSoundBatchBinding` must return one actually resolved native
process sound output per original call, with its real sound-VM context.
`SourceInstructionSoundVmOutput` submits it through the existing transport and
native prefix delivery. Direct tick versus page bank/sound/volume/pan/priority
and acceptance-time gate remain distinct. An unsupported native invocation
context is an error, not a generic message sound. Neither audio availability,
DSP behavior, rights, nor global native sound ordering is qualified by this
source-only adapter.

No external native format/layout/SDK/audio binding or new resources were
invented or installed in this task. With admitted valid bindings, the explicit
invocation reaches real retained state and the existing renderer/output path.
This does not assert that the currently available asset bundle contains those
bindings or all required native resources.

## Prepared tests and new execution proposal

`Program.cs` compiles ten actual source/test files: five previously implemented
program/host/contracts files, the new presentation contract, real text program,
retained presentation model, consumer, and this test program (the exact roster
is in the proposal). It constructs **no Game**, uninitialized object, reflection
substitute, Unity instance, browser, or fake bootstrap. A test port uses the
real portable host and real instruction dispatcher. External native/resource
bindings are explicitly synthetic and are not native/pixel qualifications.

Sixteen named behavioral cases inspect retained object identities, all 28
style/format slots, live reads, native sample results, plate conversion,
state/character recoloring and restoration, mask predicates, object arguments,
cleanup/page boundaries, final returned sleep, reentry/reset/disposal, absent
resources/native inputs, legacy no-invocation behavior and ordered sounds.

The proposed runner separately checks **eight STATIC wiring guards** and eight
static production-call-site mutations. It also independently compiles ten
actual production-source mutants and requires the expected named behavioral
assertion to fail. Compiler failure, timeout, missing case, arbitrary nonzero
status, fixture mutation, or truncated logs cannot count as a killed mutant.

Execution remains held. Only a **fresh parent admission** of the final manifest,
proposal, exact compiler/host/reference pins, runtime 10.0.0, and unused new
output footprint may run:

```text
/usr/bin/python3.12 -I -S -B tests/source-instructional-presentation/validate.py --parent-release .squad/.scratch/source-following-active-p1/metadata/source-instruction-runtime-integration-01/parent-validation-release.json
```

The working directory is the shared feature WT, **not** the primary repository.
The named `validation-output` must not exist before the admitted run and cannot
be reused. The previous instructional-box unit output and old parent releases
remain immutable. No test, compiler, Unity or browser execution occurred during
authoring.

The parent's pre-consumer 86-source Core closure must be refreshed for the new
`Contracts/SourceInstructionPresentationContracts.cs` (13 Contracts, ordinarily
87 Core sources after the owner's explicit inventory update). Frontend owns
source/test inventories. Full Runtime/Presentation API compilation additionally
needs a genuine admitted Unity reference roster and the owner's full current
source closure; neither Editor probing nor test stubs qualify it.
