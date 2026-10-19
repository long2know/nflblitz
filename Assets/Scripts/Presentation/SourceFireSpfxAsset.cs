using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;
using UnityEngine;

namespace Blitz.Presentation
{
    public sealed class SourceFireSpfxAsset : ScriptableObject
    {
        public const string ResourceKey = "BlitzSourceFireSpfx/source-fire-spfx-authoring-tga-sdl-v1/fire-spfx";
        [SerializeField] private TextAsset manifest;
        [SerializeField] private string manifestSha256;
        [SerializeField] private string[] paths;
        [SerializeField] private TextAsset[] payloads;
        [SerializeField] private Shader sourceShader;
        [SerializeField] private Shader playerFlashShader;
        public string ManifestSha256 { get { return manifestSha256; } }
        public Shader SourceShader { get { return sourceShader; } }
        public Shader PlayerFlashShader
        {
            get
            {
                SourceFireGuard.Require(playerFlashShader != null && playerFlashShader.isSupported
                    && playerFlashShader.name == "Blitz/SourcePlayerFlashPortable",
                    "Enabled actor flash requires its explicit authored textured white-combine shader; reimport the qualified bundle.");
                return playerFlashShader;
            }
        }

        public void InitializeGenerated(TextAsset provenance, string expectedSha256, string[] relativePaths,
            TextAsset[] files, Shader shader, Shader actorFlashShader = null)
        {
            manifest = provenance; manifestSha256 = expectedSha256;
            paths = relativePaths == null ? null : (string[])relativePaths.Clone();
            payloads = files == null ? null : (TextAsset[])files.Clone(); sourceShader = shader;
            playerFlashShader = actorFlashShader;
            Load();
        }

        public SourceFireSpfxBundle Load()
        {
            SourceFireGuard.Require(manifest != null && paths != null && payloads != null &&
                (paths.Length == 19 || paths.Length == 29) &&
                payloads.Length == paths.Length && sourceShader != null && sourceShader.name == "Blitz/SourceFireSpfxPortable",
                "Actual portable fire/dust provenance, original inputs, SDL geometry and shader required; no particles or art fallback.");
            var files = new Dictionary<string, TextAsset>(StringComparer.Ordinal);
            for (int i = 0; i < paths.Length; i++)
            {
                AuthorizedAssetRoot.ValidateRelativePath(paths[i]);
                SourceFireGuard.Require(payloads[i] != null && !files.ContainsKey(paths[i]), "Missing/duplicate fire payload.");
                files.Add(paths[i], payloads[i]);
            }
            return SourceFireSpfxReader.Read(manifest.bytes, manifestSha256, path =>
            {
                TextAsset value;
                SourceFireGuard.Require(files.TryGetValue(path, out value), "Missing original fire bundle payload: " + path);
                return value.bytes;
            });
        }
    }
}
