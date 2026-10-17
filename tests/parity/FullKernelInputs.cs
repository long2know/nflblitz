using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal sealed class FullKernelInputs
{
    internal const string AnimationSha256 = "f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274";
    internal const string RosterSha256 = "f747741e95766185e0c615ded549d1ce2f5fec0c48ed2ecb29ba1b755764539b";
    internal const string PlaybookSha256 = "b180291759fb22c8ae7b2de7d417867e15243f6071bb1a672f11bb2fb6da4bd3";
    internal const string MovementSha256 = "89c79007e04e9a17894c126c34dc66c199eb5a5961a4c749b1eb6b3e0e3951d6";
    internal const string MovementSelectedSha256 = "5e8fc7c5f2011303fd7b1aea87e6f957b1acdf4cbb15a43fd82f978d4b944b68";
    internal const string MovementAnimationSha256 = "f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e";
    internal const string ContactAnimationSha256 = "9cae962bbb861bffe4d379bc30919cf34c0238ed35be82e605253d3059cfede9";
    internal const string ContactFixtureSchema = "source-contact-kernel-real-inputs/v1";
    internal bool IsContactProfile { get; private set; }
    internal bool IsC2Profile { get; private set; }
    internal bool IsC2M1Profile { get; private set; }
    internal SourceC2CallbackData C2Data { get; private set; }
    internal string C2InputRoot { get; private set; }
    internal SourceVector3[] ReferencePivots { get; private set; }
    internal SourceAnimationLibrary Animations { get; private set; }
    internal SourceCatchGeometry Geometry { get; private set; }
    internal SourcePlaybook Playbook { get; private set; }
    internal SourceTeamRosterLibrary Rosters { get; private set; }
    internal SourcePlayer Player { get; private set; }
    internal SourcePlayerPose StandingPose { get; private set; }
    internal string ManifestSha256 { get; private set; }
    internal SourceMovementRuntimeData MovementRuntime { get; private set; }
    internal SourceAudioFunctionData SoundFunctions { get; private set; }

    private static string Sha256(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static byte[] Read(string root, JsonElement entry, string expectedPath, string expectedSha)
    {
        if (entry.GetProperty("path").GetString() != expectedPath ||
            entry.GetProperty("sha256").GetString() != expectedSha)
            throw new InvalidDataException("Actual fixture metadata differs from its pinned production input: " + expectedPath);
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, expectedPath));
        if (Sha256(bytes) != expectedSha)
            throw new InvalidDataException("Actual fixture bytes differ from their explicit pin: " + expectedPath);
        return bytes;
    }

    internal static FullKernelInputs Load(string manifestPath, bool movementLibrary = false,
        bool selectedMovementLibrary = false, string soundFunctionsPath = null, bool legacyAnimationLibrary = false,
        string c2InputsPath = null, string c2M1InputsPath = null)
    {
        if (c2InputsPath != null && c2M1InputsPath != null)
            throw new ArgumentException("Select exactly one old-nine or M1-successor C2 input generation.");
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("An explicit prepared real-input fixture manifest is required.", nameof(manifestPath));
        string root = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        using (JsonDocument document = JsonDocument.Parse(manifestBytes))
        {
            JsonElement manifest = document.RootElement;
            bool contactFixture = manifest.GetProperty("schema").GetString() == ContactFixtureSchema;
            if ((!contactFixture && manifest.GetProperty("schema").GetString() != "source-kernel-real-inputs/v1") ||
                manifest.GetProperty("source_revision").GetString() != "89f840d58d152a2a9f230cb8d0cdffbd554c8ded")
                throw new InvalidDataException("Expected an explicit legacy or Contact272 real-input schema at the pinned original revision.");
            if (contactFixture)
            {
                JsonElement profile = manifest.GetProperty("production_profile");
                if (profile.GetProperty("stable_id").GetString() != SourceTeamResourceProfile.ContactStableId ||
                    profile.GetProperty("animation_resource_key").GetString() != SourceTeamResourceProfile.ContactAnimationKey ||
                    profile.GetProperty("services_resource_key").GetString() != SourceInterfacePackagePlan.ContactServicesResourceKey ||
                    profile.GetProperty("animation_sha256").GetString() != ContactAnimationSha256 ||
                    profile.GetProperty("clips").GetInt32() != 272 || profile.GetProperty("selected_frames").GetInt32() != 24892 ||
                    profile.GetProperty("impact_sha256").GetString() != SourceTeamResourceProfile.ContactImpactSha256 ||
                    profile.GetProperty("contact_programs_sha256").GetString() != SourceTeamResourceProfile.ContactProgramsSha256)
                    throw new InvalidDataException("Contact272 requires its actual production profile, resource keys and generation pins.");
                Read(root, manifest.GetProperty("legacy_fixture"), "legacy-fixture.json",
                    "8167196e731762b54296a68108eda40bb3a1077bc691daa94e6cfd08640d8876");
                Read(root, manifest.GetProperty("contact_impact"), "contact-impact.json", SourceTeamResourceProfile.ContactImpactSha256);
            }
            else if (manifest.TryGetProperty("contact_animation", out _) || manifest.TryGetProperty("production_profile", out _))
                throw new InvalidDataException("A legacy library cannot be silently relabeled as Contact272.");
            if (selectedMovementLibrary && !movementLibrary)
                throw new ArgumentException("The selected156 control is an explicit movement library, never a canonical117 alias.");
            bool contactLibrary = contactFixture && !legacyAnimationLibrary && !selectedMovementLibrary;
            string libraryDirectory = contactLibrary ? "contact-animation" :
                selectedMovementLibrary ? "movement-selected" : movementLibrary ? "movement-animation" : "animation";
            string entry = contactLibrary ? "contact_animation" :
                selectedMovementLibrary ? "movement_selected_animation" : movementLibrary ? "movement_animation" : "animation";
            byte[] inventory = Read(root, manifest.GetProperty(entry), libraryDirectory + "/export-inventory.json",
                contactLibrary ? ContactAnimationSha256 :
                selectedMovementLibrary ? MovementSelectedSha256 : movementLibrary ? MovementAnimationSha256 : AnimationSha256);
            var result = new FullKernelInputs { ManifestSha256 = Sha256(manifestBytes), IsContactProfile = contactLibrary };
            result.Animations = SourceAnimationLibrary.Read(inventory,
                relative => File.ReadAllBytes(Path.Combine(root, libraryDirectory, relative)));
            if (result.Animations.SequenceNames.Count != (contactLibrary ? 272 : selectedMovementLibrary ? 156 : movementLibrary ? 157 : 117) ||
                result.Animations.FrameCount != (contactLibrary ? 24892 : selectedMovementLibrary ? 13420 : movementLibrary ? 13490 : 10288))
                throw new InvalidDataException("The explicitly selected actual animation library inventory is required; identities are never aliased.");
            if (contactLibrary) SourceTeamResourceProfile.Contact272.RequireLibrary(result.Animations);
            byte[] model = Read(root, manifest.GetProperty("model"), "models/plyr266.h",
                "9d91e22e0f80dc369a52c8956c6fb3f7b37504a3b266a8cba4f5125d93741149");
            byte[] skeleton = Read(root, manifest.GetProperty("skeleton"), "models/skeleton.h",
                "a957d80a322948d7a41667cf80ab621a2ff7b5e74eee6d4da8fba31f216c0a93");
            result.Player = GeneratedPlayerReader.Read(Encoding.UTF8.GetString(model), "models/plyr266.h",
                Encoding.UTF8.GetString(skeleton), "models/skeleton.h",
                new PlayerSymbolSelection("plyr266_limbs", "fbplyr_skel", "model266_limbs", "plyr266_textures"));
            var conversion = new PlayerCoordinateConversion(SignedSourceAxis.PositiveX, SignedSourceAxis.PositiveY,
                SignedSourceAxis.NegativeZ, SourceTriangleOrder.Renderer321);
            if (result.Player.Nodes.Count != SourceTiming.JointCount)
                throw new InvalidDataException("Actual imported source rig must contain fifteen joints.");
            var pivots = new SourceVector3[SourceTiming.JointCount];
            for (int i = 0; i < pivots.Length; i++)
            {
                SourceRigNode node = result.Player.Nodes[i];
                if (node.Index != i || node.ParentIndex != SourceAnimationSkeleton.ParentIndices[i])
                    throw new InvalidDataException("Actual imported rig node order/parent mismatch.");
                AssetVector3 pivot = conversion.Convert(node.BindPivot);
                pivots[i] = new SourceVector3(pivot.X, pivot.Y, pivot.Z);
            }
            result.Geometry = new SourceCatchGeometry(result.Animations, pivots);
            result.ReferencePivots = pivots;
            JsonElement poseEntry;
            if (manifest.TryGetProperty("standing_pose", out poseEntry))
            {
                byte[] poseBytes = Read(root, poseEntry, "standing-pose.json",
                    poseEntry.GetProperty("sha256").GetString());
                result.StandingPose = SourcePlayerPoseReader.Read(poseBytes, "standing-pose.json",
                    poseEntry.GetProperty("sha256").GetString(), result.Player);
            }
            result.Rosters = SourceTeamRosterLibrary.Read(
                Read(root, manifest.GetProperty("rosters"), "rosters.json", RosterSha256), RosterSha256);
            if (result.Rosters.Teams.Count != 31 || result.Rosters.Teams.Sum(team => team.Players.Count) != 496)
                throw new InvalidDataException("Actual original 31x16 roster service is required.");
            byte[] playbook = Read(root, manifest.GetProperty("playbook"), "playbook/catalog.tsv", PlaybookSha256);
            using (var reader = new StringReader(Encoding.UTF8.GetString(playbook)))
                result.Playbook = SourcePlaybook.LoadConverted(reader);
            SourceInterfaceMovementData movement = SourceInterfaceMovementData.Read(
                Read(root, manifest.GetProperty("movement"), "movement-runtime.json", MovementSha256), MovementSha256);
            result.MovementRuntime = new SourceMovementRuntimeData(new SourceMovementData(movement.Constants, movement.Tables),
                movement.Friction, movement.Defense.Select(row => new SourceMovementDefenseRow(
                    row.Line, row.Back, row.Base, row.Range, row.Plow)).ToArray());
            byte[] soundFunctions = soundFunctionsPath == null
                ? Read(root, manifest.GetProperty("sound_functions"), "audio-functions/sound-functions.safn",
                    GameSimulation.SourceSoundVmDataSha256)
                : File.ReadAllBytes(soundFunctionsPath);
            result.SoundFunctions = SourceAudioFunctionData.Read(soundFunctions, GameSimulation.SourceSoundVmDataSha256);
            if (c2InputsPath != null || c2M1InputsPath != null)
            {
                if (selectedMovementLibrary || legacyAnimationLibrary)
                    throw new InvalidDataException("Canonical281 cannot alias a historical selected156/legacy library.");
                result.IsC2M1Profile = c2M1InputsPath != null;
                result.C2InputRoot = Path.GetFullPath(c2M1InputsPath ?? c2InputsPath);
                string animationRoot = Path.Combine(result.C2InputRoot, "animation281");
                byte[] c2Inventory = File.ReadAllBytes(Path.Combine(animationRoot, "export-inventory.json"));
                if (Sha256(c2Inventory) != SourceTeamResourceProfile.C2AnimationSha256)
                    throw new InvalidDataException("Canonical281 requires its exact frozen production inventory.");
                result.Animations = SourceAnimationLibrary.Read(c2Inventory,
                    relative => File.ReadAllBytes(Path.Combine(animationRoot, relative)));
                SourceTeamResourceProfile.Contact281C2.RequireLibrary(result.Animations);
                if (result.Animations.SequenceNames.Count != 281 || result.Animations.FrameCount != 26230)
                    throw new InvalidDataException("Canonical281 requires every actual clip/frame, never a272 relabel.");
                byte[] callbacks = File.ReadAllBytes(Path.Combine(result.C2InputRoot, "c2-callback-data.json"));
                byte[] hit = File.ReadAllBytes(Path.Combine(result.C2InputRoot, "m-qbfite-hit-extension.safn"));
                result.C2Data = result.IsC2M1Profile ? SourceInterfacePackagePlan.ReadC2M1Callbacks(callbacks, hit)
                    : SourceInterfacePackagePlan.ReadC2Callbacks(callbacks, hit);
                result.Geometry = new SourceCatchGeometry(result.Animations, pivots);
                result.IsContactProfile = false;
                result.IsC2Profile = true;
            }
            return result;
        }
    }

    internal GameSimulation Create(int seed = 1, SourceMatchConfiguration configuration = null,
        Func<int, ISourceRandom> randomFactory = null, SourcePresentationSettings presentation = null,
        SourceMovementSettings movementSettings = null, bool camera = true)
    {
        SourceMovementSettings settings = movementSettings ?? new SourceMovementSettings(0, 0, false, 0, 0, false);
        GameSimulation game;
        if (IsC2Profile)
        {
            if (configuration == null || presentation == null)
                throw new InvalidDataException("Canonical281 callers must provide their existing explicit match/presentation inputs.");
            game = IsC2M1Profile
                ? SourceC2Composition.CreateM1Simulation(Animations, ReferencePivots, Playbook, configuration,
                    Rosters, presentation, MovementRuntime, settings, SoundFunctions,
                    SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(C2Data), randomFactory, seed)
                : SourceC2Composition.CreateSimulation(Animations, ReferencePivots, Playbook, configuration,
                    Rosters, presentation, MovementRuntime, settings, SoundFunctions,
                    SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(C2Data), randomFactory, seed);
        }
        else
        {
            game = new GameSimulation(Animations, Geometry, Playbook, configuration,
                randomFactory, seed, Rosters, presentation, MovementRuntime, settings);
            game.BindSourceSoundVmData(SoundFunctions);
        }
        if (camera)
            game.SourceCameraHeadingProvider = (snapshot, tick) => SetControlledMovementCamera(game, snapshot, tick);
        return game;
    }

    // Named 512x384 source-DIP test camera; not browser resolution or original normal-camera qualification.
    internal static int SetControlledMovementCamera(GameSimulation game, IGameState snapshot, long tick)
    {
        SourceVector3 focus = game.SourceBallWorldPosition;
        var eye = new SourceVector3(focus.X - 120f, 85f, focus.Z - 40f);
        float theta = (float)Math.Atan2(focus.X - eye.X, focus.Z - eye.Z);
        SourceCinematicTransform frame = SourceCinematicTransform.FromState(
            new SourceCinematicSeed(eye, focus, 150f, theta, -.4f));
        game.SetSourceMovementCameraFrame(new SourceMovementCameraFrame(frame.Position, frame.Transform, 512f, 384f), tick);
        return (int)(theta * (1024f / SourceCinematicMath.TwoPi)) & 1023;
    }
}
