using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Presentation;

namespace Blitz.SourcePlayer;

public sealed class SourceWorldRenderPacket
{
    public ReadOnlyCollection<double> Vertices { get; }
    public ReadOnlyCollection<int> Links { get; }
    public string Metadata { get; }

    internal SourceWorldRenderPacket(double[] vertices, int[] links, string metadata)
    {
        DataCatalog.Require(vertices.Length == links.Length / 4 * 30 && links.Length % 4 == 0 &&
            vertices.All(double.IsFinite), "SOURCE_WORLD_PACKET_SIZE: six raster/ST vertices and four selectors per source link required.");
        Vertices = Array.AsReadOnly(vertices); Links = Array.AsReadOnly(links); Metadata = metadata;
    }

    // Reuses the existing Effects import; no new loader import or independent presentation call.
    public string AttachEffects(string effects)
    {
        JsonObject envelope = JsonNode.Parse(effects)?.AsObject() ??
            throw new InvalidDataException("SOURCE_WORLD_EFFECTS_ENVELOPE: the existing effects metadata object is required.");
        DataCatalog.Require(!envelope.ContainsKey("world"), "SOURCE_WORLD_EFFECTS_DUPLICATE: world may be attached only once.");
        envelope["world"] = new JsonObject {
            ["vertices"] = JsonSerializer.SerializeToNode(Vertices),
            ["links"] = JsonSerializer.SerializeToNode(Links), ["state"] = JsonNode.Parse(Metadata)
        };
        return envelope.ToJsonString();
    }

    public string AttachStatus(string status)
    {
        JsonObject envelope = JsonNode.Parse(status)?.AsObject() ??
            throw new InvalidDataException("SOURCE_WORLD_STATUS_ENVELOPE: the actual Present status object is required.");
        DataCatalog.Require(!envelope.ContainsKey("world"), "SOURCE_WORLD_STATUS_DUPLICATE: world may be attached only once.");
        envelope["world"] = JsonNode.Parse(Metadata);
        return envelope.ToJsonString();
    }
}

public sealed class SourceWorldPresentation
{
    private readonly PlayerData data;
    private readonly ISourceWorldRenderSink sink;
    private long observedTick = -1;
    public long Epoch { get; }
    public long SourceFramesObserved { get; private set; }
    public SourceWorldFog Fog { get; }
    public SourceWorldUserFogConfiguration UserConfiguration { get; }
    public int UserFogInitializations => UserConfiguration == null ? 0 : 1;
    public ReadOnlyCollection<SourcePlayerShadow> Actors { get; private set; } =
        Array.AsReadOnly(Array.Empty<SourcePlayerShadow>());
    public SourceWorldRenderPacket Packet { get; private set; }
    public SourceEnvironmentSelection Venue { get; private set; }
    public SourceEnvironmentBundle SelectedWorld { get; private set; }
    public const string Schema = "blitz-source-world/v1";
    private static readonly string[] authoringPaths = {
        "img3d/PSSHOULD.TGA", "img3d/PSLIMB.TGA", "img3d/PSTORSO.TGA", "img3d/PSHAND.TGA"
    };
    private static readonly string[] authoringBlobs = {
        "b05993dc691d1c6d7632c26fa37092d09ebba4d9", "a589bfe2eb87dea0fff00421fdd81c25aad65ea4",
        "cbe1aebd1249da69cf6136e1072c20dc532901c2", "e6f69144c2ff170d25afcc0398c0f2d68ed2c700"
    };

