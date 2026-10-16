using System;
using Blitz.Contracts;

namespace Blitz.Presentation
{
    public static class SourceCinematicDepthRange
    {
        // Unity needs a finite depth plane. The source's angular/near zone test is not a far-distance cutoff.
        public static float RequiredFarPlane(SourceVector3 eye, SourceVector3 minimum, SourceVector3 maximum,
            float existingMinimumFar = 230f, float roundingMargin = 1f)
        {
            SourceCinematicMath.RequireFinite(eye); SourceCinematicMath.RequireFinite(minimum);
            SourceCinematicMath.RequireFinite(maximum); SourceCinematicMath.RequireFinite(existingMinimumFar);
            SourceCinematicMath.RequireFinite(roundingMargin);
            if (minimum.X > maximum.X || minimum.Y > maximum.Y || minimum.Z > maximum.Z ||
                existingMinimumFar <= 0f || roundingMargin < 0f)
                throw new ArgumentOutOfRangeException(nameof(minimum));
            float x = Math.Max(Math.Abs(eye.X - minimum.X), Math.Abs(eye.X - maximum.X));
            float y = Math.Max(Math.Abs(eye.Y - minimum.Y), Math.Abs(eye.Y - maximum.Y));
            float z = Math.Max(Math.Abs(eye.Z - minimum.Z), Math.Abs(eye.Z - maximum.Z));
            return Math.Max(existingMinimumFar, (float)Math.Sqrt((double)x * x + (double)y * y + (double)z * z) + roundingMargin);
        }
    }
}
