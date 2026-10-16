using System;
using System.Collections.Generic;
using System.Reflection;
using Blitz.Contracts;
#if UNITY_EDITOR
using UnityEngine;
#endif

namespace Blitz.Editor
{
    public static class NormalCameraChecks
    {
        private const float ActorHeight = 2.35f;
        private const float ActorRadius = 1.05f;
        private static int assertions;
        private static readonly BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public static void Verify()
        {
            assertions = 0;
            ProjectionAndSetup();
            TrackingAndTiming();
            PassAndTransitions();
            IncompleteAndFirstDeadView();
            FieldEdges();
            ActualKernel();
#if UNITY_EDITOR
            UnityProjectionAndLayout();
            Debug.Log("PASS: normal camera / " + assertions + " behavioral assertions / pure model + real Unity perspective and HUD viewport.");
#else
            Console.WriteLine("PASS: normal camera / " + assertions + " behavioral assertions / exact production model + kernel; Unity projection pending.");
#endif
        }

        private static void ProjectionAndSetup()
        {
            float[] aspects = { 4f / 3f, 16f / 9f, 0.62f, 9f / 16f, 3.2f, 8.9f };
            foreach (int direction in new[] { 1, -1 })
                foreach (float aspect in aspects)
                {
                    View state = new View(direction, direction > 0 ? 20f : 80f);
                    Rig rig = new Rig();
                    Frame frame = rig.Update(state, aspect, 0f, true);
                    Near(frame.Focus.X, state.LineOfScrimmage - 50f, 0.0001f, "Setup must target the LOS, not a distant controlled defender.");
                    Near(frame.Focus.Z, 0f, 0.0001f, "Setup lateral focus must be the offense QB.");
                    Check(frame.Direction == direction && direction * (frame.Focus.X - frame.Eye.X) > 0f,
                        "Both teams must be viewed from behind their offense.");
                    Near(frame.EyeHeight, 2f + frame.Distance * (float)Math.Sin(25d * Math.PI / 180d), 0.0001f,
                        "Six-foot focus and -25 degree source phi must determine the eye height in yards.");
                    Near(direction * (frame.Focus.X - frame.Eye.X), frame.Distance * (float)Math.Cos(25d * Math.PI / 180d), 0.0001f,
                        "Source MOVEVIEW must determine longitudinal eye distance.");
                    Check(frame.Distance >= 30f && frame.Distance < 50f, "Setup must retain the 30-yard source baseline without framing the entire field.");
                    if (aspect >= 4f / 3f)
                        Near(frame.Fov, 32.25515165f, 0.0001f, "Projection must derive vertical FOV from the engine's 664px focal length at 512x384.");
                    else
                        Near(frame.TanVertical * aspect, 256f / 664f, 0.0001f, "Portrait adaptation must preserve the reference horizontal aperture.");
                    Projected center = Project(frame, frame.Focus.X, 2f, frame.Focus.Z);
                    Near(center.X, 0.5f, 0.0001f, "Focus must project to the horizontal center.");
                    Near(center.Y, 0.5f, 0.0001f, "Focus must project to the vertical center.");
                    Near(center.Depth, frame.Distance, 0.0001f, "Focus depth must equal camera distance.");
                    foreach (PlayerView player in state.Players)
                        if (player.Team == state.Possession)
                            VisibleActor(frame, player.Position, "setup offense");
                    state.Phase = GamePhase.PreSnap;
                    state.Tick++;
                    Frame preSnap = rig.Update(state, aspect, 1f / 60f);
                    Near(preSnap.Focus.X, frame.Focus.X, 0.0001f, "Selection-to-pre-snap must not jump the camera.");
                    float nearHeight = Project(frame, frame.Focus.X, ActorHeight, 0f).Y - Project(frame, frame.Focus.X, 0f, 0f).Y;
                    float farX = frame.Focus.X + direction * 15f;
                    float farHeight = Project(frame, farX, ActorHeight, 0f).Y - Project(frame, farX, 0f, 0f).Y;
                    Check(nearHeight > farHeight * 1.15f, "Perspective must make a distant equal-height actor smaller; orthographic framing is not acceptable.");
                }
        }

