using System;
using System.Collections.Generic;
using Blitz.Contracts;
using Blitz.Runtime;
using UnityEngine;

namespace Blitz.Presentation
{
    [DisallowMultipleComponent]
    public sealed class PlayHud : MonoBehaviour
    {
        private BlitzRuntime runtime;
        private GUIStyle label;
        private GUIStyle paragraph;
        private GUIStyle button;
        private readonly GUIContent textContent = new GUIContent();
        private readonly List<Rect> worldLabels = new List<Rect>();
        private readonly List<string> notices = new List<string>();
        private string toast;
        private float toastUntil;

        internal void Initialize(BlitzRuntime owner)
        {
            runtime = owner;
        }

        internal void ClearNotices()
        {
            notices.Clear();
            toast = null;
        }

        internal void OnGameEvent(GameEvent gameEvent)
        {
            string message = EventText(gameEvent);
            if (string.IsNullOrEmpty(message))
                return;
            notices.Insert(0, message);
            if (notices.Count > 3)
                notices.RemoveAt(3);
            if (gameEvent.Type == GameEventType.PassCaught || gameEvent.Type == GameEventType.FirstDown
                || gameEvent.Type == GameEventType.TurnoverOnDowns || gameEvent.Type == GameEventType.Touchdown
                || gameEvent.Type == GameEventType.Safety || gameEvent.Type == GameEventType.QuarterEnded)
            {
                toast = message;
                toastUntil = Time.unscaledTime + 3f;
            }
        }

        private void OnGUI()
        {
            if (runtime == null || runtime.State == null)
                return;
            EnsureStyles();
            GUIUtility.keyboardControl = 0;
            HudLayout layout = new HudLayout(Screen.width, Screen.height);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(layout.Scale, layout.Scale, 1f));
            IGameState state = runtime.State;
            DrawHeader(layout, state);
            DrawPlayerLabels(layout, state);
            if (layout.Sidebar > 0f)
            {
                Rect side = new Rect(layout.Width - layout.Sidebar + 12f, layout.Header + 12f,
                    layout.Sidebar - 24f, layout.Height - layout.Header - layout.Footer - 24f);
                Panel(side, Palette.Panel);
                DrawContext(new Rect(side.x + 16f, side.y + 14f, side.width - 32f, side.height - 28f), state, false);
                DrawFooter(layout, state);
            }
            else
            {
                Rect bottom = new Rect(0f, layout.Height - layout.Footer, layout.Width, layout.Footer);
                Panel(bottom, Palette.Panel);
                DrawCompactContext(layout, state, bottom);
            }
            DrawFieldOverlay(layout, state);
            if (runtime.InputSuspended)
                DrawPause(layout);
            GUI.matrix = previousMatrix;
            GUI.color = Color.white;
        }

        private void DrawHeader(HudLayout layout, IGameState state)
        {
            float width = layout.Width;
            Panel(new Rect(0f, 0f, width, layout.Header), Palette.Ink);
            Color possessionColor = state.Possession == TeamId.Home ? Palette.Home : Palette.Away;
            Panel(new Rect(0f, layout.Header - 3f, width, 3f), possessionColor);
            if (layout.Narrow)
            {
                Text(new Rect(16f, 8f, 220f, 26f), "NFL BLITZ 2000", 22, Palette.White, true);
                if (Button(new Rect(width - 108f, 8f, 92f, 28f), "R  RESTART", Palette.Muted))
                    runtime.RestartMatch();
                Score(new Rect(16f, 39f, 145f, 52f), "HOME", state.HomeScore, Palette.Home, false);
                Score(new Rect(width - 161f, 39f, 145f, 52f), "AWAY", state.AwayScore, Palette.Away, true);
                Text(new Rect(width / 2f - 57f, 41f, 114f, 26f), Clock(state), 26, Palette.White, true, TextAnchor.MiddleCenter);
                Text(new Rect(width / 2f - 57f, 69f, 114f, 18f), "QUARTER " + state.Quarter + " / 4", 12, Palette.Muted, false, TextAnchor.MiddleCenter);
                Text(new Rect(16f, 101f, width - 32f, 18f), PossessionText(state), 14, possessionColor, true);
                Text(new Rect(16f, 120f, width - 32f, 19f), DownText(state) + "     |     " + Spot(state.LineOfScrimmage), 17, Palette.White, true);
                return;
            }

            Text(new Rect(20f, 12f, 180f, 32f), "NFL BLITZ 2000", 26, Palette.White, true);
            Text(new Rect(21f, 44f, 190f, 18f), "UNITY SOURCE PORT / IN PROGRESS", 11, Palette.Muted);
            float middle = width * 0.5f;
            Score(new Rect(middle - 268f, 9f, 168f, 58f), "HOME", state.HomeScore, Palette.Home, false);
            Score(new Rect(middle + 100f, 9f, 168f, 58f), "AWAY", state.AwayScore, Palette.Away, true);
            Text(new Rect(middle - 90f, 6f, 180f, 38f), Clock(state), 33, Palette.White, true, TextAnchor.MiddleCenter);
            Text(new Rect(middle - 90f, 44f, 180f, 20f), "QUARTER " + state.Quarter + " / 4", 13, Palette.Muted, true, TextAnchor.MiddleCenter);
            if (Button(new Rect(width - 132f, 19f, 112f, 34f), "R  RESTART", Palette.Muted))
                runtime.RestartMatch();
            float statsY = layout.Header - 32f;
            Panel(new Rect(0f, statsY - 3f, width, 32f), new Color32(20, 45, 52, 255));
            Text(new Rect(20f, statsY, width * 0.43f, 25f), PossessionText(state), 16, possessionColor, true);
            Text(new Rect(width * 0.44f, statsY, width * 0.3f, 25f), DownText(state), 18, Palette.White, true);
            Text(new Rect(width - 204f, statsY, 184f, 25f), Spot(state.LineOfScrimmage), 15, Palette.Muted, false, TextAnchor.MiddleRight);
        }

