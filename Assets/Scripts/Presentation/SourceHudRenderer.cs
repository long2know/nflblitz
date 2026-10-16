using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Runtime;
using UnityEngine;

namespace Blitz.Presentation
{
    public sealed class SourceHudRenderer
    {
        private readonly BlitzRuntime runtime;
        private readonly SourceHudAsset asset;
        private readonly SourceHudComposer composer;
        private readonly SourceHudStatusAnimation status = new SourceHudStatusAnimation();
        private readonly SourceHudEventJournal journal = new SourceHudEventJournal();
        public long ConsumedSourceEvents { get; private set; }
        public event Action<SourceGameEvent> SourceEventConsumed;

        public SourceHudRenderer(BlitzRuntime runtime, SourceHudAsset asset)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.asset = asset ?? throw new ArgumentNullException(nameof(asset));
            composer = new SourceHudComposer(asset.Data);
        }

        public void OnSourceEvent(SourceGameEvent value) { journal.Append(value); }

        public void OnHostStep(IGameState state)
        {
            var source = state as ISourceMatchState;
            if (source == null || source.Match == null)
                throw new SourceInterfaceException("Source HUD consumer received a legacy snapshot.");
            SourceHudState hud = new SourceHudState(state, asset.Data);
            status.Advance(source.SourceTick, hud.ClockWarning);
            journal.Drain(value =>
            {
                Action<SourceGameEvent> consumer = SourceEventConsumed;
                if (consumer != null) consumer(value);
                ConsumedSourceEvents++;
            });
        }

        public void Clear()
        { status.Clear(); journal.Clear(); ConsumedSourceEvents = 0; }

        public void OnGUI()
        {
            SourceHudViewport viewport = SourceHudWebViewport.Measure();
            ISourceMatchState state = runtime.State as ISourceMatchState;
            if (state == null || state.Match == null) throw new SourceInterfaceException("Source HUD cannot infer match state from legacy labels.");
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && !runtime.InputSuspended)
            {
                var layout = new SourceHudLayout(viewport, state.Match.Stations.Count);
                double x = Event.current.mousePosition.x / viewport.CssToGuiX, y = Event.current.mousePosition.y / viewport.CssToGuiY;
                for (int i = 0; i < state.Match.Configuration.Stations.Count; i++)
                {
                    SourceHudRect station = layout.Station(i, state.Match.Stations.Count);
                    if (!station.Contains(x, y)) continue;
                    double third = (x - station.X) / station.Width;
                    runtime.PulseSourceButton(state.Match.Configuration.Stations[i].StationId,
                        third < 1d / 3 ? SourceButtons.A : third < 2d / 3 ? SourceButtons.B : SourceButtons.C);
                    Event.current.Use();
                    return;
                }
            }
            if (Event.current.type != EventType.Repaint) return;
            Draw(composer.Compose(runtime.State, viewport, runtime.SourceSelection, status.RedClock, runtime.InputSuspended), viewport);
            if (state.Match.Phase != SourceMatchPhase.PreSnap && state.Match.Phase != SourceMatchPhase.LiveBall || runtime.GameCamera == null)
                return;
            foreach (SourceStationView station in state.Match.Stations)
            {
                if (station.ControlledPlayerId < 0) continue;
                PlayerView actor = default(PlayerView);
                bool found = false;
                foreach (PlayerView player in state.Players)
                    if (player.Id == station.ControlledPlayerId) { actor = player; found = true; break; }
                if (!found || !actor.Source.IsSpecified || actor.Source.ControllerStationId != station.StationId)
                    throw new SourceInterfaceException("Original indicator requires actual station/controller ownership.");
                Vector3 point = runtime.GameCamera.WorldToScreenPoint(new Vector3(actor.Position.X - 50, actor.Height + 2.3f, actor.Position.Z));
                if (point.z <= 0) continue;
                Draw(composer.Indicator(station.StationId, point.x / viewport.DevicePixelRatio,
                    (viewport.BufferHeight - point.y) / viewport.DevicePixelRatio, viewport, state.Match.Stations.Count), viewport);
            }
        }

        private void Draw(IReadOnlyList<SourceHudPrimitive> primitives, SourceHudViewport viewport)
        {
            GL.PushMatrix();
            bool begun = false;
            string texture = null;
            try
            {
                GL.LoadPixelMatrix(0, (float)(viewport.BufferWidth / viewport.GuiPixelsPerPoint),
                    (float)(viewport.BufferHeight / viewport.GuiPixelsPerPoint), 0);
                foreach (SourceHudPrimitive primitive in primitives)
                {
                    if (texture != primitive.Texture)
                    {
                        if (begun) { GL.End(); begun = false; }
                        texture = primitive.Texture;
                        asset.Material.mainTexture = asset.Texture(texture);
                        if (!asset.Material.SetPass(0))
                            throw new SourceInterfaceException("Original HUD shader pass is unavailable; no generic GUI fallback is drawn.");
                        GL.Begin(GL.TRIANGLES); begun = true;
                    }
                    uint color = primitive.Color;
                    GL.Color(new Color32((byte)(color >> 16), (byte)(color >> 8), (byte)color, (byte)(color >> 24)));
                    foreach (SourceHudVertex vertex in primitive.Vertices)
                    {
                        GL.TexCoord2((float)vertex.S, (float)vertex.T);
                        GL.Vertex3((float)(vertex.X * viewport.CssToGuiX), (float)(vertex.Y * viewport.CssToGuiY), 0);
                    }
                }
            }
            finally
            {
                if (begun) GL.End();
                GL.PopMatrix();
            }
        }
    }
}
