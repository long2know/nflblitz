using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.Presentation
{
    public enum SourceWorldFogOrigin { Unbound, Versus, UserConfiguration }
    public enum SourceWorldUserFogSelection { None, Normal, Thick, Night, Smog }

    public sealed class SourceWorldUserFogConfiguration
    {
        public SourceWorldUserFogSelection Selection { get; }
        public int? SavedFogMask { get; }
        public int? SavedThickFogMask { get; }
        public int? VersusFogMask { get; }
        public int? VersusThickFogMask { get; }
        public uint ColorRgb { get; }
        public float Thickness { get; }
        internal bool FogNonzero { get; }
        internal bool ThickFogNonzero { get; }

        private SourceWorldUserFogConfiguration(SourceWorldUserFogSelection selection, int? savedFog, int? savedThick)
        {
            SourceWorldGuard.Require(selection >= SourceWorldUserFogSelection.None && selection <= SourceWorldUserFogSelection.Smog &&
                (!savedFog.HasValue || savedFog.Value >= 0) && (!savedThick.HasValue || savedThick.Value >= 0),
                "SOURCE_WORLD_FOG_SELECTION: GC_FOG selection must be0..4 and saved source masks must be nonnegative or unknown.");
            Selection = selection; SavedFogMask = savedFog; SavedThickFogMask = savedThick;
            int assignedFog = selection == SourceWorldUserFogSelection.None || selection == SourceWorldUserFogSelection.Thick ? 0 : 1;
            int assignedThick = selection == SourceWorldUserFogSelection.Thick ? 1 : 0;
            // vsscreen.c saves both masks, applies GC_FOG, then ORs the saved power-ups back.
            VersusFogMask = savedFog.HasValue ? assignedFog | savedFog.Value : (int?)null;
            VersusThickFogMask = savedThick.HasValue ? assignedThick | savedThick.Value : (int?)null;
            FogNonzero = assignedFog != 0 || savedFog.HasValue && savedFog.Value != 0;
            ThickFogNonzero = assignedThick != 0 || savedThick.HasValue && savedThick.Value != 0;
            ColorRgb = selection == SourceWorldUserFogSelection.None || selection == SourceWorldUserFogSelection.Night ? 0u :
                selection == SourceWorldUserFogSelection.Smog ? 0x00673c00u : 0x00a0a0a0u;
            Thickness = selection == SourceWorldUserFogSelection.None ? 0f :
                selection == SourceWorldUserFogSelection.Normal ? 1250f :
                selection == SourceWorldUserFogSelection.Thick ? 500f : 700f;
        }

        public static SourceWorldUserFogConfiguration ApplyVersus(SourceWorldUserFogSelection selection,
            int? savedFogMask = null, int? savedThickFogMask = null)
        {
            return new SourceWorldUserFogConfiguration(selection, savedFogMask, savedThickFogMask);
        }

        public SourceWorldFog InitializeAfterFieldProcs(int? fieldSetupIsFoggy = null)
        {
            SourceWorldGuard.Require(!fieldSetupIsFoggy.HasValue ||
                fieldSetupIsFoggy.Value >= 0 && fieldSetupIsFoggy.Value <= 1,
                "SOURCE_WORLD_FOG_INPUT: field_setup.is_foggy must be an actual0|1 source outcome or unknown.");
            return SourceWorldFog.FromUserConfiguration(this, fieldSetupIsFoggy);
        }
    }

    public sealed class SourceWorldFog
    {
        public SourceWorldFogOrigin Origin { get; }
        public bool ConfigurationBound { get { return Origin != SourceWorldFogOrigin.Unbound; } }
        public bool? SourceEnabled { get; }
        public bool Enabled { get { return SourceEnabled == true; } }
        public int? IsFoggy { get; }
        public int? FogMask { get; }
        public int? ThickFogMask { get; }
        public uint ColorRgb { get; }
        public float Start { get { return Enabled ? 1f : 0f; } }
        public float End { get; }
        public const int OnDrawOrder = 0x8000;
        public const int OffDrawOrder = 0x58000;

        private SourceWorldFog(SourceWorldFogOrigin origin, bool? enabled, int? isFoggy,
            int? fogMask, int? thickFogMask, uint color, float end)
        {
            Origin = origin; SourceEnabled = enabled; IsFoggy = isFoggy;
            FogMask = fogMask; ThickFogMask = thickFogMask; ColorRgb = color; End = end;
        }

        public static SourceWorldFog Unbound()
        {
            return new SourceWorldFog(SourceWorldFogOrigin.Unbound, null, null, null, null, 0, 0);
        }

        // field_setup's ordered randrng(100) result must be supplied by its source owner.
        public static SourceWorldFog Versus(int field, int stadium, int fieldSetupIsFoggy, int fogMask, int thickFogMask)
        {
            SourceWorldGuard.Require(field >= 0 && field <= 6 && stadium >= 0 && stadium <= 3 &&
                fieldSetupIsFoggy >= 0 && fieldSetupIsFoggy <= 1 && fogMask >= 0 && thickFogMask >= 0,
                "SOURCE_WORLD_FOG_INPUT: explicit source selectors, field_setup result and power-up operands required.");
            int foggy = thickFogMask != 0 ? 2 : fogMask != 0 ? 1 : fieldSetupIsFoggy;
            if (field == 4) foggy = 1;
            uint color = field == 4 ? 0x00549ec9u : stadium == 1 ? 0x00808080u : 0x00a0a0a0u;
            float end = field == 4 ? 3700f : foggy == 1 ? 1250f : 500f;
            return new SourceWorldFog(SourceWorldFogOrigin.Versus, foggy != 0, foggy,
                fogMask, thickFogMask, foggy == 0 ? 0 : color, foggy == 0 ? 0 : end);
        }

        public static SourceWorldFog User(int fogMask, int thickFogMask, uint fogColor, float fogThickness)
        {
            SourceWorldGuard.Require(fogMask >= 0 && thickFogMask >= 0 && fogColor <= 0xffffff,
                "SOURCE_WORLD_FOG_INPUT: explicit user fog masks and RGB required.");
            bool enabled = fogMask != 0 || thickFogMask != 0;
            SourceWorldGuard.Require(!enabled || SourceWorldGuard.Finite(fogThickness) && fogThickness > 1f,
                "SOURCE_WORLD_FOG_RANGE: enabled user fog requires its actual finite thickness greater than1.");
            return new SourceWorldFog(SourceWorldFogOrigin.UserConfiguration, enabled, enabled ? (int?)1 : null,
                thickFogMask != 0 ? 0 : fogMask, thickFogMask, enabled ? fogColor : 0, enabled ? fogThickness : 0);
        }

        public static SourceWorldFog UserSelection(int selection)
        {
            return SourceWorldUserFogConfiguration.ApplyVersus((SourceWorldUserFogSelection)selection).InitializeAfterFieldProcs();
        }

        internal static SourceWorldFog FromUserConfiguration(SourceWorldUserFogConfiguration configuration, int? fieldSetupIsFoggy)
        {
            bool knownNone = configuration.VersusFogMask == 0 && configuration.VersusThickFogMask == 0;
            bool? enabled = configuration.FogNonzero || configuration.ThickFogNonzero ? true : knownNone ? false : (bool?)null;
            SourceWorldGuard.Require(enabled != true || configuration.Thickness > 1f,
                "SOURCE_WORLD_USER_FOG_RANGE: retained versus power-ups with GC_FOG None request the original1..0 table; portable table equivalence is unqualified.");
            int? fog = configuration.ThickFogNonzero ? 0 :
                configuration.VersusThickFogMask == 0 || configuration.VersusFogMask == 0 ? configuration.VersusFogMask : null;
            return new SourceWorldFog(SourceWorldFogOrigin.UserConfiguration, enabled,
                enabled == true ? 1 : fieldSetupIsFoggy, fog, configuration.VersusThickFogMask,
                configuration.ColorRgb, configuration.Thickness);
        }

        public float PortableFactor(float cameraDepth)
        {
            SourceWorldGuard.Require(SourceWorldGuard.Finite(cameraDepth), "SOURCE_WORLD_FOG_DEPTH: finite camera W required.");
            return !Enabled ? 0 : Math.Max(0f, Math.Min(1f, (cameraDepth - Start) / (End - Start)));
        }
    }

    public static class SourceWorldVenue
    {
        public static string FieldTextureTable(int selector)
        {
            switch (selector)
            {
                case 2: return "astro_turf_field_textures";
                case 3: return "dirt_field_textures";
                case 4: return "snow_field_textures";
                case 5: return "asphalt_field_textures";
                case 6: return "mud_field_textures";
                default: return "field2_textures";
            }
        }

        public static string StadiumTextureTable(int selector)
        {
            return selector == 0 ? "day_stadium_textures" : selector == 2 ? "snow_stadium_textures" : "stadium_textures";
        }

        public static int DrawOrder(string pass)
        {
            switch (pass)
            {
                case "field":
                case "stadium": return 0x10000;
                case "field2":
                case "goalposts": return 0x48000;
                default: throw new InvalidOperationException("SOURCE_WORLD_PASS: unknown original field draw pass: " + pass);
            }
        }

        public static void RequireAdmitted(int field, int stadium, string prefix, int loadedField,
            int loadedStadium, string loadedPrefix)
        {
            SourceWorldGuard.Require(field == loadedField && stadium == loadedStadium && prefix == loadedPrefix,
                "SOURCE_WORLD_VENUE_NOT_ADMITTED: requested field" + field + "/stadium" + stadium + "/" + prefix +
                " differs from world/environment.json field" + loadedField + "/stadium" + loadedStadium + "/" + loadedPrefix +
                "; no alternate texture, end-zone or stadium fallback.");
        }
    }

    public readonly struct SourceShadowLink
    {
        public int FirstJoint { get; }
        public int SecondJoint { get; }
        public float FirstWidth { get; }
        public float SecondWidth { get; }
        public float FirstExtension { get; }
        public float SecondExtension { get; }
        public int TextureSlot { get; }
        internal SourceShadowLink(int first, int second, float firstWidth, float secondWidth,
            float firstExtension, float secondExtension, int texture)
        {
            FirstJoint = first; SecondJoint = second; FirstWidth = firstWidth; SecondWidth = secondWidth;
            FirstExtension = firstExtension; SecondExtension = secondExtension; TextureSlot = texture;
        }
    }

    public sealed class SourcePlayerShadowLink
    {
        public int LinkIndex { get; }
        public int TextureSlot { get; }
        public int DepthBias { get { return 28 - 2 * LinkIndex; } }
        public bool Accepted { get; }
        public ReadOnlyCollection<SourceVector3> CameraVertices { get; }
        public ReadOnlyCollection<SourceVector3> RasterVertices { get; }
        internal SourcePlayerShadowLink(int index, int texture, SourceVector3[] camera,
            SourceVector3[] raster, bool accepted)
        {
            LinkIndex = index; TextureSlot = texture; Accepted = accepted;
            CameraVertices = Array.AsReadOnly(camera); RasterVertices = Array.AsReadOnly(raster);
        }
    }

    public sealed class SourcePlayerShadow
    {
        public int ActorId { get; }
        public int RawFlags { get; }
        public bool OnScreen { get { return (RawFlags & 2) != 0; } }
        public ReadOnlyCollection<SourceVector3> CameraJoints { get; }
        public ReadOnlyCollection<SourceVector3> GroundJoints { get; }
        public ReadOnlyCollection<SourcePlayerShadowLink> Links { get; }
        internal SourcePlayerShadow(int actor, int flags, SourceVector3[] camera, SourceVector3[] ground,
            List<SourcePlayerShadowLink> links)
        {
            ActorId = actor; RawFlags = flags; CameraJoints = Array.AsReadOnly(camera);
            GroundJoints = Array.AsReadOnly(ground); Links = links.AsReadOnly();
        }
    }

    public static class SourcePlayerShadowModel
    {
        public const string PlayerGitBlob = "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c";
        public static ReadOnlyCollection<SourceShadowLink> Linkage { get; } = Array.AsReadOnly(new[] {
            new SourceShadowLink(0, 8, 2.4f, 2.4f, .3f, .8f, 2),
            new SourceShadowLink(2, 5, 1.92f, 1.92f, .3f, .3f, 0),
            new SourceShadowLink(3, 2, 1.44f, 1.56f, .3f, .3f, 1),
            new SourceShadowLink(4, 3, 1.44f, 1.44f, .3f, .3f, 3),
            new SourceShadowLink(6, 5, 1.44f, 1.56f, .3f, .3f, 1),
            new SourceShadowLink(7, 6, 1.44f, 1.44f, .3f, .3f, 3),
            new SourceShadowLink(10, 0, 1.92f, 1.92f, .3f, .3f, 1),
            new SourceShadowLink(11, 10, 1.44f, 1.44f, .3f, .3f, 1),
            new SourceShadowLink(13, 0, 1.92f, 1.92f, .3f, .3f, 1),
            new SourceShadowLink(14, 13, 1.44f, 1.44f, .3f, .3f, 1)
        });
        private static readonly int[] used = { 0, 2, 3, 4, 5, 6, 7, 8, 10, 11, 13, 14 };
        public static SourceVector3 Sun { get { return new SourceVector3(1.54f, -3.07f, -1.44f); } }
        public static ReadOnlyCollection<string> TextureNames { get; } = Array.AsReadOnly(new[] {
            "psshould.wms", "pslimb.wms", "pstorso.wms", "pshand.wms"
        });

        public static SourcePlayerShadow Project(int actor, int rawFlags, IReadOnlyList<SourceVector3> worldJoints,
            SourceCinematicTransform camera, int hres, int vres)
        {
            SourceCameraFrameClock.RequireOriginalRasterAspect(hres, vres);
            SourceWorldGuard.Require(actor >= 0 && actor < 14 && camera != null && worldJoints != null &&
                worldJoints.Count == 15, "SOURCE_WORLD_SHADOW_INPUT: actual actor, camera and fifteen world joints required.");
            var joints = new SourceVector3[15];
            var ground = new SourceVector3[15];
            var links = new List<SourcePlayerShadowLink>();
            for (int i = 0; i < joints.Length; i++)
                joints[i] = ToCamera(worldJoints[i], camera);
            if ((rawFlags & 2) == 0) return new SourcePlayerShadow(actor, rawFlags, joints, ground, links);
            SourceVector3 eye = camera.Position;
            SourceVector3 f = Rotate(new SourceVector3(-eye.X, -eye.Y, -eye.Z), camera);
            SourceVector3 sun = Rotate(Sun, camera);
            var normal = new SourceVector3(camera.Transform[1], camera.Transform[5], camera.Transform[9]);
            foreach (int i in used)
            {
                float t = Dot(normal, Subtract(f, joints[i])) / Sun.Y;
                ground[i] = Add(joints[i], Scale(sun, t));
                RequireFinite(ground[i]);
            }
            for (int i = 0; i < Linkage.Count; i++)
            {
                SourceShadowLink link = Linkage[i];
                SourceVector3 first = ground[link.FirstJoint], second = ground[link.SecondJoint];
                SourceVector3 length = Subtract(second, first), cross = Cross(length, normal);
                float d = Length(length), crossLength = Length(cross);
                // Original reciprocal normalization becomes non-finite and fails trivial acceptance here.
                if (d == 0f || crossLength == 0f)
                {
                    links.Add(new SourcePlayerShadowLink(i, link.TextureSlot,
                        new SourceVector3[0], new SourceVector3[0], false));
                    continue;
                }
                SourceVector3 right = Scale(cross, 1f / crossLength), up = Scale(length, 1f / d);
                var vertices = new[] {
                    Add(Add(first, Scale(right, link.FirstWidth)), Scale(up, -d * link.FirstExtension)),
                    Add(Add(second, Scale(right, link.SecondWidth)), Scale(up, d * link.SecondExtension)),
                    Add(Add(second, Scale(right, -link.SecondWidth)), Scale(up, d * link.SecondExtension)),
                    Add(Add(first, Scale(right, -link.FirstWidth)), Scale(up, -d * link.FirstExtension))
                };
                var raster = new SourceVector3[4];
                bool accepted = true;
                for (int corner = 0; corner < 4; corner++)
                {
                    RequireFinite(vertices[corner]);
                    raster[corner] = Raster(vertices[corner], hres, vres);
                    // draw_player_shadow deliberately tests A/B/C, not D.
                    if (corner < 3)
                        accepted &= raster[corner].X >= -hres && raster[corner].X < hres * 2f &&
                            raster[corner].Y >= -vres && raster[corner].Y < vres * 2f &&
                            raster[corner].Z > 0f && raster[corner].Z <= 1f;
                    if (!SourceWorldGuard.Finite(raster[corner].X) || !SourceWorldGuard.Finite(raster[corner].Y) ||
                        !SourceWorldGuard.Finite(raster[corner].Z)) accepted = false;
                }
                links.Add(new SourcePlayerShadowLink(i, link.TextureSlot, vertices, raster, accepted));
            }
            return new SourcePlayerShadow(actor, rawFlags, joints, ground, links);
        }

        public static SourceVector3 ToCamera(SourceVector3 world, SourceCinematicTransform camera)
        {
            RequireFinite(world);
            SourceVector3 p = Rotate(Subtract(world, camera.Position), camera);
            return new SourceVector3(p.X + camera.Transform[3], p.Y + camera.Transform[7], p.Z + camera.Transform[11]);
        }

        private static SourceVector3 Raster(SourceVector3 camera, int hres, int vres)
        {
            float reciprocal = 1f / camera.Z;
            // GAME.H FOCFAC=390/300; unlike active geom.s, this routine uses665.6 at512.
            float x = -(camera.X * (1.3f * 2f) * reciprocal);
            float y = camera.Y * (1.3f * 2f) * reciprocal;
            x *= hres / 2f;
            y *= hres / 2f * (vres == 256 ? .666667f : 1f);
            x += hres / 2f; y += vres / 2f;
            x += 2097152f; y += 2097152f;
            x -= 2097152f; y -= 2097152f;
            return new SourceVector3(x, y, reciprocal);
        }

        private static SourceVector3 Rotate(SourceVector3 p, SourceCinematicTransform camera)
        {
            var m = camera.Transform;
            return new SourceVector3(p.X * m[0] + p.Y * m[1] + p.Z * m[2],
                p.X * m[4] + p.Y * m[5] + p.Z * m[6], p.X * m[8] + p.Y * m[9] + p.Z * m[10]);
        }
        private static SourceVector3 Add(SourceVector3 a, SourceVector3 b)
        { return new SourceVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        private static SourceVector3 Subtract(SourceVector3 a, SourceVector3 b)
        { return new SourceVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        private static SourceVector3 Scale(SourceVector3 a, float b)
        { return new SourceVector3(a.X * b, a.Y * b, a.Z * b); }
        private static float Dot(SourceVector3 a, SourceVector3 b)
        { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        private static SourceVector3 Cross(SourceVector3 a, SourceVector3 b)
        { return new SourceVector3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
        private static float Length(SourceVector3 p) { return (float)Math.Sqrt(Dot(p, p)); }
        private static void RequireFinite(SourceVector3 p)
        {
            SourceWorldGuard.Require(SourceWorldGuard.Finite(p.X) && SourceWorldGuard.Finite(p.Y) && SourceWorldGuard.Finite(p.Z),
                "SOURCE_WORLD_SHADOW_FINITE: source joints and projected geometry must be finite.");
        }
    }

    internal static class SourceWorldGuard
    {
        internal static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        internal static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
