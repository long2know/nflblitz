using System;
using System.Collections.Generic;
using Blitz.Contracts;
using Blitz.Runtime;
#if UNITY_EDITOR
using System.Reflection;
using Blitz.Presentation;
using UnityEditor;
using UnityEngine;
#endif

namespace Blitz.Editor
{
    internal static class FixedStepBridgeChecks
    {
        internal static void Verify()
        {
            ObservedSimulation simulation = new ObservedSimulation();
            FixedStepBridge bridge = new FixedStepBridge(simulation);
            List<GameEvent> events = new List<GameEvent>();
            bridge.EventRaised += events.Add;
            Check(bridge.Advance(1000d, new FieldVector(1f, 1f), true) == 6, "A long frame must be capped at six ticks.");
            Check(bridge.State.Tick == 6 && bridge.State.Phase == GamePhase.PlaySelect, "Neutral selection must remain playable.");
            Check(bridge.Advance(0d, default(FieldVector), false) == 0, "Excess wall time must be dropped, not banked for the next frame.");
            for (int i = 0; i < simulation.Commands.Count; i++)
            {
                PlayerCommand command = simulation.Commands[i];
                Check(command.Move.X == 1f && command.Move.Z == 1f && command.Turbo, "Held movement/turbo must repeat on every fixed tick.");
            }
            simulation.Commands.Clear();

            bridge.QueueAction(InputAction.SelectPlay, PlayId.QuickPass);
            bridge.QueueAction(InputAction.SelectPlay, PlayId.Run);
            Check(bridge.Advance(0.1d, default(FieldVector), false) == 6, "A tenth second must execute six ticks.");
            Check(bridge.State.Phase == GamePhase.PreSnap && bridge.State.HomePlay == PlayId.QuickPass, "A queued selection must reach the real kernel.");
            Check(bridge.State.PreSnapTicksRemaining == SliceRules.PreSnapTicks - 5, "The selection tick must not decrement its new timer.");
            Check(simulation.Commands[0].Action == InputAction.SelectPlay, "The selection edge must reach the first tick.");
            for (int i = 1; i < simulation.Commands.Count; i++)
                Check(simulation.Commands[i].Action == InputAction.None, "A phase transition must discard the second queued selection.");
            Check(Count(events, GameEventType.PlaySelected) == 1, "The selection edge must be drained exactly once.");
            simulation.Commands.Clear();

            bridge.QueueAction(InputAction.Snap);
            bridge.Advance(0.1d, default(FieldVector), false);
            Check(bridge.State.Phase == GamePhase.LiveBall, "Snap must start the real live play.");
            Check(bridge.State.QuarterTicksRemaining == SliceRules.QuarterTicks - 5, "The snap tick must not spend live clock.");
            Check(Count(events, GameEventType.Snapped) == 1, "An early tick's event must survive a multi-tick rendered frame.");
            Check(simulation.Commands[0].Action == InputAction.Snap, "The snap edge must reach the first tick.");
            for (int i = 1; i < simulation.Commands.Count; i++)
                Check(simulation.Commands[i].Action == InputAction.None, "A held render frame must not repeat an action edge.");
            simulation.Commands.Clear();

            bridge.QueueAction(InputAction.Pass, targetPlayerId: 4);
            bridge.Advance(0.1d, default(FieldVector), false);
            Check(Count(events, GameEventType.PassThrown) == 1, "A real eligible pass must reach the kernel and its event sink.");
            for (int i = 1; i < events.Count; i++)
                Check(events[i].Tick >= events[i - 1].Tick, "Per-tick event draining must preserve event order.");

            long pausedTick = bridge.State.Tick;
            bridge.QueueAction(InputAction.Pass, targetPlayerId: 5);
            bridge.SetActive(false);
            bridge.QueueAction(InputAction.Snap);
            Check(bridge.Advance(1000d, new FieldVector(1f, 0f), true) == 0, "An unfocused game must not advance.");
            Check(bridge.State.Tick == pausedTick, "Hidden-tab wall time must not become match time.");
            bridge.SetActive(true);
            Check(bridge.Advance(1000d, new FieldVector(1f, 0f), true) == 0, "Discard the resume frame, including its stale delta.");
            Check(bridge.Advance(0d, default(FieldVector), false) == 0, "Resume must start with an empty accumulator.");
            simulation.Commands.Clear();
            Check(bridge.Advance(FixedStepBridge.StepSeconds, default(FieldVector), false) == 1, "Normal fixed ticks must resume.");
            Check(simulation.Commands[0].Action == InputAction.None, "Focus loss must clear a queued action.");

            bridge.QueueAction(InputAction.Pass, targetPlayerId: 6);
            bridge.Restart();
            Check(bridge.State.Tick == 0 && bridge.State.Phase == GamePhase.PlaySelect, "Restart must reset the actual game.");
            Check(bridge.State.HomeScore == 0 && bridge.State.AwayScore == 0, "Restart must reset the score.");
            Check(bridge.Advance(1000d, default(FieldVector), false) == 0, "Restart must discard the old frame.");
            simulation.Commands.Clear();
            Check(bridge.Advance(0.1d, default(FieldVector), false) == 6, "Restart must allow normal stepping.");
            Check(bridge.State.Phase == GamePhase.PlaySelect, "Restart must not replay a stale action.");
            for (int i = 0; i < simulation.Commands.Count; i++)
                Check(simulation.Commands[i].Action == InputAction.None, "Restart must clear commands, not just rely on wrong-phase rejection.");
            long tick = bridge.State.Tick;
            bridge.Advance(double.NaN, default(FieldVector), false);
            bridge.Advance(double.PositiveInfinity, default(FieldVector), false);
            bridge.Advance(-1d, default(FieldVector), false);
            Check(bridge.State.Tick == tick, "Invalid frame durations must not corrupt the accumulator.");
        }