        private static void TrackingAndTiming()
        {
            foreach (int direction in new[] { 1, -1 })
            {
                View state = View.Isolated(direction, direction > 0 ? 40f : 60f, 10f, 3);
                state.LineOfScrimmage = direction > 0 ? 45f : 55f;
                Rig rig = new Rig();
                Frame quarterback = rig.Update(state, 4f / 3f, 0f, true);
                Near(quarterback.Focus.X, state.Ball.Position.X - 50f + direction * 6f, 0.0001f, "Live QB focus must lead six yards in attack direction.");
                Near(quarterback.Focus.Z, 10f * 0.68f * 0.82f, 0.0001f, "QB sideline panning must use the source .68*.82 factor.");
                Near(quarterback.Distance, 34f, 0.0001f, "QB distancing must be 30 yards plus .40 times lateral yards, not archive units.");
                state.SetBall(direction > 0 ? 46f : 54f, 10f, 3);
                state.Tick++;
                Frame runner = rig.Update(state, 4f / 3f, 0f, true);
                Near(runner.Focus.X, state.Ball.Position.X - 50f, 0.0001f, "Crossing the LOS must leave the slice's QB-mode adapter.");
                Near(runner.Distance, 19f, 0.0001f, "An isolated runner must use the 19-yard source minimum.");
                VisibleActor(runner, state.Ball.Position, "runner");
                state.SetBall(direction > 0 ? 54f : 46f, 10f, 3);
                state.Tick++;
                Frame moving = rig.Update(state, 4f / 3f, 1f / 60f);
                Check(direction * (moving.Focus.X - runner.Focus.X) > 0f
                    && direction * (moving.Focus.X - (state.Ball.Position.X - 50f)) < 0f, "Normal following must move smoothly toward the carrier.");
                Near(direction * (moving.Focus.X - runner.Focus.X), 0.9914103f, 0.00001f,
                    "Real-time following must use the measured .08/.35 source pole at 57 Hz.");
                for (int i = 0; i < 120; i++)
                    moving = rig.Update(state, 4f / 3f, 1f / 60f);
                Near(moving.Focus.X, state.Ball.Position.X - 50f, 0.257f, "Tracking must converge inside the source two-unit dead zone.");
                Near(Math.Abs(moving.Focus.X - (state.Ball.Position.X - 50f)), 2f / 7.8f, 0.00001f,
                    "The source two-unit tracking zone must be normalized to yards, not silently removed.");
                Frame invalid = rig.Update(state, 4f / 3f, float.NaN);
                Near(invalid.Focus.X, moving.Focus.X, 0f, "Nonfinite frame time must not move the camera.");
                state.SetBall(direction > 0 ? 60f : 40f, 10f, 3);
                Frame resumed = rig.Update(state, 4f / 3f, 10000f);
                Near(resumed.Focus.X, state.Ball.Position.X - 50f, 0.0001f, "A long suspended frame must realign, not integrate accumulated tab time.");
                Frame after = rig.Update(state, 4f / 3f, 0f);
                Near(after.Focus.X, resumed.Focus.X, 0f, "Suspended wall time must not be banked.");
            }
            float expected = float.NaN;
            foreach (int rate in new[] { 30, 60, 120 })
            {
                View state = View.Isolated(1, 40f, 0f, 4);
                Rig rig = new Rig();
                rig.Update(state, 4f / 3f, 0f, true);
                state.SetBall(50f, 0f, 4);
                Frame frame = default(Frame);
                for (int i = 0; i < rate; i++)
                    frame = rig.Update(state, 4f / 3f, 1f / rate);
                if (!float.IsNaN(expected))
                    Near(frame.Focus.X, expected, 0.0001f, "Following must depend on elapsed frame time, not render frame count.");
                expected = frame.Focus.X;
            }
        }

