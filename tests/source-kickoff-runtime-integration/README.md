# Explicit owned kickoff admission in the existing runtime

The existing `BlitzRuntime` now accepts the owned child/wait/flash admission after
`Awake` has completed successfully, on the actual source profile, before its
first source tick. Bind its existing presentation first, then explicitly call:

```csharp
runtime.BindSourceInstructionPresentation(resources, sounds, objectFlash);
runtime.BindSourceKickoffInstructionOwnedHelpers(
    allAbcdMask, processIds, instructionInputs, admissionState,
    childProcessId, maximumPlayers, parentButtonAMask, parentButtonBMask,
    childInputs, playerButtons, childOperands, childEffects, ownedHelpers, childReturns);
```

Every argument is caller-supplied. Existing profile selection is unchanged;
there is no URL flag, default activation, inferred native mask, process-return
policy, new tutorial or resource fallback. `childEffects` must be the real child
presentation/effect service; the four-page renderer is not a replacement for it.
The separate `objectFlash` remains the explicit SDK object-plate binding.

`SourceInstructionConsumer` owns preparation, initial binding and pending recovery
of one coherent admission. Its narrow `ISourceInstructionAdmissionProgramBinding`
capability is implemented by `SourceInstructionGameBinding`, which forwards to
**its retained actual GameSimulation's** `BindSourceInstructionAdmission`.
`SourceInstructionRuntime` still enforces binding before the first source tick.
The existing `WithOwnedHelpers` / `WithSourceChild` composition lets the dispatcher
supply its real host and admission state to the owned factories. Existing
simulation catch and play-selection-return producers perform admission; this API
does not manually create or advance a tutorial process.

The consumer's single forwarding owner implements both invocation effects and
admission state. Its reads/writes use the supplied live state. Initialization
0/2 and kickoff entry 1 are not sent to the older page-only presentation;
instruction entry -1 and completion 2 reach both that live owner and the
existing presentation. Automatic serials are adopted at the existing
`AfterSourceStep` notification (or before the first presentation effect), so
resource ownership, source masks, sound contexts and cleanup use the real
admitted process. No replacement host services the owned child/wait/flash.

Binding twice, binding after a manual invocation/first source tick, incomplete
binding, and manual invocation in owned mode are rejected. Actual binding
failure aborts presentation and surfaces through `BlitzRuntime.SourceFailure`;
it cannot silently become manual success. A failed initial bind is pending in the
consumer but is **not retained by Game**. Only the existing actual restart
notification retries that same binding against the fresh actual program; ordinary
steps never retry a poisoned dispatcher or replay initialization effects. Readiness
is set only after successful initialization. Repeated failure aborts presentation,
remains non-ready and blocks dispatch until another actual reset. `RestartMatch`
also surfaces source-profile restart exceptions through `SourceFailure`, including
the R recovery path entered while an earlier failure is displayed.

For an established successful binding, Game already retains and reinitializes it
on restart. The consumer resets only presentation in that case: it does not bind
or initialize it a second time. Default/unbound and explicit manual four-page
behavior remain unchanged, including argument validation before preparation.
Existing external-helper contracts are untouched; switching paths requires a fresh
simulation/runtime, not a hot rebind.

## Focused portable regression and API-only compilation boundary

`Program.cs` exercises the **production consumer lifecycle**, its coherent owner
and existing presentation, backed by the actual portable dispatcher/host:
explicit bind; catch -> deferred source child -> owned wait/flash
-> child cleanup/explicit returns -> coordinator button suffix; automatic
selection-return pages/frame/sound serials; failure/reset and retained binding.
The recovery regression deliberately throws from a synthetic live-state provider
after a real dispatcher initialization write. It checks pending/non-ready state,
poisoned-dispatch rejection, no ordinary-step retry/manual fallback/hot rebind,
repeated failure on a fresh host/dispatcher, then successful fresh-program recovery
using the identical binding. It also checks that established bindings initialize
once per actual reset, default/manual separation and pre-preparation argument errors.
Its primitive, resource and effect providers are explicitly synthetic test
services. The portable adapter implements the program binding and narrow admission
capability, retains admission only after successful real initialization, and
reinitializes retained admission when it creates its fresh dispatcher. Recovery
itself runs in the production consumer, not in a proxy helper. This adapter does not
construct, fake, reflect on or execute Game.

The exact eleven-unit C#7.3/NetStandard2.1 closure, source pins and already approved
pinned compiler route are in
`.squad/.scratch/source-following-active-p1/metadata/source-kickoff-runtime-integration-01/handoff.json`
(worktree-relative). The closure remains eleven units; no complete-file addition
or extra dependency is required for recovery. The parent compiled and executed
the recovery generation: **218 assertions passed in one combined case**, with
clean compiler output and empty managed stderr. Its current source pins, commands
and captured result are in `restart-recovery-validation-01/{admission,result}.json`;
`restart-recovery-quality-review.json` passes the scoped source/evidence review
with no material findings. These are not 218 independent scenarios or genuine
Game/runtime observations. The earlier 143-assertion `parent-validation-01` and
its `quality-review.json` describe the pre-recovery generation, not this fix.
Captured outputs are consumed evidence; another execution requires fresh input
admission and an unused output. Do not rerun historical `validate.py` or reuse
consumed capture outputs.

This portable test does not compile or execute the Unity-dependent
`SourceInstructionRuntime` / `BlitzRuntime`. The same recovery generation also
compiled **all 137 complete non-Editor player units**, including both runtime
files, using C#7.3, `UNITY_WEBGL` and warnings as errors, against 121 NetStandard2.1
references and seven genuine Unity 6000.3.24f1 reference modules. The
`restart-recovery-validation-01/player-api-compile.process.json` capture records
exit 0 with empty streams; the result records stable inputs. Its exact reference
tuple and current source pins are in that generation's `admission.json`.
That is **API-compilation-only evidence**, not a Unity build, Editor, Game,
native/browser execution, scheduling/ABI/process-return, asset-rights or
end-to-end fidelity result.