        // Observation only: all state, events and rules still come from the production kernel.
        private sealed class ObservedSimulation : IGameSimulation
        {
            private readonly IGameSimulation inner = new Blitz.Gameplay.GameSimulation(1);
            internal readonly List<PlayerCommand> Commands = new List<PlayerCommand>();

            public IGameState State { get { return inner.State; } }
            public IReadOnlyList<GameEvent> Events { get { return inner.Events; } }

            public void Step(PlayerCommand command)
            {
                Commands.Add(command);
                inner.Step(command);
            }

            public void Restart(int seed = 1)
            {
                inner.Restart(seed);
            }
        }

        private static int Count(List<GameEvent> events, GameEventType type)
        {
            int count = 0;
            for (int i = 0; i < events.Count; i++)
                if (events[i].Type == type)
                    count++;
            return count;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Fixed-step bridge contract failed: " + message);
        }

#if BLITZ_BRIDGE_VALIDATION
        private static void Main()
        {
            Verify();
            Console.WriteLine("PASS: exact kernel + fixed-step bridge: cap/edges/phase boundaries/events/focus/restart/nonfinite time.");
        }
#endif
    }

#if UNITY_EDITOR
    public static class HudTypographyChecks
    {
        private static int measurements;

        public static void Verify()
        {
            GUISkin skin = UnityEngine.Object.Instantiate(EditorGUIUtility.GetBuiltinSkin(EditorSkin.Game));
            GameObject root = new GameObject("HUD typography check") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                measurements = 0;
                skin.label.wordWrap = true;
                skin.button.wordWrap = true;
                PlayHud hud = root.AddComponent<PlayHud>();
                Call(hud, "CreateStyles", skin);
                GUIStyle label = Style(hud, "label");
                GUIStyle paragraph = Style(hud, "paragraph");
                GUIStyle button = Style(hud, "button");
                Check(!label.wordWrap && !button.wordWrap, "Fixed labels/buttons must override inherited wordWrap=true.");
                Check(paragraph.wordWrap, "Descriptions must retain deliberate multiline wrapping.");
                Check(label.clipping == TextClipping.Clip && button.clipping == TextClipping.Clip,
                    "Fixed labels/buttons must use explicit clipping.");
                Check(label.font != null && button.font != null && paragraph.font == label.font,
                    "Measurements must use the actual player font, not an editor/default-font substitute.");

                List<string> headings = new List<string>
                {
                    (string)Call(null, "ContextHeading", new Blitz.Gameplay.GameSimulation(1).State),
                    "SET YOUR DEFENSE", "READY TO SNAP", "READ THE OFFENSE", "FIND THE CATCH",
                    "MAKE YOUR MOVE", "STOP THE DRIVE", "HOME WINS", "AWAY WINS", "TIE GAME"
                };
                foreach (PlayOutcome outcome in Enum.GetValues(typeof(PlayOutcome)))
                    headings.Add((string)Call(null, "OutcomeText", outcome));
                int[,] viewports = { { 1280, 720 }, { 1000, 560 }, { 768, 740 }, { 650, 740 }, { 360, 740 }, { 320, 568 } };
                for (int i = 0; i < viewports.GetLength(0); i++)
                {
                    object layout = Activator.CreateInstance(typeof(PlayHud).Assembly.GetType("Blitz.Presentation.HudLayout", true),
                        BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { viewports[i, 0], viewports[i, 1] }, null);
                    float width = Layout(layout, "Width");
                    float sidebar = Layout(layout, "Sidebar");
                    bool narrow = width < 650f;
                    float contextWidth = sidebar > 0f ? sidebar - 56f : width - 32f;
                    Rect heading = new Rect(0f, 0f, contextWidth, sidebar > 0f ? 38f : 25f);
                    foreach (string text in headings)
                        Fits(hud, label, text, heading, sidebar > 0f ? 27 : narrow ? 23 : 22, true);
                    Fits(hud, label, "<<<  AWAY BALL  /  YOU PLAY DEFENSE",
                        new Rect(0f, 0f, narrow ? width - 32f : width * 0.43f, narrow ? 18f : 25f), narrow ? 14 : 16, true);
                    Fits(hud, label, "4th & GOAL (100)     |     AT HOME 20", new Rect(0f, 0f, width - 32f, 19f), 17, true);
                    Fits(hud, label, "YOU: BLOCK 1 / TURBO", new Rect(0f, 0f, narrow ? contextWidth * 0.45f : contextWidth, 18f), 12, true);
                    Fits(hud, label, "WASD / arrows move   SHIFT turbo   F tackle   Q switch",
                        new Rect(0f, 0f, width - 32f, 19f), 12, false);
                    Fits(hud, button, "R  RESTART", new Rect(0f, 0f, narrow ? 92f : 112f, narrow ? 28f : 34f), narrow ? 14 : 17, true);
                    Fits(hud, button, "2  QUICK PASS", new Rect(0f, 0f, (contextWidth - 10f) / 2f, 54f), 17, true);
                    Fits(hud, button, "Q   SWITCH", new Rect(0f, 0f, (contextWidth - 8f) / 2f, 46f), 17, true);
                    Fits(hud, button, "1\nWR 1", new Rect(0f, 0f, (contextWidth - 12f) / 3f, 59f), 14, true);
                    Fits(hud, button, "SPACE  SNAP  /  AUTO 5.0s", new Rect(0f, 0f, contextWidth, 50f), 17, true);
                    Rect body = new Rect(0f, 0f, sidebar > 0f || narrow ? contextWidth : contextWidth * 0.53f,
                        sidebar > 0f ? 52f : narrow ? 39f : 37f);
                    string[] descriptions =
                    {
                        "Four downs to gain thirty yards. Choose 1 or 2 to line up.",
                        "You control Home in cyan. Choose a scheme; Away will snap automatically.",
                        "HOME: QUICK PASS / AWAY: COVER. SPACE snaps now, or wait for auto-snap.",
                        "Close the gap with WASD / SHIFT. F tackles in reach. Q selects another Home defender."
                    };
                    foreach (string text in descriptions)
                        Fits(hud, paragraph, text, body, sidebar > 0f ? 15 : 14, false);
                    Rect scene = new Rect(0f, Layout(layout, "Header"), width - sidebar,
                        Layout(layout, "Height") - Layout(layout, "Header") - Layout(layout, "Footer"));
                    VerifyWorldLabels(hud, label, scene);
                }
                Fits(hud, label, "PAUSED / GAME UNFOCUSED", new Rect(0f, 0f, 416f, 30f), 22, true);
                Fits(hud, paragraph, "Click the game to return. Release held keys.\nHidden-tab time is not match time.",
                    new Rect(0f, 0f, 408f, 40f), 14, false);
                List<Rect> occupied = (List<Rect>)Field("worldLabels").GetValue(hud);
                occupied.Clear();
                Rect placed;
                Check(Place(hud, new Rect(0f, 0f, 200f, 32f), new Vector2(100f, 16f), "YOU / QB / BALL", 14, out placed),
                    "One bounded world label should fit.");
                Check(!Place(hud, new Rect(0f, 0f, 200f, 32f), new Vector2(100f, 16f), "FIRST DOWN", 11, out placed),
                    "A crowded viewport must omit a lower-priority label, not overlap another.");
                Check(!Place(hud, new Rect(0f, 0f, 79f, 32f), Vector2.zero, "SCRIMMAGE", 11, out placed),
                    "A world label must not escape a viewport too small for its minimum bounds.");
                LogFit(hud, label, "CALL YOUR PLAY", new Rect(0f, 0f, 244f, 38f), 27);
                LogFit(hud, label, "TURNOVER ON DOWNS", new Rect(0f, 0f, 244f, 38f), 27);
                LogFit(hud, button, "R  RESTART", new Rect(0f, 0f, 112f, 34f), 17);
                LogFit(hud, button, "R  RESTART", new Rect(0f, 0f, 92f, 28f), 14);
                Debug.Log("PASS: HUD typography / " + measurements + " actual-font fits / six desktop, compact and narrow layouts / "
                    + label.font.name + " / inherited wrapping, long headings, restart, explicit pass lines, wrapped bodies and bounded nonoverlapping world labels.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(skin);
            }
        }