        private void DrawContext(Rect rect, IGameState state, bool compact)
        {
            float x = rect.x;
            float y = rect.y;
            float width = rect.width;
            Text(new Rect(x, y, width, 20f), PhaseText(state.Phase), 13, Palette.Gold, true);
            Text(new Rect(x, y + 26f, width, 38f), ContextHeading(state), compact ? 23 : 27, Palette.White, true);
            Paragraph(new Rect(x, y + 70f, width, 52f), ContextDescription(state), 15, Palette.Muted);
            float actionY = y + 132f;
            switch (state.Phase)
            {
                case GamePhase.PlaySelect:
                    PlayCard(new Rect(x, actionY, width, 70f), 1, state);
                    PlayCard(new Rect(x, actionY + 80f, width, 70f), 2, state);
                    break;
                case GamePhase.PreSnap:
                    Text(new Rect(x, actionY, width, 46f),
                        (state.PreSnapTicksRemaining / (float)SliceRules.TicksPerSecond).ToString("0.0") + "s", 38, Palette.Gold, true);
                    if (state.Possession == TeamId.Home)
                    {
                        if (Button(new Rect(x, actionY + 57f, width, 49f), "SPACE   SNAP", Palette.Home))
                            runtime.PrimaryAction();
                    }
                    else
                        Text(new Rect(x, actionY + 57f, width, 30f), "AWAY AUTO-SNAPS", 18, Palette.Away, true);
                    break;
                case GamePhase.LiveBall:
                    DrawLiveActions(new Rect(x, actionY, width, 132f), state, false);
                    break;
                case GamePhase.DeadBall:
                    if (Button(new Rect(x, actionY + 5f, width, 50f), "SPACE   NEXT PLAY", Palette.Home))
                        runtime.PrimaryAction();
                    Paragraph(new Rect(x, actionY + 69f, width, 52f), "The lines now mark the NEXT scrimmage. Choose another play to line up.", 14, Palette.Muted);
                    break;
                case GamePhase.GameOver:
                    if (Button(new Rect(x, actionY + 5f, width, 50f), "R   PLAY AGAIN", Palette.Home))
                        runtime.RestartMatch();
                    Text(new Rect(x, actionY + 71f, width, 28f), "FOUR QUARTERS COMPLETE", 14, Palette.Gold, true);
                    break;
            }
            float infoY = Mathf.Max(actionY + 168f, rect.yMax - 142f);
            if (infoY + 140f <= rect.yMax + 8f)
            {
                DrawTurbo(new Rect(x, infoY, width, 49f), state);
                Text(new Rect(x, infoY + 60f, width, 18f), "LATEST ON THE FIELD", 11, Palette.Muted, true);
                for (int i = 0; i < notices.Count && i < 3; i++)
                    Text(new Rect(x, infoY + 81f + i * 18f, width, 18f), notices[i], 12, i == 0 ? Palette.White : Palette.Muted);
                if (notices.Count == 0)
                    Paragraph(new Rect(x, infoY + 81f, width, 38f), "Four downs. Thirty yards.\nMake the next play count.", 13, Palette.Muted);
            }
        }

