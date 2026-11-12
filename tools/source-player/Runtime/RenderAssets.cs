using System.Collections.ObjectModel;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;
using Blitz.Presentation;
using OriginalPlayer = Blitz.AssetImport.SourcePlayer;

namespace Blitz.SourcePlayer;

public sealed class RenderAssets
{
    private readonly PlayerData data;
    private readonly IRenderSink sink;
    private readonly HashSet<string> meshes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> appearances = new(StringComparer.Ordinal);
    private readonly ReadOnlyCollection<BindPoseLimbMesh>[] models = new ReadOnlyCollection<BindPoseLimbMesh>[32];
    private readonly Dictionary<string, SourceTeamTexture> textures;
    private Part[] poppedHeadParts;
    public int MeshCount => meshes.Count;
    public int AppearanceCount => appearances.Count;
    public int TextureCount { get; private set; }
    public int WorldDrawCount { get; private set; }
    public float ActorHeightYards { get; private set; }
    public float ActorRadiusYards { get; private set; }
    public sealed record Part(string mesh, int slot);

    public RenderAssets(PlayerData data, IRenderSink sink, Action<string> progress = null)
    {
        this.data = data;
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        textures = data.Teams.Textures.ToDictionary(t => t.Id, StringComparer.Ordinal);
        progress?.Invoke("Uploading exact original world/atlas pixels to WebGL");
        UploadTextures();
        UploadWorld();
        progress?.Invoke("Building original player limb selectors and BCD number geometry");
        for (int i = 0; i < models.Length; i++)
        {
            OriginalPlayer model = SourceTeamGeneratedModels.ReadModel(data.Rosters, i, data.Header);
            models[i] = SourceTeamGeneratedModels.BuildModel(model);
            var joints = models[i].Select((limb, j) => new {
                drawEnabled = model.Nodes[j].DrawEnabled, parts = Register(limb)
            }).ToArray();
            sink.Model(i, JsonSerializer.Serialize(joints));
            if (i < 4) Measure(model);
            if (i == 0 && data.PoppedHead != null)
                poppedHeadParts = Register(SourceTeamGeneratedModels.BuildPoppedHead(model, data.PoppedHead));
        }
        UploadBall();
        if (data.Fire?.HelmetGeometry != null) UploadHelmet();
    }

    private void Measure(OriginalPlayer model)
    {
        float minimumY = float.MaxValue, maximumY = float.MinValue, radius = 0f;
        foreach (SourceLimb limb in model.Limbs)
            foreach (AssetVector3 p in limb.Vertices)
            {
                minimumY = Math.Min(minimumY, p.Y);
                maximumY = Math.Max(maximumY, p.Y);
                radius = Math.Max(radius, (float)Math.Sqrt(p.X * p.X + p.Z * p.Z));
            }
        float maximumScale = data.Rosters.Teams.Where(team =>
                team.TeamIndex == data.Configuration.Match.HomeTeam.TeamIndex ||
                team.TeamIndex == data.Configuration.Match.AwayTeam.TeamIndex)
            .SelectMany(team => team.Players).Max(player => player.ModelScale);
        ActorHeightYards = Math.Max(ActorHeightYards, (maximumY - minimumY) * maximumScale / SourceTiming.SourceUnitsPerYard);
        ActorRadiusYards = Math.Max(ActorRadiusYards, radius * maximumScale / SourceTiming.SourceUnitsPerYard);
    }