        private static void PassAndTransitions()
        {
            foreach (int direction in new[] { 1, -1 })
                foreach (float aspect in new[] { 4f / 3f, 0.62f, 16f / 9f })
                {
                    View state = View.Isolated(direction, direction > 0 ? 40f : 60f, 0f, 3);
                    state.LineOfScrimmage = direction > 0 ? 45f : 55f;
                    Rig rig = new Rig();
                    rig.Update(state, aspect, 0f, true);
                    int receiver = direction > 0 ? 4 : 11;
                    state.Place(receiver, direction > 0 ? 60f : 40f, 0f);
                    state.Ball = new BallView(BallMode.InFlight, state.Ball.Position, 5f, -1, receiver);
                    state.Tick++;
                    Frame thrown = rig.Update(state, aspect, 0f, true);
                    float sourceDepth = -5f + 15f * (float)Math.Sin(25d * Math.PI / 180d) / 2f + 6f;
                    Near(thrown.Focus.X, direction * sourceDepth, 0.0001f,
                        "Pass framing must use the source .75 ball bias, phi-biased span and six-yard lead, not the controlled thrower.");
                    for (int tick = 0; tick < 30; tick++)
                    {
                        state.Tick++;
                        state.Ball = new BallView(BallMode.InFlight,
                            new FieldVector((direction > 0 ? 40f : 60f) + direction * tick * 0.6f, tick * 0.2f), 5.8f, -1, receiver);
                        Frame flight = rig.Update(state, aspect, 1f / 60f);
                        VisiblePoint(flight, state.Ball.Position, state.Ball.Height, "flight ball");
                        VisibleActor(flight, state.Players[receiver].Position, "intended receiver");
                        Check(flight.Distance < 75f, "A normal pass must not trigger full-field zoom.");
                    }
                    state.SetBall(direction > 0 ? 60f : 40f, 0f, 4);
                    state.Tick++;
                    Frame caught = default(Frame);
                    for (int i = 0; i < 120; i++)
                        caught = rig.Update(state, aspect, 1f / 60f);
                    Near(caught.Focus.X, direction * 10f, 0.257f, "A catch must transition back to carrier framing.");
                    Check(caught.Distance < 19.27f, "A caught pass must return to close runner distance.");
                    state.Possession = direction > 0 ? TeamId.Away : TeamId.Home;
                    state.OffenseDirection = -direction;
                    state.LineOfScrimmage = direction > 0 ? 85f : 15f;
                    state.Phase = GamePhase.DeadBall;
                    state.LastOutcome = PlayOutcome.TurnoverOnDowns;
                    state.Ball = new BallView(BallMode.Dead, state.Ball.Position, 0f, -1, -1);
                    state.Tick++;
                    Frame dead = rig.Update(state, aspect, 1f / 60f);
                    Check(dead.Direction == direction, "Dead-ball next-possession fields must not flip the view of frozen actors.");
                    Near(dead.Focus.X, caught.Focus.X, 0.001f, "Dead-ball framing must not chase the next LOS.");
                    state.Phase = GamePhase.GameOver;
                    state.Tick++;
                    Check(rig.Update(state, aspect, 1f / 60f).Direction == direction, "Final must retain the ended play's orientation.");
                    View next = new View(-direction, direction > 0 ? 85f : 15f) { Tick = state.Tick + 1, PlayNumber = 2 };
                    Frame lineup = rig.Update(next, aspect, 1f / 60f);
                    Check(lineup.Direction == -direction, "The next lineup must adopt the new offense, not retain dead-ball orientation.");
                    Near(lineup.Focus.X, next.LineOfScrimmage - 50f, 0.0001f, "A new lineup must snap to the actual next scrimmage.");
                    Frame restart = rig.Update(new View(1, 20f), aspect, 1f / 60f);
                    Check(restart.Direction == 1, "Tick/play reset must restore Home orientation without a cross-field sweep.");
                    Near(restart.Focus.X, -30f, 0.0001f, "Restart must reset camera focus.");
                }
        }

