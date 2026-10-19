using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using OriginalPlayer = Blitz.AssetImport.SourcePlayer;

namespace Blitz.SourcePlayer;

public sealed class PlayerData
{
    public DataCatalog Catalog { get; }
    public SourceAnimationLibrary Animations { get; }
    public SourceTeamRosterLibrary Rosters { get; }
    public SourcePlaybook Playbook { get; }
    public SourceVector3[] ReferencePivots { get; }
    public OriginalPlayer ReferencePlayer { get; }
    public SourceMovementRuntimeData Movement { get; }
    public SourceMovementSettings MovementSettings { get; }
    public SourceInterfaceMovementBootstrap Bootstrap { get; }
    public SourceAudioFunctionData Sound { get; }
    public SourceC2CallbackData Callbacks { get; }
    public SourceInterfacePackageConfiguration Configuration { get; }
    public SourceTeamPresentationData Teams { get; }
    public SourceInterfaceData Hud { get; }
    public SourceCinematicLibrary Impact { get; }
    public SourceNormalCameraData NormalCamera { get; }
    public SourceEnvironmentBundle World { get; }
    public SourceFireSpfxBundle Fire { get; }
    public SourceTeamPoppedHeadData PoppedHead { get; }
    public SourceFreeBallData FreeBall { get; }
    private readonly Dictionary<string, string> headers = new(StringComparer.Ordinal);

    public PlayerData(DataCatalog data, Action<string> progress = null)
    {
        Catalog = data ?? throw new ArgumentNullException(nameof(data));
        progress?.Invoke("Verifying actual281 animation/pose library");
        Animations = SourceAnimationLibrary.Read(data.EntryBytes("animations"), path => data.Read("animation/" + path));
        SourceTeamResourceProfile.Contact281C2M1.RequireLibrary(Animations);
        progress?.Invoke("Loading original roster, rules, input, sound-selection and movement services");
        Rosters = SourceTeamRosterLibrary.Read(data.EntryBytes("rosters"), SourceTeamPresentationData.PinnedRosterSha256);
        using (var reader = new StringReader(DataCatalog.Text(data.EntryBytes("playbook"))))
            Playbook = SourcePlaybook.LoadConverted(reader);
        DataCatalog.Require(data.EntryHash("playbook") == SourceInterfacePackagePlan.PlaybookSha256,
            "The exact actual original production playbook is required.");
        Configuration = SourceInterfacePackagePlan.ReadConfiguration(data.EntryBytes("settings"), data.EntryHash("settings"));
        Bootstrap = SourceInterfaceMovementBootstrap.Read(data.EntryBytes("bootstrap"), data.EntryHash("bootstrap"));
        MovementSettings = new SourceMovementSettings(Bootstrap.FieldIndex, Bootstrap.InfiniteTurboTeamMask,
            Bootstrap.SpeedPowerup, Bootstrap.FastTurboTeamMask, Bootstrap.RunOutOfBoundsTeamMask, Bootstrap.ViolencePowerup);
        var movement = SourceInterfaceMovementData.Read(data.EntryBytes("movement"), SourceInterfaceMovementData.ContentSha256);
        Movement = new SourceMovementRuntimeData(new SourceMovementData(movement.Constants, movement.Tables),
            movement.Friction, movement.Defense.Select(row => new SourceMovementDefenseRow(row.Line, row.Back,
                row.Base, row.Range, row.Plow)).ToArray());
        Sound = SourceInterfacePackagePlan.ReadSoundVmFunctions(data.EntryBytes("sound"),
            SourceInterfacePackagePlan.SoundVmFunctionsSha256);
        Callbacks = SourceInterfacePackagePlan.ReadC2M1Callbacks(data.EntryBytes("callbacks"), data.EntryBytes("hit"));
        ReferencePlayer = GeneratedPlayerReader.Read(DataCatalog.Text(data.EntryBytes("model")), "models/plyr266.h",
            DataCatalog.Text(data.EntryBytes("skeleton")), "models/skeleton.h",
            new PlayerSymbolSelection("plyr266_limbs", "fbplyr_skel", "model266_limbs", "plyr266_textures"));
        var conversion = new PlayerCoordinateConversion(SignedSourceAxis.PositiveX, SignedSourceAxis.PositiveY,
            SignedSourceAxis.NegativeZ, SourceTriangleOrder.Renderer321);
        DataCatalog.Require(ReferencePlayer.Nodes.Count == SourceTiming.JointCount,
            "The actual original fifteen-joint rig is required.");
        ReferencePivots = new SourceVector3[SourceTiming.JointCount];
        for (int i = 0; i < ReferencePivots.Length; i++)
        {
            SourceRigNode node = ReferencePlayer.Nodes[i];
            DataCatalog.Require(node.Index == i && node.ParentIndex == SourceAnimationSkeleton.ParentIndices[i],
                "Original player joint/parent order changed.");
            AssetVector3 pivot = conversion.Convert(node.BindPivot);
            ReferencePivots[i] = new SourceVector3(pivot.X, pivot.Y, pivot.Z);
        }
        progress?.Invoke("Verifying original field/stadium, uniform/number atlas and HUD identities");
        Teams = SourceTeamPresentationData.Read(data.EntryBytes("teams"), data.EntryHash("teams"), Rosters);
        foreach (var input in Teams.Inputs.Values.Where(input => input.Path.StartsWith("models/", StringComparison.Ordinal)))
        {
            byte[] bytes = data.Read("teams/" + input.Path);
            Teams.VerifyInput(input.Path, bytes);
            headers.Add(input.Path, DataCatalog.Text(bytes));
        }
        Hud = SourceInterfaceData.Read(data.EntryBytes("hud"), SourceInterfacePackagePlan.HudSha256);
        Impact = SourceCinematicLibrary.Read(data.EntryBytes("impact"), SourceTeamResourceProfile.C2M1ImpactSha256);
        SourceTeamResourceProfile.Contact281C2M1.RequireImpact(Impact);
        DataCatalog.Require(data.HasEntry("normalCamera"),
            "The explicit source profile requires pinned original normal/kickoff camera data; legacy EMA is not a fallback.");
        NormalCamera = SourceNormalCameraData.Read(data.EntryBytes("normalCamera"));
        World = SourceEnvironmentReader.Read(data.EntryBytes("world"), data.EntryHash("world"),
            path => data.Read("world/" + path));
        if (data.HasEntry("fire"))
        {
            progress?.Invoke("Verifying original SDL/seventeen TRAIL authoring frames; portable not native WMS");
            Fire = SourceFireSpfxReader.Read(data.EntryBytes("fire"), data.EntryHash("fire"),
                path => data.Read("fire/" + path));
        }
        if (data.HasEntry("poppedHead"))
        {
            progress?.Invoke("Verifying original phead SDL source-authored geometry, raw texture4/HEDSHU slot1");
            PoppedHead = SourceTeamPoppedHeadData.Read(data.EntryBytes("poppedHead"), data.EntryHash("poppedHead"),
                path => data.Read("popped-head/" + path));
        }
        progress?.Invoke("Verifying original medball2 free-ball authoring origin and HEDSHU/HEDSHU2 decals");
        FreeBall = SourceFreeBallData.Read(data.EntryBytes("freeBall"), data.EntryHash("freeBall"),
            path => data.Read("free-ball/" + path));
        SourceFreeBallData.RequireTextures(Teams);
    }

    public string Header(string path) => headers.TryGetValue(path, out string text) ? text :
        throw new PlayerAssetException("Required verified original generated header is missing: " + path);
}