        private void DrawCompactContext(HudLayout layout, IGameState state, Rect rect)
        {
            float margin = 16f;
            float y = rect.y + 10f;
            float width = rect.width - margin * 2f;
            bool narrow = layout.Narrow;
            Text(new Rect(margin, y, width, 25f), ContextHeading(state), narrow ? 23 : 22, Palette.White, true);
            float descriptionWidth = narrow ? width : width * 0.53f;
            Paragraph(new Rect(margin, y + 30f, descriptionWidth, narrow ? 39f : 37f), ContextDescription(state), 14, Palette.Muted);
            Rect actions = narrow
                ? new Rect(margin, y + 76f, width, 58f)
                : new Rect(layout.Width * 0.57f, y + 4f, layout.Width * 0.43f - margin, 68f);
            switch (state.Phase)
            {
                case GamePhase.PlaySelect:
                    float cardWidth = (actions.width - 10f) / 2f;
                    if (Button(new Rect(actions.x, actions.y, cardWidth, 54f), state.Possession == TeamId.Home ? "1  RUN" : "1  COVER", Palette.Home))
                        runtime.SelectPlay(1);
                    if (Button(new Rect(actions.x + cardWidth + 10f, actions.y, cardWidth, 54f), state.Possession == TeamId.Home ? "2  QUICK PASS" : "2  BLITZ", Palette.Gold))
                        runtime.SelectPlay(2);
                    break;
                case GamePhase.PreSnap:
                    string countdown = (state.PreSnapTicksRemaining / (float)SliceRules.TicksPerSecond).ToString("0.0") + "s";
                    if (state.Possession == TeamId.Home)
                    {
                        if (Button(new Rect(actions.x, actions.y, actions.width, 50f), "SPACE  SNAP  /  AUTO " + countdown, Palette.Home))
                            runtime.PrimaryAction();
                    }
                    else
                        Text(new Rect(actions.x, actions.y, actions.width, 48f), "AWAY SNAPS IN " + countdown, 18, Palette.Away, true);
                    break;
                case GamePhase.LiveBall:
                    DrawLiveActions(actions, state, true);
                    break;
                case GamePhase.DeadBall:
                    if (Button(new Rect(actions.x, actions.y, actions.width, 50f), "SPACE  NEXT PLAY", Palette.Home))
                        runtime.PrimaryAction();
                    break;
                case GamePhase.GameOver:
                    if (Button(new Rect(actions.x, actions.y, actions.width, 50f), "R  PLAY AGAIN", Palette.Home))
                        runtime.RestartMatch();
                    break;
            }
            float bottomY = narrow ? y + 142f : y + 86f;
            DrawTurbo(new Rect(margin, bottomY, narrow ? width * 0.45f : width * 0.26f, 40f), state);
            Rect mini = new Rect(narrow ? layout.Width * 0.53f : layout.Width * 0.32f, bottomY + 1f,
                narrow ? width * 0.45f : width * 0.3f, 40f);
            DrawMiniField(mini, state);
            if (!narrow)
                Paragraph(new Rect(layout.Width * 0.66f, bottomY, layout.Width * 0.34f - margin, 48f),
                    "WASD / arrows move   SHIFT turbo\n1 / 2 / 3 pass   F tackle   Q switch", 13, Palette.Muted);
            if (narrow)
            {
                Text(new Rect(margin, y + 195f, width, 19f), "WASD / arrows move   SHIFT turbo   F tackle   Q switch", 12, Palette.White);
                Text(new Rect(margin, y + 217f, width, 18f), "W = +X field   D = +Z field   R restart", 12, Palette.Muted);
            }
            else
                Text(new Rect(margin, rect.yMax - 21f, width, 18f), "W = +X field   D = +Z field   |   Space snap / next   |   R restart", 12, Palette.Muted);
        }

