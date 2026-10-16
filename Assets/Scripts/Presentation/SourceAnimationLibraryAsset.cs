using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;
using UnityEngine;

namespace Blitz.Presentation
{
    public sealed class SourceAnimationLibraryAsset : ScriptableObject
    {
        [SerializeField] private TextAsset inventory = null;
        [SerializeField] private string inventorySha256 = null;
        [SerializeField] private string[] sourcePaths = null;
        [SerializeField] private TextAsset[] decodedClips = null;
        private SourceAnimationLibrary cached;

        public string InventorySha256 { get { return inventorySha256; } }

        public ISourceAnimationLibrary Load()
        {
            if (cached != null) return cached;
            if (inventory == null || string.IsNullOrEmpty(inventorySha256) ||
                AuthorizedAssetRoot.Sha256(inventory.bytes) != inventorySha256 ||
                sourcePaths == null || decodedClips == null || sourcePaths.Length != decodedClips.Length)
                throw new PlayerAssetException("Bundled original animation library metadata/content pin is missing or changed.");
            var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            for (int i = 0; i < sourcePaths.Length; i++)
            {
                AuthorizedAssetRoot.ValidateRelativePath(sourcePaths[i]);
                if (decodedClips[i] == null || bytes.ContainsKey(sourcePaths[i]))
                    throw new PlayerAssetException("Bundled original animation clip reference is missing/duplicated: " + sourcePaths[i]);
                bytes.Add(sourcePaths[i], decodedClips[i].bytes);
            }
            var consumed = new HashSet<string>(StringComparer.Ordinal);
            SourceAnimationLibrary library = SourceAnimationLibrary.Read(inventory.bytes, path =>
            {
                byte[] data;
                if (!bytes.TryGetValue(path, out data))
                    throw new PlayerAssetException("Bundled original animation clip TextAsset is missing: " + path);
                consumed.Add(path);
                return data;
            });
            if (consumed.Count != bytes.Count)
                throw new PlayerAssetException("Bundled original animation library contains unmanifested clip references.");
            cached = library;
            return cached;
        }

#if UNITY_EDITOR
        public void Initialize(TextAsset manifest, string expectedManifestSha256, string[] paths, TextAsset[] clips)
        {
            inventory = manifest; inventorySha256 = expectedManifestSha256;
            sourcePaths = (string[])paths.Clone(); decodedClips = (TextAsset[])clips.Clone();
            cached = null;
            Load();
        }
#endif
    }
}