    private void UploadTextures()
    {
        var uploadedWorld = new HashSet<string>(StringComparer.Ordinal);
        foreach (SourceEnvironmentBundle world in Worlds())
        for (int i = 0; i < world.Manifest.textures.Length; i++)
        {
            var info = world.Manifest.textures[i];
            string id = WorldTextureId(world, i);
            if (!uploadedWorld.Add(id)) continue;
            sink.Texture(id, info.width, info.height, world.TextureRgba[i], info.mipCount,
                true, true, info.rgbaSha256, info.sourcePath + " / " + info.sourceFormat);
            TextureCount++;
        }
        foreach (SourceTeamTexture texture in data.Teams.Textures)
        {
            byte[] source = data.Catalog.Read("teams/" + texture.Path);
            data.Teams.VerifyTexture(texture, source);
            sink.Texture("teams/" + texture.Id, texture.Width, texture.Height,
                DecodeTga(source, texture.Path), 1, true, true, texture.Sha256, texture.Representation);
            TextureCount++;
        }
        if (data.OriginalPresentation != null)
            foreach (SourceOriginalRaster raster in data.OriginalPresentation.Rasters)
            {
                sink.Texture(raster.TextureId, raster.Width, raster.Height, raster.CopyRgba(), 1,
                    raster.Kind == "shadow", true, raster.Sha256, raster.SourcePath + " / " + raster.Representation);
                TextureCount++;
            }
        if (data.Catalog.Profile.UsesRecordPresentation)
        {
            byte[] solid = { 255, 255, 255, 255 };
            sink.Texture("hud/record-solid", 1, 1, solid, 1, false, false, DataCatalog.Hash(solid),
                "procedural original editor line/triangle geometry color carrier; NOT original raster art or an image substitute");
            TextureCount++;
        }
        foreach (var pair in data.Hud.Textures)
        {
            SourceInterfaceTexture texture = pair.Value;
            byte[] rgba = data.Hud.RequireTextureBytes(pair.Key, data.Catalog.Read("hud/" + texture.Path));
            sink.Texture("hud/" + pair.Key, texture.Width, texture.Height, rgba, 1, false,
                !texture.PointSampled, texture.Sha256, "original authored HUD raster / portable origin");
            TextureCount++;
            if (pair.Key == "interface")
            {
                byte[] white = SourceHudTextureMask.WhiteFlash(rgba);
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(white)).ToLowerInvariant();
                sink.Texture("hud/interface-white", texture.Width, texture.Height, white, 1, false,
                    !texture.PointSampled, hash, "original GR_COLORCOMBINE_ONE / black chroma key, authored-alpha portable mask");
                TextureCount++;
                if (data.Catalog.Profile.UsesDefaultAudibles)
                {
                    byte[] chroma = SourceHudTextureMask.BlackChromaKey(rgba);
                    string chromaHash = DataCatalog.Hash(chroma);
                    sink.Texture("hud/interface-chroma", texture.Width, texture.Height, chroma, 1, false,
                        !texture.PointSampled, chromaHash, "original DECAL_TEXTURE / black chroma key, unchanged authored RGB/nonblack alpha");
                    TextureCount++;
                }
            }
        }
    }

    private void UploadWorld()
    {
        foreach (SourceEnvironmentBundle world in Worlds()) UploadWorld(world);
    }

    private IEnumerable<SourceEnvironmentBundle> Worlds()
    {
        if (data.WorldCatalog == null) return new[] { data.World };
        return data.WorldCatalog.Worlds.OrderBy(world => world.ManifestSha256 == data.World.ManifestSha256 ? 0 : 1);
    }

    private string WorldTextureId(SourceEnvironmentBundle world, int index)
    {
        return data.WorldCatalog == null ? "world/" + index :
            "world/catalog/" + world.Manifest.textures[index].sourcePath.Substring("img3d/".Length).ToLowerInvariant();
    }

    private void UploadWorld(SourceEnvironmentBundle world)
    {
        object venue = SourceWorldPresentation.DescribeVenue(world);
        foreach (var draw in world.Manifest.draws.OrderBy(draw => draw.renderQueue))
        {
            SourceEnvironmentMeshData mesh = world.Meshes[draw.meshIndex];
            SourceEnvironmentReader.ValidateDrawState(draw.state, draw.name);
            for (int sub = 0; sub < mesh.Indices.Length; sub++)
            {
                string id = "world/" + draw.meshIndex + "/" + sub;
                if (meshes.Add(id))
                {
                    double[] vertices = new double[mesh.Positions.Length / 3 * 5];
                    for (int i = 0; i < vertices.Length / 5; i++)
                    {
                        vertices[i * 5] = mesh.Positions[i * 3];
                        vertices[i * 5 + 1] = mesh.Positions[i * 3 + 1];
                        vertices[i * 5 + 2] = -mesh.Positions[i * 3 + 2];
                        vertices[i * 5 + 3] = mesh.Uvs[i * 2];
                        vertices[i * 5 + 4] = mesh.Uvs[i * 2 + 1];
                    }
                    sink.Mesh(id, vertices, mesh.Indices[sub]);
                }
                int slot = mesh.MaterialSlots[sub];
                var binding = world.Manifest.slots[slot];
                DataCatalog.Require(binding.sourceSet == draw.sourceSet && binding.textureIndex >= 0,
                    "Missing original world texture binding: " + draw.name);
                sink.WorldDraw(id, WorldTextureId(world, binding.textureIndex), JsonSerializer.Serialize(new {
                    name = draw.name, renderPass = draw.renderPass, draw.renderQueue,
                    venue, sourceSet = draw.sourceSet, materialSlot = slot,
                    binding.sourceTexture, binding.authoredName, binding.selectedName,
                    sourcePath = world.Manifest.textures[binding.textureIndex].sourcePath,
                    textureSha256 = world.Manifest.textures[binding.textureIndex].rgbaSha256,
                    sourceDrawOrder = SourceWorldVenue.DrawOrder(draw.renderPass),
                    depthFunction = draw.state.depthFunction, depthMask = draw.state.depthMask,
                    depthBias = draw.state.depthBias, alphaFunction = draw.state.alphaFunction,
                    alphaValue = draw.state.alphaValue, colorFunction = draw.state.colorFunction,
                    alphaSource = draw.state.alphaSource, rgbSource = draw.state.rgbSource,
                    rgbDestination = draw.state.rgbDestination, alphaSourceBlend = draw.state.alphaSourceBlend,
                    alphaDestinationBlend = draw.state.alphaDestinationBlend, cullMode = draw.state.cullMode
                }));
                WorldDrawCount++;
            }
        }
    }

    private Part[] Register(BindPoseLimbMesh limb)
    {
        var parts = new List<Part>();
        for (int slot = 0; slot < limb.SubmeshTriangles.Count; slot++)
        {
            var triangles = limb.SubmeshTriangles[slot];
            if (triangles.Count == 0) continue;
            string id = "limbs/" + limb.Symbol + "/" + slot;
            if (meshes.Add(id))
                sink.Mesh(id, Vertices(limb), triangles.ToArray());
            parts.Add(new Part(id, slot));
        }
        return parts.ToArray();
    }

    private static double[] Vertices(BindPoseLimbMesh mesh, AssetVector3 sourceCenter = default)
    {
        var result = new double[mesh.Positions.Count * 5];
        for (int i = 0; i < mesh.Positions.Count; i++)
        {
            AssetVector3 p = mesh.Positions[i];
            result[i * 5] = p.X - sourceCenter.X;
            result[i * 5 + 1] = p.Y - sourceCenter.Y;
            result[i * 5 + 2] = -p.Z - sourceCenter.Z;
            result[i * 5 + 3] = mesh.Uvs[i].X;
            result[i * 5 + 4] = mesh.Uvs[i].Y;
        }
        return result;
    }

    private void UploadBall()
    {
        BindPoseLimbMesh mesh = data.FreeBall.BuildSourceMesh();
        var vertices = new double[mesh.Positions.Count * 5];
        for (int i = 0; i < mesh.Positions.Count; i++)
        {
            AssetVector3 p = mesh.Positions[i]; AssetVector2 uv = mesh.Uvs[i];
            vertices[i * 5] = p.X; vertices[i * 5 + 1] = p.Y; vertices[i * 5 + 2] = p.Z;
            vertices[i * 5 + 3] = uv.X; vertices[i * 5 + 4] = uv.Y;
        }
        const string id = "original/free-ball/limb_medball_obj_big";
        meshes.Add(id);
        sink.Mesh(id, vertices, mesh.SubmeshTriangles[0].ToArray());
        sink.FreeBall(id, "teams/" + SourceFreeBallData.TextureId, "teams/" + SourceFreeBallData.AlternateTextureId,
            JsonSerializer.Serialize(new {
                origin = SourceFreeBallData.Origin, sourceSha256 = SourceFreeBallData.SourceSha256,
                geometrySha256 = SourceFreeBallData.HeaderSha256, manifestSha256 = data.FreeBall.ContentSha256,
                textureSha256 = SourceFreeBallData.TextureSha256, alternateTextureSha256 = SourceFreeBallData.AlternateTextureSha256,
                sourcePivot = "authoring-origin-unchanged", sourceBasis = "original-source-XYZ",
                sourceTriangleOrder = "Renderer321", rawTextureCode = 0, sourceScale = 1,
                nativeGeneratedHeaderEquivalent = false
            }));
    }

    private void UploadHelmet()
    {
        SourceFireGeometry geometry = data.Fire.HelmetGeometry;
        var vertices = new double[geometry.VertexIndices.Count * 5];
        var indices = new int[geometry.VertexIndices.Count];
        for (int i = 0; i < indices.Length; i++)
        {
            int corner = i / 3 * 3 + (i % 3 == 1 ? 2 : i % 3 == 2 ? 1 : 0);
            SourceVector3 p = geometry.Positions[geometry.VertexIndices[corner]];
            int uv = geometry.UvIndices[corner] * 2;
            vertices[i * 5] = p.X; vertices[i * 5 + 1] = p.Y; vertices[i * 5 + 2] = p.Z;
            vertices[i * 5 + 3] = geometry.RawAuthoringUvs[uv];
            vertices[i * 5 + 4] = geometry.RawAuthoringUvs[uv + 1]; indices[i] = i;
        }
        meshes.Add(SourceFireSpfxReader.HelmetMeshId);
        sink.Mesh(SourceFireSpfxReader.HelmetMeshId, vertices, indices);
    }

    public (SourceTeamAppearance Appearance, int Id) Bind(PlayerView actor, IGameState state)
    {
        SourceTeamDrawState draw = SourceInterfacePresentation.DrawState(actor, state);
        var match = ((ISourceMatchState)state).Match;
        var appearance = SourceTeamPresentationModel.Select(data.Rosters,
            match.Configuration.HomeTeam.TeamIndex, match.Configuration.AwayTeam.TeamIndex,
            (int)SourceInterfacePresentation.Settings(state).DesignatedHomeSide, actor, state.Ball.CarrierId, draw, data.PoppedHead);
        return BindAppearance(appearance);
    }

    public (SourceTeamAppearance Appearance, int Id) BindTeamSelection(PlayerView actor)
    {
        DataCatalog.Require(actor.Id is 3 or 10 && actor.Source.Roster.RosterIndex == 3 &&
            actor.Animation.Primary.ScriptSymbol is "qb_ready_anim" or "qb_ready_f_anim",
            "Team selection binds only its actual side quarterback/static record3.");
        SourceRosterPlayer player = data.Rosters.GetPlayerData(actor.Source.Roster);
        SourceRosterUniform uniform = data.Rosters.GetUniform(actor.Source.Roster.TeamIndex,
            SourceUniformVariant.Home, player.Skin);
        SourceRosterModel model = data.Rosters.ActorModels.SelectModel(SourceBodySize.Normal, true,
            actor.Team == TeamId.Home ? SourceHeldBallModel.Left : SourceHeldBallModel.Right);
        SourceRosterJersey numbers = data.Rosters.ActorModels.Jerseys[2 |
            ((player.Profile.PackedJerseyNumber >> 4) != 0 ? 4 : 0)];
        var appearance = new SourceTeamAppearance(player, uniform, model, numbers, null,
            (int)actor.Team * 2 + uniform.DecalOffset, 1f, null);
        return BindAppearance(appearance, "tmsel/");
    }

    private (SourceTeamAppearance Appearance, int Id) BindAppearance(SourceTeamAppearance appearance, string prefix = "")
    {
        string key = prefix + appearance.SourcePlayerId + "/" + appearance.UniformIdentity + "/" +
            (int)appearance.Uniform.Skin + "/" + appearance.Numbers.LimbSymbol + "/" +
            (appearance.CustomHead?.Index ?? -1) + "/" + (appearance.PoppedHead?.ContentSha256 ?? "helmeted");
        if (!appearances.TryGetValue(key, out int id))
        {
            string number = "numbers/" + appearance.Numbers.LimbSymbol + "/" + appearance.Player.Profile.PackedJerseyNumber;
            if (meshes.Add(number))
            {
                var mesh = SourceTeamGeneratedModels.BuildNumbers(data.Header(appearance.Numbers.Asset.SourcePath),
                    appearance.Numbers.LimbSymbol, appearance.Player.Profile.PackedJerseyNumber,
                    data.ReferencePlayer.Nodes[1].BindPivot);
                sink.Mesh(number, Vertices(mesh), mesh.SubmeshTriangles[0].ToArray());
            }
            Part[] headParts = null;
            if (appearance.CustomHead != null)
            {
                OriginalPlayer model = SourceTeamGeneratedModels.ReadModel(data.Rosters, appearance.Model.Index,
                    data.Header, appearance.CustomHead.Index);
                headParts = Register(SourceTeamGeneratedModels.BuildModel(model)[data.Rosters.ActorModels.HeadJointIndex]);
            }
            else if (appearance.PoppedHead != null)
            {
                DataCatalog.Require(poppedHeadParts is { Length: 1 } &&
                    poppedHeadParts[0].slot == SourceTeamPoppedHeadData.LogicalTextureSlot,
                    "The original phead source mesh must bind raw texture4/logical HEDSHU slot1 at the actual neck.");
                headParts = poppedHeadParts;
            }
            var slots = appearance.Uniform.Slots.Select(slot => "teams/" + slot.Id).ToList();
            if (appearance.CustomHead != null) slots.Add("teams/" + appearance.CustomHead.Texture.Id);
            foreach (var slot in slots)
                DataCatalog.Require(textures.ContainsKey(slot.Substring("teams/".Length)),
                    "Original selected texture is missing: " + slot + ". No recolor/atlas substitution.");
            id = appearances.Count;
            appearances.Add(key, id);
            sink.Appearance(id, JsonSerializer.Serialize(new {
                sourcePlayerId = appearance.SourcePlayerId,
                teamIndex = appearance.Player.Profile.Identity.TeamIndex,
                rosterIndex = appearance.Player.Profile.Identity.RosterIndex,
                uniformIdentity = appearance.UniformIdentity, skin = (int)appearance.Uniform.Skin,
                modelScale = appearance.ModelScale, numberMesh = number,
                numberTexture = slots[data.Rosters.ActorModels.NumberTextureSlot],
                numberMatrix = data.Rosters.ActorModels.NumberMatrixIndex,
                headJoint = data.Rosters.ActorModels.HeadJointIndex, headParts,
                headVariant = appearance.CustomHead != null ? "original-station-custom" :
                    appearance.PoppedHead != null ? "original-phead-source-authored" : "original-helmeted",
                headSourceSha256 = appearance.PoppedHead != null ? SourceTeamPoppedHeadData.SourceSha256 : null,
                headGeometrySha256 = appearance.PoppedHead != null ? SourceTeamPoppedHeadData.HeaderSha256 : null,
                headOrigin = appearance.PoppedHead != null ? SourceTeamPoppedHeadData.Origin : null,
                textures = slots,
                textureHashes = slots.Select(slot => textures[slot.Substring("teams/".Length)].Sha256).ToArray(),
                originalNumber = appearance.Player.Profile.PackedJerseyNumber, tint = "none"
            }));
        }
        return (appearance, id);
    }

    public static byte[] DecodeTga(byte[] bytes, string label)
    {
        TgaImageInfo info = TgaImageInfo.Read(bytes, label);
        var rgba = new byte[info.Width * info.Height * 4];
        int stride = info.BitsPerPixel / 8;
        for (int row = 0; row < info.Height; row++)
        for (int column = 0; column < info.Width; column++)
        {
            int source = info.PixelOffset + (row * info.Width + column) * stride;
            int x = info.RightOrigin ? info.Width - 1 - column : column;
            int y = info.TopOrigin ? info.Height - 1 - row : row;
            int target = (y * info.Width + x) * 4;
            rgba[target] = bytes[source + 2];
            rgba[target + 1] = bytes[source + 1];
            rgba[target + 2] = bytes[source];
            rgba[target + 3] = stride == 4 ? bytes[source + 3] : (byte)255;
        }
        return rgba;
    }
}