        private static void VerifyWorldLabels(PlayHud hud, GUIStyle label, Rect scene)
        {
            List<Rect> occupied = (List<Rect>)Field("worldLabels").GetValue(hud);
            occupied.Clear();
            string[] texts = { "YOU / QB / BALL", "YOU / 3  WR3 / TARGET", "YOU / DEF 7", "YOU / BLOCK 1 / BALL", "SCRIMMAGE", "FIRST DOWN" };
            foreach (string text in texts)
            {
                int size = text == "SCRIMMAGE" || text == "FIRST DOWN" ? 11 : 14;
                Rect placed;
                Check(Place(hud, scene, scene.center, text, size, out placed), "World label could not fit: " + text);
                Check(placed.width >= 72f && placed.width <= Mathf.Min(240f, scene.width - 8f), "World label width is not bounded: " + text);
                Check(placed.xMin >= scene.xMin && placed.xMax <= scene.xMax && placed.yMin >= scene.yMin && placed.yMax <= scene.yMax,
                    "World label escaped the field: " + text);
                for (int i = 0; i < occupied.Count - 1; i++)
                    Check(!placed.Overlaps(occupied[i]), "World labels overlap: " + text);
                Fits(hud, label, text, new Rect(0f, 0f, placed.width - 8f, placed.height - 1f), size, true);
            }
            foreach (Vector2 edge in new[] { scene.min, scene.max })
            {
                occupied.Clear();
                Rect placed;
                Check(Place(hud, scene, edge, "YOU / DEF 7 / BALL", 14, out placed), "Edge label should clamp inside the field.");
                Check(scene.Contains(placed.min) && scene.Contains(placed.max), "Clamped edge label escaped the field.");
            }
        }

