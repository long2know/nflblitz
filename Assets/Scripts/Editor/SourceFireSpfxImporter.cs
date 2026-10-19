using System;
using System.Collections.Generic;
using System.IO;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Presentation;
using UnityEditor;
using UnityEngine;

namespace Blitz.Editor
{
    public static class SourceFireSpfxImporter
    {
        public const string Destination = "Assets/Imported/source-fire-spfx-authoring-tga-sdl-v1";
        public static SourceFireSpfxAsset ImportPortableBundle(string absolutePreparedRoot, string expectedManifestSha256)
        {
            var root = new AuthorizedAssetRoot(absolutePreparedRoot);
            byte[] manifest = root.ReadBytes("manifest.json", 65536);
            SourceFireSpfxBundle bundle = SourceFireSpfxReader.Read(manifest, expectedManifestSha256, p => root.ReadBytes(p, 65536));
            var project = new AuthorizedAssetRoot(Path.GetFullPath("."));
            string destination = project.NewPath(Destination);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Scripts/Presentation/SourceFireSpfx.shader");
            Shader flash = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Scripts/Presentation/SourcePlayerFlash.shader");
            SourceFireGuard.Require(shader != null, "Source fire shader must exist; no built-in particle/material substitution.");
            SourceFireGuard.Require(flash != null, "Explicit authored source-player white-combine shader is required.");
            var paths = new List<string> { "geometry.bytes" };
            paths.Add("inputs/models/spfx.sdl.bytes");
            for (int i = 1; i <= 17; i++) paths.Add("inputs/img3d/trail" + i + ".tga.bytes");
            for (int i = 1; i <= bundle.DustFrames.Count; i++) paths.Add("inputs/img3d/dirt" + i + ".tga.bytes");
            Directory.CreateDirectory(destination);
            var files = new TextAsset[paths.Count];
            for (int i = 0; i < paths.Count; i++)
            {
                string target = Destination + "/" + paths[i];
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                { byte[] bytes = root.ReadBytes(paths[i], 65536); stream.Write(bytes, 0, bytes.Length); }
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
                files[i] = AssetDatabase.LoadAssetAtPath<TextAsset>(target);
            }
            string provenancePath = Destination + "/manifest.json.bytes";
            File.WriteAllBytes(provenancePath, manifest);
            AssetDatabase.ImportAsset(provenancePath, ImportAssetOptions.ForceSynchronousImport);
            var asset = ScriptableObject.CreateInstance<SourceFireSpfxAsset>();
            asset.InitializeGenerated(AssetDatabase.LoadAssetAtPath<TextAsset>(provenancePath), bundle.ManifestSha256, paths.ToArray(), files, shader, flash);
            string resourcePath = Destination + "/Resources/" + SourceFireSpfxAsset.ResourceKey + ".asset";
            Directory.CreateDirectory(Path.GetDirectoryName(resourcePath));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.CreateAsset(asset, resourcePath); AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
