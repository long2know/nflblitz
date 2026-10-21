"""Ten causal, compilable changes to real isolated production source, never live source/data."""
MUTATIONS = (
    ("strict-counter-boundary", "Assets/Scripts/Gameplay/SourceMovementIntegration.cs",
     "Model.PreSnapTicksRemaining >= SourceClock.DefaultAutoHikeTicks - 15",
     "Model.PreSnapTicksRemaining > SourceClock.DefaultAutoHikeTicks - 15",
     "strict-counter-boundary"),
    ("cyclic-not-nearest", "Assets/Scripts/Gameplay/SourceMovementIntegration.cs",
     "                selected++;\n                count--;",
     "                selected += 2;\n                count--;",
     "cyclic-not-nearest"),
    ("occupied-skip", "Assets/Scripts/Gameplay/SourceMovementIntegration.cs",
     "count > 0 && (Model.Players[selected].SourceControllerStation != -1",
     "count > 0 && (false",
     "occupied-skip"),
    ("ten-loop-unconditional-assignment", "Assets/Scripts/Gameplay/SourceMovementIntegration.cs",
     "            ActorState next = Model.Players[selected];",
     "            if (count == 0) return;\n            ActorState next = Model.Players[selected];",
     "ten-loop-unconditional-assignment"),
    ("fake-B-external-edge-reinjection", "Assets/Scripts/Gameplay/SourceMovementIntegration.cs",
     "? buttons & ~SourceDroneButtons.B : buttons;", "? buttons : buttons;",
     "fake-B-external-edge-reinjection"),
    ("immediate-entry-double-advance", "Assets/Scripts/Gameplay/SourceAnimationState.cs",
     "if (program == null || tick == lastAdvanceTick)",
     "if (program == null || tick == lastAdvanceTick && actor.Id == -1)",
     "immediate-entry-double-advance"),
    ("missing-ordered-randrng10", "Assets/Scripts/Gameplay/SourceMovementIntegration.cs",
     'if (SourceDraw(10, "player.c:_mode_dbps:2785", SourceRandomKind.RandRange) < 35)',
     "if (actor.Id >= 0)",
     "missing-ordered-randrng10"),
    ("wrong-binary32-stream-timing", "Assets/Scripts/Gameplay/SourceAnimationPrograms.Movement.cs",
     'new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 19, b: 13)',
     'new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 19, b: 14)',
     "wrong-binary32-stream-timing"),
    ("wrong-authentic-clip", "Assets/Scripts/Gameplay/SourceAnimationPrograms.Movement.cs",
     'new SourceAnimationInstruction(SourceAnimationOp.NewSequence, text: "M_HOPCLP")',
     'new SourceAnimationInstruction(SourceAnimationOp.NewSequence, text: "RUSH_C")',
     "wrong-authentic-clip"),
    ("missing-synchronous-resolved-callback", "Assets/Scripts/Presentation/SourceCinematicPlayback.cs",
     "            actualRawOutputConsumer(dispatch);",
     "            if (dispatch.C2Context == null || dispatch.C2Context.Kind != SourceC2SoundFunctionKind.AttackSoundFixed)\n"
     "                actualRawOutputConsumer(dispatch);",
     "missing-synchronous-resolved-callback"),
)

ROOT_MUTATIONS = (
    ("stale-root-origin", "Assets/Scripts/Gameplay/SourceAnimationState.cs",
     "            transitionRootFrom = SourceFrameInterpolationRoots && (Mode & SourceAnimationModes.Secondary) == 0\n"
     "                && sourceClip != null && previousFrame >= 0\n"
     "                ? Frame(sourceClip, previousFrame).ScaledRoot.Y * actor.ModelScale : actor.SourceRoot.Y;",
     "            transitionRootFrom = actor.SourceRoot.Y;",
     "original VM root-Y offset24;"),
    ("completion-root-refresh", "Assets/Scripts/Gameplay/SourceAnimationState.cs",
     "                Mode &= ~(SourceAnimationModes.Interpolating | SourceAnimationModes.AnchorY);\n                return;",
     "                Mode &= ~(SourceAnimationModes.Interpolating | SourceAnimationModes.AnchorY);\n"
     "                actor.SourceRoot = new SourceVector3(actor.SourceRoot.X, transitionRootTo, actor.SourceRoot.Z);\n"
     "                return;",
     "original VM root-Y offset39;"),
    ("completion-without-hold", "Assets/Scripts/Gameplay/SourceAnimationState.cs",
     "                Mode &= ~(SourceAnimationModes.Interpolating | SourceAnimationModes.AnchorY);\n                return;",
     "                Mode &= ~(SourceAnimationModes.Interpolating | SourceAnimationModes.AnchorY);\n"
     "                hold = 0;\n                return;",
     "original VM interpolation hold target offset39;"),
    ("completion-loses-anchor", "Assets/Scripts/Gameplay/SourceAnimationState.cs",
     "                Mode &= ~(SourceAnimationModes.Interpolating | SourceAnimationModes.AnchorY);\n                return;",
     "                Mode &= ~(SourceAnimationModes.Interpolating | SourceAnimationModes.AnchorY | SourceAnimationModes.AnchorXZ);\n"
     "                return;",
     "original VM mode/anchor offset39;"),
    ("interrupted-source-header", "Assets/Scripts/Gameplay/SourceAnimationState.cs",
     "            return transitionTicks > 0 && transitionCount < transitionTicks ? transitionSourceClip : clip;",
     "            return clip;",
     "original VM root-Y offset30;"),
)


def apply(raw, spec):
    identity, _, old, new, _ = spec
    if raw.count(old.encode()) != 1:
        raise ValueError("Causal control needs one real production anchor: " + identity)
    changed = raw.replace(old.encode(), new.encode())
    if changed == raw:
        raise ValueError("Causal control did not change real production: " + identity)
    return changed
