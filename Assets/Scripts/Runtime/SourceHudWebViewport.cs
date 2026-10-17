using System.Runtime.InteropServices;
using Blitz.Presentation;
using UnityEngine;

namespace Blitz.Runtime
{
    public static class SourceHudWebViewport
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern double SourceCabinetViewport(int metric);
#endif
        public static SourceHudViewport Measure()
        {
            Vector2 origin = GUIUtility.GUIToScreenPoint(Vector2.zero);
            Vector2 point = GUIUtility.GUIToScreenPoint(Vector2.right);
            double points = Mathf.Abs(point.x - origin.x);
#if UNITY_WEBGL && !UNITY_EDITOR
            return new SourceHudViewport((int)SourceCabinetViewport(2), (int)SourceCabinetViewport(3),
                SourceCabinetViewport(0), SourceCabinetViewport(1), SourceCabinetViewport(4), points);
#else
            return new SourceHudViewport(Screen.width, Screen.height, Screen.width / points, Screen.height / points, points, points);
#endif
        }
    }
}
