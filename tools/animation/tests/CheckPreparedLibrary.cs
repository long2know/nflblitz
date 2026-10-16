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

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3)
                throw new ArgumentException("Expected bundle root, baseline inventory, baseline root.");
            string root = Path.GetFullPath(args[0]);
            string baselineRoot = Path.GetFullPath(args[2]);
            SourceAnimationLibrary library = SourceAnimationLibrary.Read(
                File.ReadAllBytes(Path.Combine(root, "export-inventory.json")),
                path => File.ReadAllBytes(Path.Combine(root, path)));
            SourceAnimationLibrary baseline = SourceAnimationLibrary.Read(
                File.ReadAllBytes(args[1]),
                path => File.ReadAllBytes(Path.Combine(baselineRoot, path)));
            Check(library.SequenceNames.Count == 56 && library.FrameCount == 3731,
                "Frozen library must contain exactly 56 clips / 3731 selected frames.");
            Check(SourceAnimationLibrary.SourceRevision == "89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
                "Production source revision.");
            Check(SourceAnimationLibrary.ConverterRevision == "1792293aaaea00a919b59e8c05716978e85b4333",
                "Production converter revision.");
            int sourceFrames = 0;
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
            Check(sourceFrames == 4046, "Frozen authoring frame total.");
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