        private static void IncompleteAndFirstDeadView()
        {
            foreach (int direction in new[] { 1, -1 })
            {
                View state = View.Isolated(direction, direction > 0 ? 40f : 60f, 0f, 3);
                state.LineOfScrimmage = direction > 0 ? 45f : 55f;
                int receiver = direction > 0 ? 4 : 11;
                state.Place(receiver, direction > 0 ? 60f : 40f, 5f);
                Rig rig = new Rig();
                rig.Update(state, 4f / 3f, 0f, true);
                state.Ball = new BallView(BallMode.InFlight, state.Ball.Position, 4f, -1, receiver);
                state.Tick++;
                rig.Update(state, 4f / 3f, 1f / 60f);
                state.Phase = GamePhase.DeadBall;
                state.LastOutcome = PlayOutcome.Incomplete;
                state.LineOfScrimmage = direction > 0 ? 45f : 55f;
                state.Ball = new BallView(BallMode.Dead, new FieldVector(direction > 0 ? 62f : 38f, 7f), 0f, -1, -1);
                state.Tick++;
                Frame dead = rig.Update(state, 0.62f, 0f, true);
                Near(dead.Focus.X, direction * 10f, 0.0001f, "An incomplete pass must retain its remembered intended receiver as the source's dead-ball target.");
                Near(dead.Focus.Z, 5f, 0.0001f, "Incomplete framing must not substitute next LOS or the thrower.");
                VisibleActor(dead, state.Players[receiver].Position, "incomplete receiver");
                VisiblePoint(dead, state.Ball.Position, 0.3f, "incomplete ball");
                state.LastOutcome = PlayOutcome.Touchdown;
                state.Possession = direction > 0 ? TeamId.Away : TeamId.Home;
                state.OffenseDirection = -direction;
                Frame firstDead = new Rig().Update(state, 4f / 3f, 0f, true);
                Check(firstDead.Direction == direction, "A first observation after a score must infer the ended play, not the receiving team's direction.");
            }
            View tallState = View.Isolated(1, 50f, 0f, 4);
            Frame normal = new Rig().Update(tallState, 4f / 3f, 0f, true);
            Frame tall = new Rig(12f, ActorRadius).Update(tallState, 4f / 3f, 0f, true);
            Check(tall.Distance > normal.Distance + 10f, "Actor framing must consume the supplied geometry envelope, not assume original mesh scale.");
            VisiblePoint(tall, tallState.Ball.Position, 12f, "parameterized tall mesh envelope");
        }

        private static void FieldEdges()
        {
            foreach (int direction in new[] { 1, -1 })
                foreach (float x in new[] { -9.5f, 1f, 99f, 109.5f })
                    foreach (float z in new[] { -SliceRules.FieldHalfWidthYards, SliceRules.FieldHalfWidthYards })
                        foreach (float aspect in new[] { 4f / 3f, 0.62f, 3.2f })
                        {
                            View state = View.Isolated(direction, x, z, 4);
                            Frame frame = new Rig().Update(state, aspect, 0f, true);
                            Check(direction * frame.Focus.X >= -62f, "Source backfield limiting must prevent a target behind the six-foot back boundary allowance.");
                            if (direction * (x - 50f) < -56.39468088f)
                                Near(direction * frame.Focus.X, -56.39468088f, 0.0001f,
                                    "Own-end focus must apply the source alpha/sine ground-intercept limit with 7.8 units per yard.");
                            Near(Math.Abs(frame.Focus.Z), 182.85f / 7.8f + 8f / 3f - 19f / 2f, 0.0001f,
                                "Runner sideline limiting must use source field width and eight-foot allowance in yards.");
                            VisibleActor(frame, state.Ball.Position, "end-zone/sideline actor");
                            VisiblePoint(frame, state.Ball.Position, 1.15f, "boundary ball");
                        }
        }

