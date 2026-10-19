using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.AssetImport;
using Blitz.Contracts;

namespace Blitz.Presentation
{
    public readonly struct SourceFireRgba
    {
        public float R { get; }
        public float G { get; }
        public float B { get; }
        public float A { get; }
        public SourceFireRgba(float r, float g, float b, float a)
        { SourceFireGuard.Finite(r); SourceFireGuard.Finite(g); SourceFireGuard.Finite(b); SourceFireGuard.Finite(a);
            R = r; G = g; B = b; A = a; }
    }

    public sealed class SourceFireMaterialState
    {
        public string AlphaSource { get { return "TEXTURE_ALPHA"; } }
        public string TextureCombine { get { return "DECAL"; } }
        public string ColorCombine { get { return "TEXTURE_TIMES_ALPHA"; } }
        public string RequestedSourceDepth { get { return "WBUFFER"; } }
        public string DepthFunction { get { return "ALWAYS"; } }
        public bool DepthWrite { get { return true; } }
        public string AlphaTest { get { return "ALWAYS"; } }
        public bool ChromaKey { get { return false; } }
        public string BlendSourceRgb { get { return "SRC_ALPHA"; } }
        public string BlendDestinationRgb { get { return "ONE_MINUS_SRC_ALPHA"; } }
        public string BlendSourceAlpha { get { return "SRC_ALPHA"; } }
        public string BlendDestinationAlpha { get { return "ONE_MINUS_SRC_ALPHA"; } }
        public string Wrap { get { return "WRAP/WRAP"; } }
        public string Filter { get { return "BILINEAR/BILINEAR"; } }
        public SourceFireCull InheritedCull { get; }
        public string DepthOrigin { get; }
        public SourceFireMaterialState(SourceFireRenderBasis basis)
        { if (basis == null) throw new ArgumentNullException(nameof(basis)); InheritedCull = basis.InheritedCull; DepthOrigin = basis.DepthOrigin; }
        public static SourceFireRgba Combine(SourceFireRgba texel)
        { return new SourceFireRgba(texel.R * texel.A, texel.G * texel.A, texel.B * texel.A, texel.A); }
    }

    public readonly struct SourceFirePlayerFlashState
    {
        public bool White { get; }
        public string OverrideColorCombine { get { return White ? "DECAL_TEXTURE_ADD_CCALPHA" : null; } }
        public uint ConstantColor { get { return 0xc0000000; } }
        public string RestoreColorCombine { get { return "TEXTURE_TIMES_ALPHA"; } }
        public uint RestoreConstantColor { get { return 0xff000000; } }
        public SourceFirePlayerFlashState(int actualRawFlags) { White = (actualRawFlags & SourceFireIds.FlashWhite) != 0; }
    }

    public sealed class SourceFireDrawPacket
    {
        public long ObjectSerial { get; }
        public int TextureFrame { get; }
        public SourceFireTextureKind TextureKind { get; }
        public int TextureSlot { get; }
        public int IntegerAspect { get; }
        public string TextureSourceSha256 { get; }
        public ReadOnlyCollection<SourceVector3> CameraVertices { get; }
        public ReadOnlyCollection<SourceVector3> ProjectedRasterVertices { get; }
        public ReadOnlyCollection<float> UnityUvs { get; }
        public ReadOnlyCollection<float> GlideSt { get; }
        public SourceFireMaterialState State { get; }
        public int DrawOrder { get { return SourceFireIds.PuffDrawOrder; } }
        internal SourceFireDrawPacket(SourceFireParticle particle, SourceFireTextureFrame texture, SourceVector3[] camera,
            SourceVector3[] raster, float[] uvs, float[] glideSt, SourceFireMaterialState state)
        {
            ObjectSerial = particle.ObjectSerial; TextureFrame = particle.TextureFrame; IntegerAspect = particle.IntegerAspect;
            TextureKind = particle.TextureKind; TextureSlot = particle.TextureSlot;
            TextureSourceSha256 = texture.SourceSha256; CameraVertices = Array.AsReadOnly(camera);
            ProjectedRasterVertices = Array.AsReadOnly(raster); UnityUvs = Array.AsReadOnly(uvs); GlideSt = Array.AsReadOnly(glideSt); State = state;
        }
    }

