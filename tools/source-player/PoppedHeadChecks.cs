using System.Text.Json;
using System.Text.Json.Nodes;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.SourcePlayer;

static class PoppedHeadChecks
{
    private static int checks;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("POPPED HEAD: " + message);
        checks++;
    }

    public static void Run(PlayerData data)
    {
        Require(data.PoppedHead != null, "The actual original phead input generation must be installed.");
        var head = data.PoppedHead!;
        var limb = head.Limb;
        Require(limb.Symbol == "limb_phead_obj" && limb.Vertices.Count == 83 &&
            limb.TextureCoordinates.Count == 161 && limb.Faces.Count == 157 && !head.NativeGeneratedHeaderEquivalent,
            "The real SDL83/161/157 inventory and portable origin must survive the actual production reader.");
        foreach (int index in Enumerable.Range(0, 32))
        {
            var reference = SourceTeamGeneratedModels.ReadModel(data.Rosters, index, data.Header);
            var mesh = SourceTeamGeneratedModels.BuildPoppedHead(reference, head);
            Require(mesh.Symbol == limb.Symbol && mesh.SubmeshTriangles.Count == 9 &&
                mesh.SubmeshTriangles[1].Count == 157 * 3 &&
                mesh.SubmeshTriangles.Where((_, slot) => slot != 1).All(group => group.Count == 0),
                "All32 body selectors must bind the actual phead neck mesh exclusively to raw4/logical HEDSHU1.");
            var pivot = reference.Nodes[SourceTeamPoppedHeadData.HeadJoint].BindPivot;
            int corner = 0;
            foreach (var face in limb.Faces)
            foreach (int c in Enumerable.Range(0, 3))
            {
                var source = limb.Vertices[face.VertexIndices[c]];
                var st = limb.TextureCoordinates[face.TextureCoordinateIndices[c]];
                int vertex = mesh.SubmeshTriangles[1][corner++];
                var actual = mesh.Positions[vertex];
                var uv = mesh.Uvs[vertex];
                Require(actual.X == source.X - pivot.X && actual.Y == source.Y - pivot.Y &&
                    actual.Z == -source.Z + pivot.Z && uv.X == st.X / 256 && uv.Y == 1 - st.Y / 256,
                    "Actual mapped phead geometry must use original bind pivot, handedness, winding and square SDL UVs.");
            }
        }
        var sink = new Sink();
        var host = new PlayerHost(data, sink);
        var actor = host.Game.State.Players.Single(p => p.Source.ControllerStationId == 0);
        // Isolated render-adapter fixtures only; no live Match/actor/simulation is changed.
        var poppedActor = new PlayerView(actor.Id, actor.Team, actor.Position, actor.Velocity, actor.Turbo01,
            actor.Height, actor.FacingDirection, actor.Role, actor.Animation, actor.ModelScale,
            new SourceActorView(0, actor.Source.Roster, presentation: new SourceActorPresentationView(0x00800000, 5)));
        var normal = host.Assets.Bind(actor, host.Game.State);
        var selected = host.Assets.Bind(poppedActor, host.Game.State);
        var packet = sink.Appearances[selected.Id];
        Require(poppedActor.Source.Presentation.RawFlags == 0x00800000 && selected.Appearance.PoppedHead == head &&
            selected.Appearance.CustomHead == null && normal.Id != selected.Id,
            "The actual provider must retain PF_POP_HELMET and select a distinct real phead appearance, never a flag clear.");
        Require(packet.GetProperty("headParts").ValueKind == JsonValueKind.Array,
            "Actual render binding must select phead geometry/HEDSHU1 at joint8, not the body/custom helmet mesh.");
        var parts = packet.GetProperty("headParts").EnumerateArray().ToArray();
        Require(packet.GetProperty("headVariant").GetString() == "original-phead-source-authored" &&
            packet.GetProperty("headSourceSha256").GetString() == SourceTeamPoppedHeadData.SourceSha256 &&
            parts.Length == 1 && parts[0].GetProperty("slot").GetInt32() == 1 &&
            parts[0].GetProperty("mesh").GetString() == "limbs/limb_phead_obj/1",
            "Actual render binding must select phead geometry/HEDSHU1 at joint8, not the body/custom helmet mesh.");
        Require(packet.GetProperty("headJoint").GetInt32() == 8 &&
            selected.Appearance.UniformIdentity == normal.Appearance.UniformIdentity &&
            packet.GetProperty("textures")[1].GetString() == "teams/" + selected.Appearance.Uniform.Slots[1].Id,
            "Original phead must retain the actual actor/team/skin HEDSHU identity, without tint or uniform substitution.");
        Require(host.Assets.Bind(actor, host.Game.State).Id == normal.Id,
            "Returning to a real nonpopped snapshot must restore the original helmeted appearance.");
        var match = ((ISourceMatchState)host.Game.State).Match.Configuration;
        for (int custom = 0; custom < 20; custom++)
        {
            var draw = new SourceTeamDrawState(false, true, false, false, false, custom, true);
            var customHead = SourceTeamPresentationModel.Select(data.Rosters, match.HomeTeam.TeamIndex,
                match.AwayTeam.TeamIndex, 0, poppedActor, 3, draw, head);
            Require(customHead.CustomHead.Index == custom && customHead.PoppedHead == null,
                "Every original station custom head must retain active-C priority over PF_POP_HELMET.");
        }
        byte[] manifest = data.Catalog.EntryBytes("poppedHead");
        string hash = data.Catalog.EntryHash("poppedHead");
        Reject(() => SourceTeamPoppedHeadData.Read(manifest, hash, path => path.EndsWith(".h", StringComparison.Ordinal) ?
            data.Catalog.Read("popped-head/" + path).Concat(new byte[] { 32 }).ToArray() : data.Catalog.Read("popped-head/" + path)),
            "geometry/header content pin", "A changed real converted header must be rejected by the named content gate.");
        foreach (var mutation in new[] { ("logicalTextureSlot", JsonValue.Create(4)!), ("headerPath", JsonValue.Create("../head.h")!) })
        {
            var changed = JsonNode.Parse(manifest)!; changed[mutation.Item1] = mutation.Item2;
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(changed);
            Reject(() => SourceTeamPoppedHeadData.Read(bytes, AuthorizedAssetRoot.Sha256(bytes),
                path => data.Catalog.Read("popped-head/" + path)), null,
                "Wrong logical slot/path metadata must not relabel an original phead generation.");
        }
        Console.WriteLine(JsonSerializer.Serialize(new { poppedHeadChecks = checks, status = "passed",
            originalSdl = SourceTeamPoppedHeadData.SourceSha256, reconstructedHeader = SourceTeamPoppedHeadData.HeaderSha256,
            boundary = "Original-input parser/mesh/provider adapter checks, including explicitly synthetic actor DTOs; not live gameplay or native head.h pixels." }));
    }

    private static void Reject(Action action, string? text, string message)
    {
        bool rejected = false;
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or PlayerAssetException or SourceRosterException)
        { rejected = text == null || error.Message.Contains(text, StringComparison.Ordinal); }
        Require(rejected, message);
    }

    private sealed class Sink : IRenderSink
    {
        public Dictionary<int, JsonElement> Appearances = new();
        public void Texture(string id, int width, int height, byte[] rgba, int mipCount, bool repeat, bool linear, string hash, string origin) { }
        public void Mesh(string id, double[] vertices, int[] indices) { }
        public void WorldDraw(string mesh, string texture, string state) { }
        public void Model(int id, string joints) { }
        public void Appearance(int id, string metadata) => Appearances.Add(id, JsonDocument.Parse(metadata).RootElement.Clone());
        public void FreeBall(string mesh, string texture, string alternateTexture, string metadata) { }
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] flags, double[] flash, string metadata) { }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hud, int[] colors, string[] textures, int[] counts, string status) { }
    }
}
