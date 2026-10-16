using Blitz.Contracts;
using UnityEngine;

namespace Blitz.Presentation
{
    internal readonly struct HudLayout
    {
        internal readonly float Scale;
        internal readonly float Width;
        internal readonly float Height;
        internal readonly bool Narrow;
        internal readonly float Header;
        internal readonly float Footer;
        internal readonly float Sidebar;
        internal readonly Rect Viewport;

        internal HudLayout(int pixelWidth, int pixelHeight)
        {
            Scale = pixelWidth < 650 ? Mathf.Max(0.5f, pixelWidth / 480f) : Mathf.Clamp(pixelWidth / 1280f, 0.82f, 1.4f);
            Width = pixelWidth / Scale;
            Height = pixelHeight / Scale;
            Narrow = Width < 650f;
            Sidebar = pixelWidth >= 1000 && pixelHeight >= 520 ? 300f : 0f;
            Header = Narrow ? 142f : 104f;
            Footer = Sidebar > 0f ? 88f : Narrow ? 252f : 184f;
            if (Height < 500f)
            {
                Header = 90f;
                Footer = 126f;
            }
            float sceneHeight = Mathf.Max(1f, Height - Header - Footer);
            Viewport = new Rect(0f, Footer / Height, (Width - Sidebar) / Width, sceneHeight / Height);
        }
    }

    internal sealed class PlayCamera
    {
        private readonly Camera camera;
        // Yard-space envelope of FieldScene's interim primitive actor/held ball, not an original mesh measurement.
        private readonly NormalCameraModel model = new NormalCameraModel(2.35f, 1.05f);
        private bool wasFocused;

        internal Camera Camera { get { return camera; } }

        internal PlayCamera(Transform parent)
        {
            camera = new GameObject("Play camera").AddComponent<Camera>();
            camera.transform.SetParent(parent, false);
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Palette.Ink;
            camera.orthographic = false;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 230f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
        }

        internal void Render(IGameState state, bool immediate)
        {
            HudLayout layout = new HudLayout(Screen.width, Screen.height);
            camera.rect = layout.Viewport;
            float aspect = Mathf.Max(0.01f, camera.pixelWidth / (float)Mathf.Max(1, camera.pixelHeight));
            bool focused = Application.isFocused;
            NormalCameraFrame frame = model.Update(state, aspect, focused ? Time.unscaledDeltaTime : 0f,
                immediate || (focused && !wasFocused));
            wasFocused = focused;
            camera.aspect = frame.Aspect;
            camera.fieldOfView = frame.VerticalFieldOfView;
            camera.transform.position = new Vector3(frame.Eye.X, frame.EyeHeight, frame.Eye.Z);
            camera.transform.LookAt(new Vector3(frame.Focus.X, NormalCameraModel.FocusHeightYards, frame.Focus.Z));
        }
    }
}