        private void DrawLiveActions(Rect rect, IGameState state, bool horizontal)
        {
            if (state.Possession == TeamId.Away)
            {
                float gap = 8f;
                float width = horizontal ? (rect.width - gap) / 2f : rect.width;
                if (Button(new Rect(rect.x, rect.y, width, 46f), "F   TACKLE", Palette.Away))
                    runtime.QueueAction(InputAction.Tackle);
                if (Button(new Rect(horizontal ? rect.x + width + gap : rect.x, horizontal ? rect.y : rect.y + 56f, width, 46f), "Q   SWITCH", Palette.Home))
                    runtime.QueueAction(InputAction.SwitchDefender);
                return;
            }
            bool canPass = state.Ball.Mode == BallMode.Held && state.Ball.CarrierId == 3
                && state.Ball.Position.X <= state.LineOfScrimmage;
            if (canPass)
            {
                float width = (rect.width - 12f) / 3f;
                for (int target = 0; target < 3; target++)
                {
                    if (Button(new Rect(rect.x + target * (width + 6f), rect.y, width, horizontal ? 46f : 59f),
                        (target + 1) + (horizontal ? "  PASS" : "\nWR " + (target + 1)), Palette.Home))
                        runtime.QueueAction(InputAction.Pass, targetPlayerId: target + 4);
                }
                if (!horizontal)
                    Paragraph(new Rect(rect.x, rect.y + 76f, rect.width, 42f), "Target numbers match the field markers. One forward pass per play.", 14, Palette.Muted);
            }
            else
            {
                string message = state.Ball.Mode == BallMode.InFlight ? "PASS IN FLIGHT" : "RUN INTO SPACE";
                Text(new Rect(rect.x, rect.y, rect.width, 32f), message, horizontal ? 17 : 22, Palette.Home, true);
                if (!horizontal)
                    Paragraph(new Rect(rect.x, rect.y + 43f, rect.width, 52f), "WASD / arrows to run.\nHold SHIFT for a turbo burst.", 15, Palette.White);
            }
        }

        private void DrawFooter(HudLayout layout, IGameState state)
        {
            float y = layout.Height - layout.Footer;
            Panel(new Rect(0f, y, layout.Width, layout.Footer), Palette.Ink);
            Text(new Rect(20f, y + 11f, 620f, 23f), "WASD / ARROWS   MOVE       SHIFT   TURBO", 16, Palette.White, true);
            Text(new Rect(20f, y + 36f, 650f, 20f), "1 / 2 choose   SPACE snap / next   1 / 2 / 3 pass   F tackle   Q switch", 13, Palette.Muted);
            Text(new Rect(20f, y + 59f, 650f, 18f), "W = +X field   D = +Z field    |    Cyan ring = YOU    Gold line = FIRST DOWN", 11, Palette.Muted);
            Rect mini = new Rect(layout.Width - 360f, y + 13f, 335f, 46f);
            DrawMiniField(mini, state);
            Text(new Rect(mini.x, mini.yMax + 5f, mini.width, 16f), "HOME 0    >    30-YARD SERIES    <    100 AWAY", 11, Palette.Muted, false, TextAnchor.MiddleCenter);
        }

