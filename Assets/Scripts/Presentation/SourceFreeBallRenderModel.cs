using System;
using System.Collections.ObjectModel;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.Presentation
{
    public readonly struct SourceFreeBallState
    {
        public bool IsSpecified { get; }
        public long SourceTick { get; }
        public bool Visible { get; }
        public SourceVector3 Position { get; }
        public int Heading { get; }
        public int Elevation { get; }
        public int Roll { get; }
        public float Scale { get; }
        public int RawFlags { get; }
        public int PupBigBall { get; }
        public SourceFreeBallState(long sourceTick, bool visible, SourceVector3 position,
            int heading, int elevation, int roll, float scale, int rawFlags, int pupBigBall)
        {
            if (sourceTick < 0 || !Finite(position.X) || !Finite(position.Y) || !Finite(position.Z) ||
                !Finite(scale) || scale <= 0)
                throw new ArgumentException("Actual finite source ball state and positive source scale required.");
            SourceTick = sourceTick; Visible = visible; Position = position; Heading = heading;
            Elevation = elevation; Roll = roll; Scale = scale; RawFlags = rawFlags; PupBigBall = pupBigBall;
            IsSpecified = true;
        }
        private static bool Finite(float x) { return !float.IsNaN(x) && !float.IsInfinity(x); }
    }

    public sealed class SourceFreeBallDraw
    {
        public SourceFreeBallState State { get; }
        public ReadOnlyCollection<float> WorldMatrix { get; }
        public bool Alternate { get { return (State.RawFlags & 0x40) != 0; } }
        public bool White { get { return (State.RawFlags & 1) != 0; } }
        public int Alpha { get; }
        public int EffectiveAlpha { get { return White ? 255 : Alpha; } }
        public SourceBallTrail? Trail { get; }
        internal SourceFreeBallDraw(SourceFreeBallState state, float[] matrix, int alpha = 255, SourceBallTrail? trail = null)
        { State = state; WorldMatrix = Array.AsReadOnly(matrix); Alpha = alpha; Trail = trail; }

        public float[] CameraMatrix(SourceCinematicTransform camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var relative = new float[12];
            WorldMatrix.CopyTo(relative, 0);
            relative[3] -= camera.Position.X; relative[7] -= camera.Position.Y; relative[11] -= camera.Position.Z;
            var basis = new float[12];
            for (int i = 0; i < basis.Length; i++) basis[i] = camera.Transform[i];
            return SourceFreeBallRenderModel.Multiply(basis, relative);
        }

        public float[] CameraMatrix(SourceFireCameraFrame camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var relative = new float[12]; WorldMatrix.CopyTo(relative, 0);
            relative[3] -= camera.Position.X; relative[7] -= camera.Position.Y; relative[11] -= camera.Position.Z;
            var basis = new float[12]; camera.Transform.CopyTo(basis, 0);
            return SourceFreeBallRenderModel.Multiply(basis, relative);
        }
    }

    public sealed class SourceLooseHelmetDraw
    {
        public SourceLooseHelmet Helmet { get; }
        public SourceFreeBallDraw Transform { get; }
        internal SourceLooseHelmetDraw(SourceLooseHelmet helmet, SourceFreeBallDraw transform)
        { Helmet = helmet; Transform = transform; }
        public string AlphaSource { get { return "CC_ALPHA"; } }
        public string DepthFunction { get { return "LESS"; } }
        public bool DepthWrite { get { return true; } }
        public string Wrap { get { return "WRAP/WRAP"; } }
        public string ColorCombine { get { return Transform.White ? "DECAL_TEXTURE_ADD_CCALPHA/FF" : "TEXTURE_TIMES_ALPHA"; } }
    }

    public static class SourceFreeBallRenderModel
    {
        public static SourceFreeBallDraw Evaluate(SourceFreeBallState state, SourceDirectionMath directions)
        { return Build(state, directions, 255, null); }

        public static SourceLooseHelmetDraw EvaluateHelmet(SourceLooseHelmet helmet, long sourceTick,
            int globalBallFlags, SourceDirectionMath directions)
        {
            SourceFireGuard.Require(helmet.ObjectSerial > 0 && sourceTick >= helmet.EntrySourceTick && helmet.Decal.IsSpecified,
                "An actual detached PHELM object, entry decal and current source frame are required.");
            var state = new SourceFreeBallState(sourceTick, !helmet.Hidden, helmet.Position,
                helmet.Heading, helmet.Elevation, helmet.Roll, helmet.Scale, globalBallFlags & 1, 0);
            return new SourceLooseHelmetDraw(helmet, Build(state, directions, helmet.Alpha, null));
        }

        public static SourceFreeBallDraw EvaluateTrail(SourceBallTrail trail, long sourceTick, int currentCarrier,
            int globalBallFlags, int pupBigBall, SourceDirectionMath directions)
        {
            SourceFireGuard.Require(trail.ObjectSerial > 0 && trail.ProcessSerial > 0,
                "An unspecified football-trail object is not a render fallback.");
            if (sourceTick < trail.EntrySample.SourceTick)
                throw new ArgumentException("A football ghost cannot predate its process-entry copy.");
            SourceFireBallRenderSample entry = trail.EntrySample;
            var state = new SourceFreeBallState(sourceTick,
                !trail.Hidden && (currentCarrier == -1 || (globalBallFlags & 0x800) != 0),
                entry.WorldPosition, entry.Heading, entry.Elevation, entry.Roll, trail.Scale,
                trail.LocalFlags | (globalBallFlags & 1), pupBigBall);
            return Build(state, directions, trail.Alpha, trail);
        }

        private static SourceFreeBallDraw Build(SourceFreeBallState state, SourceDirectionMath directions,
            int alpha, SourceBallTrail? trail)
        {
            if (directions == null) throw new ArgumentNullException(nameof(directions));
            if (!state.IsSpecified) throw new ArgumentException("Unspecified free-ball render state is not a valid resource fallback.");
            // player.c:draw_ball, pipe3D.c:rotx/roty/rotz/mxm. These are source
            // integer-angle table reads, not Euler degrees, velocity guesses or a render clock.
            float sx = directions.Sin(-state.Elevation), cx = directions.Cos(-state.Elevation);
            float sy = directions.Sin(state.Heading), cy = directions.Cos(state.Heading);
            float sz = directions.Sin(state.Roll), cz = directions.Cos(state.Roll);
            float[] x = { 1, 0, 0, 0, 0, cx, -sx, 0, 0, sx, cx, 0 };
            float[] y = { cy, 0, sy, 0, 0, 1, 0, 0, -sy, 0, cy, 0 };
            float[] z = { cz, -sz, 0, 0, sz, cz, 0, 0, 0, 0, 1, 0 };
            float[] matrix = Multiply(Multiply(y, x), z);
            float scale = state.Scale;
            if (state.PupBigBall != 0) scale *= 1.5f;
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++) matrix[row * 4 + column] *= scale;
            matrix[3] = state.Position.X; matrix[7] = state.Position.Y; matrix[11] = state.Position.Z;
            return new SourceFreeBallDraw(state, matrix, alpha, trail);
        }

        public static float[] Multiply(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != 12 || b.Length != 12)
                throw new ArgumentException("Source affine row-major3x4 matrices required.");
            var c = new float[12];
            for (int i = 8; i >= 0; i -= 4)
            {
                c[i] = a[i] * b[0] + a[i + 1] * b[4] + a[i + 2] * b[8];
                c[i + 1] = a[i] * b[1] + a[i + 1] * b[5] + a[i + 2] * b[9];
                c[i + 2] = a[i] * b[2] + a[i + 1] * b[6] + a[i + 2] * b[10];
                c[i + 3] = a[i] * b[3] + a[i + 1] * b[7] + a[i + 2] * b[11] + a[i + 3];
            }
            return c;
        }
    }
}
