#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;
using UnityEngine;

namespace Blitz.Editor
{
    public static class SourceRuntimeChecks
    {
        public static void VerifyFromCommandLine()
        {
            FixedStepBridgeChecks.Verify();
            SourceAnimationImporter.ImportFromCommandLine();
            SourceAnimationLibraryAsset asset = Resources.Load<SourceAnimationLibraryAsset>(SourcePlayerProvider.AnimationResourceKey);
            if (asset == null) throw new InvalidOperationException("Source runtime checks require the imported original Resources library.");
            var simulation = new GameSimulation(asset.Load(), 1);
            Action clearInput = FixedStepBridge.BindSourceInputReset(simulation);
            FieldInfo field = typeof(GameSimulation).GetField("pendingSourceActions", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("The frozen source input-buffer test seam is missing.");
            var pending = (Queue<PlayerCommand>)field.GetValue(simulation);
            int checks = 0;
            Action<bool, string> check = (condition, message) =>
            {
                checks++;
                if (!condition) throw new InvalidOperationException("Source runtime input assertion: " + message);
            };
            simulation.Step(new PlayerCommand(action: InputAction.SelectPlay));
            ISourceGameState first = (ISourceGameState)simulation.State;
            check(first.Tick == 1 && first.SourceTick == 0 && pending.Count == 1,
                "first skipped host tick retains the actual kernel edge");
            var bridge = new FixedStepBridge(simulation, clearInput);
            IGameState beforeFocus = simulation.State;
            bridge.SetActive(false);
            check(pending.Count == 0, "focus loss clears an edge already latched inside the kernel");
            check(ReferenceEquals(beforeFocus, simulation.State), "input reset does not step, restart or republish physics/clock state");
            check(bridge.Advance(1000, new FieldVector(1, 0), true) == 0 && simulation.State.Tick == 1,
                "hidden wall time does not advance");
            bridge.SetActive(true);
            check(bridge.Advance(1000, default(FieldVector), false) == 0 && simulation.State.Tick == 1,
                "resume discards stale elapsed time");
            check(bridge.Advance(FixedStepBridge.StepSeconds, default(FieldVector), false) == 1 &&
                ((ISourceGameState)simulation.State).SourceTick == 1 && pending.Count == 0,
                "normal source consumption resumes without stale edge replay");
            check(Math.Abs(bridge.RenderInterpolation01 - 0.9f) < 0.00001f,
                "render interpolation uses the57/60 integer remainder");
            bridge.Restart();
            check(simulation.State.Tick == 0 && ((ISourceGameState)simulation.State).SourceTick == 0 &&
                ReferenceEquals(bridge.PreviousState, bridge.State) && pending.Count == 0,
                "restart resets input and render history");
            check(bridge.Advance(1000, default(FieldVector), false) == 0, "restart discards the previous render delta");
            IGameState detached = simulation.State;
            int firstFrame = detached.Players[0].Animation.Primary.Sample.SourceFrame;
            for (int i = 0; i < 120; i++) bridge.Advance(FixedStepBridge.StepSeconds, default(FieldVector), false);
            check(simulation.State.Tick == 120 && ((ISourceGameState)simulation.State).SourceTick == 114,
                "120 actual host steps produce114 source steps");
            check(detached.Tick == 0 && detached.Players[0].Animation.Primary.Sample.SourceFrame == firstFrame,
                "render history snapshots remain detached");
            for (int i = 0; i < simulation.State.Players.Count; i++)
                check(simulation.State.Players[i].Animation.IsSpecified && simulation.State.Players[i].Role != ActorRole.Unspecified,
                    "actor " + i + " has actual source state");
            var output = new AuthorizedAssetRoot(Path.GetDirectoryName(Application.dataPath));
            string relative = SourceAnimationImporter.RequiredArgument(Environment.GetCommandLineArgs(), "-runtimeInputProof");
            File.WriteAllText(output.NewPath(relative), JsonUtility.ToJson(new Proof {
                schema = "source-runtime-input-native/v1", assertions = checks, hostTicks = simulation.State.Tick,
                sourceTicks = ((ISourceGameState)simulation.State).SourceTick, inventorySha256 = asset.InventorySha256,
                scope = "Actual production source kernel and bridge, native imported library; focus/reset changes only transient input, never actor/clock/root state."
            }, true) + "\n");
            Debug.Log("PASS: " + checks + " actual source-kernel/bridge native focus, skipped-host edge, exactly-once clearing, interpolation, restart and detached-state assertions.");
            SourceAnimationChecks.VerifyOriginalFromCommandLine();
        }
        [Serializable] private sealed class Proof
        {
            public string schema, inventorySha256, scope;
            public int assertions;
            public long hostTicks, sourceTicks;
        }
    }
}
#endif
