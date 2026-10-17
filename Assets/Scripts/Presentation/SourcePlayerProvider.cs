using System;
using Blitz.AssetImport;
using Blitz.Contracts;
using UnityEngine;

namespace Blitz.Presentation
{
    public static class SourcePlayerProvider
    {
        public const string LocalPreviewId = "local-plyr266";
        public const string ResourceKey = "BlitzSourcePlayers/" + LocalPreviewId + "/player";
        public const string AnimationResourceKey = "BlitzSourcePlayers/" + LocalPreviewId + "/animation-library";
        public const string TeamPresentationResourceKey = "BlitzSourcePlayers/original-teams/team-presentation";
        public const string TeamAnimationResourceKey = "BlitzSourcePlayers/original-teams/animation-library";
        public const string TeamImpactResourceKey = "BlitzSourcePlayers/original-teams/impact-library";
        public const string TeamAnimationManifestSha256 = "f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274";

        public static SourceTeamPresentationAsset ResolveRequiredTeamPresentation()
        {
            SourceTeamPresentationAsset asset = Resources.Load<SourceTeamPresentationAsset>(TeamPresentationResourceKey);
            if (asset == null)
                throw new InvalidOperationException("Original opposing-team presentation requires Resources/" +
                    TeamPresentationResourceKey + ". Run the explicit original team importer; fourteen matching uniforms are not a fallback.");
            asset.Load();
            return asset;
        }

        public static SourceCinematicLibrary ResolveRequiredCinematicLibrary()
        {
            SourceCinematicLibraryAsset asset = Resources.Load<SourceCinematicLibraryAsset>(TeamImpactResourceKey);
            if (asset == null)
                throw new InvalidOperationException("Original impact camera/effect catalog is missing at Resources/" +
                    TeamImpactResourceKey + "; no generic tackle presentation is substituted.");
            return asset.Load();
        }

        internal static SourcePlayerRig ResolveActorMode()
        {
            string mode = RequestedMode(Application.absoluteURL);
            if (mode == "graybox")
            {
                Debug.Log("Actor mode: explicitly selected procedural graybox.");
                return null;
            }
            SourceTeamPresentationAsset teams = Resources.Load<SourceTeamPresentationAsset>(TeamPresentationResourceKey);
            if (teams == null)
            {
                if (mode == "source" || Resources.Load<GameObject>(ResourceKey) != null)
                    throw new InvalidOperationException("Original opposing-team assets are missing at Resources/" +
                        TeamPresentationResourceKey + ". The old single Chicago/PC_WHITE preview is not an opposing-team fallback.");
                Debug.Log("Actor mode: procedural graybox (no local source prefab installed). Use ?actors=source to require source assets or ?actors=graybox explicitly.");
                return null;
            }
            SourcePlayerRig prefab = teams.Template;
            prefab.ValidateGeneratedRig();
            Debug.Log("Actor mode: original opposing-team uniforms/models/BCD numbers/rosters; explicit source draw state and117-clip library required.");
            return prefab;
        }

        internal static ISourceAnimationLibrary ResolveRequiredAnimationLibrary(SourcePlayerRig sourcePrefab)
        {
            if (sourcePrefab == null) return null;
            string key = sourcePrefab.StableId == "original-teams" ? TeamAnimationResourceKey : AnimationResourceKey;
            SourceAnimationLibraryAsset asset = Resources.Load<SourceAnimationLibraryAsset>(key);
            if (asset == null)
                throw new InvalidOperationException("Source actor mode requires the imported original animation library at Resources/" +
                    key + ". Run the explicit clip importer; no static pose will be substituted.");
            if (sourcePrefab.StableId == "original-teams" && asset.InventorySha256 != TeamAnimationManifestSha256)
                throw new InvalidOperationException("Original team runtime requires the final immutable117-clip manifest, not the old56 library or an aliased inventory pin.");
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
