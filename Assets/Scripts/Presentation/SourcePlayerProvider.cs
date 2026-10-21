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
        public const string MovementTeamPresentationResourceKey = "BlitzSourcePlayers/original-teams-movement157/team-presentation";
        public const string MovementAnimationResourceKey = SourceTeamResourceProfile.MovementAnimationKey;
        public const string MovementImpactResourceKey = "BlitzSourcePlayers/original-teams-movement157/impact-library";
        public const string MovementAnimationManifestSha256 = SourceTeamResourceProfile.MovementAnimationSha256;
        public const string ContactTeamPresentationResourceKey = "BlitzSourcePlayers/original-teams-contact272/team-presentation";
        public const string ContactAnimationResourceKey = SourceTeamResourceProfile.ContactAnimationKey;
        public const string ContactImpactResourceKey = "BlitzSourcePlayers/original-teams-contact272/impact-library";
        public const string ContactAnimationManifestSha256 = SourceTeamResourceProfile.ContactAnimationSha256;
        public const string C2TeamPresentationResourceKey = "BlitzSourcePlayers/original-teams-contact281-c2/team-presentation";
        public const string C2AnimationResourceKey = SourceTeamResourceProfile.C2AnimationKey;
        public const string C2ImpactResourceKey = "BlitzSourcePlayers/original-teams-contact281-c2/impact-library";

        public static SourceTeamPresentationAsset ResolveRequiredTeamPresentation()
        { return ResolveRequiredTeamPresentation(SourceTeamResourceProfile.B1Final117); }

        public static SourceTeamPresentationAsset ResolveRequiredTeamPresentation(SourcePlayerRig sourcePrefab)
        {
            if (sourcePrefab == null) throw new ArgumentNullException(nameof(sourcePrefab));
            return ResolveRequiredTeamPresentation(SourceTeamResourceProfile.FromStableId(sourcePrefab.StableId));
        }

        private static SourceTeamPresentationAsset ResolveRequiredTeamPresentation(SourceTeamResourceProfile profile)
        {
            SourceTeamPresentationAsset asset = Resources.Load<SourceTeamPresentationAsset>(profile.TeamResourceKey);
            if (asset == null)
                throw new InvalidOperationException("Original opposing-team presentation requires Resources/" +
                    profile.TeamResourceKey + ". Run the explicit matching team importer; fourteen matching uniforms are not a fallback.");
            asset.Load();
            if (asset.Template.StableId != profile.StableId)
                throw new InvalidOperationException("Original team asset/template profile identity differs; no117/157 alias.");
            return asset;
        }

        public static SourceCinematicLibrary ResolveRequiredCinematicLibrary()
        { return ResolveRequiredCinematicLibrary(null); }

        public static SourceCinematicLibrary ResolveRequiredCinematicLibrary(SourcePlayerRig sourcePrefab)
        {
            SourceTeamResourceProfile profile = sourcePrefab == null ? SourceTeamResourceProfile.B1Final117 :
                SourceTeamResourceProfile.FromStableId(sourcePrefab.StableId);
            SourceCinematicLibraryAsset asset = Resources.Load<SourceCinematicLibraryAsset>(profile.ImpactResourceKey);
            if (asset == null)
                throw new InvalidOperationException("Original impact camera/effect catalog is missing at Resources/" +
                    profile.ImpactResourceKey + "; no generic tackle presentation is substituted.");
            SourceCinematicLibrary library = asset.Load();
            profile.RequireImpact(library);
            return library;
        }

        public static SourceNormalCameraData ResolveRequiredNormalCameraData(SourcePlayerRig sourcePrefab)
        {
            if (sourcePrefab == null || !SourceTeamResourceProfile.FromStableId(sourcePrefab.StableId).UsesM1Services)
                throw new InvalidOperationException("Original CAMBOT normal requires the explicit M1 source profile.");
            SourceTeamResourceProfile profile = SourceTeamResourceProfile.FromStableId(sourcePrefab.StableId);
            SourceCinematicLibraryAsset asset = Resources.Load<SourceCinematicLibraryAsset>(profile.ImpactResourceKey);
            if (asset == null) throw new InvalidOperationException("The original M1 camera resource is missing.");
            profile.RequireImpact(asset.Load());
            return asset.LoadOriginalNormal();
        }

        internal static SourcePlayerRig ResolveActorMode()
        { return ResolveActorMode(false); }

        internal static SourcePlayerRig ResolveActorMode(bool movement157)
        {
            SourceTeamResourceProfile profile = movement157 ? SourceTeamResourceProfile.Movement157 : SourceTeamResourceProfile.B1Final117;
            return ResolveActorMode(profile);
        }

        internal static SourcePlayerRig ResolveActorMode(SourceTeamResourceProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            string mode = RequestedMode(Application.absoluteURL);
            if (mode == "graybox")
            {
                Debug.Log("Actor mode: explicitly selected procedural graybox.");
                return null;
            }
            SourceTeamPresentationAsset teams = Resources.Load<SourceTeamPresentationAsset>(profile.TeamResourceKey);
            if (teams == null)
            {
                if (mode == "source" || Resources.Load<GameObject>(ResourceKey) != null)
                    throw new InvalidOperationException("Original opposing-team assets are missing at Resources/" +
                        profile.TeamResourceKey + ". The old single Chicago/PC_WHITE preview or another animation profile is not a fallback.");
                Debug.Log("Actor mode: procedural graybox (no local source prefab installed). Use ?actors=source to require source assets or ?actors=graybox explicitly.");
                return null;
            }
            SourcePlayerRig prefab = teams.Template;
            prefab.ValidateGeneratedRig();
            if (prefab.StableId != profile.StableId)
                throw new InvalidOperationException("Original team prefab resource profile differs.");
            Debug.Log("Actor mode: original opposing-team uniforms/models/BCD numbers/rosters; explicit " + profile.StableId +
                " draw state and" + profile.ClipCount + "-clip library required.");
            return prefab;
        }

        internal static ISourceAnimationLibrary ResolveRequiredAnimationLibrary(SourcePlayerRig sourcePrefab)
        {
            if (sourcePrefab == null) return null;
            SourceTeamResourceProfile profile = sourcePrefab.StableId == LocalPreviewId ? null :
                SourceTeamResourceProfile.FromStableId(sourcePrefab.StableId);
            string key = profile == null ? AnimationResourceKey : profile.AnimationResourceKey;
            SourceAnimationLibraryAsset asset = Resources.Load<SourceAnimationLibraryAsset>(key);
            if (asset == null)
                throw new InvalidOperationException("Source actor mode requires the imported original animation library at Resources/" +
                    key + ". Run the explicit clip importer; no static pose will be substituted.");
            ISourceAnimationLibrary library = asset.Load();
            if (profile != null)
            {
                SourceAnimationLibrary actual = library as SourceAnimationLibrary;
                if (actual == null) throw new InvalidOperationException("Original team mode requires its actual typed animation library.");
                profile.RequireInventory(asset.InventorySha256, actual.SequenceNames.Count, actual.FrameCount);
            }
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