        private static void ActualKernel()
        {
            Blitz.Gameplay.GameSimulation simulation = new Blitz.Gameplay.GameSimulation(1);
            Rig rig = new Rig();
            rig.Update(simulation.State, 0.62f, 0f, true);
            simulation.Step(new PlayerCommand(action: InputAction.SelectPlay, play: PlayId.QuickPass));
            Check(simulation.State.Phase == GamePhase.PreSnap, "Camera integration must use real selection.");
            rig.Update(simulation.State, 0.62f, 1f / 60f);
            simulation.Step(new PlayerCommand(action: InputAction.Snap));
            simulation.Step(new PlayerCommand(action: InputAction.Pass, targetPlayerId: 4));
            Check(simulation.State.Ball.Mode == BallMode.InFlight, "Camera integration must exercise an actual kernel pass.");
            int ticks = 0;
            while (simulation.State.Phase == GamePhase.LiveBall && ticks++ < 600)
            {
                IGameState state = simulation.State;
                Frame frame = rig.Update(state, 0.62f, 1f / 60f);
                VisiblePoint(frame, state.Ball.Position, Math.Max(0.3f, state.Ball.Height), "actual kernel ball");
                if (state.Ball.Mode == BallMode.InFlight)
                    VisibleActor(frame, state.Players[state.Ball.TargetPlayerId].Position, "actual kernel receiver");
                simulation.Step(new PlayerCommand(new FieldVector(0f, 1f), turbo: true));
            }
            Check(simulation.State.Phase == GamePhase.DeadBall, "The bounded actual-kernel camera trace must reach play end.");
            rig.Update(simulation.State, 0.62f, 1f / 60f);
            simulation.Step(new PlayerCommand(action: InputAction.Continue));
            Frame next = rig.Update(simulation.State, 0.62f, 1f / 60f);
            Near(next.Focus.X, simulation.State.LineOfScrimmage - 50f, 0.0001f, "Actual Continue must realign camera to next LOS.");
            simulation.Restart();
            Near(rig.Update(simulation.State, 0.62f, 0f, true).Focus.X, -30f, 0.0001f, "Actual Restart must realign camera.");
        }

        private static void VisibleActor(Frame frame, FieldVector position, string label)
        {
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    foreach (float y in new[] { 0f, ActorHeight })
                        VisiblePoint(frame, new FieldVector(position.X + x * ActorRadius, position.Z + z * ActorRadius), y, label);
        }

        private static void VisiblePoint(Frame frame, FieldVector position, float height, string label)
        {
            Projected point = Project(frame, position.X - 50f, height, position.Z);
            Check(point.Depth >= 0.49f && point.X >= 0.059f && point.X <= 0.941f && point.Y >= 0.059f && point.Y <= 0.941f,
                label + " escaped the safe perspective viewport: " + point.X + ", " + point.Y + ", depth=" + point.Depth);
        }

        private static Projected Project(Frame frame, float x, float y, float z)
        {
            double pitch = 25d * Math.PI / 180d;
            double dx = x - frame.Eye.X;
            double dy = y - frame.EyeHeight;
            double depth = frame.Direction * dx * Math.Cos(pitch) - dy * Math.Sin(pitch);
            double up = frame.Direction * dx * Math.Sin(pitch) + dy * Math.Cos(pitch);
            double right = -frame.Direction * (z - frame.Eye.Z);
            return new Projected((float)(0.5d + right / (2d * depth * frame.TanVertical * frame.Aspect)),
                (float)(0.5d + up / (2d * depth * frame.TanVertical)), (float)depth);
        }

