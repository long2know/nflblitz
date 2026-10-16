#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Blitz.AssetImport;
using Blitz.Presentation;
using UnityEditor;
using UnityEngine;

namespace Blitz.Editor
{
    public static class SourceAnimationImporter
    {
        public static void ImportFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string root = RequiredArgument(args, "-animationAssetRoot");
            string manifest = RequiredArgument(args, "-animationManifest");
            string expectedHash = RequiredArgument(args, "-animationManifestSha256");
            string id = RequiredArgument(args, "-animationStableId");
            if (id != SourcePlayerProvider.LocalPreviewId)
                throw new PlayerAssetException("This explicit clip import targets the installed local-plyr266 rig only.");
            var input = new AuthorizedAssetRoot(root);
            byte[] inventory = input.ReadBytes(manifest, 1024 * 1024);
            if (AuthorizedAssetRoot.Sha256(inventory) != expectedHash)
                throw new PlayerAssetException("Explicit original animation manifest SHA256 mismatch; no assets were written.");
            var clips = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            SourceAnimationLibrary library = SourceAnimationLibrary.Read(inventory, path =>
            {
                byte[] bytes = input.ReadBytes(path, 8 * 1024 * 1024);
                clips.Add(path, bytes);
                return bytes;
            });
            string resourceFolder = "Assets/Imported/" + id + "/Resources/BlitzSourcePlayers/" + id;
            string libraryPath = resourceFolder + "/animation-library.asset";
            var output = new AuthorizedAssetRoot(Path.GetDirectoryName(Application.dataPath));
            output.ExistingDirectory(resourceFolder);
            SourcePlayerRig prefab = AssetDatabase.LoadAssetAtPath<GameObject>(resourceFolder + "/player.prefab")?.GetComponent<SourcePlayerRig>();
            if (prefab == null || prefab.StableId != id || prefab.JointCount != 15)
                throw new PlayerAssetException("Import the original fifteen-joint source player before the animation library.");
            prefab.ValidateGeneratedRig();
            SourceAnimationLibraryAsset existing = AssetDatabase.LoadAssetAtPath<SourceAnimationLibraryAsset>(libraryPath);
            if (existing != null)
            {
                if (existing.InventorySha256 != expectedHash)
                    throw new PlayerAssetException("A different immutable imported animation library already exists; no overwrite is allowed.");
                existing.Load();
                Debug.Log("PASS: existing original animation library content pins verified; no assets overwritten.");
                return;
            }
            output.NewPath(libraryPath);
            string folder = resourceFolder + "/Animations";
            if (AssetDatabase.IsValidFolder(folder))
                throw new PlayerAssetException("An incomplete animation import already exists; inspect it rather than silently overwriting.");
            output.NewPath(folder);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(resourceFolder, "Animations")))
                throw new PlayerAssetException("Could not reserve the explicit animation asset folder.");
            TextAsset bundledInventory = WriteTextAsset(output, folder + "/inventory.json", inventory);
            var paths = new List<string>(); var assets = new List<TextAsset>();
            foreach (KeyValuePair<string, byte[]> pair in clips)
            {
                string name = Path.GetFileNameWithoutExtension(pair.Key);
                TextAsset text = WriteTextAsset(output, folder + "/" + name + ".json", pair.Value);
                paths.Add(pair.Key); assets.Add(text);
            }
            SourceAnimationLibraryAsset libraryAsset = ScriptableObject.CreateInstance<SourceAnimationLibraryAsset>();
            libraryAsset.Initialize(bundledInventory, expectedHash, paths.ToArray(), assets.ToArray());
            AssetDatabase.CreateAsset(libraryAsset, libraryPath);
            AssetDatabase.SaveAssets();
            SourceAnimationLibraryAsset saved = AssetDatabase.LoadAssetAtPath<SourceAnimationLibraryAsset>(libraryPath);
            if (saved == null || saved.Load().GetClip("M_GU_C2").SelectedSourceFrames.Count != 60)
                throw new PlayerAssetException("Persisted original animation library/selected-frame map verification failed.");
            string report = JsonUtility.ToJson(new ImportReport {
                schema = "source-animation-import/v1", inventorySha256 = expectedHash, library = libraryPath,
                clips = library.SequenceNames.Count, selectedFrames = library.FrameCount,
                sourceRevision = SourceAnimationLibrary.SourceRevision,
                runtimeAccess = "Bundled Resources TextAssets only; no original data is tracked or externally read at runtime."
            }, true) + "\n";
            WriteNew(output, "Assets/Imported/" + id + "/animation-import-report.json", Encoding.UTF8.GetBytes(report));
            Debug.Log("PASS: imported continuous original animation library: " + library.SequenceNames.Count + " clips / " +
                library.FrameCount + " selected frames; inventory SHA256=" + expectedHash + ".");
        }

        private static TextAsset WriteTextAsset(AuthorizedAssetRoot output, string path, byte[] bytes)
        {
            WriteNew(output, path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null || AuthorizedAssetRoot.Sha256(asset.bytes) != AuthorizedAssetRoot.Sha256(bytes))
                throw new PlayerAssetException("Persisted original animation TextAsset bytes changed: " + path);
            return asset;
        }
        private static void WriteNew(AuthorizedAssetRoot output, string relative, byte[] bytes)
        {
            using (var stream = new FileStream(output.NewPath(relative), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(bytes, 0, bytes.Length);
        }
        internal static string RequiredArgument(string[] args, string name)
        {
            string result = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != name) continue;
                if (result != null || i + 1 == args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    throw new PlayerAssetException("Missing/duplicate explicit command-line argument " + name + ".");
                result = args[++i];
            }
            if (string.IsNullOrEmpty(result)) throw new PlayerAssetException("Required explicit command-line argument " + name + ".");
            return result;
        }
        [Serializable] private sealed class ImportReport
        {
            public string schema, inventorySha256, library, sourceRevision, runtimeAccess;
            public int clips, selectedFrames;
        }
    }
}
#endif
