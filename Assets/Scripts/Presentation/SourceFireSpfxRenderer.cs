using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blitz.Presentation
{
    public sealed class SourceFireSpfxRenderer : MonoBehaviour
    {
        private Camera target;
        private CommandBuffer commands;
        private SourceFireSpfxFrameConsumer consumer;
        private SourceFireSpfxBundle data;
        private Texture2D[] textures;
        private Material[] materials;
        private readonly Dictionary<long, Mesh> meshes = new Dictionary<long, Mesh>();
        public SourceFireSpfxFrameConsumer Consumer { get { return consumer; } }

        public void Initialize(SourceFireSpfxAsset asset, Camera actualSourceCamera)
        {
            SourceFireGuard.Require(asset != null && actualSourceCamera != null && commands == null,
                "Bind the actual authenticated fire asset and camera once; no implicit default asset/camera.");
            data = asset.Load(); target = actualSourceCamera; consumer = new SourceFireSpfxFrameConsumer(data);
            SourceFireGuard.Require(asset.SourceShader.isSupported, "Exact source-fire shader unsupported on this graphics backend; no fallback material.");
            textures = new Texture2D[17]; materials = new Material[17];
            for (int i = 0; i < 17; i++)
            {
                SourceFireTextureFrame original = data.Frames[i];
                textures[i] = new Texture2D(original.Width, original.Height, TextureFormat.RGBA32, false, true)
                { name = original.SourcePath, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 0 };
                textures[i].LoadRawTextureData(original.CopyRgba()); textures[i].Apply(false, true);
                materials[i] = new Material(asset.SourceShader) { name = "Original TRAIL frame" + (i + 1), mainTexture = textures[i] };
            }
            commands = new CommandBuffer { name = "Source fire: ordered immutable57Hz authoring-input objects (portable)" };
            target.AddCommandBuffer(CameraEvent.AfterForwardAlpha, commands);
        }

        public void ConsumeAfterSourceStep(SourceFireRenderFrame frame)
        {
            SourceFireGuard.Require(consumer != null && commands != null, "Initialize the actual fire renderer before installing the Source57 host.");
            if (ReferenceEquals(frame, consumer.LastFrame)) return;
            IReadOnlyList<SourceFireDrawPacket> draws = consumer.Consume(frame);
            commands.Clear();
            var alive = new HashSet<long>();
            var raster = new Vector4(512f, frame.Basis.VerticalResolution, 664f,
                frame.Basis.VerticalResolution == 256 ? 2f / 3f : 1f);
            foreach (SourceFireDrawPacket draw in draws)
            {
                alive.Add(draw.ObjectSerial);
                Mesh mesh;
                if (!meshes.TryGetValue(draw.ObjectSerial, out mesh))
                {
                    mesh = new Mesh { name = "Original SDL puff logical object" + draw.ObjectSerial };
                    mesh.MarkDynamic(); meshes.Add(draw.ObjectSerial, mesh);
                }
                var vertices = new Vector3[6]; var uvs = new Vector2[6]; var triangles = new int[6];
                for (int i = 0; i < 6; i++)
                {
                    SourceVector3 p = draw.CameraVertices[i]; vertices[i] = new Vector3(p.X, p.Y, p.Z);
                    uvs[i] = new Vector2(draw.UnityUvs[2 * i], draw.UnityUvs[2 * i + 1]); triangles[i] = i;
                }
                mesh.Clear(); mesh.vertices = vertices; mesh.uv = uvs; mesh.triangles = triangles;
                Material material = materials[draw.TextureFrame];
                material.SetInt("_Cull", (int)draw.State.InheritedCull);
                var properties = new MaterialPropertyBlock(); properties.SetVector("_SourceRaster", raster);
                commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0, properties);
            }
            var retired = new List<long>();
            foreach (KeyValuePair<long, Mesh> pair in meshes) if (!alive.Contains(pair.Key)) retired.Add(pair.Key);
            foreach (long serial in retired) { Destroy(meshes[serial]); meshes.Remove(serial); }
        }

        public void ResetSourceEpoch()
        {
            SourceFireGuard.Require(commands != null && data != null, "Initialize before resetting the source effect epoch.");
            commands.Clear();
            foreach (Mesh mesh in meshes.Values) Destroy(mesh);
            meshes.Clear();
            consumer = new SourceFireSpfxFrameConsumer(data);
        }

        // No Update/LateUpdate clock, animation, RNG, pose sampling, or synthetic audio.
        private void OnDestroy()
        {
            if (commands != null)
            { if (target != null) target.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, commands); commands.Release(); commands = null; }
            foreach (Mesh mesh in meshes.Values) Destroy(mesh);
            meshes.Clear();
            if (materials != null) foreach (Material material in materials) if (material != null) Destroy(material);
            if (textures != null) foreach (Texture2D texture in textures) if (texture != null) Destroy(texture);
        }
    }
}
