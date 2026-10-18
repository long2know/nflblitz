using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blitz.Presentation
{
    public sealed class SourceFreeBallRenderer : MonoBehaviour
    {
        private Camera target;
        private CommandBuffer commands;
        private SourceFreeBallAsset assets;
        private Material primary, alternate;
        private float verticalResolution;

        public void Initialize(SourceFreeBallAsset asset, Camera actualSourceCamera, float sourceVerticalResolution)
        {
            if (asset == null || actualSourceCamera == null || commands != null)
                throw new InvalidOperationException("Bind the actual original free-ball asset/source camera once.");
            SourceCameraFrameClock.RequireOriginalRasterAspect(512, sourceVerticalResolution);
            asset.Load(); assets = asset; target = actualSourceCamera; verticalResolution = sourceVerticalResolution;
            primary = asset.CreateMaterial(false); alternate = asset.CreateMaterial(true);
            commands = new CommandBuffer { name = "Original free ball: medball2 / immutable ball_proc rotation" };
            target.AddCommandBuffer(CameraEvent.AfterForwardOpaque, commands);
        }

        public void Present(SourceFreeBallDraw draw, SourceCinematicTransform sourceCamera)
        {
            if (commands == null || draw == null)
                throw new InvalidOperationException("Initialize the original free-ball renderer and bind its actual source state.");
            commands.Clear();
            if (!draw.State.Visible) return;
            float[] source = draw.CameraMatrix(sourceCamera);
            Matrix4x4 matrix = Matrix4x4.identity;
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 4; column++) matrix[row, column] = source[row * 4 + column];
            var properties = new MaterialPropertyBlock();
            properties.SetMatrix("_BallSourceMatrix", matrix);
            properties.SetVector("_SourceRaster", new Vector4(512, verticalResolution, 664,
                verticalResolution == 256 ? 2f / 3f : 1f));
            properties.SetFloat("_BallWhite", draw.White ? 1 : 0);
            properties.SetFloat("_BallAlpha", draw.Alpha / 255f);
            commands.DrawMesh(assets.Mesh, Matrix4x4.identity, draw.Alternate ? alternate : primary, 0, 0, properties);
        }

        private void OnDestroy()
        {
            if (commands != null)
            {
                if (target != null) target.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, commands);
                commands.Release(); commands = null;
            }
            if (primary != null) Destroy(primary);
            if (alternate != null) Destroy(alternate);
        }
    }
}
