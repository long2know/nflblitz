using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Presentation;

namespace Blitz.SourcePlayer;

public static class FrameMath
{
    public static double[] Camera(SourceCinematicTransform camera, float width, float height, float far)
    {
        double[] projection = CameraSpace(width, height, far);
        var m = camera.Transform;
        var eye = camera.Position;
        var view = new double[] {
            m[0], m[4], m[8], 0, m[1], m[5], m[9], 0, m[2], m[6], m[10], 0,
            -(m[0] * eye.X + m[1] * eye.Y + m[2] * eye.Z) + m[3],
            -(m[4] * eye.X + m[5] * eye.Y + m[6] * eye.Z) + m[7],
            -(m[8] * eye.X + m[9] * eye.Y + m[10] * eye.Z) + m[11], 1
        };
        return Multiply(projection, view);
    }

    public static double[] CameraSpace(float width, float height, float far)
    {
        SourceCameraFrameClock.RequireOriginalRasterAspect(width, height);
        double near = 0.3 * SourceTiming.SourceUnitsPerYard;
        DataCatalog.Require(far > near && float.IsFinite(far), "Invalid actual source world depth envelope.");
        // The same unclamped active-geom.s projection as the approved FX consumer.
        // Reflect raster X here, never the Core camera, actor, or historical cache.
        return new double[] {
            -2 * NormalCameraModel.ReferenceFocalPixels / width, 0, 0, 0,
            0, 2 * NormalCameraModel.ReferenceFocalPixels * (height == 256 ? 2d / 3d : 1d) / height, 0, 0,
            0, 0, (far + near) / (far - near), 1,
            0, 0, -2 * far * near / (far - near), 0
        };
    }

    public static void Joint(double[] destination, int offset, SourceJointTransform joint,
        SourceVector3 root, float scale)
    {
        // Conjugate the unchanged production Unity-basis pose by C=diag(1,1,-1).
        // This is the same local.Z reflection used by SourceCatchGeometry.CameraJoints.
        SourceRotation q = joint.Rotation;
        double x = -q.X, y = -q.Y, z = q.Z, w = q.W;
        destination[offset] = (1 - 2 * (y * y + z * z)) * scale;
        destination[offset + 1] = 2 * (x * y + z * w) * scale;
        destination[offset + 2] = 2 * (x * z - y * w) * scale;
        destination[offset + 3] = 0;
        destination[offset + 4] = 2 * (x * y - z * w) * scale;
        destination[offset + 5] = (1 - 2 * (x * x + z * z)) * scale;
        destination[offset + 6] = 2 * (y * z + x * w) * scale;
        destination[offset + 7] = 0;
        destination[offset + 8] = 2 * (x * z + y * w) * scale;
        destination[offset + 9] = 2 * (y * z - x * w) * scale;
        destination[offset + 10] = (1 - 2 * (x * x + y * y)) * scale;
        destination[offset + 11] = 0;
        destination[offset + 12] = root.X + joint.Position.X;
        destination[offset + 13] = root.Y + joint.Position.Y;
        destination[offset + 14] = root.Z - joint.Position.Z;
        destination[offset + 15] = 1;
    }

    public static SourceVector3 Root(PlayerView actor) => new(
        (actor.Position.X - 50f) * SourceTiming.SourceUnitsPerYard,
        actor.Height * SourceTiming.SourceUnitsPerYard, actor.Position.Z * SourceTiming.SourceUnitsPerYard);

    public static double[] FreeBall(SourceFreeBallDraw draw)
    {
        var packet = new double[20];
        packet[0] = draw.State.Visible ? 1 : 0;
        packet[1] = draw.State.SourceTick; packet[2] = draw.State.RawFlags; packet[3] = draw.Alpha;
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 4; column++)
                packet[4 + column * 4 + row] = draw.WorldMatrix[row * 4 + column];
        packet[19] = 1;
        return packet;
    }

    public static double[] FreeBalls(SourceFreeBallDraw main, IReadOnlyList<SourceFreeBallDraw> trails)
    {
        var packet = new double[20 * (trails.Count + 1)];
        FreeBall(main).CopyTo(packet, 0);
        for (int i = 0; i < trails.Count; i++)
            FreeBall(trails[i]).CopyTo(packet, 20 * (i + 1));
        return packet;
    }

    public static double[] Multiply(double[] left, double[] right)
    {
        var result = new double[16];
        for (int column = 0; column < 4; column++)
        for (int row = 0; row < 4; row++)
        for (int i = 0; i < 4; i++)
            result[column * 4 + row] += left[i * 4 + row] * right[column * 4 + i];
        return result;
    }
}
