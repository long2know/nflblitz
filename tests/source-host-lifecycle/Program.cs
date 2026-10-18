using System;
using System.Reflection;
using Blitz.Runtime;
using UnityEngine;

internal static class LifecycleChecks
{
    private static int assertions, failures;

    private static void Require(bool condition, string name)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(name);
    }

    private static Exception Invoke(BlitzRuntime runtime, string method, params object[] arguments)
    {
        try
        {
            typeof(BlitzRuntime).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(runtime, arguments);
            return null;
        }
        catch (TargetInvocationException error) { return error.InnerException; }
    }

    private static void Call(BlitzRuntime runtime, string method, params object[] arguments)
    {
        Exception error = Invoke(runtime, method, arguments);
        Require(error == null, method + " must not throw: " + error);
    }

    private static BlitzRuntime Start(string fault = null, bool source = true)
    {
        HostProbe.Reset(fault, source);
        var runtime = new BlitzRuntime();
        Call(runtime, "Awake");
        return runtime;
    }

    private static void PressRestart(BlitzRuntime runtime)
    {
        Input.Clear();
        Call(runtime, "Update");
        Input.Press(KeyCode.R);
        Exception error = Invoke(runtime, "Update");
        Require(runtime.SourceFailure != null, "failed-bootstrap/R retains visible SourceFailure before any restart work");
        Require(HostProbe.Bridge.Restarts == 0, "failed-bootstrap/R never calls bridge.Restart");
        Require(HostProbe.Bridge.Activations == 0, "failed-bootstrap/R never calls SetActive(true)");
        Require(error == null, "failed-bootstrap/R never dereferences an incomplete HUD: " + error);
        Input.Clear();
    }

    private static void FailedBootstrap(string fault, string expected)
    {
        BlitzRuntime runtime = Start(fault);
        Require(runtime.IsSourceProfile, "failed-bootstrap retains explicit source profile");
        Require(runtime.SourceFailure != null && runtime.SourceFailure.Contains(expected),
            "failed-bootstrap exposes the required-resource/data/material cause");
        string failure = runtime.SourceFailure;
        Require(HostProbe.Bridge != null && !HostProbe.Bridge.Active, "failed-bootstrap deactivates the existing bridge");
        Require(HostProbe.Errors == 1, "failed-bootstrap logs one explicit failure, no swallowed fallback");
        Require(HostProbe.Hud == null, "required ball bootstrap fails before HUD creation");
        Require(HostProbe.Bridge.HookCount == 0, "required ball bootstrap fails before bridge hook installation");
        Require(HostProbe.CommandBuffers == 0, "failed data/material does not attach a ready ball consumer");
        if (fault == "missing-resource")
            Require(HostProbe.Count("add.SourceFreeBallRenderer") == 0 && HostProbe.Count("ball.load") == 0,
                "missing resource fails at the actual Resources.Load guard before renderer initialization");
        else
        {
            Require(HostProbe.Count("add.SourceFreeBallRenderer") == 1 && HostProbe.Count("ball.load") == 1,
                "throwing initialization executes the real SourceFreeBallRenderer.Initialize");
            Require(HostProbe.Count("ball.material.primary") == (fault == "data" ? 0 : 1),
                "data failure precedes material creation; material failure reaches the real initializer");
            Require(HostProbe.Count("ball.material.alternate") == (fault == "alternate-material" ? 1 : 0),
                "alternate material failure occurs after the primary material, not a fabricated early failure");
        }
        PressRestart(runtime);
        PressRestart(runtime);
        Call(runtime, "RestartMatch");
        Require(runtime.SourceFailure == failure, "direct restart retains the exact startup failure");
        Call(runtime, "OnApplicationFocus", false);
        Call(runtime, "OnApplicationFocus", true);
        Call(runtime, "OnApplicationPause", true);
        Call(runtime, "OnApplicationPause", false);
        runtime.isActiveAndEnabled = false;
        Call(runtime, "OnDisable");
        runtime.isActiveAndEnabled = true;
        Call(runtime, "OnEnable");
        Require(!HostProbe.Bridge.Active && HostProbe.Bridge.Activations == 0,
            "failed-bootstrap/focus-pause-enable never reactivates an incomplete bridge");
        Call(runtime, "LateUpdate");
        Call(runtime, "OnGUI");
        Require(GUI.LastBox.Contains(failure) && GUI.LastBox.StartsWith("SOURCE MODE ERROR\n"),
            "failed-bootstrap/R/focus retains the actual OnGUI failure banner");
        Require(HostProbe.Count("field.render") == 0 && HostProbe.Count("camera.render") == 0 &&
            HostProbe.Count("camera.render.reset") == 0 && HostProbe.Captures == 0 && HostProbe.Draws == 0,
            "failed-bootstrap never enters normal presentation or silently skips its required consumer");
        Require(HostProbe.Bridge.SourceAdvances == 0 && HostProbe.Bridge.Restarts == 0 &&
            HostProbe.Count("hud.clear") == 0 && HostProbe.Count("hud.source") == 0 &&
            HostProbe.Count("sound.reset") == 0 && HostProbe.Count("fire.reset") == 0,
            "failed-bootstrap has no premature gameplay/HUD/event/replay restart effects");
        Require(runtime.SourceFailure == failure && HostProbe.Errors == 1, "failed-bootstrap remains the original visible fail-stop");
        Call(runtime, "OnDestroy");
    }

    private static void LateBootstrapFailure(string fault)
    {
        BlitzRuntime runtime = Start(fault);
        Require(runtime.SourceFailure != null, "late-bootstrap failure is visible");
        Require(HostProbe.CommandBuffers == 1, "late-bootstrap failure happens after required ball initialization");
        string failure = runtime.SourceFailure;
        PressRestart(runtime);
        Require(runtime.SourceFailure == failure && !HostProbe.Bridge.Active,
            "startup readiness includes HUD/hooks/initial presentation, not just the ball asset");
        Call(runtime, "OnDestroy");
    }

    private static void ReadySourceRestart()
    {
        BlitzRuntime runtime = Start();
        Require(runtime.IsSourceProfile && runtime.SourceFailure == null, "ready source bootstrap succeeds");
        Require(HostProbe.Bridge.HookCount == 8 && HostProbe.CommandBuffers == 1 && HostProbe.Hud != null,
            "ready source has every actual bridge hook, HUD and required consumer");
        Require(HostProbe.Captures == 1 && HostProbe.Draws == 1, "ready source presents its required ball at startup");
        int cues = 0, events = 0, before = 0, after = 0;
        runtime.SourcePresentationCueObserved += value => cues++;
        runtime.SourceEventRaised += value => events++;
        runtime.BeforeHostStep += value => before++;
        runtime.AfterHostStep += value => after++;
        Call(runtime, "OnEnable");
        Require(HostProbe.Bridge.Active, "ready source OnEnable permits activation");
        Input.Press(KeyCode.R);
        Call(runtime, "Update");
        Require(HostProbe.Bridge.Restarts == 1 && runtime.SourceFailure == null, "ready source public R restarts");
        Require(HostProbe.Count("sound.reset") == 1 && HostProbe.Count("fire.reset") == 1,
            "ready-source-restart preserves the sound/replay and fire epoch restart hooks");
        Require(HostProbe.Count("hud.clear") == 1 && HostProbe.Count("selection.clear") == 1 &&
            HostProbe.Count("field.reset") == 1 && HostProbe.Count("input.clear") == 1,
            "ready source restart clears HUD/selection/history/input once");
        Require(HostProbe.Count("hud.source") == 2 && HostProbe.Count("camera.observe") == 1 &&
            HostProbe.Count("camera.render.reset") == 2 && cues == 1,
            "ready source restart observes rewind before one initial cue batch and reset camera render");
        int rewind = HostProbe.Calls.LastIndexOf("bridge.restart");
        int observe = HostProbe.Calls.LastIndexOf("camera.observe");
        int cue = HostProbe.Calls.LastIndexOf("camera.cue");
        int render = HostProbe.Calls.LastIndexOf("camera.render.reset");
        Require(rewind < observe && observe < cue && cue < render,
            "ready source restart preserves rewind/observe/initial-cue/render order");
        HostProbe.CabinetHeld = true;
        Call(runtime, "Update");
        Require(HostProbe.Bridge.Restarts == 1, "held R cannot restart through the neutral gate");
        Input.Clear();
        Call(runtime, "Update");
        Input.Press(KeyCode.R);
        Call(runtime, "Update");
        Require(HostProbe.Bridge.Restarts == 1, "held cabinet input cannot release the restart neutral gate");
        Input.Clear(); HostProbe.CabinetHeld = false;
        Call(runtime, "Update");
        Input.Press(KeyCode.R);
        Call(runtime, "Update");
        Require(HostProbe.Bridge.Restarts == 2 && HostProbe.Bridge.HookCount == 8,
            "neutral source input permits a second public restart without duplicating hooks");
        Call(runtime, "Update");
        Require(HostProbe.Bridge.Restarts == 2, "held R alone cannot release a neutral cabinet's restart gate");
        Input.Clear();
        Call(runtime, "Update");
        Call(runtime, "LateUpdate");
        Require(HostProbe.Captures == 2 && HostProbe.Draws == 2, "ready restarted source still consumes the required free ball");
        Require(events > 0 && before == after && before > 0 && HostProbe.Count("hud.event") > 0 &&
            HostProbe.Count("hud.source-event") > 0, "ready restarted source retains HUD and public host/game events");
        Call(runtime, "OnApplicationFocus", false);
        Require(!HostProbe.Bridge.Active, "ready source loses focus without running");
        Call(runtime, "OnApplicationFocus", true);
        Require(HostProbe.Bridge.Active, "ready source regains focus normally");
        Call(runtime, "OnDestroy");
        Require(HostProbe.Bridge.HookCount == 0, "ready source destruction removes every bridge hook");
    }

    private static void ReadySourceRuntimeFailure()
    {
        BlitzRuntime runtime = Start();
        Call(runtime, "OnEnable");
        runtime.FailSourceMode(new InvalidOperationException("post-ready transport failure"));
        Call(runtime, "OnApplicationFocus", false);
        Call(runtime, "OnApplicationFocus", true);
        Call(runtime, "OnEnable");
        Require(!HostProbe.Bridge.Active, "post-ready failure remains stopped across focus/enable");
        Input.Clear(); Call(runtime, "Update");
        Input.Press(KeyCode.R); Call(runtime, "Update");
        Require(runtime.SourceFailure == null && HostProbe.Bridge.Restarts == 1 && HostProbe.Bridge.Active,
            "fully initialized source retains recovery from a later runtime failure");
        Require(HostProbe.Hud != null && HostProbe.CommandBuffers == 1 && HostProbe.Bridge.HookCount == 8,
            "post-ready recovery never substitutes missing consumers");
        Call(runtime, "OnDestroy");
    }

    private static void LegacyRestart()
    {
        BlitzRuntime runtime = Start(source: false);
        Require(!runtime.IsSourceProfile && runtime.SourceFailure == null, "explicit actors=graybox retains diagnostic Legacy mode");
        Call(runtime, "OnEnable");
        Input.Press(KeyCode.R); Call(runtime, "Update");
        Require(HostProbe.Bridge.Restarts == 1 && HostProbe.Bridge.Active, "ready Legacy public R restarts");
        Require(HostProbe.Count("hud.clear") == 1 && HostProbe.Count("field.reset") == 1 &&
            HostProbe.Count("camera.render.reset") == 2, "Legacy restart preserves HUD/history/reset camera");
        Require(HostProbe.Count("hud.source") == 0 && HostProbe.Count("camera.observe") == 0 &&
            HostProbe.Captures == 0 && HostProbe.CommandBuffers == 0, "Legacy never enters source-only consumers/cues");
        Call(runtime, "Update");
        Require(HostProbe.Bridge.Restarts == 1, "Legacy held R waits for release");
        Input.Clear(); Call(runtime, "Update");
        Input.Press(KeyCode.R); Call(runtime, "Update");
        Require(HostProbe.Bridge.Restarts == 2, "Legacy neutral release permits ordinary restart");
        Call(runtime, "OnDestroy");
        Require(HostProbe.Bridge.HookCount == 0, "Legacy destruction removes every bridge hook");
    }

    private static void Run(string name, Action action)
    {
        try { action(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }

    private static int Main()
    {
        Run("missing-required-resource", () => FailedBootstrap("missing-resource", "Missing required original free-ball"));
        Run("throwing-required-data", () => FailedBootstrap("data", "medball2 data failure"));
        Run("throwing-primary-material", () => FailedBootstrap("primary-material", "free-ball material failure"));
        Run("throwing-alternate-material", () => FailedBootstrap("alternate-material", "free-ball material failure"));
        Run("throwing-late-HUD-initialization", () => LateBootstrapFailure("hud"));
        Run("throwing-initial-camera-presentation", () => LateBootstrapFailure("initial-camera"));
        Run("ready-source-public-R-neutral-events-cameras", ReadySourceRestart);
        Run("ready-source-runtime-failure-recovery", ReadySourceRuntimeFailure);
        Run("explicit-Legacy-public-R", LegacyRestart);
        Console.WriteLine("RESULT cases=9 assertions=" + assertions + " failures=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
