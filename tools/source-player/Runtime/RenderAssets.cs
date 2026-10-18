using System.Collections.ObjectModel;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Runtime;
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
        }
        UploadBall();
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
        for (int i = 0; i < data.World.Manifest.textures.Length; i++)
        {
            var info = data.World.Manifest.textures[i];
            sink.Texture("world/" + i, info.width, info.height, data.World.TextureRgba[i], info.mipCount,
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
        foreach (var pair in data.Hud.Textures)
        {
            SourceInterfaceTexture texture = pair.Value;
            byte[] rgba = data.Hud.RequireTextureBytes(pair.Key, data.Catalog.Read("hud/" + texture.Path));
            sink.Texture("hud/" + pair.Key, texture.Width, texture.Height, rgba, 1, false,
                !texture.PointSampled, texture.Sha256, "original authored HUD raster / portable origin");
            TextureCount++;
        }
    }

    private void UploadWorld()
    {
        var world = data.World;
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
                sink.WorldDraw(id, "world/" + binding.textureIndex, JsonSerializer.Serialize(new {
                    name = draw.name, renderPass = draw.renderPass, draw.renderQueue,
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
        // Original hand001b's material6 is the ball, not the skin triangles. Recenter that
        // exact geometry for the separate ball. Its native free-ball orientation is unqualified.
        BindPoseLimbMesh hand = models[8][4];
        var triangles = hand.SubmeshTriangles[6];
        DataCatalog.Require(triangles.Count >= 12, "Original ball material6 geometry is missing; no sphere fallback.");
        var points = triangles.Distinct().Select(i => hand.Positions[i]).ToArray();
        var center = new AssetVector3((points.Min(p => p.X) + points.Max(p => p.X)) / 2,
            (points.Min(p => p.Y) + points.Max(p => p.Y)) / 2,
            -(points.Min(p => p.Z) + points.Max(p => p.Z)) / 2);
        const string id = "original/free-ball/hand001b-material6";
        meshes.Add(id);
        sink.Mesh(id, Vertices(hand, center), triangles.ToArray());
        var uniform = data.Rosters.GetUniform(data.Configuration.Match.HomeTeam.TeamIndex,
            SourceUniformVariant.Home, SourceSkinSelector.White);
        string texture = "teams/" + uniform.Slots[6].Id;
        sink.FreeBall(id, texture, "original hand001b material6, recentered; native free-ball orientation unqualified");
    }

    public (SourceTeamAppearance Appearance, int Id) Bind(PlayerView actor, IGameState state)
    {
        SourceTeamDrawState draw = SourceInterfacePresentation.DrawState(actor, state);
        var match = ((ISourceMatchState)state).Match;
        var appearance = SourceTeamPresentationModel.Select(data.Rosters,
            match.Configuration.HomeTeam.TeamIndex, match.Configuration.AwayTeam.TeamIndex,
            (int)SourceInterfacePresentation.Settings(state).DesignatedHomeSide, actor, state.Ball.CarrierId, draw);
        string key = appearance.SourcePlayerId + "/" + appearance.UniformIdentity + "/" +
            (int)appearance.Uniform.Skin + "/" + appearance.Numbers.LimbSymbol + "/" +
            (appearance.CustomHead?.Index ?? -1);
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
