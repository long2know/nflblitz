using System;
using UnityEngine;

namespace Blitz.Presentation
{
    internal static class SourcePlayerProvider
    {
        internal const string LocalPreviewId = "local-plyr266";
        internal const string ResourceKey = "BlitzSourcePlayers/" + LocalPreviewId + "/player";

        internal static SourcePlayerRig ResolveActorMode()
        {
            string mode = RequestedMode(Application.absoluteURL);
            if (mode == "graybox")
            {
                Debug.Log("Actor mode: explicitly selected procedural graybox.");
                return null;
            }
            GameObject asset = Resources.Load<GameObject>(ResourceKey);
            if (asset == null)
            {
                if (mode == "source")
                    throw new InvalidOperationException("Source actor mode requested, but authorized local player prefab is missing at Resources/" +
                        ResourceKey + ". No graybox substitute was created.");
                Debug.Log("Actor mode: procedural graybox (no local source prefab installed). Use ?actors=source to require source assets or ?actors=graybox explicitly.");
                return null;
            }
            SourcePlayerRig prefab = asset.GetComponent<SourcePlayerRig>();
            if (prefab == null) throw new InvalidOperationException("Local source player prefab has no generated SourcePlayerRig; refusing primitive substitution.");
            prefab.ValidateGeneratedRig();
            if (prefab.HasStaticSourcePose)
                Debug.Log("Actor mode: local source mesh/textures, " + prefab.PoseSequence + " frame" + prefab.PoseSourceFrame +
                    " source pose (static; no original continuous animation; not verified pre-snap).");
            else
                Debug.Log("Actor mode: local source mesh/textures, authoring bind pose only; NOT standing or original animation.");
            return prefab;
        }

        internal static SourcePlayerRig AttachBindPose(SourcePlayerRig prefab, Transform facing)
        {
            SourcePlayerRig rig = SourcePlayerRig.InstantiateRequired(prefab, facing);
            // The source-camera contract documents 390 archive field units = 50 gameplay yards.
            // Mesh/pivot assets remain in source units; only this presentation root is converted.
            rig.transform.localScale = Vector3.one / NormalCameraModel.ArchiveUnitsPerYard;
            if (rig.HasStaticSourcePose) return rig;
            MeshRenderer[] renderers = rig.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("Source player prefab contains no renderers.");
            float ground = float.PositiveInfinity;
            foreach (MeshRenderer renderer in renderers)
                if (renderer.enabled) ground = Mathf.Min(ground, renderer.bounds.min.y);
            if (float.IsInfinity(ground)) throw new InvalidOperationException("Source player has no enabled mesh; refusing graybox substitution.");
            Vector3 correction = facing.InverseTransformVector(Vector3.up * (facing.position.y - ground));
            rig.transform.localPosition += correction;
            return rig;
        }

        private static string RequestedMode(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            int start = url.IndexOf('?');
            if (start < 0) return "";
            string query = url.Substring(start + 1).Split('#')[0];
            string result = "";
            foreach (string pair in query.Split('&'))
            {
                string[] parts = pair.Split('=');
                if (parts.Length == 0 || parts[0] != "actors") continue;
                if (result.Length > 0 || parts.Length != 2 || (parts[1] != "source" && parts[1] != "graybox"))
                    throw new InvalidOperationException("Visual actor mode must be a single ?actors=source or ?actors=graybox parameter.");
                result = parts[1];
            }
            return result;
        }
    }
}