    // Consumed after source work. Not calling Consume on a skipped host frame holds these exact packets.
    public sealed class SourceFireSpfxFrameConsumer
    {
        private readonly SourceFireSpfxBundle data;
        public SourceFireRenderFrame LastFrame { get; private set; }
        public ReadOnlyCollection<SourceFireDrawPacket> Draws { get; private set; }
        public SourceFireSpfxFrameConsumer(SourceFireSpfxBundle data)
        { this.data = data ?? throw new ArgumentNullException(nameof(data)); Draws = new List<SourceFireDrawPacket>().AsReadOnly(); }

        public ReadOnlyCollection<SourceFireDrawPacket> Consume(SourceFireRenderFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            SourceFireGuard.Require(frame.AssetManifestSha256 == data.ManifestSha256,
                "The source process frame must bind this exact authenticated portable asset generation.");
            if (ReferenceEquals(frame, LastFrame)) return Draws;
            SourceFireGuard.Require(LastFrame == null || frame.SourceTick > LastFrame.SourceTick,
                "Presentation accepts immutable increasing after-step frames, never ages/replays effects in Update.");
            var packets = new List<SourceFireDrawPacket>();
            foreach (SourceFireParticle particle in frame.Particles)
            {
                SourceFireTextureFrame texture = data.Texture(particle.TextureKind, particle.TextureFrame);
                SourceFireGuard.Require(particle.AssetSet == (particle.TextureKind == SourceFireTextureKind.Dust ?
                    SourceFireIds.DustAssetSet : SourceFireIds.PortableAssetSet) &&
                    particle.IntegerAspect == texture.IntegerAspect, "Fire/dust frame/data-set aspect mismatch.");
                if (particle.Hidden) continue;
                var camera = new SourceVector3[6]; var raster = new SourceVector3[6];
                var uvs = new float[12]; var glideSt = new float[12];
                SourceVector3 translation = CameraTranslation(particle.WorldPosition, frame.Basis.Camera);
                float sf, tf; AspectScales(particle.IntegerAspect, out sf, out tf);
                for (int i = 0; i < 6; i++)
                {
                    SourceVector3 p = data.Geometry.Positions[data.Geometry.VertexIndices[i]];
                    // draw_spfx's fixed camera-space cm, not a joint-attached or actor-facing billboard.
                    camera[i] = new SourceVector3(p.Y + translation.X, p.X + translation.Y, -p.Z + translation.Z);
                    raster[i] = ProjectContinuous(camera[i], frame.Basis.VerticalResolution);
                    int uv = data.Geometry.UvIndices[i] * 2;
                    float s = data.Geometry.RawAuthoringUvs[uv], t = data.Geometry.RawAuthoringUvs[uv + 1];
                    glideSt[2 * i] = s * sf; glideSt[2 * i + 1] = (1f - t) * tf;
                    uvs[2 * i] = glideSt[2 * i] / sf; uvs[2 * i + 1] = 1f - glideSt[2 * i + 1] / tf;
                }
                packets.Add(new SourceFireDrawPacket(particle, texture, camera, raster,
                    uvs, glideSt, new SourceFireMaterialState(frame.Basis)));
            }
            LastFrame = frame; Draws = packets.AsReadOnly(); return Draws;
        }

        public static SourceVector3 CameraTranslation(SourceVector3 world, SourceFireCameraFrame camera)
        {
            SourceVector3 c = camera.Position; ReadOnlyCollection<float> m = camera.Transform;
            float x = world.X - c.X, y = world.Y - c.Y, z = world.Z - c.Z;
            return new SourceVector3(x * m[0] + y * m[1] + z * m[2],
                x * m[4] + y * m[5] + z * m[6], x * m[8] + y * m[9] + z * m[10]);
        }

        public static SourceVector3 ProjectContinuous(SourceVector3 camera, int verticalResolution)
        {
            SourceFireGuard.Require(camera.Z != 0 && (verticalResolution == 256 || verticalResolution == 384),
                "Portable continuous projection cannot represent reciprocal zero or an undeclared DIP raster.");
            float reciprocal = 1f / camera.Z;
            float yScale = verticalResolution == 256 ? 2f / 3f : 1f;
            // Active geom.s nmadd.s negates accumulated camera X before perspective.
            return new SourceVector3(256f - camera.X * (664f * reciprocal),
                verticalResolution * .5f + camera.Y * yScale * (664f * reciprocal), reciprocal);
        }

        public static void AspectScales(int aspect, out float s, out float t)
        {
            SourceFireGuard.Require(aspect >= 0 && aspect <= 6, "Pinned Glide integer aspect index required.");
            s = aspect <= 3 ? 256f : 256f / (1 << (aspect - 3));
            t = aspect >= 3 ? 256f : 256f / (1 << (3 - aspect));
        }
    }
}
