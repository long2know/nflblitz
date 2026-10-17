using System;
using Blitz.AssetImport;
using UnityEngine;

namespace Blitz.Presentation
{
    public sealed class SourceCinematicLibraryAsset : ScriptableObject
    {
        [SerializeField] private TextAsset catalog;
        [SerializeField] private string contentSha256;
        private SourceCinematicLibrary library;
        public string ContentSha256 { get { return contentSha256; } }

        public void Initialize(TextAsset originalCatalog, string expectedSha256)
        {
            catalog = originalCatalog; contentSha256 = expectedSha256; library = null;
            Load();
        }

        public SourceCinematicLibrary Load()
        {
            if (library != null) return library;
            if (catalog == null || string.IsNullOrEmpty(contentSha256))
                throw new InvalidOperationException("Original impact presentation catalog is missing; no generic tackle camera is substituted.");
            library = SourceCinematicLibrary.Read(catalog.bytes, contentSha256);
            return library;
        }
    }
}
