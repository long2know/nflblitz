using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Presentation;
using Blitz.Runtime;

internal static class Program
{
    private static int assertions;
    private static int projectionRevisionAssertions;
    private static SourceFireSpfxBundle data;
    private static string bundleRoot, manifestSha;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };

    private sealed class DrawRow
    {
        public long tick; public int bound; public int value; public int line; public long process;
    }
    private sealed class Host
    {
        internal readonly SourceFireSpfxRuntime Runtime;
        internal readonly List<DrawRow> Random = new List<DrawRow>();
        internal readonly List<SourceFireDirectSound> Sounds = new List<SourceFireDirectSound>();
        internal readonly List<SourceFireSite> Wakes = new List<SourceFireSite>();
        internal readonly int[] Flags = new int[14];
        internal readonly List<int> FlagReads = new List<int>(), FlagWrites = new List<int>();
        internal List<int> Tape;
        internal int TapeIndex, Mode = 1, CacheReads, CacheOffset = 1;
        internal int Epoch;
        internal long Tick = -1, NextSerial = 1000;
        internal Action<SourceFireWakeSlot, int> HostContinuation;
        internal SourceFireCachedPlayerFrame CacheOverride;
        internal SourceVector3 Ball = new SourceVector3(-100, 25, 35);
        internal Host(SourceFireStalePolicy stale = SourceFireStalePolicy.PortableCancelDeletedAndFreezeOrphan, int epoch = 100)
        {
            Epoch = epoch; Runtime = new SourceFireSpfxRuntime(data);
            Runtime.Reset(new SourceFireBootstrap(SourceFireOrdering.PortableDeclaredSlots, SourceFireClockOrigin.PortableLogicalEpoch,
                stale, 0, epoch, "declared-component-fixture-pcount-not-native-epoch"));
        }
        internal SourceFireHostCallbacks Callbacks()
        {
            return new SourceFireHostCallbacks(request =>
            {
                int value = 0;
                if (Tape != null)
                { Check(TapeIndex < Tape.Count, "production made an extra shared-host RNG draw"); value = Tape[TapeIndex++]; }
                Check(request.Family == "randrng" && value >= 0 && value < request.Bound, "exclusive source randrng bound/family");
                Random.Add(new DrawRow { tick = Tick, bound = request.Bound, value = value, line = request.Site.SourceLine, process = request.Site.NativeProcessSerial });
                return value;
            }, () => Mode, actor =>
            {
                CacheReads++;
                if (CacheOverride != null) return CacheOverride;
                var joints = new SourceVector3[15];
                for (int i = 0; i < 15; i++) joints[i] = new SourceVector3(i + CacheOffset, i + CacheOffset + 1, i + CacheOffset + 2);
                return new SourceFireCachedPlayerFrame(actor, 0, Camera(0), joints, "declared actual jpos/camera fixture pair; retained from tick0",
                    "explicit component fixture does not execute the actor/cache producer; not a native suspension trace");
            }, () => new SourceFireBallWorldSample(Tick, Ball, "declared live ball at this wake"), actor =>
            { FlagReads.Add(actor); return Flags[actor]; },
            (actor, value, site) => { FlagWrites.Add(actor); Flags[actor] = value; }, site => Wakes.Add(site),
            (slot, ordinal) =>
            { if (HostContinuation == null) throw new Exception("Unexpected real-host slot in component fixture."); HostContinuation(slot, ordinal); },
            sound => { Sounds.Add(sound); return new SourceFireSoundSubmission(false, "original-call-metadata-only-no-DSP"); },
            () => ++NextSerial);
        }
        internal void Step()
        {
            Tick++;
            Runtime.BeginSourceTick(Runtime.PortableHeadThenHostTick(Tick, Epoch + (int)Tick, new SourceFireWakeSlot[0]), Callbacks());
        }
        internal SourceFireSite Site(int actor = -1, long serial = 8000, int ordinal = 0)
        { return new SourceFireSite(Tick, Epoch + (int)Tick, serial, actor < 0 ? 0 : 0x300 + actor, 20, ordinal, actor,
            "player.c", "declared_component_host", 1); }
        internal SourceFireRenderFrame Frame()
        { return Runtime.CaptureFrame(new SourceFireRenderBasis(Camera(Tick), 384, SourceFireCull.Off)); }
        internal void Control(SourceFireOperation op, int id = 0, int mask = -1, long caller = 8000)
        { Runtime.Apply(SourceFireCommand.Control(Site(-1, caller), op, id, mask)); }
        internal long Spawn(SourceFireEntry entry, int a0, int a1, int a2)
        { return Runtime.Apply(SourceFireCommand.Create(Site(entry == SourceFireEntry.BallPuff ? -1 : a0), entry, new SourceFireArgs(a0, a1, a2))); }
    }

    private static SourceFireCameraFrame Camera(long tick)
    {
        return new SourceFireCameraFrame(tick, new SourceVector3(10, 20, 30),
            new float[] { 0, 0, 1, 0, 0, 1, 0, 0, -1, 0, 0, 0 }, "declared rotated cambot fixture, not identity fallback");
    }
    private static int Main(string[] args)
    {
        try
        {
            Check(args.Length == 5, "explicit bundle/hash/case/output/root arguments");
            bundleRoot = args[0]; manifestSha = args[1];
            data = SourceFireSpfxReader.ReadAuthorized(bundleRoot, "manifest.json", manifestSha);
            Loader(); EntryAndLifetimes(); ObjectAndGroupControls(); Producers(); OrderedHost(); object render = Renderer();
            List<object> traces = SourceCases(args[2]);
            List<object> emitters = EmitterSourceCases(args[2]);
            List<object> ballSequence = BallSourceCases(args[2]);
            List<object> controls = ControlSourceCases();
            Inventory(args[4]);
            int retainedAssertions = assertions - projectionRevisionAssertions;
            int beforeLineup = assertions;
            List<object> lineupClears = FlashLineupScopes();
            int lineupScopeAssertions = assertions - beforeLineup;
            File.WriteAllText(args[3], JsonSerializer.Serialize(new { assertions, retainedAssertions, projectionRevisionAssertions, lineupScopeAssertions,
                lineupClears, cases = traces, emitters, ballSequence, controls, render, nativeQualified = false }, Json));
            Console.WriteLine("PASS fire-spfx: " + assertions + " actual production assertions; " + traces.Count +
                " extracted-C component cases; producer integration/Unity execution/native pixels/DSP unqualified.");
            return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine("FIRE-SPFX ASSERTION: " + error); return 1; }
    }

    private static void Loader()
    {
        Equal(data.Frames.Count, 17, "real seventeen original frames");
        Equal(data.Geometry.Positions.Count, 4, "actual original SDL vertices");
        Equal(data.Geometry.VertexIndices.Count, 6, "actual original SDL triangle corners");
        Check(data.Geometry.Positions.All(p => Math.Abs(p.X) == 4 && Math.Abs(p.Y) == 4 && p.Z == 0), "factor4 original source-unit geometry");
        Check(!data.NativeConvertedTextureEquivalent && !data.GeneratedSpfxHeaderEquivalent &&
            data.InputOrigin == SourceFireSpfxReader.InputOrigin, "authoring origin does not imply native converted equivalence");
        var root = new AuthorizedAssetRoot(bundleRoot);
        for (int i = 0; i < 17; i++)
        {
            SourceFireTextureFrame frame = data.Frames[i];
            byte[] original = root.ReadBytes("inputs/img3d/trail" + (i + 1) + ".tga.bytes", 65536);
            TgaImageInfo info = TgaImageInfo.Read(original, frame.SourcePath);
            byte[] rgba = frame.CopyRgba();
            Equal(rgba.Length, 4096, "full original alpha/pixel payload");
            for (int p = 0; p < rgba.Length; p += 4)
            {
                Check(rgba[p] == original[info.PixelOffset + p + 2] && rgba[p + 1] == original[info.PixelOffset + p + 1] &&
                    rgba[p + 2] == original[info.PixelOffset + p] && rgba[p + 3] == original[info.PixelOffset + p + 3],
                    "every production RGBA pixel/alpha preserves original TGA input");
            }
            byte saved = rgba[0]; rgba[0] ^= 255; Equal(frame.CopyRgba()[0], saved, "immutable decoded pixels");
            Equal(frame.IntegerAspect, SourceFireSpfxReader.AspectFromDecodedDimensions(info.Width, info.Height), "derived authoring integer aspect");
        }
        Reject(() => SourceFireSpfxReader.ReadAuthorized(bundleRoot, "manifest.json", new string('0', 64)), "wrong manifest SHA");
        Reject(() => SourceFireSpfxReader.ReadAuthorized(bundleRoot + "/..", "manifest.json", manifestSha), "authorized root dot alias");
        MutatedManifest(m => m.frames = m.frames.Take(16).ToArray(), "seventeen frame count");
        MutatedManifest(m => m.inputs = m.inputs.Take(17).ToArray(), "eighteen originals count");
        MutatedManifest(m => m.inputOrigin = "native-WMS", "wrong native origin");
        MutatedManifest(m => m.nativeConvertedTextureEquivalent = true, "no native TEXUS equivalence");
        MutatedManifest(m => m.generatedSpfxHeaderEquivalent = true, "no generated header claim");
        MutatedManifest(m => m.reuseAuthorizationConfirmed = false, "no inferred redistribution/reuse rights");
        MutatedManifest(m => m.sourceGeometryFactor = 1, "no normalized substitute quad");
        MutatedManifest(m => m.frames[0].integerAspect = 0, "Glide enum is not float width/height");
        MutatedManifest(m => m.frames[0].aspectOrigin = "observed-original-WMS-header", "no false original-header observation");
        MutatedManifest(m => m.frames[0].width = 64, "metadata must match decoded image");
        MutatedManifest(m => m.frames[0].alphaBits = 0, "alpha metadata exact");
        MutatedManifest(m => m.frames[0].topOrigin = true, "origin metadata exact");
        MutatedManifest(m => m.inputs[1].payloadPath = "../trail1.tga", "authorized file escape");
        byte[] bad = root.ReadBytes("inputs/img3d/trail1.tga.bytes", 65536); bad[18] ^= 1;
        byte[] manifestBytes = root.ReadBytes("manifest.json", 65536);
        Reject(() => SourceFireSpfxReader.Read(manifestBytes, manifestSha,
            p => p == "inputs/img3d/trail1.tga.bytes" ? bad : root.ReadBytes(p, 65536)), "wrong actual payload hash");
        SourceFireSpfxManifest originalManifest = Manifest(manifestBytes);
        originalManifest.inputs[1].sha256 = AuthorizedAssetRoot.Sha256(bad);
        byte[] repinned = JsonSerializer.SerializeToUtf8Bytes(originalManifest, Json);
        Reject(() => SourceFireSpfxReader.Read(repinned, AuthorizedAssetRoot.Sha256(repinned),
            p => p == "inputs/img3d/trail1.tga.bytes" ? bad : root.ReadBytes(p, 65536)), "changing manifest SHA cannot change original Git pin");
        byte[] geometry = root.ReadBytes("geometry.bytes", 65536);
        byte[] wrongIndex = (byte[])geometry.Clone(); BitConverter.GetBytes(4).CopyTo(wrongIndex, 100);
        Reject(() => SourceFireSpfxReader.ReadGeometry(wrongIndex), "actual SDL vertex index range");
        byte[] wrongUv = (byte[])geometry.Clone(); BitConverter.GetBytes(4).CopyTo(wrongUv, 124);
        Reject(() => SourceFireSpfxReader.ReadGeometry(wrongUv), "actual SDL UV index range");
        byte[] wrongFormat = (byte[])geometry.Clone(); wrongFormat[0] ^= 1;
        Reject(() => SourceFireSpfxReader.ReadGeometry(wrongFormat), "geometry format");
        SourceFireTextureRecord textureRecord = Manifest(manifestBytes).frames[0];
        foreach (int header in new[] { 2, 12, 16, 17 })
        {
            byte[] changed = root.ReadBytes("inputs/img3d/trail1.tga.bytes", 65536);
            changed[header] = header == 2 ? (byte)10 : header == 12 ? (byte)16 : header == 16 ? (byte)24 : (byte)40;
            Reject(() => SourceFireSpfxReader.DecodeOriginalTga(changed, textureRecord, "declared bad original", manifestSha, 1),
                "real decoder rejects changed format/dimensions/alpha/origin");
        }
        Equal(SourceFireSpfxReader.AspectFromDecodedDimensions(256, 32), 0, "Glide8x1 enum");
        Equal(SourceFireSpfxReader.AspectFromDecodedDimensions(32, 256), 6, "Glide1x8 enum");
        Reject(() => SourceFireSpfxReader.AspectFromDecodedDimensions(30, 32), "unrepresentable authoring dimensions");
    }

    private static void EntryAndLifetimes()
    {
        var h = new Host(); h.Step(); h.Tape = new List<int> { 2, 1, 3000, 3 };
        long p = h.Spawn(SourceFireEntry.PlayerPuff, 2, 7, 0);
        Equal(h.CacheReads, 0, "qcreate does not sample pose");
        Equal(h.Random.Count, 0, "qcreate does not run entry or draw RNG inline");
        Equal(h.Frame().Particles.Count, 0, "birth has no puff object yet");
        h.CacheOffset = 101; h.Step();
        SourceFireParticle puff = h.Frame().Particles.Single();
        Near(puff.WorldPosition.X, -98f, "first wake samples changed actual paired cache");
        Near(puff.WorldPosition.Y, 130f, "first visible player puff has jitter but no rise");
        Near(puff.WorldPosition.Z, 138f, "transpose rotation and no Z jitter");
        Near(puff.VelocityY, .6f, "original player rise velocity");
        Equal(puff.TextureFrame, 3, "source first texture selection");
        Check(h.Random.Select(r => r.bound).SequenceEqual(new[] { 5, 3, 10000, 4 }), "original ordered entry RNG bounds");
        Check(h.Random.All(r => r.process == p), "each waking child shares the Core host ledger under its own process context");
        Equal(h.Wakes.Single().NativeRunLevel, 0, "qcreate defaults to level0, not group-derived actor level");
        var missing = new Host(); missing.Step(); missing.Spawn(SourceFireEntry.PlayerPuff, 0, 4, 0);
        missing.CacheOverride = new SourceFireCachedPlayerFrame(1, 0, Camera(0), Enumerable.Repeat(new SourceVector3(1, 2, 3), 15).ToArray(), "wrong actor");
        Reject(() => missing.Step(), "no guessed wrist/cache actor fallback");
        Reject(() => new SourceFireCachedPlayerFrame(0, 0, Camera(0), new SourceVector3[14], "bad"), "all fifteen paired joints required");
        Reject(() => new SourceFireCachedPlayerFrame(0, 0, Camera(1), new SourceVector3[15], "unpaired"),
            "previous jpos must not use current camera");
        var oldCache = new Host(); oldCache.Step(); oldCache.Step(); oldCache.Spawn(SourceFireEntry.PlayerPuff, 0, 4, 0);
        oldCache.CacheOverride = new SourceFireCachedPlayerFrame(0, 0, Camera(0), Enumerable.Repeat(new SourceVector3(1, 2, 3), 15).ToArray(), "old-without-suspension");
        Reject(() => oldCache.Step(), "unexplained stale paired cache fails, never freshens by inference");
        var ball = new Host(); ball.Step(); ball.Spawn(SourceFireEntry.BallPuff, 4, 2, 0); ball.Ball = new SourceVector3(-200, 40, 50);
        ball.Tape = new List<int> { 3, 4, 4 }; ball.Step();
        SourceFireParticle b = ball.Frame().Particles.Single();
        Near(b.WorldPosition.X, -203f, "ball samples live point at first wake");
        Near(b.WorldPosition.Y, 42.35f, "ball puff first visible sample already rises");
        Equal(b.TextureFrame, 4, "ball end evaluated before start");
        Equal(ball.Runtime.Processes.Single().End, 13, "ball end17-randrng5");
        var burst = new Host(); burst.Step(); burst.Mode = SourceFireIds.InPlay;
        long parent = burst.Spawn(SourceFireEntry.PlayerBurst, 4, 5, 4); burst.Step();
        Check(burst.Runtime.Processes.Count(pv => pv.Entry == SourceFireEntry.PlayerPuff) == 6, "live-entry parent still emits one full wave");
        Equal(burst.Runtime.Processes.Single(pv => pv.Serial == parent).SleepField, (ushort)4, "parent sleeps original delay after wave");
        for (int i = 0; i < 4; i++) burst.Step();
        Check(burst.Runtime.Processes.All(pv => pv.Serial != parent), "post-sleep live check ends parent, not its already created children");
        Check(burst.Runtime.Processes.Any(pv => pv.Entry == SourceFireEntry.PlayerPuff), "losing parent is not automatic child cancellation");
    }

    private static void ObjectAndGroupControls()
    {
        var h = new Host(); h.Step();
        long player = h.Spawn(SourceFireEntry.PlayerPuff, 1, 4, 0);
        long ball = h.Spawn(SourceFireEntry.BallPuff, 3, -1, 2);
        long flash = h.Spawn(SourceFireEntry.FlashPlayerWhite, 1, 10, 3);
        h.Flags[1] = 0x55; h.Step();
        Check(h.Wakes.Select(s => s.NativeProcessSerial).SequenceEqual(new[] { flash, ball, player }), "known head insertion reverses births");
        Check(h.Frame().Particles.Select(o => o.ProcessSerial).SequenceEqual(new[] { ball, player }), "new equal-priority objects follow existing nodes");
        h.Control(SourceFireOperation.HideAllObjects);
        Check(h.Frame().Particles.All(o => o.Hidden), "hide affects all existing objects");
        long later = h.Spawn(SourceFireEntry.PlayerPuff, 2, 7, 0); h.Step();
        Check(!h.Frame().Particles.Single(o => o.ProcessSerial == later).Hidden, "hide is not a global future-object latch");
        h.Control(SourceFireOperation.UnhideAllObjects);
        Check(h.Frame().Particles.All(o => !o.Hidden), "unhide existing objects");
        h.Control(SourceFireOperation.KillProcessGroup, SourceFireIds.Spfx);
        Check(h.Runtime.Processes.Any(p => p.Serial == ball) && h.Runtime.Processes.Any(p => p.Serial == flash), "SPFX kill does not widen to PLAYER or PFLASH");
        Equal(h.Frame().Particles.Count, 3, "kill has no automatic object deletion");
        Check(h.Frame().PortableBoundaries.Any(b => b.StartsWith("portable:freeze-orphan")), "orphan stack-data policy explicitly reported");
        h.Control(SourceFireOperation.KillProcessGroup, SourceFireIds.PlayerFlash);
        Check((h.Flags[1] & 0x400) != 0, "flash kill does not implicitly clear shared raw bit");
        h.Runtime.Apply(SourceFireCommand.ReinitializePlayerFlash(h.Site(), SourceFireLineupScope.FromOriginalSide(-1)));
        Equal(h.Flags[1], 0x55, "real lineup clears exactly PF_FLASHWHITE");
        h.Control(SourceFireOperation.DeleteObjectGroup, SourceFireIds.PuffObject);
        Equal(h.Frame().Particles.Count, 0, "object mask deletion distinct from process kill");
        Check(h.Runtime.Processes.Any(p => p.Serial == ball), "object deletion alone does not kill PLAYER continuation");
        h.Step();
        Check(h.Runtime.Processes.All(p => p.Serial != ball) && h.Frame().PortableBoundaries.Any(b => b.Contains("cancel-deleted")),
            "portable deleted-node cancellation does not invent native node reuse");
        var strict = new Host(SourceFireStalePolicy.RejectUndefinedAccess); strict.Step();
        strict.Spawn(SourceFireEntry.BallPuff, 3, -1, 2); strict.Step();
        strict.Control(SourceFireOperation.DeleteObjectGroup, SourceFireIds.PuffObject);
        Reject(() => strict.Step(), "strict stale-node boundary");
        var susp = new Host(); susp.Step();
        long spfx = susp.Spawn(SourceFireEntry.PlayerPuff, 1, 4, 0);
        long bp = susp.Spawn(SourceFireEntry.BallPuff, 3, -1, -2);
        susp.Control(SourceFireOperation.SuspendMask, SourceFireIds.Spfx, 0);
        Check(susp.Runtime.Processes.All(p => (p.SleepField & 0x8000) == 0), "suspend compares masked pid to UNMASKED requested id");
        susp.Control(SourceFireOperation.SuspendMask, 0, 0x8000);
        Check(susp.Runtime.Processes.All(p => p.SleepField == 0x8001), "suspension preserves short sleep");
        susp.Control(SourceFireOperation.ResumeMask, SourceFireIds.Spfx);
        susp.Step();
        Check(susp.Frame().Particles.Single().ProcessSerial == spfx && susp.Runtime.Processes.Single(p => p.Serial == bp).SleepField == 0x8001,
            "SPFX resumes in selection; PLAYER ball puff stays suspended");
        susp.Control(SourceFireOperation.ResumeAll); susp.Step();
        Check(susp.Frame().Particles.Any(p => p.ProcessSerial == bp), "resume_all preserves and resumes sleeping continuation");
        susp.Control(SourceFireOperation.KillProcessGroup, SourceFireIds.Spfx, -1, spfx);
        Check(susp.Runtime.Processes.Any(p => p.Serial == spfx), "killall excludes caller");
        var delayed = new Host(); delayed.Step(); long delayedFlash = delayed.Spawn(SourceFireEntry.FlashPlayerWhite, 3, 2, 3);
        delayed.Step(); delayed.Step();
        Equal(delayed.Runtime.Processes.Single().SleepField, (ushort)2, "flash continuation partway through sleep3");
        delayed.Control(SourceFireOperation.SuspendMask, 0, 0x8000);
        delayed.Step(); delayed.Step();
        Equal(delayed.Runtime.Processes.Single().SleepField, (ushort)0x8002, "suspended visits do not age remaining sleep");
        delayed.Control(SourceFireOperation.ResumeMask, SourceFireIds.PlayerFlash, -1, delayedFlash);
        Equal(delayed.Runtime.Processes.Single().SleepField, (ushort)2, "resume includes caller and clears only suspension bit");
        delayed.Step(); Check((delayed.Flags[3] & 0x400) != 0, "resuming sleep2 does not clear white early");
        delayed.Flags[3] |= 0x2000; delayed.Step();
        Equal(delayed.Flags[3], 0x2000, "flash clears shared0x400 in order while preserving a competing writer's other bits");
    }

    private static List<object> FlashLineupScopes()
    {
        var rows = new List<object>();
        foreach (int side in new[] { 0, 1, -1 })
        {
            string label = side < 0 ? "ALL" : "SIDE" + side;
            var h = new Host(); h.Step();
            for (int actor = 0; actor < 14; actor++) h.Flags[actor] = 0x440;
            h.Runtime.WeaponPreSnapTail(h.Site(4), 4, 4, 2, false, false, false);
            h.Runtime.WeaponPreSnapTail(h.Site(4), 4, 4, 3, false, false, false);
            h.Runtime.DefensivePreSnapTail(h.Site(10), 10, true);
            long otherFlash = h.Spawn(SourceFireEntry.FlashPlayerWhite, side == 1 ? 0 : 7, 10, 3);
            long[] processes = h.Runtime.Processes.Select(p => p.Serial).ToArray();
            int defenseLatch = h.Runtime.DefensiveFlashCount;
            h.FlagReads.Clear(); h.FlagWrites.Clear();
            int rngBefore = h.Random.Count;
            // Deliberately keep actor0 in the site: it must never imply all-player scope.
            var site = new SourceFireSite(h.Tick, h.Epoch, 8000, 0, 20, 0, 0, "nfl.c", "lineup_players", 3271);
            h.Runtime.Apply(SourceFireCommand.ReinitializePlayerFlash(site, SourceFireLineupScope.FromOriginalSide(side)));
            for (int actor = 0; actor < 14; actor++)
            {
                bool selected = side < 0 || actor / 7 == side;
                Equal(h.Flags[actor], selected ? 0x40 : 0x440,
                    "FLASH " + label + (selected ? " selected raw bits preserved" : " untouched other-team raw flags"));
            }
            int[] expected = Enumerable.Range(side == 1 ? 7 : 0, side < 0 ? 14 : 7).ToArray();
            Check(h.FlagReads.SequenceEqual(expected), "FLASH " + label + " exact host read-set");
            Check(h.FlagWrites.SequenceEqual(expected), "FLASH " + label + " exact host write-set");
            Equal(h.Random.Count - rngBefore, 0, "FLASH " + label + " clear consumes zero RNG");
            Check(h.Runtime.Processes.Select(p => p.Serial).SequenceEqual(processes)
                && h.Runtime.Processes.Any(p => p.Serial == otherFlash && p.GroupId == SourceFireIds.PlayerFlash),
                "FLASH " + label + " clear preserves sleeping PFLASH processes");
            Check(h.Runtime.AlmostFireFlash && h.Runtime.FireFlash && h.Runtime.DefensiveFlashCount == defenseLatch,
                "FLASH " + label + " actor clear does not reset separate global latches");
            rows.Add(new { side, reads = h.FlagReads.ToArray(), writes = h.FlagWrites.ToArray(),
                flags = (int[])h.Flags.Clone(), rngDelta = h.Random.Count - rngBefore });
            h.Runtime.Apply(SourceFireCommand.Control(site, SourceFireOperation.ResetLineupLatches));
            Check(!h.Runtime.AlmostFireFlash && !h.Runtime.FireFlash && h.Runtime.DefensiveFlashCount == 0
                && h.FlagWrites.SequenceEqual(expected), "FLASH " + label + " global latch reset remains separate without actor writes");
        }
        var bits = new Host(); bits.Step();
        for (int actor = 0; actor < 14; actor++) bits.Flags[actor] = unchecked((int)0xf1202440);
        bits.Runtime.Apply(SourceFireCommand.ReinitializePlayerFlash(bits.Site(0), SourceFireLineupScope.FromOriginalSide(1)));
        for (int actor = 0; actor < 14; actor++)
            Equal(bits.Flags[actor], actor < 7 ? unchecked((int)0xf1202440) : unchecked((int)0xf1202040),
                "FLASH side1 preserves all unowned raw bits");
        var invalid = new Host(); invalid.Step();
        Reject(() => SourceFireLineupScope.FromOriginalSide(2), "FLASH invalid positive side");
        Reject(() => SourceFireLineupScope.FromOriginalSide(-2), "FLASH only explicit canonical all-player -1");
        Reject(() => SourceFireCommand.ReinitializePlayerFlash(invalid.Site(0), default(SourceFireLineupScope)),
            "FLASH default scope must not imply actor0 or global clear");
        Reject(() => invalid.Control(SourceFireOperation.ReinitializePlayerFlash), "FLASH generic id0 control cannot clear globally");
        Reject(() => invalid.Control(SourceFireOperation.ReinitializePlayerFlash, -1), "FLASH generic id-1 is not a typed all-player request");
        Check(invalid.FlagReads.Count == 0 && invalid.FlagWrites.Count == 0 && invalid.Random.Count == 0,
            "FLASH invalid scope has no host flag or RNG side effects");
        return rows;
    }

    private static void Producers()
    {
        var h = new Host(epoch: 101); h.Step(); h.Tape = new List<int> { 3 };
        h.Runtime.MaybePlayerFire(h.Site(3), new SourceFirePlayerEmitterInput(3, true, 0, 1, 0, 0, false, true, true));
        Equal(h.Random.Count, 1, "eligible maybe_plyr_fire draws before failed pcount modulo gate");
        Equal(h.Runtime.Processes.Count, 0, "modulo rejection does not create puff");
        var e = new Host(); e.Step();
        e.Runtime.MaybePlayerFire(e.Site(3), new SourceFirePlayerEmitterInput(3, true, 0, 1, 0, 1, false, true, true));
        Equal(e.Runtime.Processes.Single().Args.A1, 7, "flipped offensive QB uses LWRIST");
        e.Runtime.MaybePlayerFire(e.Site(4), new SourceFirePlayerEmitterInput(4, true, 1, 0, 0, 1, true, true, false));
        Equal(e.Runtime.Processes[0].Args.A1, 4, "non-QB offense always RWRIST");
        int before = e.Random.Count;
        e.Runtime.MaybePlayerFire(e.Site(5), new SourceFirePlayerEmitterInput(5, true, -1, 1, 0, 0, true, false, false));
        e.Runtime.MaybePlayerFire(e.Site(5), new SourceFirePlayerEmitterInput(5, true, 0, 1, 0, 0x100000, true, false, false));
        e.Runtime.MaybePlayerFire(e.Site(5), new SourceFirePlayerEmitterInput(5, false, 0, 1, 0, 0, true, false, false));
        e.Runtime.MaybePlayerFire(e.Site(5), new SourceFirePlayerEmitterInput(5, true, 0, 0, 0, 0, false, false, false));
        Equal(e.Random.Count, before, "ineligible drag/CPU/not-fire/stationary calls draw nothing");
        e.Runtime.MaybePlayerFire(e.Site(10), new SourceFirePlayerEmitterInput(10, true, 2, 0, -1, 0, false, false, false));
        Check(e.Runtime.Processes.Take(3).Select(pv => pv.Args.A1).SequenceEqual(new[] { 0, 11, 14 }), "defense creates LANKLE,RANKLE,PELVIS without blue/inactive path");
        var p = new Host(); p.Step();
        p.Runtime.WeaponPreSnapTail(p.Site(4), 4, 4, 2, true, false, false);
        Check(p.Runtime.AlmostFireFlash && p.Runtime.FireFlash && p.Runtime.Processes.Count == 2,
            "independent heating and team-fire branches both run; no extra !team_fire guard");
        Equal(p.Sounds.Single().Call, 193, "heating direct sound193 exactly once");
        Check(p.Sounds[0].Bank == "players" && p.Sounds[0].Volume == 235 && p.Sounds[0].Pan == 127 && p.Sounds[0].ResolvedPriority == 0,
            "heating sound original bank/volume/pan/priority");
        p.Runtime.WeaponPreSnapTail(p.Site(4), 4, 4, 3, true, false, false);
        Equal(p.Runtime.Processes.Count, 2, "shared burning latch prevents duplicate independent branch requests");
        for (int i = 0; i < 5; i++) p.Runtime.DefensivePreSnapTail(p.Site(9), 9, true);
        Equal(p.Runtime.DefensiveFlashCount, 4, "global defensive limit4, not one per actor or human-only");
        p.Control(SourceFireOperation.ResetLineupLatches);
        Check(!p.Runtime.AlmostFireFlash && !p.Runtime.FireFlash && p.Runtime.DefensiveFlashCount == 0, "lineup explicitly resets all three latches");
        var c = new Host(); c.Step();
        c.Runtime.SuccessfulOffensiveCatch(c.Site(4), 4, 4, 2, 0, true);
        Check(c.Sounds.Select(s => s.Call).SequenceEqual(new[] { 192, 192 }) && c.Runtime.Processes.Single().Args.A1 == 6,
            "potential catch fire is two ordered calls and six-wave burst before controlled fire");
        Check(c.Frame().Records.Select(r => r.Kind).SequenceEqual(new[] { "DirectSound", "DirectSound", "CreateProcess" }), "sound duplicate/order is not coalesced");
        c.Mode = SourceFireIds.PlayOver; c.Runtime.SuccessfulOffensiveCatch(c.Site(4), 4, 4, 2, 0, true);
        Equal(c.Sounds.Count, 2, "raw GM_PLAY_OVER guard at exact catch site");
        var d = new Host(); d.Step();
        Reject(() => d.Runtime.DefensiveSecondLossFireSite(d.Site(), "actual_selected_announcer_fixture", null, null), "unknown pobj_hit is not known NULL");
        Equal(d.Sounds.Single().Call, 185, "unknown selected hit blocks conditional branch only, not announcer185");
        d.Runtime.DefensiveSecondLossFireSite(d.Site(), "actual_selected_announcer_fixture", -1, null);
        Equal(d.Runtime.Processes.Count, 0, "known NULL does not infer defender/tackler target");
        d.Runtime.DefensiveSecondLossFireSite(d.Site(), "actual_selected_announcer_fixture", 12, null);
        Equal(d.Runtime.Processes.Single().Args.A0, 12, "burst targets actual selected QB hit partner");
        Check(d.Sounds.Select(s => s.Call).SequenceEqual(new[] { 185, 185, 185, 192, 192 }), "announcer then two exact player calls");
        var b = new Host(); b.Step();
        Equal(b.Runtime.BallFreeOrPhantomStep(b.Site(), new SourceFireBallEmitterInput(SourceBallKind.Bobble, false, false, false, false, 2)),
            SourceFireBallEffect.None, "ball private selector starts NONE");
        b.Runtime.BallFreeOrPhantomStep(b.Site(), new SourceFireBallEmitterInput(SourceBallKind.Pass, true, false, false, false, 2));
        Check(b.Runtime.Processes.Reverse().Select(pv => pv.Args.A0).SequenceEqual(new[] { 4, 3, 3 }) &&
            b.Runtime.Processes.Reverse().Select(pv => pv.Args.A2).SequenceEqual(new[] { 0, 2, -2 }), "ordered exact PLAYER_PID triplet arguments");
        Equal(b.Runtime.BallFreeOrPhantomStep(b.Site(), new SourceFireBallEmitterInput(SourceBallKind.Bobble, false, false, false, false, 3)),
            SourceFireBallEffect.Fire, "Bobble retains previous effect even without FirePass flag");
        Equal(b.Runtime.Processes.Count, 3, "odd post-increment flight time does not emit");
        Equal(b.Runtime.BallFreeOrPhantomStep(b.Site(), new SourceFireBallEmitterInput(SourceBallKind.Lateral, true, true, false, true, 4)),
            SourceFireBallEffect.None, "lateral cancels selector without killing existing puffs");
        b.Runtime.BallFreeOrPhantomStep(b.Site(), new SourceFireBallEmitterInput(SourceBallKind.FieldGoal, false, false, false, true, 6));
        Equal(b.Runtime.BallEffect, SourceFireBallEffect.Fire, "unbounced field goal uses offense team fire");
        Equal(b.Runtime.BallFreeOrPhantomStep(b.Site(), new SourceFireBallEmitterInput(SourceBallKind.FieldGoal, false, false, true, true, 8)),
            SourceFireBallEffect.Blink, "field goal bounce precedes fire");
        b.Mode = SourceFireIds.InPlay;
        Equal(b.Runtime.BallFreeOrPhantomStep(b.Site(), new SourceFireBallEmitterInput(SourceBallKind.Punt, true, false, false, true, 9)),
            SourceFireBallEffect.BallTrail, "punt keeps original nonfire trail");
        b.Mode = SourceFireIds.PlayOver;
        Equal(b.Runtime.BallFreeOrPhantomStep(b.Site(), new SourceFireBallEmitterInput(SourceBallKind.Pass, true, false, false, true, 10)),
            SourceFireBallEffect.None, "raw play-over overrides selected FIRE");
        b.Control(SourceFireOperation.ResetBallProcessEffect); Equal(b.Runtime.BallEffect, SourceFireBallEffect.None, "actual ball_proc entry reset explicit");
    }

    private static void OrderedHost()
    {
        var h = new Host(); h.Step(); long effect = h.Spawn(SourceFireEntry.PlayerPuff, 1, 4, 0);
        var order = new List<string>();
        h.HostContinuation = (slot, ordinal) =>
        {
            order.Add(slot.Function);
            if (slot.Function == "game_proc") h.Ball = new SourceVector3(-300, 45, 60);
            Equal(ordinal, slot.Function == "game_proc" ? 0 : 2, "Core continuation executes in declared native0 slot");
        };
        h.Tick = 1;
        h.Runtime.BeginSourceTick(new SourceFireTickInput(1, 101, new[] {
            new SourceFireWakeSlot(SourceFireSlotKind.HostContinuation, 8000, "game_proc"),
            new SourceFireWakeSlot(SourceFireSlotKind.EffectProcess, effect, "plyr_fire_trail_proc"),
            new SourceFireWakeSlot(SourceFireSlotKind.HostContinuation, 8001, "individual_C2_whistle_proc")
        }, "portable:declared-game-effect-whistle-order", true), h.Callbacks());
        Check(order.SequenceEqual(new[] { "game_proc", "individual_C2_whistle_proc" }), "no independent whistle-before-FX phase controller");
        Equal(h.Wakes.Single().WakeOrdinal, 1, "effect RNG uses exact declared wake ordinal, separate from run level/group");
        var strict = new SourceFireSpfxRuntime(data);
        strict.Reset(new SourceFireBootstrap(SourceFireOrdering.VerifiedDispatchWitness, SourceFireClockOrigin.VerifiedOriginalPcount,
            SourceFireStalePolicy.RejectUndefinedAccess, 0, 100, "component boundary test only, no native claim"));
        Reject(() => strict.BeginSourceTick(new SourceFireTickInput(0, 100, new SourceFireWakeSlot[0], "unknown-sort-tie"), h.Callbacks()),
            "strict rejects missing actual sort/order witness");
        var missing = new Host(); missing.Step(); missing.Spawn(SourceFireEntry.PlayerPuff, 0, 4, 0); missing.Tick = 1;
        Reject(() => missing.Runtime.BeginSourceTick(new SourceFireTickInput(1, 101, new SourceFireWakeSlot[0], "portable:omitted"), missing.Callbacks()),
            "portable input cannot silently omit default-level0 process nodes");
    }

    private static object Renderer()
    {
        var h = new Host(); h.Step(); h.Spawn(SourceFireEntry.PlayerPuff, 2, 7, 0); h.Step();
        SourceFireRenderFrame frame = h.Frame();
        var consumer = new SourceFireSpfxFrameConsumer(data);
        int draws = h.Random.Count;
        IReadOnlyList<SourceFireDrawPacket> packets = consumer.Consume(frame);
        SourceFireDrawPacket packet = packets.Single();
        Equal(packet.CameraVertices.Count, 6, "production renderer seam uses actual two source triangles");
        SourceVector3 center = SourceFireSpfxFrameConsumer.CameraTranslation(frame.Particles[0].WorldPosition, frame.Basis.Camera);
        for (int i = 0; i < 6; i++)
        {
            SourceVector3 original = data.Geometry.Positions[data.Geometry.VertexIndices[i]], actual = packet.CameraVertices[i];
            Near(actual.X, original.Y + center.X, "source fixed camera basis swaps X/Y");
            Near(actual.Y, original.X + center.Y, "source fixed camera basis maps X to Y");
            Near(actual.Z, -original.Z + center.Z, "source fixed basis reverses Z");
            int uv = data.Geometry.UvIndices[i] * 2;
            Near(packet.UnityUvs[2 * i], data.Geometry.RawAuthoringUvs[uv], "actual original source UV index");
            Near(packet.UnityUvs[2 * i + 1], data.Geometry.RawAuthoringUvs[uv + 1], "explicit portable original-authoring T origin");
        }
        Check(packet.State.DepthWrite && packet.State.DepthFunction == "ALWAYS" && packet.State.RequestedSourceDepth == "WBUFFER" &&
            packet.State.DepthOrigin.Contains("not-native") && !packet.State.ChromaKey && packet.State.AlphaTest == "ALWAYS", "precise draw_spfx depth/write/alpha-test boundary");
        Check(packet.State.BlendSourceRgb == "SRC_ALPHA" && packet.State.BlendSourceAlpha == "SRC_ALPHA" &&
            packet.State.BlendDestinationRgb == "ONE_MINUS_SRC_ALPHA" && packet.State.BlendDestinationAlpha == "ONE_MINUS_SRC_ALPHA",
            "both color and alpha blend factors are source-alpha, never additive");
        Check(packet.State.ColorCombine == "TEXTURE_TIMES_ALPHA" && packet.State.AlphaSource == "TEXTURE_ALPHA" &&
            packet.State.TextureCombine == "DECAL" && packet.State.Wrap == "WRAP/WRAP" && packet.State.Filter == "BILINEAR/BILINEAR",
            "source combine, authentic frame sampling and wrap");
        SourceFireRgba combined = SourceFireMaterialState.Combine(new SourceFireRgba(1, .5f, .25f, .5f));
        Near(combined.R, .5f, "texture times alpha precedes separate source-alpha blend");
        Near(combined.G, .25f, "combine green"); Near(combined.B, .125f, "combine blue"); Near(combined.A, .5f, "texture alpha source");
        Check(ReferenceEquals(consumer.Consume(frame), packets), "repeated/skipped host rendering holds exact immutable frame");
        Equal(h.Random.Count, draws, "zero renderer/host-frame RNG and no effect aging");
        Reject(() => new SourceFireParticle(1, 1001, new SourceVector3(-100, 20, 30), .35f, 17, 3, false), "frame17 exceeds actual seventeen-frame data");
        var wrongGeneration = new SourceFireRenderFrame(2, 102, frame.Bootstrap, "portable:wrong-generation",
            new SourceFireRenderBasis(Camera(2), 384, SourceFireCull.Off), frame.Particles, frame.ActorRawFlags,
            new SourceFireRecord[0], new string[0], new string('0', 64));
        Reject(() => consumer.Consume(wrongGeneration), "render consumer refuses a different authenticated asset generation");
        int beforeProjection = assertions;
        List<object> projection = Projection(frame.Bootstrap);
        // J1 replaces two F30 projection assertions; measure its added coverage separately.
        projectionRevisionAssertions = assertions - beforeProjection - 2;
        var all = new List<SourceFireParticle>();
        for (int i = 0; i < 17; i++) all.Add(new SourceFireParticle(i + 1, i + 100, new SourceVector3(-100, 20, 30), .35f, i, 3, i == 7));
        var full = new SourceFireRenderFrame(2, 102, frame.Bootstrap, "portable:fixture", new SourceFireRenderBasis(Camera(2), 256, SourceFireCull.Front),
            all, new int[14], new SourceFireRecord[0], new string[0], data.ManifestSha256);
        IReadOnlyList<SourceFireDrawPacket> outputs = consumer.Consume(full);
        Equal(outputs.Count, 16, "hidden existing object omitted, other authentic frames retained");
        foreach (SourceFireDrawPacket output in outputs)
        {
            Equal(output.TextureSourceSha256, data.Frames[output.TextureFrame].SourceSha256, "actual ordinal original TRAIL binding");
            Equal(output.State.InheritedCull, SourceFireCull.Front, "draw_spfx inherits caller cull, does not invent a cull reset");
        }
        var white = new SourceFirePlayerFlashState(0x1400);
        Check(white.White && white.OverrideColorCombine == "DECAL_TEXTURE_ADD_CCALPHA" && white.ConstantColor == 0xc0000000 &&
            white.RestoreColorCombine == "TEXTURE_TIMES_ALPHA" && white.RestoreConstantColor == 0xff000000, "exact player raw flash combine/restoration handoff");
        Check(!new SourceFirePlayerFlashState(0x1000).White, "unrelated player bit is not a fire tint");
        var declared = new SourceFireRenderFrame(3, 103, frame.Bootstrap, "portable:declared-draw_spfx-C-fixture",
            new SourceFireRenderBasis(Camera(3), 384, SourceFireCull.Off),
            new[] { new SourceFireParticle(1, 1001, new SourceVector3(-100, 25, 35), .35f, 0, 3, false) },
            new int[14], new SourceFireRecord[0], new string[0], data.ManifestSha256);
        SourceFireDrawPacket actualDraw = consumer.Consume(declared).Single();
        SourceFireMaterialState state = actualDraw.State;
        return new { cameraVertices = actualDraw.CameraVertices.Select(p => new[] { p.X, p.Y, p.Z }), projection,
            symbolicStates = new[] { state.AlphaSource == "TEXTURE_ALPHA" ? 1 : -1,
                0, state.TextureCombine == "DECAL" ? 2 : -1, 0, state.Wrap == "WRAP/WRAP" ? 3 : -1, state.Wrap == "WRAP/WRAP" ? 3 : -1,
                state.RequestedSourceDepth == "WBUFFER" ? 4 : -1, state.DepthFunction == "ALWAYS" ? 5 : -1,
                state.AlphaTest == "ALWAYS" ? 5 : -1, state.DepthWrite ? 1 : 0, !state.ChromaKey ? 6 : -1,
                state.ColorCombine == "TEXTURE_TIMES_ALPHA" ? 7 : -1, state.BlendSourceRgb == "SRC_ALPHA" ? 8 : -1,
                state.BlendDestinationRgb == "ONE_MINUS_SRC_ALPHA" ? 9 : -1, state.BlendSourceAlpha == "SRC_ALPHA" ? 8 : -1,
                state.BlendDestinationAlpha == "ONE_MINUS_SRC_ALPHA" ? 9 : -1 } };
    }

    private static List<object> Projection(SourceFireBootstrap bootstrap)
    {
        var rows = new List<object>();
        foreach (int height in new[] { 384, 256 })
        {
            foreach (int side in new[] { 1, -1 })
            {
                var camera = new SourceVector3(side, 2, 100);
                SourceVector3 raster = SourceFireSpfxFrameConsumer.ProjectContinuous(camera, height);
                Near(raster.X, side > 0 ? 249.36f : 262.64f,
                    "geom.s horizontal projection " + (side > 0 ? "+X" : "-X") + " raster512x" + height);
                Near(raster.Y, height == 384 ? 205.28f : 136.853333f,
                    "geom.s original vertical scale raster512x" + height);
                Near(raster.Z, .01f, "geom.s reciprocal depth unchanged");
                rows.Add(new { origin = "direct-camera", verticalResolution = height,
                    camera = new[] { camera.X, camera.Y, camera.Z }, raster = new[] { raster.X, raster.Y, raster.Z } });
            }
            foreach (int side in new[] { 1, -1 })
            {
                SourceFireDrawPacket before = null;
                var consumer = new SourceFireSpfxFrameConsumer(data);
                for (int moved = 0; moved < 2; moved++)
                {
                    long tick = 10 + moved;
                    var offset = new SourceVector3(moved * 7, moved * -5, moved * 9);
                    var originalCamera = Camera(tick);
                    var camera = new SourceFireCameraFrame(tick, new SourceVector3(
                        originalCamera.Position.X + offset.X, originalCamera.Position.Y + offset.Y,
                        originalCamera.Position.Z + offset.Z), originalCamera.Transform,
                        "declared rotated camera/world co-translation projection fixture");
                    var world = new SourceVector3(-90 + offset.X, 32 + offset.Y, 30 + side * 20 + offset.Z);
                    var frame = new SourceFireRenderFrame(tick, 100 + (int)tick, bootstrap,
                        "portable:projection-camera-world-coupling", new SourceFireRenderBasis(camera, height, SourceFireCull.Off),
                        new[] { new SourceFireParticle(1, 1001, world, .35f, 0, 3, false) },
                        new int[14], new SourceFireRecord[0], new string[0], data.ManifestSha256);
                    SourceFireDrawPacket packet = consumer.Consume(frame).Single();
                    for (int i = 0; i < packet.CameraVertices.Count; i++)
                    {
                        SourceVector3 original = data.Geometry.Positions[data.Geometry.VertexIndices[i]];
                        SourceVector3 actual = packet.CameraVertices[i], raster = packet.ProjectedRasterVertices[i];
                        // The declared rotated camera maps (world-camera) to (Z,Y,-X).
                        // Keep draw_spfx cm unreflected; geom.s applies negation afterwards.
                        float x = original.Y + side * 20, y = original.X + 12, z = -original.Z + 100;
                        Near(actual.X, x, "original cm camera X not falsified for projection");
                        Near(actual.Y, y, "original cm camera Y unchanged for projection");
                        Near(actual.Z, z, "original cm camera Z unchanged for projection");
                        Near(raster.X, 256f - 664f * x / z, "geom.s packet horizontal projection");
                        Near(raster.Y, height * .5f + 664f * y / z * (height == 256 ? 2f / 3f : 1f),
                            "geom.s packet vertical projection");
                        Check(side > 0 ? raster.X < 256f : raster.X > 256f,
                            "nonzero world/camera X projects to the original opposite raster side");
                        if (before != null)
                        {
                            Near(actual.X, before.CameraVertices[i].X, "co-moving world/camera preserves transformed X");
                            Near(actual.Y, before.CameraVertices[i].Y, "co-moving world/camera preserves transformed Y");
                            Near(actual.Z, before.CameraVertices[i].Z, "co-moving world/camera preserves transformed Z");
                            Near(raster.X, before.ProjectedRasterVertices[i].X, "co-moving world/camera preserves raster X");
                            Near(raster.Y, before.ProjectedRasterVertices[i].Y, "co-moving world/camera preserves raster Y");
                        }
                        rows.Add(new { origin = moved == 0 ? "consumer-before-co-translation" : "consumer-after-co-translation",
                            verticalResolution = height, camera = new[] { actual.X, actual.Y, actual.Z },
                            raster = new[] { raster.X, raster.Y, raster.Z } });
                    }
                    before = packet;
                }
            }
        }
        return rows;
    }

    private static List<object> SourceCases(string path)
    {
        var results = new List<object>();
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path)))
        {
            foreach (JsonElement fixture in document.RootElement.GetProperty("cases").EnumerateArray())
            {
                string name = fixture.GetProperty("name").GetString();
                var h = new Host();
                h.Mode = fixture.GetProperty("modeSwitchTick").GetInt32() == 0 ? 2 : 1;
                h.Flags[2] = 0x55;
                int[] args = fixture.GetProperty("args").EnumerateArray().Select(e => e.GetInt32()).ToArray();
                h.Tape = fixture.GetProperty("draws").ValueKind == JsonValueKind.Null ? null :
                    fixture.GetProperty("draws").EnumerateArray().Select(e => e.GetInt32()).ToList();
                SourceFireEntry entry = (SourceFireEntry)Enum.Parse(typeof(SourceFireEntry), fixture.GetProperty("kind").GetString());
                h.Step(); long serial = h.Spawn(entry, args[0], args[1], args[2]);
                var rows = new List<object>(); var childJoints = new List<int>(); var childLines = new List<int>(); var returnCauses = new List<int>();
                int count = fixture.GetProperty("ticks").GetInt32();
                for (int t = 0; t <= count; t++)
                {
                    if (t > 0)
                    { h.Mode = t >= fixture.GetProperty("modeSwitchTick").GetInt32() ? 2 : 1; h.Step(); }
                    SourceFireRenderFrame frame = h.Frame();
                    foreach (SourceFireRecord record in frame.Records.Where(r => r.Kind == "CreateProcess" && r.ProcessSerial != serial))
                    {
                        SourceFireProcessView child = h.Runtime.Processes.Single(pv => pv.Serial == record.ProcessSerial);
                        childJoints.Add(child.Args.A1); childLines.Add(record.Site.SourceLine);
                    }
                    returnCauses.AddRange(frame.Records.Where(r => r.Kind == "ReturnDie" && r.ProcessSerial == serial).Select(r => r.Value));
                    SourceFireProcessView process = h.Runtime.Processes.FirstOrDefault(pv => pv.Serial == serial);
                    SourceFireParticle[] own = frame.Particles.Where(o => o.ProcessSerial == serial).ToArray();
                    rows.Add(new { tick = t, alive = process != null, sleep = process == null ? 0 : (int)process.SleepField,
                        frame = own.Length == 0 ? -1 : own[0].TextureFrame, x = own.Length == 0 ? 0 : own[0].WorldPosition.X,
                        y = own.Length == 0 ? 0 : own[0].WorldPosition.Y, z = own.Length == 0 ? 0 : own[0].WorldPosition.Z,
                        vy = own.Length == 0 ? 0 : own[0].VelocityY, flags = h.Flags[2], childJoints = childJoints.ToArray(),
                        rngCount = h.Random.Count(r => r.process == serial) });
                }
                List<DrawRow> random = h.Random.Where(r => r.process == serial).ToList();
                Check(returnCauses.SequenceEqual(new[] { 1 }), "normal shell return retires with die1, never auto-deletes objects");
                results.Add(new { name, rows, random = random.Select(r => new { r.tick, r.bound, r.value, r.line }), childLines, returnCauses });
            }
        }
        return results;
    }

    private static SourceFireSpfxManifest Manifest(byte[] bytes)
    {
        using (var stream = new MemoryStream(bytes))
            return (SourceFireSpfxManifest)new DataContractJsonSerializer(typeof(SourceFireSpfxManifest)).ReadObject(stream);
    }
    private static object[] Requests(Host host, int start)
    {
        return host.Frame().Records.Skip(start).Where(r => r.Kind == "CreateProcess").Select(record =>
        {
            SourceFireProcessView process = host.Runtime.Processes.Single(p => p.Serial == record.ProcessSerial);
            return (object)new { args = new[] { process.Args.A0, process.Args.A1, process.Args.A2, process.Args.A3 },
                group = process.GroupId, line = record.Site.SourceLine };
        }).ToArray();
    }
    private static List<object> EmitterSourceCases(string path)
    {
        var results = new List<object>();
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path)))
            foreach (JsonElement fixture in document.RootElement.GetProperty("maybeCases").EnumerateArray())
            {
                var host = new Host(epoch: fixture.GetProperty("pcount").GetInt32());
                host.Step(); host.Tape = new List<int> { fixture.GetProperty("draw").GetInt32() };
                int actor = fixture.GetProperty("actor").GetInt32();
                host.Runtime.MaybePlayerFire(host.Site(actor), new SourceFirePlayerEmitterInput(actor,
                    fixture.GetProperty("teamFire").GetBoolean(), fixture.GetProperty("station").GetInt32(),
                    fixture.GetProperty("vx").GetSingle(), fixture.GetProperty("vz").GetSingle(), fixture.GetProperty("flags").GetInt32(),
                    fixture.GetProperty("reacting").GetBoolean(), fixture.GetProperty("offense").GetBoolean(), fixture.GetProperty("qb").GetBoolean()));
                results.Add(new { name = fixture.GetProperty("name").GetString(), requests = Requests(host, 0),
                    random = host.Random.Select(r => new { r.tick, r.bound, r.value, r.line }) });
            }
        return results;
    }
    private static List<object> BallSourceCases(string path)
    {
        var results = new List<object>(); var host = new Host(); host.Step();
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path)))
            foreach (JsonElement fixture in document.RootElement.GetProperty("ballSequence").EnumerateArray())
            {
                host.Mode = fixture.GetProperty("mode").GetInt32();
                int start = host.Frame().Records.Count;
                SourceFireBallEffect effect = host.Runtime.BallFreeOrPhantomStep(host.Site(),
                    new SourceFireBallEmitterInput((SourceBallKind)Enum.Parse(typeof(SourceBallKind), fixture.GetProperty("kind").GetString()),
                        fixture.GetProperty("fire").GetBoolean(), fixture.GetProperty("turbo").GetBoolean(), fixture.GetProperty("bounce").GetBoolean(),
                        fixture.GetProperty("teamFire").GetBoolean(), fixture.GetProperty("flight").GetInt32()));
                results.Add(new { effect = effect.ToString(), requests = Requests(host, start) });
            }
        Equal(host.Random.Count, 0, "original ball selector/triplet has no direct RNG");
        return results;
    }
    private static List<object> ControlSourceCases()
    {
        var results = new List<object>(); var host = new Host(); host.Step();
        host.Spawn(SourceFireEntry.PlayerPuff, 1, 4, 0);
        long ball = host.Spawn(SourceFireEntry.BallPuff, 3, -1, 2);
        host.Spawn(SourceFireEntry.FlashPlayerWhite, 1, 10, 3); host.Step();
        Action<string> capture = stage => results.Add(new { stage,
            processes = host.Runtime.Processes.Select(p => new { serial = p.Serial, group = p.GroupId, sleep = (int)p.SleepField }),
            objects = host.Frame().Particles.Select(p => new { owner = p.ProcessSerial, hidden = p.Hidden }) });
        capture("first-entries");
        host.Control(SourceFireOperation.HideAllObjects); capture("hidden-existing");
        host.Spawn(SourceFireEntry.PlayerPuff, 2, 7, 0); host.Step(); capture("new-equal-priority-not-hidden");
        host.Control(SourceFireOperation.SuspendMask, SourceFireIds.Spfx, 0); capture("masked-pid-not-masked-id");
        host.Control(SourceFireOperation.UnhideAllObjects); capture("unhidden-existing");
        host.Control(SourceFireOperation.KillProcessGroup, SourceFireIds.Spfx); capture("SPFX-kill-not-object-delete");
        host.Control(SourceFireOperation.KillProcessGroup, SourceFireIds.PlayerFlash); capture("PFLASH-separate");
        host.Control(SourceFireOperation.DeleteObjectGroup, SourceFireIds.PuffObject); capture("objects-deleted-PLAYER-remains");
        host.Control(SourceFireOperation.SuspendMask, 0, 0x8000); capture("suspended-player-preserves-sleep");
        host.Control(SourceFireOperation.ResumeMask, SourceFireIds.Spfx); capture("SPFX-resume-does-not-resume-PLAYER");
        host.Control(SourceFireOperation.ResumeAll); capture("resume-all-preserves-sleep");
        host.Control(SourceFireOperation.KillProcessGroup, SourceFireIds.Player, -1, ball); capture("killall-excludes-caller");
        return results;
    }
    private static void MutatedManifest(Action<SourceFireSpfxManifest> mutate, string reason)
    {
        var root = new AuthorizedAssetRoot(bundleRoot);
        SourceFireSpfxManifest manifest = Manifest(root.ReadBytes("manifest.json", 65536)); mutate(manifest);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        Reject(() => SourceFireSpfxReader.Read(bytes, AuthorizedAssetRoot.Sha256(bytes), p => root.ReadBytes(p, 65536)), reason);
    }
    private static void Inventory(string root)
    {
        string[] owned = { "Contracts/SourceFireSpfxContracts.cs", "AssetImport/SourceFireSpfxData.cs", "Runtime/SourceFireSpfxRuntime.cs",
            "Presentation/SourceFireSpfxRenderModel.cs", "Presentation/SourceFireSpfxAsset.cs", "Presentation/SourceFireSpfxRenderer.cs", "Editor/SourceFireSpfxImporter.cs" };
        foreach (string path in owned)
            Check(File.Exists(Path.Combine(root, "Assets/Scripts", path)), "exact seven new production scripts really exist: " + path);
    }
    private static void Check(bool value, string message)
    { assertions++; if (!value) throw new Exception(message); }
    private static void Equal<T>(T actual, T expected, string message)
    { Check(EqualityComparer<T>.Default.Equals(actual, expected), message + "; actual=" + actual + " expected=" + expected); }
    private static void Near(float actual, float expected, string message)
    { Check(Math.Abs(actual - expected) < .0001f, message + "; actual=" + actual + " expected=" + expected); }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); }
        catch (PlayerAssetException) { rejected = true; }
        catch (InvalidOperationException) { rejected = true; }
        catch (ArgumentException) { rejected = true; }
        catch (SerializationException) { rejected = true; }
        Check(rejected, "required rejection: " + message);
    }
}