        private static bool Place(PlayHud hud, Rect scene, Vector2 anchor, string text, int size, out Rect rect)
        {
            object[] args = { scene, anchor, text, size, null };
            bool placed = (bool)Call(hud, "TryWorldLabelRect", args);
            rect = (Rect)args[4];
            return placed;
        }

        private static void Fits(PlayHud hud, GUIStyle style, string text, Rect rect, int size, bool bold)
        {
            style.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            bool fits = (bool)Call(hud, "FitText", style, rect, text, size);
            GUIContent content = new GUIContent(text);
            Vector2 measured = style.wordWrap
                ? new Vector2(rect.width - 1f, style.CalcHeight(content, rect.width - 1f))
                : style.CalcSize(content);
            string detail = text.Replace("\n", " / ") + " / " + style.font.name + " " + style.fontSize
                + " / measured " + measured + " / rect " + rect.size;
            Check(fits && measured.x <= rect.width - 1f && measured.y <= rect.height - 1f, "Text clips: " + detail);
            Check(style.fontSize >= Mathf.Min(10, size) && style.fontSize <= size, "Font fit escaped its bounds: " + detail);
            if (!style.wordWrap && text.IndexOf('\n') < 0)
                Check(style.CalcHeight(content, rect.width) <= rect.height, "A fixed-line label became multiline: " + detail);
            measurements++;
        }

        private static void LogFit(PlayHud hud, GUIStyle style, string text, Rect rect, int size)
        {
            Fits(hud, style, text, rect, size, true);
            Debug.Log("HUD metric: " + text + " / " + style.font.name + " " + style.fontSize + " / measured "
                + style.CalcSize(new GUIContent(text)) + " / rect " + rect.size);
        }

        private static object Call(PlayHud hud, string name, params object[] args)
        {
            MethodInfo method = typeof(PlayHud).GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
            Check(method != null, "Production HUD method is missing: " + name);
            return method.Invoke(hud, args);
        }

        private static FieldInfo Field(string name)
        {
            return typeof(PlayHud).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static GUIStyle Style(PlayHud hud, string name)
        {
            return (GUIStyle)Field(name).GetValue(hud);
        }

        private static float Layout(object layout, string name)
        {
            return (float)layout.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(layout);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("HUD typography contract failed: " + message);
        }
    }
#endif
}