    public SourceWorldPresentation(PlayerData data, ISourceWorldRenderSink sink, long epoch,
        SourceWorldUserFogConfiguration userConfiguration = null)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.sink = sink ?? throw new InvalidDataException("SOURCE_WORLD_RECEIVER_MISSING: source-world=portable requires its actual GPU packet sink.");
        Epoch = epoch;
        var world = data.World.Manifest;
        SelectedWorld = data.World;
        Venue = SourceEnvironmentReader.ValidateSelection(world);
        SourceWorldVenue.RequireAdmitted(data.Bootstrap.FieldIndex, world.stadiumSelector, world.homePrefix,
            world.fieldSelector, world.stadiumSelector, world.homePrefix);
        DataCatalog.Require(!world.textures.Any(texture => authoringPaths.Contains(texture.sourcePath,
            StringComparer.OrdinalIgnoreCase)), "SOURCE_WORLD_RESOURCE_ADMISSION: new shadow art needs an explicitly extended authenticated package; no implicit admission.");
        DataCatalog.Require(!data.Catalog.Profile.UsesCompleteSourcePrograms || data.OriginalPresentation?.Shadows.Count == 4,
            "SOURCE_WORLD_RESOURCE_ADMISSION: CompleteOriginal303 requires its separately authenticated four-material package.");
        UserConfiguration = userConfiguration;
        // nfl.c starts field draw procs, then chooses do_user_fog before the first source wake.
        Fog = userConfiguration?.InitializeAfterFieldProcs() ?? SourceWorldFog.Unbound();
    }

    public void Send(IGameState state, double[] actualJointMatrices, SourceCinematicTransform camera,
        long cameraSourceTick, float interpolation, long presentSequence)
    {
        var source = state as ISourceGameState ??
            throw new InvalidDataException("SOURCE_WORLD_CLOCK: an actual source game snapshot is required.");
        if (data.World.Manifest.schema == SourceEnvironmentSelection.Schema)
        {
            var presentation = state as ISourcePresentationState;
            DataCatalog.Require(presentation?.Presentation != null && presentation.Match != null,
                "SOURCE_WORLD_VENUE_NOT_ADMITTED: the actual designated home side and match teams are required.");
            SourceMatchConfiguration match = presentation.Match.Configuration;
            int homeTeam = presentation.Presentation.DesignatedHomeSide == TeamId.Home ?
                match.HomeTeam.TeamIndex : match.AwayTeam.TeamIndex;
            var actual = new SourceEnvironmentSelection(data.Bootstrap.FieldIndex, Venue.Stadium,
                data.Rosters.GetTeamData(homeTeam).UniformPrefix);
            SelectedWorld = data.RequireWorld(actual);
            Venue = SourceEnvironmentReader.ValidateSelection(SelectedWorld.Manifest);
            SourceWorldVenue.RequireAdmitted(actual.Field, actual.Stadium, actual.HomePrefix,
                Venue.Field, Venue.Stadium, Venue.HomePrefix);
        }
        DataCatalog.Require(source.SourceTick >= 0 && source.SourceTick >= observedTick &&
            (cameraSourceTick >= 0 || cameraSourceTick == -1 && source.SourceTick == 0) &&
            cameraSourceTick <= source.SourceTick &&
            float.IsFinite(interpolation) && interpolation is >= 0 and <= 1 && presentSequence > 0 &&
            state.Players.Count == 14 && actualJointMatrices is { Length: 14 * 15 * 16 } &&
            actualJointMatrices.All(double.IsFinite),
            "SOURCE_WORLD_FRAME: actual monotonic source clock, current render alpha and fourteen finite affine poses required.");
        if (source.SourceTick != observedTick) { SourceFramesObserved++; observedTick = source.SourceTick; }
        var actors = new SourcePlayerShadow[14];
        var vertices = new List<double>();
        var selectors = new List<int>();
        int[] corners = { 0, 1, 2, 2, 3, 0 };
        int[] s = { 255, 255, 0, 0 }, t = { 255, 0, 0, 255 };
        foreach (PlayerView actor in state.Players.OrderBy(p => p.Id))
        {
            var worldJoints = new SourceVector3[15];
            for (int joint = 0; joint < 15; joint++)
            {
                int offset = (actor.Id * 15 + joint) * 16;
                worldJoints[joint] = new SourceVector3((float)actualJointMatrices[offset + 12],
                    (float)actualJointMatrices[offset + 13], (float)actualJointMatrices[offset + 14]);
            }
            SourcePlayerShadow shadow = SourcePlayerShadowModel.Project(actor.Id,
                actor.Source.Presentation.RawFlags, worldJoints, camera,
                (int)data.Bootstrap.HorizontalResolution, (int)data.Bootstrap.VerticalResolution);
            actors[actor.Id] = shadow;
            foreach (SourcePlayerShadowLink link in shadow.Links.Where(link => link.Accepted))
            {
                selectors.AddRange(new[] { actor.Id, link.LinkIndex, link.TextureSlot, link.DepthBias });
                foreach (int corner in corners)
                {
                    SourceVector3 v = link.RasterVertices[corner];
                    vertices.AddRange(new double[] { v.X, v.Y, v.Z, s[corner], t[corner] });
                }
            }
        }
        Actors = Array.AsReadOnly(actors);
        var m = camera.Transform; var eye = camera.Position;
        double[] depthRow = { m[8], m[9], m[10], -(m[8] * eye.X + m[9] * eye.Y + m[10] * eye.Z) + m[11] };
        var manifest = SelectedWorld.Manifest;
        string inheritedCull = manifest.draws.Where(draw => SourceWorldVenue.DrawOrder(draw.renderPass) == 0x10000)
            .OrderBy(draw => draw.renderQueue).Last().state.cullMode;
        string metadata = JsonSerializer.Serialize(new {
            schema = Schema, enabled = true, epoch = Epoch, presentSequence, sourceTick = source.SourceTick,
            cameraSourceTick, renderInterpolation01 = interpolation, sourceFramesObserved = SourceFramesObserved,
            hres = data.Bootstrap.HorizontalResolution, vres = data.Bootstrap.VerticalResolution,
            sourceRevision = SourceEnvironmentReader.SourceCommit, playerGitBlob = SourcePlayerShadowModel.PlayerGitBlob,
            fieldGitBlob = "2821ba68986cb7e62c42cf920747c5ba9b91ea37",
            cameraDepthRow = depthRow,
            venue = DescribeVenue(SelectedWorld),
            fog = new {
                configurationBound = Fog.ConfigurationBound, origin = Fog.Origin.ToString(), enabled = Fog.Enabled,
                sourceEnabled = Fog.SourceEnabled,
                isFoggy = Fog.IsFoggy, fogMask = Fog.FogMask, thickFogMask = Fog.ThickFogMask,
                colorRgb = Fog.ColorRgb, start = Fog.Start, end = Fog.End,
                onDrawOrder = SourceWorldFog.OnDrawOrder, offDrawOrder = SourceWorldFog.OffDrawOrder,
                arithmetic = "portable continuous camera-W linear; native guFogGenerateLinear table/Glide interpolation unqualified",
                userConfiguration = UserConfiguration == null ? null : new {
                    selection = (int)UserConfiguration.Selection, name = UserConfiguration.Selection.ToString(),
                    sourceKey = 4, appliedAt = "vsscreen.c:1041-1049 apply_user_config(GC_FOG), then OR saved power-ups",
                    savedFogMask = UserConfiguration.SavedFogMask, savedThickFogMask = UserConfiguration.SavedThickFogMask,
                    versusFogMask = UserConfiguration.VersusFogMask, versusThickFogMask = UserConfiguration.VersusThickFogMask,
                    initializedAt = "nfl.c:680-685 after do_field_procs, do_user_fog; before source wake1",
                    initializations = UserFogInitializations, fieldSetupIsFoggy = (int?)null
                },
                missingInput = UserConfiguration == null ?
                    "No admitted field_setup.is_foggy outcome, versus fog masks or actual GC_FOG selection." :
                    Fog.SourceEnabled == null ?
                    "GC_FOG None preserves unknown saved versus masks; source enable/early-return and prior is_foggy remain unknown. Rendering stays off." :
                    "Saved versus mask numerics remain unknown unless determined by the actual source branch; no field_setup RNG outcome is inferred."
            },
            shadows = new {
                geometry = "player.c:draw_player_shadow actual rendered joint pivots; not fixed blobs or a shadow map",
                materialReady = data.OriginalPresentation != null, rasterStride = 5, selectorStride = 4, acceptedLinks = selectors.Count / 4,
                sourceDrawOrder = 0x20200, inheritedCull,
                alphaSource = "GR_ALPHASOURCE_TEXTURE_ALPHA", alphaFunction = "GR_CMP_GREATER", alphaValue = 100,
                colorFunction = "GR_COLORCOMBINE_TEXTURE_TIMES_ALPHA", textureFunction = "GR_TEXTURECOMBINE_DECAL",
                clampMode = "GR_TEXTURECLAMP_WRAP", filter = "BILINEAR",
                depthMode = "GR_DEPTHBUFFER_WBUFFER", depthFunction = "GR_CMP_GREATER", depthWrite = true,
                rgbSource = "GR_BLEND_SRC_ALPHA", rgbDestination = "GR_BLEND_ONE_MINUS_SRC_ALPHA",
                alphaSourceBlend = "GR_BLEND_SRC_ALPHA", alphaDestinationBlend = "GR_BLEND_ONE_MINUS_SRC_ALPHA",
                textures = SourcePlayerShadowModel.TextureNames.Select((name, slot) => new {
                    slot, sourceName = name, authoringPath = authoringPaths[slot], authoringGitBlob = authoringBlobs[slot],
                    texture = data.OriginalPresentation?.Shadows[slot].TextureId,
                    textureSha256 = data.OriginalPresentation?.Shadows[slot].Sha256,
                    missing = data.OriginalPresentation == null
                }).ToArray(),
                actors = actors.Select(actor => new { id = actor.ActorId, rawFlags = actor.RawFlags,
                    onScreen = actor.OnScreen, acceptedLinks = actor.Links.Count(link => link.Accepted) }).ToArray(),
                blocker = data.OriginalPresentation == null ?
                    "SOURCE_WORLD_SHADOW_TEXTURES_NOT_ADMITTED: player_shadow_init requires psshould.wms, pslimb.wms, pstorso.wms, pshand.wms. No substitute is drawn." : null,
                depthConversion = "portable LESS plus source-bias/65535 normalized16 proxy; native floating-WBUFFER/depth-bias/pixels unqualified"
            },
            powerups = new {
                field = data.Bootstrap.FieldIndex, infiniteTurboTeamMask = data.Bootstrap.InfiniteTurboTeamMask,
                speed = data.Bootstrap.SpeedPowerup, fastTurboTeamMask = data.Bootstrap.FastTurboTeamMask,
                runOutOfBoundsTeamMask = data.Bootstrap.RunOutOfBoundsTeamMask, violence = data.Bootstrap.ViolencePowerup,
                pupBigBall = data.Configuration.Presentation.PupBigBall,
                policy = "Existing admitted movement/ball/fire consumers unchanged; no world flag or gameplay mutation."
            },
            nativePixelsQualified = false
        });
        Packet = new SourceWorldRenderPacket(vertices.ToArray(), selectors.ToArray(), metadata);
        sink.WorldPresentation(Packet);
    }

    internal static object DescribeVenue(SourceEnvironmentBundle world)
    {
        SourceEnvironmentSelection selection = SourceEnvironmentReader.ValidateSelection(world.Manifest);
        bool selected = world.Manifest.schema == SourceEnvironmentSelection.Schema;
        return new {
            manifest = world.ManifestSha256, stableId = world.Manifest.stableId,
            selectionSchema = world.Manifest.schema, selectionId = world.Manifest.selectionId,
            field = selection.Field, stadium = selection.Stadium, homePrefix = selection.HomePrefix,
            homeTeamIndex = selection.HomeTeamIndex, fieldTextureTable = selection.FieldTextureTable,
            stadiumTextureTable = selection.StadiumTextureTable,
            alternateVenuesAdmitted = selected, teamEndZonesBound = selected
        };
    }
}