internal sealed class SourceTeamSelectionActors
{
    private readonly PlayerData data;
    private readonly SourcePortableProcessHost processes;
    private readonly ISourcePoseInterpolation interpolation;
    private readonly IReadOnlyDictionary<string, SourceAnimationProgram> programs;
    private readonly ActorState[] actors = new ActorState[2];
    private readonly long[] serials = new long[2];
    private readonly int[] initialTeams = new int[2];
    private SourceMatchConfiguration selection;
    private long nextSerial;
    private bool visible;
    internal bool Completed { get; private set; }
    internal long SourceTick => processes.SourceTick;
    internal IReadOnlyList<SourceProcessView> Processes => processes.Processes;
    internal IReadOnlyList<SourcePossessionProcessRecord> Records => processes.Records;
    internal IReadOnlyList<PlayerView> Players => visible
        ? Array.AsReadOnly(actors.Where(actor => actor != null).Select(View).ToArray())
        : Array.Empty<PlayerView>();

    internal SourceTeamSelectionActors(PlayerData data, long sourceTick)
    {
        DataCatalog.Require(data.Catalog.Profile.IsCompleteAv306TwoStationRecords,
            "The original team-selection producer requires its separately selected workflow.");
        this.data = data;
        processes = SourcePortableProcessHost.AtSourceTick(sourceTick);
        interpolation = SourceAnimationSampling.CreateOriginalPoseInterpolation(data.Playbook.Directions.Sin);
        programs = SourceAnimationPrograms.CreateCompleteAv306Extension();
    }