        private static void Near(float actual, float expected, float tolerance, string message)
        {
            Check(!float.IsNaN(actual) && Math.Abs(actual - expected) <= tolerance, message + " actual=" + actual + ", expected=" + expected);
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static T Read<T>(object value, string field)
        {
            return (T)value.GetType().GetField(field, Members).GetValue(value);
        }

        private sealed class Rig
        {
            private readonly object model;
            private readonly MethodInfo update;

            internal Rig(float actorHeight = ActorHeight, float actorRadius = ActorRadius)
            {
                Type type = typeof(IGameState).Assembly.GetType("Blitz.Presentation.NormalCameraModel", true);
                model = Activator.CreateInstance(type, Members, null, new object[] { actorHeight, actorRadius }, null);
                update = type.GetMethod("Update", Members);
            }

            internal Frame Update(IGameState state, float aspect, float seconds, bool reset = false)
            {
                return new Frame(update.Invoke(model, new object[] { state, aspect, seconds, reset }));
            }
        }

        private readonly struct Frame
        {
            internal readonly FieldVector Focus;
            internal readonly FieldVector Eye;
            internal readonly float EyeHeight;
            internal readonly float Distance;
            internal readonly float Aspect;
            internal readonly float Fov;
            internal readonly int Direction;
            internal float TanVertical { get { return (float)Math.Tan(Fov * Math.PI / 360d); } }

            internal Frame(object frame)
            {
                Focus = Read<FieldVector>(frame, "Focus");
                Eye = Read<FieldVector>(frame, "Eye");
                EyeHeight = Read<float>(frame, "EyeHeight");
                Distance = Read<float>(frame, "Distance");
                Aspect = Read<float>(frame, "Aspect");
                Fov = Read<float>(frame, "VerticalFieldOfView");
                Direction = Read<int>(frame, "Direction");
            }
        }

        private readonly struct Projected
        {
            internal readonly float X;
            internal readonly float Y;
            internal readonly float Depth;
            internal Projected(float x, float y, float depth) { X = x; Y = y; Depth = depth; }
        }

        private sealed class View : IGameState
        {
            private readonly List<PlayerView> players = new List<PlayerView>();
            public long Tick { get; set; }
            public int PlayNumber { get; set; }
            public GamePhase Phase { get; set; }
            public TeamId Possession { get; set; }
            public int OffenseDirection { get; set; }
            public int Quarter { get { return 1; } }
            public int QuarterTicksRemaining { get { return 7200; } }
            public int PreSnapTicksRemaining { get { return 300; } }
            public int Down { get { return 1; } }
            public float LineOfScrimmage { get; set; }
            public float FirstDownLine { get { return LineOfScrimmage + OffenseDirection * 30f; } }
            public int HomeScore { get { return 0; } }
            public int AwayScore { get { return 0; } }
            public int ControlledPlayerId { get { return 3; } }
            public PlayId HomePlay { get { return PlayId.QuickPass; } }
            public PlayId AwayPlay { get { return PlayId.Cover; } }
            public PlayOutcome LastOutcome { get; set; }
            public BallView Ball { get; set; }
            public IReadOnlyList<PlayerView> Players { get { return players; } }

            internal View(int direction, float line)
            {
                Phase = GamePhase.PlaySelect;
                OffenseDirection = direction;
                Possession = direction > 0 ? TeamId.Home : TeamId.Away;
                LineOfScrimmage = line;
                for (int id = 0; id < 14; id++)
                {
                    int slot = id % 7;
                    bool offense = id / 7 == (int)Possession;
                    float depth = offense ? (slot == 3 ? -4f : slot < 3 ? 0f : -1.2f) : 12f;
                    float lateral = slot < 3 ? (slot - 1) * 4.2f : slot == 4 ? -14f : slot == 5 ? 14f : slot == 6 ? -6.5f : 0f;
                    players.Add(new PlayerView(id, (TeamId)(id / 7), new FieldVector(line + direction * depth, lateral), default(FieldVector), 1f));
                }
                SetBall(line - direction * 4f, 0f, 3);
            }

            internal static View Isolated(int direction, float x, float z, int slot)
            {
                View state = new View(direction, 50f) { Phase = GamePhase.LiveBall, Tick = 100, PlayNumber = 1 };
                for (int id = 0; id < 14; id++)
                    state.Place(id, x < 50f ? 105f : -5f, 0f);
                state.SetBall(x, z, slot);
                return state;
            }

            internal void Place(int id, float x, float z)
            {
                players[id] = new PlayerView(id, (TeamId)(id / 7), new FieldVector(x, z), default(FieldVector), 1f);
            }

            internal void SetBall(float x, float z, int slot)
            {
                int id = (int)Possession * 7 + slot;
                Place(id, x, z);
                Ball = new BallView(BallMode.Held, new FieldVector(x, z), 1.1f, id, -1);
            }
        }

#if UNITY_EDITOR
        private static void UnityProjectionAndLayout()
        {
            Type layoutType = typeof(IGameState).Assembly.GetType("Blitz.Presentation.HudLayout", true);
            int[,] sizes = { { 1280, 720 }, { 1000, 560 }, { 768, 740 }, { 650, 740 }, { 360, 740 }, { 320, 568 }, { 640, 360 } };
            for (int i = 0; i < sizes.GetLength(0); i++)
            {
                object layout = Activator.CreateInstance(layoutType, Members, null, new object[] { sizes[i, 0], sizes[i, 1] }, null);
                Rect rect = Read<Rect>(layout, "Viewport");
                float height = Read<float>(layout, "Height");
                float width = Read<float>(layout, "Width");
                Check(rect.yMin >= Read<float>(layout, "Footer") / height - 0.00001f
                    && rect.yMax <= 1f - Read<float>(layout, "Header") / height + 0.00001f,
                    "The field viewport must exclude both HUD bands, including short windows.");
                Near(rect.xMax, (width - Read<float>(layout, "Sidebar")) / width, 0.00001f, "The camera must exclude the HUD sidebar.");
            }
            GameObject root = new GameObject("Normal camera regression check") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Type cameraType = typeof(IGameState).Assembly.GetType("Blitz.Presentation.PlayCamera", true);
                object adapter = Activator.CreateInstance(cameraType, Members, null, new object[] { root.transform }, null);
                View state = new View(1, 50f);
                cameraType.GetMethod("Render", Members).Invoke(adapter, new object[] { state, true });
                Camera camera = (Camera)cameraType.GetProperty("Camera", Members).GetValue(adapter, null);
                Check(!camera.orthographic, "The actual Unity camera must use perspective.");
                Frame model = new Rig().Update(state, camera.aspect, 0f, true);
                Near(camera.fieldOfView, model.Fov, 0.0001f, "The runtime adapter must apply the model's projection.");
                Near(camera.transform.position.x, model.Eye.X, 0.0001f, "The runtime adapter must apply model eye X.");
                Near(camera.transform.position.y, model.EyeHeight, 0.0001f, "The runtime adapter must apply model eye height.");
                foreach (Vector3 point in new[] { new Vector3(0f, 2f, 0f), new Vector3(6f, 2f, 3f), new Vector3(-4f, 0f, 0f) })
                {
                    Projected expected = Project(model, point.x, point.y, point.z);
                    Vector3 actual = camera.WorldToViewportPoint(point);
                    Near(actual.x, expected.X, 0.0001f, "Unity's horizontal perspective must agree with the independent pinhole oracle.");
                    Near(actual.y, expected.Y, 0.0001f, "Unity's vertical perspective must agree with the independent pinhole oracle.");
                    Near(actual.z, expected.Depth, 0.0001f, "Unity's eye depth must agree with the model.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
#endif

#if BLITZ_CAMERA_VALIDATION
        private static int Main()
        {
            try { Verify(); return 0; }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }
#endif
    }
}
