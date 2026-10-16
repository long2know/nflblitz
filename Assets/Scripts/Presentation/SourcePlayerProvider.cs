using System;
using Blitz.Contracts;
using UnityEngine;

namespace Blitz.Presentation
{
    public static class SourcePlayerProvider
    {
        public const string LocalPreviewId = "local-plyr266";
        public const string ResourceKey = "BlitzSourcePlayers/" + LocalPreviewId + "/player";
        public const string AnimationResourceKey = "BlitzSourcePlayers/" + LocalPreviewId + "/animation-library";

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
            Debug.Log("Actor mode: local source mesh/textures, continuous original animation; explicit gameplay scripts/frames, no static fallback.");
            return prefab;
        }

        internal static ISourceAnimationLibrary ResolveRequiredAnimationLibrary(SourcePlayerRig sourcePrefab)
        {
            if (sourcePrefab == null) return null;
            SourceAnimationLibraryAsset asset = Resources.Load<SourceAnimationLibraryAsset>(AnimationResourceKey);
            if (asset == null)
                throw new InvalidOperationException("Source actor mode requires the imported original animation library at Resources/" +
                    AnimationResourceKey + ". Run the explicit clip importer; no static pose will be substituted.");
            ISourceAnimationLibrary library = asset.Load();
            Debug.Log("Original animation library loaded; inventory SHA256=" + asset.InventorySha256 +
                "; source-clock samples required. Full gameplay/wall-clock parity is not asserted.");
            return library;
        }

        internal static SourcePlayerRig AttachBindPose(SourcePlayerRig prefab, Transform facing)
        {
            SourcePlayerRig rig = SourcePlayerRig.InstantiateRequired(prefab, facing);
            // The source-camera contract documents 390 archive field units = 50 gameplay yards.
            // Mesh/pivot assets remain in source units; only this presentation root is converted.
            rig.transform.localScale = Vector3.one / NormalCameraModel.ArchiveUnitsPerYard;
            rig.transform.localPosition = Vector3.zero;
            rig.transform.localRotation = Quaternion.identity;
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