    internal void Advance(long sourceTick, SourceMatchConfiguration selected)
    {
        DataCatalog.Require(!Completed, "A retired team-selection scene cannot resume its old player processes.");
        processes.BeginTick(sourceTick);
        if (selected != null && !visible)
        {
            RetireActors();
            for (int side = 0; side < 2; side++)
            {
                initialTeams[side] = side == 0 ? selected.HomeTeam.TeamIndex : selected.AwayTeam.TeamIndex;
                serials[side] = ++nextSerial;
                processes.Create(serials[side], 0x0500, "tmsel_player_proc", 0);
            }
        }
        visible = selected != null;
        if (visible) selection = selected;
        for (int side = 1; side >= 0; side--)
        {
            if (serials[side] == 0 || !processes.Visit(serials[side])) continue;
            try
            {
                if (actors[side] == null)
                {
                    SourceRosterIdentity roster = new(initialTeams[side], 3);
                    var actor = new ActorState(side == 0 ? 3 : 10) {
                        Role = ActorRole.Quarterback, Action = ActorAction.Idle,
                        SourceMode = SourceActorMode.Quarterback, SourceRoster = roster,
                        SourceControllerStation = -1, ModelScale = data.Rosters.GetPlayerData(roster).Profile.ModelScale,
                        Facing = side == 0 ? 490 : 512, TargetFacing = side == 0 ? 490 : 512,
                        Flipped = side == 0, SourceRawFlags = side == 0 ? 1 : 0
                    };
                    SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(side == 0 ? 18 : -18, 0, 65));
                    actor.AnimationState = new SourceAnimationState(data.Animations, actor.Id, programs);
                    actor.AnimationState.BindSourcePoseInterpolation(interpolation);
                    actor.AnimationState.BindOriginalChangeSemantics();
                    actor.AnimationState.Primary.SourceFrameInterpolationRoots = true;
                    actors[side] = actor;
                    processes.Record("CreateObject", serials[side], 0x0500, actor.Id);
                    actor.AnimationState.FullBody(side == 0 ? "qb_ready_f_anim" : "qb_ready_anim",
                        actor, sourceTick, NoRandom, AnimationSelectionEvidence.SourceVerified);
                    actor.AnimationState.Primary.AdvanceTeamSelectionEntry(actor, sourceTick, NoRandom, processes);
                }
                else
                {
                    if (visible)
                        actors[side].SourceRoster = new SourceRosterIdentity(
                            side == 0 ? selection.HomeTeam.TeamIndex : selection.AwayTeam.TeamIndex, 3);
                    actors[side].AnimationState.Advance(actors[side], sourceTick, NoRandom);
                }
                processes.Sleep(1);
            }
            finally { processes.Complete(); }
        }
    }

    internal void CompleteHandoff()
    {
        visible = false;
        RetireActors();
        Completed = true;
    }

    private void RetireActors()
    {
        for (int side = 0; side < 2; side++)
        {
            if (serials[side] != 0) processes.Kill(serials[side]);
            actors[side] = null;
            serials[side] = 0;
        }
    }

    private PlayerView View(ActorState actor)
    {
        return new PlayerView(actor.Id, actor.Team, actor.Position, default, 0, actor.Height,
            SourcePoseMath.Facing(actor.Facing), ActorRole.Quarterback,
            actor.AnimationState.View(actor, SourceTick), actor.ModelScale,
            new SourceActorView(-1, actor.SourceRoster,
                presentation: new SourceActorPresentationView(actor.SourceRawFlags, 5)));
    }

    internal object RenderPacket(RenderAssets assets, IReadOnlyList<SourceHudPrimitive> hud, long epoch)
    {
        IReadOnlyList<PlayerView> views = Players;
        if (views.Count == 0) return null;
        DataCatalog.Require(views.Count == 2, "The active team-selection scene requires both actual side actors.");
        int boundary = -1;
        for (int i = 0; i < hud.Count; i++)
            if (hud[i].TextContent != null) { boundary = i; break; }
        DataCatalog.Require(boundary > 0, "The team actors require their actual opaque-backdrop/foreground HUD boundary.");
        var matrices = new double[2 * 15 * 16];
        var models = new int[2];
        var appearances = new int[2];
        var meta = new object[2];
        for (int side = 0; side < 2; side++)
        {
            PlayerView player = views.Single(value => (int)value.Team == side);
            var binding = assets.BindTeamSelection(player);
            SourceJointPose pose = SourceAnimationSampling.SampleLayer(data.Animations, player.Animation.Primary);
            SourceEvaluatedPose evaluated = SourceAnimationMatrices.EvaluateSource(data.ReferencePivots, pose,
                player.Animation.Primary, player.FacingDirection, default, 1f);
            SourceVector3 position = SourcePlayerDriver.WorldPosition(actors[side]);
            for (int joint = 0; joint < 15; joint++)
                FrameMath.Joint(matrices, (side * 15 + joint) * 16, evaluated.Joints[joint], position, 1f);
            models[side] = binding.Appearance.Model.Index;
            appearances[side] = binding.Id;
            SourceProcessView process = processes.Require(serials[side]);
            meta[side] = new {
                id = player.Id, team = side, teamIndex = player.Source.Roster.TeamIndex, rosterIndex = 3,
                facing = actors[side].Facing, rawFlags = actors[side].SourceRawFlags,
                script = player.Animation.Primary.ScriptSymbol, sourceFrame = player.Animation.Primary.Sample.SourceFrame,
                processSerial = process.Serial, processBirthTick = process.BirthTick, lastWakeTick = process.LastWakeTick,
                sourcePosition = new[] { position.X, position.Y, position.Z }, ascale = player.ModelScale,
                renderScale = 1, sourceInstance = player.Animation.Primary.InstanceId
            };
        }
        return new {
            schema = "source-tmsel-players/v1", epoch, sourceTick = SourceTick,
            projection = FrameMath.CameraSpace(data.Bootstrap.HorizontalResolution, data.Bootstrap.VerticalResolution,
                Math.Max(128f, 65f + assets.ActorHeightYards * SourceTiming.SourceUnitsPerYard * 4f)),
            joints = matrices, models, appearances, beforeHudPrimitive = boundary, actors = meta
        };
    }

    private static int NoRandom(int bound) =>
        throw new InvalidOperationException("The original ready-animation team-selection loop consumes no RNG.");
}
