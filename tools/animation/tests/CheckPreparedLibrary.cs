using System;
using System.IO;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;

internal static class CheckPreparedLibrary
{
    private static int assertions;

    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new Exception(label);
    }

    private static bool Same(SourceVector3 a, SourceVector3 b)
    {
        return a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    }

    private static int[] CheckCatalog(string path, byte[] inventoryBytes, byte[] baselineBytes,
        SourceAnimationLibrary library, SourceAnimationLibrary baseline)
    {
        byte[] catalogBytes = File.ReadAllBytes(path);
        using (JsonDocument catalog = JsonDocument.Parse(catalogBytes))
        using (JsonDocument inventory = JsonDocument.Parse(inventoryBytes))
        {
            JsonElement metadata = catalog.RootElement;
            Check(metadata.GetProperty("schema").GetString() == "original-wan-sequence-catalog/v1",
                "Explicit metadata catalog schema.");
            Check(metadata.GetProperty("verified_inventory_sha256").GetString() ==
                AuthorizedAssetRoot.Sha256(baselineBytes), "Catalog must pin the exact baseline inventory.");
            Check(inventory.RootElement.GetProperty("catalog_sha256").GetString() ==
                AuthorizedAssetRoot.Sha256(catalogBytes), "Bundle must identify the exact selected catalog bytes.");
            JsonElement source = metadata.GetProperty("source");
            JsonElement bundleSource = inventory.RootElement.GetProperty("source");
            Check(source.GetProperty("repository").GetString() == "historicalsource/nflblitz2000" &&
                source.GetProperty("commit").GetString() == SourceAnimationLibrary.SourceRevision &&
                bundleSource.GetProperty("repository").GetString() == source.GetProperty("repository").GetString() &&
                bundleSource.GetProperty("commit").GetString() == source.GetProperty("commit").GetString(),
                "Catalog and bundle must retain the production source revision.");
            int count = metadata.GetProperty("sequence_count").GetInt32();
            int selected = metadata.GetProperty("selected_frame_count").GetInt32();
            int sourceFrames = metadata.GetProperty("source_frame_count").GetInt32();
            JsonElement entries = metadata.GetProperty("clips");
            Check(entries.GetArrayLength() == count && count == library.SequenceNames.Count &&
                count == baseline.SequenceNames.Count && selected == library.FrameCount &&
                selected == baseline.FrameCount, "Exact catalog/bundle/baseline clip and selected-frame counts.");
            for (int i = 0; i < count; i++)
            {
                JsonElement entry = entries[i];
                string name = entry.GetProperty("runtime_name").GetString();
                Check(name == library.SequenceNames[i], "Catalog must enumerate the exact sorted runtime names.");
                ISourceAnimationClip clip = library.GetClip(name);
                Check(entry.GetProperty("source_path").GetString() == clip.SourcePath &&
                    entry.GetProperty("source_git_blob_sha1").GetString() == clip.SourceGitBlobSha1 &&
                    entry.GetProperty("source_frames").GetInt32() == clip.SourceFrameCount &&
                    entry.GetProperty("selected_frames").GetInt32() == clip.SelectedSourceFrames.Count &&
                    entry.GetProperty("source_first").GetInt32() == clip.SelectedSourceFrames[0] &&
                    entry.GetProperty("source_last").GetInt32() ==
                        clip.SelectedSourceFrames[clip.SelectedSourceFrames.Count - 1],
                    name + ": exact metadata source identity/counts/selected boundaries.");
                Check(clip.SourceFlags == baseline.GetClip(name).SourceFlags,
                    name + ": original source flags unchanged.");
            }
            return new[] { count, selected, sourceFrames };
        }
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3 && args.Length != 4)
                throw new ArgumentException("Expected bundle root, baseline inventory, baseline root, [catalog].");
            string root = Path.GetFullPath(args[0]);
            string baselineRoot = Path.GetFullPath(args[2]);
            byte[] inventoryBytes = File.ReadAllBytes(Path.Combine(root, "export-inventory.json"));
            byte[] baselineBytes = File.ReadAllBytes(args[1]);
            SourceAnimationLibrary library = SourceAnimationLibrary.Read(
                inventoryBytes, path => File.ReadAllBytes(Path.Combine(root, path)));
            SourceAnimationLibrary baseline = SourceAnimationLibrary.Read(
                baselineBytes, path => File.ReadAllBytes(Path.Combine(baselineRoot, path)));
            int[] expected = args.Length == 4
                ? CheckCatalog(args[3], inventoryBytes, baselineBytes, library, baseline)
                : new[] { 56, 3731, 4046 };
            Check(library.SequenceNames.Count == expected[0] && library.FrameCount == expected[1],
                "Library must contain the exact requested clip / selected-frame totals.");
            Check(SourceAnimationLibrary.SourceRevision == "89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
                "Production source revision.");
            Check(SourceAnimationLibrary.ConverterRevision == "1792293aaaea00a919b59e8c05716978e85b4333",
                "Production converter revision.");
            int sourceFrames = 0;
            int selectedOrdinalCheckpoints = 0;
            foreach (string name in library.SequenceNames)
            {
                ISourceAnimationClip clip = library.GetClip(name);
                ISourceAnimationClip previous = baseline.GetClip(name);
                Check(clip.SourceRevision == SourceAnimationLibrary.SourceRevision,
                    name + ": runtime SourceRevision.");
                Check(clip.SourcePath == previous.SourcePath &&
                    clip.SourceGitBlobSha1 == previous.SourceGitBlobSha1 &&
                    clip.SourceSha256 == previous.SourceSha256,
                    name + ": original SourcePath/blob/SourceSha256 must remain unchanged.");
                Check(clip.SourceFrameCount == previous.SourceFrameCount &&
                    clip.SelectedSourceFrames.Count == previous.SelectedSourceFrames.Count,
                    name + ": source and selected counts.");
                Check(clip.DecodedSha256 == AuthorizedAssetRoot.Sha256(
                    File.ReadAllBytes(Path.Combine(root, "clips", name + ".json"))),
                    name + ": actual decoded-byte identity.");
                sourceFrames += clip.SourceFrameCount;
                if (args.Length == 4)
                {
                    foreach (int ordinal in new[] { 0, clip.SelectedSourceFrames.Count / 2,
                        clip.SelectedSourceFrames.Count - 1 })
                    {
                        SourceAnimationSample sample = SourceAnimationSample.FromSelectedOrdinals(clip, ordinal, ordinal);
                        Check(sample.SequenceName == name && sample.SourceFrame == clip.SelectedSourceFrames[ordinal] &&
                            sample.NextSourceFrame == clip.SelectedSourceFrames[ordinal] && sample.Interpolation01 == 0f,
                            name + ": first/middle/last production selected-ordinal to authoring mapping.");
                        selectedOrdinalCheckpoints++;
                    }
                }
                for (int i = 0; i < clip.SelectedSourceFrames.Count; i++)
                {
                    int selected = clip.SelectedSourceFrames[i];
                    Check(selected == previous.SelectedSourceFrames[i], name + ": selected order/map.");
                    SourceAnimationFrame frame = clip.GetFrame(selected);
                    SourceAnimationFrame original = previous.GetFrame(selected);
                    Check(frame.SourceFrame == selected &&
                        Same(frame.AuthoringRoot, original.AuthoringRoot) &&
                        Same(frame.ScaledRoot, original.ScaledRoot),
                        name + ": source frame/root unchanged.");
                    Check(frame.Pose.Rotations.Count == 15, name + ": fifteen production joints.");
                    for (int joint = 0; joint < 15; joint++)
                    {
                        SourceRotation a = frame.Pose.Rotations[joint];
                        SourceRotation b = original.Pose.Rotations[joint];
                        Check(a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.W == b.W,
                            name + ": decoded rotation unchanged.");
                    }
                }
            }
            Check(sourceFrames == expected[2], "Exact requested authoring frame total.");
            Console.WriteLine(JsonSerializer.Serialize(new {
                schema = "original-wan-batch-native-check/v1",
                clips = library.SequenceNames.Count,
                selectedFrames = library.FrameCount,
                sourceFrames = sourceFrames,
                inventorySha256 = library.InventorySha256,
                runtimeSourceRevision = SourceAnimationLibrary.SourceRevision,
                converterRevision = SourceAnimationLibrary.ConverterRevision,
                sourceIdentityUnchanged = true,
                selectedMapUnchanged = true,
                allFrameValuesUnchanged = true,
                catalogChecked = args.Length == 4,
                selectedOrdinalCheckpoints = selectedOrdinalCheckpoints,
                assertions = assertions
            }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