        private void DrawPlayerLabels(HudLayout layout, IGameState state)
        {
            worldLabels.Clear();
            Camera camera = runtime.GameCamera;
            if (camera == null)
                return;
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerView player = state.Players[i];
                bool controlled = player.Id == state.ControlledPlayerId;
                bool eligible = state.Possession == TeamId.Home && player.Id >= 4 && player.Id <= 6
                    && (state.Phase == GamePhase.LiveBall || state.Phase == GamePhase.PreSnap);
                bool carrier = state.Ball.Mode == BallMode.Held && state.Ball.CarrierId == player.Id;
                bool target = state.Ball.Mode == BallMode.InFlight && state.Ball.TargetPlayerId == player.Id;
                if (!controlled && !eligible && !carrier && !target)
                    continue;
                string text = controlled ? "YOU / " + Role(player.Id, state) : Role(player.Id, state);
                if (eligible)
                    text = (controlled ? "YOU / " : "") + (player.Id - 3) + "  WR" + (player.Id - 3);
                if (carrier)
                    text += " / BALL";
                if (target)
                    text += " / TARGET";
                Color accent = target ? Palette.Gold : player.Team == TeamId.Home ? Palette.Home : Palette.Away;
                WorldLabel(camera, layout, FieldScene.Ground(player.Position) + Vector3.up * 3.1f, text, accent, 14);
            }
            WorldLabel(camera, layout, new Vector3(state.LineOfScrimmage - 50f, 0.12f, -SliceRules.FieldHalfWidthYards - 0.8f), "SCRIMMAGE", Palette.Home, 11);
            WorldLabel(camera, layout, new Vector3(state.FirstDownLine - 50f, 0.12f, -SliceRules.FieldHalfWidthYards - 0.8f), "FIRST DOWN", Palette.Gold, 11);
        }

        private void WorldLabel(Camera camera, HudLayout layout, Vector3 position, string text, Color accent, int size)
        {
            Vector3 screen = camera.WorldToScreenPoint(position);
            if (screen.z <= 0f || !camera.pixelRect.Contains(new Vector2(screen.x, screen.y)))
                return;
            Rect scene = new Rect(0f, layout.Header, layout.Width - layout.Sidebar, layout.Height - layout.Header - layout.Footer);
            Vector2 anchor = new Vector2(screen.x / layout.Scale, (Screen.height - screen.y) / layout.Scale);
            Rect rect;
            if (!TryWorldLabelRect(scene, anchor, text, size, out rect))
                return;
            Panel(rect, new Color(0.025f, 0.075f, 0.105f, 0.93f));
            Panel(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), accent);
            Text(new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height - 1f), text, size, accent, true, TextAnchor.MiddleCenter);
        }

        private bool TryWorldLabelRect(Rect scene, Vector2 anchor, string text, int size, out Rect rect)
        {
            rect = default(Rect);
            if (scene.width < 80f || scene.height < 32f)
                return false;
            label.fontSize = size;
            label.fontStyle = FontStyle.Bold;
            textContent.text = text;
            float width = Mathf.Clamp(Mathf.Ceil(label.CalcSize(textContent).x) + 10f, 72f, Mathf.Min(240f, scene.width - 8f));
            Rect candidate = new Rect(Mathf.Clamp(anchor.x - width / 2f, scene.x + 4f, scene.xMax - width - 4f),
                Mathf.Clamp(anchor.y - 11f, scene.y + 4f, scene.yMax - 28f), width, 24f);
            if (!FitText(label, new Rect(0f, 0f, width - 8f, 23f), text, size))
                return false;
            float initialY = candidate.y;
            for (int attempt = 0; attempt < 7; attempt++)
            {
                float offset = attempt == 0 ? 0f : ((attempt + 1) / 2) * 27f * (attempt % 2 == 1 ? -1f : 1f);
                candidate.y = Mathf.Clamp(initialY + offset, scene.y + 4f, scene.yMax - 28f);
                bool overlap = false;
                for (int i = 0; i < worldLabels.Count; i++)
                {
                    Rect occupied = worldLabels[i];
                    occupied.yMin -= 2f;
                    occupied.yMax += 2f;
                    if (candidate.Overlaps(occupied))
                    {
                        overlap = true;
                        break;
                    }
                }
                if (overlap)
                    continue;
                rect = candidate;
                worldLabels.Add(rect);
                return true;
            }
            return false;
        }

        private void DrawFieldOverlay(HudLayout layout, IGameState state)
        {
            float width = layout.Width - layout.Sidebar;
            Text(new Rect(16f, layout.Header + 10f, width - 32f, 21f),
                state.Possession == TeamId.Home ? "HOME ATTACKS +X  >>>" : "<<<  AWAY ATTACKS -X / YOU DEFEND", 12,
                state.Possession == TeamId.Home ? Palette.Home : Palette.Away, true);
            if (state.Phase == GamePhase.DeadBall)
                Text(new Rect(16f, layout.Header + 35f, width - 32f, 20f), "PLAY OVER / LINES SHOW NEXT SCRIMMAGE", 11, Palette.Gold, true);
            else if (!string.IsNullOrEmpty(toast) && Time.unscaledTime < toastUntil)
            {
                float toastWidth = Mathf.Min(width - 32f, 380f);
                Rect rect = new Rect((width - toastWidth) / 2f, layout.Header + 36f, toastWidth, 36f);
                Panel(rect, Palette.Ink);
                Text(rect, toast, 18, Palette.Gold, true, TextAnchor.MiddleCenter);
            }
        }

        private void DrawPause(HudLayout layout)
        {
            float width = Mathf.Min(440f, layout.Width - 32f);
            Rect rect = new Rect((layout.Width - layout.Sidebar - width) / 2f,
                layout.Header + (layout.Height - layout.Header - layout.Footer) / 2f - 52f, width, 104f);
            Panel(rect, Palette.Ink);
            Text(new Rect(rect.x + 12f, rect.y + 13f, rect.width - 24f, 30f), "PAUSED / GAME UNFOCUSED", 22, Palette.Gold, true, TextAnchor.MiddleCenter);
            Paragraph(new Rect(rect.x + 16f, rect.y + 52f, rect.width - 32f, 40f), "Click the game to return. Release held keys.\nHidden-tab time is not match time.", 14, Palette.White, TextAnchor.MiddleCenter);
        }

        private void DrawTurbo(Rect rect, IGameState state)
        {
            float turbo = 0f;
            if (state.ControlledPlayerId >= 0 && state.ControlledPlayerId < state.Players.Count)
                turbo = Mathf.Clamp01(state.Players[state.ControlledPlayerId].Turbo01);
            Text(new Rect(rect.x, rect.y, rect.width, 18f), "YOU: " + Role(state.ControlledPlayerId, state) + " / TURBO", 12, Palette.Home, true);
            Panel(new Rect(rect.x, rect.y + 25f, rect.width, 9f), Palette.Ink);
            Panel(new Rect(rect.x, rect.y + 25f, rect.width * turbo, 9f), turbo > 0.2f ? Palette.Home : Palette.Gold);
        }

        private void DrawMiniField(Rect rect, IGameState state)
        {
            Panel(rect, new Color32(29, 77, 59, 255));
            Panel(new Rect(rect.x, rect.y, rect.width / 12f, rect.height), new Color32(23, 96, 100, 255));
            Panel(new Rect(rect.xMax - rect.width / 12f, rect.y, rect.width / 12f, rect.height), new Color32(121, 67, 41, 255));
            for (int yard = 0; yard <= 100; yard += 10)
                Panel(new Rect(rect.x + (yard + 10f) / 120f * rect.width, rect.y, 1f, rect.height), new Color(0.7f, 0.85f, 0.72f, 0.3f));
            Panel(new Rect(rect.x + (state.LineOfScrimmage + 10f) / 120f * rect.width, rect.y, 2f, rect.height), Palette.Home);
            Panel(new Rect(rect.x + (state.FirstDownLine + 10f) / 120f * rect.width, rect.y, 2f, rect.height), Palette.Gold);
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerView player = state.Players[i];
                float size = player.Id == state.ControlledPlayerId ? 5f : 3f;
                Vector2 point = MiniPoint(rect, player.Position);
                Panel(new Rect(point.x - size / 2f, point.y - size / 2f, size, size), player.Team == TeamId.Home ? Palette.Home : Palette.Away);
            }
            Vector2 ball = MiniPoint(rect, state.Ball.Position);
            Panel(new Rect(ball.x - 2.5f, ball.y - 2.5f, 5f, 5f), Palette.Gold);
        }

        private static Vector2 MiniPoint(Rect rect, FieldVector position)
        {
            return new Vector2(rect.x + Mathf.Clamp01((position.X + 10f) / 120f) * rect.width,
                rect.y + (1f - Mathf.Clamp01((position.Z + SliceRules.FieldHalfWidthYards) / (SliceRules.FieldHalfWidthYards * 2f))) * rect.height);
        }

        private void PlayCard(Rect rect, int choice, IGameState state)
        {
            bool offense = state.Possession == TeamId.Home;
            string name = offense ? choice == 1 ? "RUN" : "QUICK PASS" : choice == 1 ? "COVER" : "BLITZ";
            string description = offense ? choice == 1 ? "Carry it. Find an open lane." : "Three receivers. Pick your target."
                : choice == 1 ? "Guard the first-down target." : "Pressure the ball carrier.";
            Color accent = choice == 1 ? Palette.Home : Palette.Gold;
            if (Button(rect, "", accent))
                runtime.SelectPlay(choice);
            Text(new Rect(rect.x + 13f, rect.y + 7f, rect.width - 26f, 27f), choice + "   " + name, 21, accent, true);
            Text(new Rect(rect.x + 13f, rect.y + 38f, rect.width - 26f, 21f), description, 12, Palette.Muted);
        }

        private void Score(Rect rect, string team, int score, Color color, bool right)
        {
            Panel(new Rect(rect.x, rect.y + 5f, 3f, rect.height - 10f), color);
            Text(new Rect(rect.x + 12f, rect.y, rect.width - 20f, 21f), team, 13, color, true,
                right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            Text(new Rect(rect.x + 12f, rect.y + 18f, rect.width - 20f, rect.height - 18f), score.ToString("00"), 36, Palette.White, true,
                right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
        }

        private bool Button(Rect rect, string text, Color accent)
        {
            Panel(rect, new Color32(28, 55, 67, 255));
            Panel(new Rect(rect.x, rect.y, 3f, rect.height), accent);
            button.normal.textColor = accent;
            button.hover.textColor = Palette.White;
            button.active.textColor = Palette.Ink;
            FitText(button, rect, text, rect.width < 100f ? 14 : 17);
            bool clicked = GUI.Button(rect, text, button);
            if (clicked)
            {
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
            }
            return clicked;
        }

        private void EnsureStyles()
        {
            if (label != null)
                return;
            CreateStyles(GUI.skin);
        }

        private void CreateStyles(GUISkin skin)
        {
            Font font = skin.font != null ? skin.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label = new GUIStyle(skin.label)
            {
                font = skin.label.font != null ? skin.label.font : font,
                richText = false, wordWrap = false, padding = new RectOffset(0, 0, 0, 0), clipping = TextClipping.Clip
            };
            paragraph = new GUIStyle(label) { wordWrap = true };
            button = new GUIStyle(skin.button)
            {
                font = skin.button.font != null ? skin.button.font : font,
                richText = false, wordWrap = false, clipping = TextClipping.Clip,
                fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(7, 7, 3, 3)
            };
            button.normal.background = null;
            button.hover.background = null;
            button.active.background = null;
            button.focused.background = null;
        }

        private void Text(Rect rect, string text, int size, Color color, bool bold = false, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.normal.textColor = color;
            label.alignment = alignment;
            FitText(label, rect, text, size);
            GUI.Label(rect, text, label);
        }

        private void Paragraph(Rect rect, string text, int size, Color color, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            paragraph.normal.textColor = color;
            paragraph.alignment = alignment;
            FitText(paragraph, rect, text, size);
            GUI.Label(rect, text, paragraph);
        }

        private bool FitText(GUIStyle style, Rect rect, string text, int preferredSize)
        {
            textContent.text = text;
            int minimumSize = Mathf.Min(10, preferredSize);
            for (int size = preferredSize; size >= minimumSize; size--)
            {
                style.fontSize = size;
                Vector2 measured = style.wordWrap
                    ? new Vector2(rect.width - 1f, style.CalcHeight(textContent, rect.width - 1f))
                    : style.CalcSize(textContent);
                if (measured.x <= rect.width - 1f && measured.y <= rect.height - 1f)
                    return true;
            }
            return false;
        }

        private static void Panel(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private static string Clock(IGameState state)
        {
            int seconds = (state.QuarterTicksRemaining + SliceRules.TicksPerSecond - 1) / SliceRules.TicksPerSecond;
            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }

        private static string DownText(IGameState state)
        {
            string[] names = { "", "1st", "2nd", "3rd", "4th" };
            int down = Mathf.Clamp(state.Down, 1, 4);
            int yards = Mathf.CeilToInt(Mathf.Abs(state.FirstDownLine - state.LineOfScrimmage));
            bool goal = state.FirstDownLine <= 0f || state.FirstDownLine >= 100f;
            return names[down] + " & " + (goal ? "GOAL (" + yards + ")" : yards.ToString());
        }

        private static string PossessionText(IGameState state)
        {
            return state.Possession == TeamId.Home ? "HOME BALL  >>>  YOU PLAY OFFENSE" : "<<<  AWAY BALL  /  YOU PLAY DEFENSE";
        }

        private static string Spot(float x)
        {
            if (Mathf.Abs(x - 50f) < 0.1f)
                return "AT MIDFIELD";
            return "AT " + (x < 50f ? "HOME " + Mathf.RoundToInt(x) : "AWAY " + Mathf.RoundToInt(100f - x));
        }

        private static string Role(int id, IGameState state)
        {
            if (id < 0)
                return "PLAYER";
            if ((id < 7 ? TeamId.Home : TeamId.Away) != state.Possession)
                return "DEF " + (id % 7 + 1);
            int slot = id % 7;
            return slot == 3 ? "QB" : slot >= 4 ? "WR" + (slot - 3) : "BLOCK " + (slot + 1);
        }

        private static string PhaseText(GamePhase phase)
        {
            switch (phase)
            {
                case GamePhase.PlaySelect: return "PLAY SELECT";
                case GamePhase.PreSnap: return "PRE-SNAP";
                case GamePhase.LiveBall: return "LIVE BALL";
                case GamePhase.DeadBall: return "DEAD BALL";
                default: return "FINAL";
            }
        }

        private static string ContextHeading(IGameState state)
        {
            switch (state.Phase)
            {
                case GamePhase.PlaySelect: return state.Possession == TeamId.Home ? "CALL YOUR PLAY" : "SET YOUR DEFENSE";
                case GamePhase.PreSnap: return state.Possession == TeamId.Home ? "READY TO SNAP" : "READ THE OFFENSE";
                case GamePhase.LiveBall:
                    return state.Possession == TeamId.Home ? state.Ball.Mode == BallMode.InFlight ? "FIND THE CATCH" : "MAKE YOUR MOVE" : "STOP THE DRIVE";
                case GamePhase.DeadBall: return OutcomeText(state.LastOutcome);
                default: return state.HomeScore == state.AwayScore ? "TIE GAME" : state.HomeScore > state.AwayScore ? "HOME WINS" : "AWAY WINS";
            }
        }

        private static string ContextDescription(IGameState state)
        {
            switch (state.Phase)
            {
                case GamePhase.PlaySelect:
                    return state.Possession == TeamId.Home ? "Four downs to gain thirty yards. Choose 1 or 2 to line up." : "You control Home in cyan. Choose a scheme; Away will snap automatically.";
                case GamePhase.PreSnap:
                    return "HOME: " + PlayName(state.HomePlay) + " / AWAY: " + PlayName(state.AwayPlay)
                        + (state.Possession == TeamId.Home ? ". SPACE snaps now, or wait for auto-snap." : ". Move after the snap; Q switches defender, F tackles.");
                case GamePhase.LiveBall:
                    return state.Possession == TeamId.Home ? "Follow your cyan ring. Run with WASD; SHIFT spends turbo. Pass from behind the line." : "Close the gap with WASD / SHIFT. F tackles in reach. Q selects another Home defender.";
                case GamePhase.DeadBall:
                    return "NEXT: " + (state.Possession == TeamId.Home ? "HOME" : "AWAY") + " / " + DownText(state) + " / " + Spot(state.LineOfScrimmage) + ". SPACE continues.";
                default:
                    return "HOME " + state.HomeScore + "  /  AWAY " + state.AwayScore + ". Press R for a fresh four-quarter match.";
            }
        }

        private static string PlayName(PlayId play)
        {
            return play == PlayId.QuickPass ? "QUICK PASS" : play.ToString().ToUpperInvariant();
        }

        private static string OutcomeText(PlayOutcome outcome)
        {
            switch (outcome)
            {
                case PlayOutcome.Touchdown: return "TOUCHDOWN +6";
                case PlayOutcome.Safety: return "SAFETY +2";
                case PlayOutcome.FirstDown: return "FIRST DOWN";
                case PlayOutcome.TurnoverOnDowns: return "TURNOVER ON DOWNS";
                case PlayOutcome.Incomplete: return "INCOMPLETE";
                case PlayOutcome.OutOfBounds: return "OUT OF BOUNDS";
                case PlayOutcome.Tackled: return "TACKLED";
                default: return "PLAY OVER";
            }
        }

        private static string EventText(GameEvent gameEvent)
        {
            switch (gameEvent.Type)
            {
                case GameEventType.Snapped: return "Snap / play is live";
                case GameEventType.PassThrown: return "Forward pass thrown";
                case GameEventType.PassCaught: return "CAUGHT / " + (gameEvent.Team == TeamId.Home ? "HOME" : "AWAY");
                case GameEventType.Tackled: return "Carrier tackled";
                case GameEventType.OutOfBounds: return "Out of bounds";
                case GameEventType.Incomplete: return "Pass incomplete";
                case GameEventType.FirstDown: return "FIRST DOWN / new 30-yard series";
                case GameEventType.TurnoverOnDowns: return "TURNOVER ON DOWNS";
                case GameEventType.PossessionChanged: return (gameEvent.Team == TeamId.Home ? "HOME" : "AWAY") + " takes possession";
                case GameEventType.Touchdown: return (gameEvent.Team == TeamId.Home ? "HOME" : "AWAY") + " TOUCHDOWN +6";
                case GameEventType.Safety: return (gameEvent.Team == TeamId.Home ? "HOME" : "AWAY") + " SAFETY +2";
                case GameEventType.QuarterEnded: return "QUARTER " + gameEvent.Value + " COMPLETE";
                case GameEventType.GameEnded: return "FINAL / match complete";
                default: return null;
            }
        }
    }
}
