using System;
using UnityEngine;

namespace Blitz.Presentation
{
    public static class SourceTeamVisibility
    {
        public static Vector4 ProjectBounds(SourcePlayerRig rig, Camera camera, out bool visible)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            visible = false;
            if (camera == null) return Vector4.zero;
            MeshRenderer[] renderers = rig.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) return Vector4.zero;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            Vector3 min = bounds.min, max = bounds.max;
            float x0 = float.PositiveInfinity, y0 = float.PositiveInfinity;
            float x1 = float.NegativeInfinity, y1 = float.NegativeInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = camera.WorldToViewportPoint(new Vector3((corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z));
                if (point.z < camera.nearClipPlane || point.z > camera.farClipPlane) continue;
                x0 = Mathf.Min(x0, point.x); x1 = Mathf.Max(x1, point.x);
                y0 = Mathf.Min(y0, point.y); y1 = Mathf.Max(y1, point.y);
            }
            if (x1 < 0f || x0 > 1f || y1 < 0f || y0 > 1f || x0 > x1 || y0 > y1) return Vector4.zero;
            Rect viewport = camera.rect;
            visible = true;
            return new Vector4(viewport.x + Mathf.Clamp01(x0) * viewport.width,
                viewport.y + Mathf.Clamp01(y0) * viewport.height,
                viewport.x + Mathf.Clamp01(x1) * viewport.width,
                viewport.y + Mathf.Clamp01(y1) * viewport.height);
        }
    }
}
