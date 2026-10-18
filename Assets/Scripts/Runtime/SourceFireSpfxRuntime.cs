using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.AssetImport;
using Blitz.Contracts;

namespace Blitz.Runtime
{
    public sealed class SourceFireSpfxRuntime : ISourceFireSpfxRuntime
    {
        private sealed class Process
        {
            internal long Serial, BirthTick, ObjectSerial;
            internal SourceFireEntry Entry;
            internal SourceFireArgs Args;
            internal int Group, State, Cursor, End;
            internal ushort Sleep = 1;
        }
        private sealed class Puff
        {
            internal long Serial, Owner;
            internal SourceVector3 Position;
            internal float VelocityY;
            internal int Frame;
            internal bool Hidden;
        }

        private readonly SourceFireSpfxBundle data;
        private readonly LinkedList<Process> processes = new LinkedList<Process>();
        private readonly Dictionary<long, LinkedListNode<Process>> bySerial = new Dictionary<long, LinkedListNode<Process>>();
        private readonly HashSet<long> usedProcessSerials = new HashSet<long>();
        private readonly List<Puff> objects = new List<Puff>();
        private readonly List<SourceFireRecord> records = new List<SourceFireRecord>();
        private readonly List<string> portableBoundaries = new List<string>();
        private SourceFireBootstrap bootstrap;
        private SourceFireHostCallbacks host;
        private SourceFireTickInput tick;
        private long nextObject;
        private SourceFireSite wakeSite;
        private int wakeCall;
        private bool dispatching, faulted;
        public bool AlmostFireFlash { get; private set; }
        public bool FireFlash { get; private set; }
        public int DefensiveFlashCount { get; private set; }
        public SourceFireBallEffect BallEffect { get; private set; }
        public SourceFireSpfxBundle Data { get { return data; } }

        public SourceFireSpfxRuntime(SourceFireSpfxBundle data)
        { this.data = data ?? throw new ArgumentNullException(nameof(data)); }

        public void Reset(SourceFireBootstrap value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            SourceFireGuard.Require(!dispatching, "Cannot reset while executing a source wake.");
            bootstrap = value; tick = null; host = null; faulted = false;
            processes.Clear(); bySerial.Clear(); usedProcessSerials.Clear(); objects.Clear(); records.Clear(); portableBoundaries.Clear();
            AlmostFireFlash = FireFlash = false; DefensiveFlashCount = 0; BallEffect = SourceFireBallEffect.None; nextObject = 1;
        }

        public ReadOnlyCollection<SourceFireProcessView> Processes
        {
            get
            {
                var result = new List<SourceFireProcessView>();
                foreach (Process p in processes)
                    result.Add(new SourceFireProcessView(p.Serial, p.BirthTick, p.Entry, p.Group, p.Sleep, p.Cursor, p.End, p.ObjectSerial, p.Args));
                return result.AsReadOnly();
            }
        }
        public ReadOnlyCollection<SourceFireRecord> CurrentRecords
        { get { return Array.AsReadOnly(records.ToArray()); } }

        // A deliberately named portable ordering choice, not an emulation of target qsort ties.
        public SourceFireTickInput PortableHeadThenHostTick(long sourceTick, int pcount, IReadOnlyList<SourceFireWakeSlot> declaredHostSlots)
        {
            SourceFireGuard.Require(bootstrap != null && bootstrap.Ordering == SourceFireOrdering.PortableDeclaredSlots,
                "Strict mode cannot manufacture an ordering witness.");
            if (declaredHostSlots == null) throw new ArgumentNullException(nameof(declaredHostSlots));
            var slots = new List<SourceFireWakeSlot>();
            foreach (Process p in processes)
                slots.Add(new SourceFireWakeSlot(SourceFireSlotKind.EffectProcess, p.Serial, Function(p.Entry)));
            foreach (SourceFireWakeSlot slot in declaredHostSlots)
            {
                SourceFireGuard.Require(slot.Kind == SourceFireSlotKind.HostContinuation, "Supply only declared real Core continuations.");
                slots.Add(slot);
            }
            return new SourceFireTickInput(sourceTick, pcount, slots, "portable:known-head-inserted-effects-then-explicit-host-slots");
        }

        public void BeginSourceTick(SourceFireTickInput input, SourceFireHostCallbacks callbacks)
        {
            SourceFireGuard.Require(bootstrap != null && input != null && callbacks != null && !dispatching && !faulted,
                "Explicit bootstrap/tick/host required; a failed dispatch cannot be retried against partially advanced RNG.");
            SourceFireGuard.Require(input.SourceTick == (tick == null ? bootstrap.FirstSourceTick : tick.SourceTick + 1) &&
                input.OriginalPcount == (tick == null ? bootstrap.FirstPcount : unchecked(tick.OriginalPcount + 1)),
                "Execute every57Hz source dispatch, including menus, with the declared pcount epoch; render frames do not advance it.");
            if (bootstrap.Ordering == SourceFireOrdering.VerifiedDispatchWitness)
            {
                SourceFireGuard.Require(IsWitness(input.OrderEvidence), "Strict native ordering needs an externally verified dispatch-witness SHA256, including sort/restart visits.");
                SourceFireGuard.Require(!input.SortOccurred || input.KnownEffectListOrder != null,
                    "A verified sort also needs the resulting effect-node projection of the real linked list; no stable qsort ties are invented.");
            }
            else
            {
                SourceFireGuard.Require(input.OrderEvidence.StartsWith("portable:", StringComparison.Ordinal),
                    "Portable tie/order/epoch assumptions must be labelled, never reported as a native sort witness.");
                var visited = new HashSet<long>();
                foreach (SourceFireWakeSlot slot in input.Slots)
                    if (slot.Kind == SourceFireSlotKind.EffectProcess)
                        SourceFireGuard.Require(!slot.WitnessedRestartVisit && bySerial.ContainsKey(slot.ProcessSerial) && visited.Add(slot.ProcessSerial),
                            "Portable slots must visit each pre-existing effect once, without unknown nodes or invented restarts.");
                SourceFireGuard.Require(visited.Count == processes.Count, "Portable level-zero input omitted an existing effect node.");
            }
            if (input.KnownEffectListOrder != null) ReorderKnownEffects(input.KnownEffectListOrder);
            else if (bootstrap.Ordering == SourceFireOrdering.PortableDeclaredSlots)
            {
                var projection = new List<long>();
                foreach (SourceFireWakeSlot slot in input.Slots)
                    if (slot.Kind == SourceFireSlotKind.EffectProcess) projection.Add(slot.ProcessSerial);
                ReorderKnownEffects(projection);
            }
            tick = input; host = callbacks; records.Clear(); dispatching = true;
            try
            {
                for (int ordinal = 0; ordinal < input.Slots.Count; ordinal++)
                {
                    SourceFireWakeSlot slot = input.Slots[ordinal];
                    if (slot.Kind == SourceFireSlotKind.HostContinuation)
                    {
                        SourceFireGuard.Require(!usedProcessSerials.Contains(slot.ProcessSerial), "Host/effect serial collision; allocate identities in the shared Core host.");
                        host.RunHostContinuation(slot, ordinal);
                        continue;
                    }
                    LinkedListNode<Process> node;
                    if (!bySerial.TryGetValue(slot.ProcessSerial, out node))
                    {
                        SourceFireGuard.Require(usedProcessSerials.Contains(slot.ProcessSerial), "Dispatch witness references an unknown effect node.");
                        continue; // An earlier source slot killed it.
                    }
                    Process p = node.Value;
                    SourceFireGuard.Require(slot.Function == Function(p.Entry) && (p.BirthTick < tick.SourceTick ||
                        bootstrap.Ordering == SourceFireOrdering.VerifiedDispatchWitness && slot.WitnessedRestartVisit),
                        "A birth queues sleep_time1; it cannot run inline in its creator's wake.");
                    if ((short)p.Sleep <= 0) continue;
                    p.Sleep--;
                    if (p.Sleep != 0) continue;
                    wakeSite = new SourceFireSite(tick.SourceTick, tick.OriginalPcount, p.Serial, 0, ordinal, 0,
                        p.Entry == SourceFireEntry.BallPuff ? -1 : p.Args.A0, "player.c", Function(p.Entry), EntryLine(p.Entry));
                    wakeCall = 0;
                    host.EnterEffectWake(wakeSite);
                    Record("Wake", wakeSite, p, 0, 0, slot.Function);
                    if (p.ObjectSerial != 0 && FindObject(p.ObjectSerial) == null)
                    {
                        if (bootstrap.StalePolicy == SourceFireStalePolicy.RejectUndefinedAccess)
                            throw new SourceFireBoundaryException("Original continuation references a deleted object node; target pointer reuse is undefined.");
                        Boundary("portable:cancel-deleted-object-continuation:" + p.Serial);
                        Retire(p, "PortableStaleCancel", wakeSite, 0);
                        continue;
                    }
                    Run(p);
                }
            }
            catch { faulted = true; throw; }
            finally { dispatching = false; }
        }

        public long Apply(SourceFireCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            RequireSite(command.Site);
            if (command.Operation == SourceFireOperation.CreateProcess)
            {
                long serial = host.AllocateEffectProcessSerial();
                SourceFireGuard.Require(serial > 0 && serial != command.Site.NativeProcessSerial && usedProcessSerials.Add(serial),
                    "Host must allocate a fresh globally distinct logical process serial for qcreate.");
                var p = new Process { Serial = serial, BirthTick = tick.SourceTick, Entry = command.Entry, Args = command.Args, Group = command.GroupId };
                bySerial.Add(serial, processes.AddFirst(p));
                Record("CreateProcess", command.Site, p, 0, command.GroupId, command.ProcessName);
                return serial;
            }
            if (command.Operation == SourceFireOperation.DirectSoundCall)
            {
                SourceFireSoundSubmission submission = host.SubmitSound(command.Sound);
                SourceFireGuard.Identity(submission.Evidence, "sound resolution evidence");
                SourceFireGuard.Require(!submission.Resolved || command.Sound.ResolvedPriority.HasValue,
                    "An unresolved priority cannot become a resolved direct sound submission.");
                records.Add(new SourceFireRecord("DirectSound", command.Site, command.Site.NativeProcessSerial, 0,
                    command.Sound.Call, command.Sound.Bank + "|" + command.Sound.PriorityExpression + "|resolved=" + submission.Resolved + "|" + submission.Evidence));
                return 0;
            }
            if (command.Operation == SourceFireOperation.DeleteObjectGroup)
            {
                for (int i = 0; i < objects.Count;)
                    if ((SourceFireIds.PuffObject & command.Mask) == command.GroupId)
                    { RecordObject("DeleteObjectGroup", command.Site, objects[i]); objects.RemoveAt(i); }
                    else i++;
            }
            else if (command.Operation == SourceFireOperation.HideAllObjects || command.Operation == SourceFireOperation.UnhideAllObjects)
            {
                foreach (Puff obj in objects)
                { obj.Hidden = command.Operation == SourceFireOperation.HideAllObjects; RecordObject("ObjectHidden", command.Site, obj); }
            }
            else if (command.Operation == SourceFireOperation.ResetLineupLatches)
            { AlmostFireFlash = FireFlash = false; DefensiveFlashCount = 0; }
            else if (command.Operation == SourceFireOperation.ResetBallProcessEffect)
            { BallEffect = SourceFireBallEffect.None; }
            else if (command.Operation == SourceFireOperation.ReinitializePlayerFlash)
            {
                SourceFireLineupScope scope = command.LineupScope;
                for (int actor = scope.FirstActor; actor < scope.EndActorExclusive; actor++) WriteFlash(actor, false, command.Site);
            }
            else
            {
                LinkedListNode<Process> node = processes.First;
                while (node != null)
                {
                    Process p = node.Value; node = node.Next;
                    bool caller = p.Serial == command.Site.NativeProcessSerial;
                    switch (command.Operation)
                    {
                        case SourceFireOperation.KillProcessGroup:
                            if (!caller && (p.Group & command.Mask) == (command.GroupId & command.Mask))
                                Retire(p, "KillProcess", command.Site, 0);
                            break;
                        case SourceFireOperation.SuspendMask:
                            if (!caller && (p.Group & command.Mask) == command.GroupId)
                            { p.Sleep |= 0x8000; Record("Suspend", command.Site, p, p.ObjectSerial, p.Sleep, "preserve-short-continuation"); }
                            break;
                        case SourceFireOperation.ResumeMask:
                            if ((p.Group & command.Mask) == command.GroupId)
                            { p.Sleep &= 0x7fff; Record("Resume", command.Site, p, p.ObjectSerial, p.Sleep, "preserve-short-continuation"); }
                            break;
                        case SourceFireOperation.ResumeAll:
                            p.Sleep &= 0x7fff; Record("Resume", command.Site, p, p.ObjectSerial, p.Sleep, "resume_all");
                            break;
                        default: throw new SourceFireBoundaryException("Unsupported source fire control operation.");
                    }
                }
            }
            return 0;
        }

        public SourceFireRenderFrame CaptureFrame(SourceFireRenderBasis basis)
        {
            SourceFireGuard.Require(tick != null && basis != null && basis.Camera.SourceTick <= tick.SourceTick && !dispatching && !faulted,
                "Capture only successfully completed source work and an actual current/retained camera, not a fabricated future basis.");
            var particles = new List<SourceFireParticle>();
            foreach (Puff obj in objects)
            {
                if (!bySerial.ContainsKey(obj.Owner))
                {
                    SourceFireGuard.Require(bootstrap.StalePolicy == SourceFireStalePolicy.PortableCancelDeletedAndFreezeOrphan,
                        "Killed process left unowned stack-backed object_data; native memory behavior is not defined by logical fields.");
                    Boundary("portable:freeze-orphan-stack-object:" + obj.Serial);
                }
                particles.Add(new SourceFireParticle(obj.Serial, obj.Owner, obj.Position, obj.VelocityY,
                    obj.Frame, data.Frames[obj.Frame].IntegerAspect, obj.Hidden));
            }
            var flags = new int[14];
            for (int i = 0; i < 14; i++) flags[i] = host.ReadRawActorFlags(i);
            return new SourceFireRenderFrame(tick.SourceTick, tick.OriginalPcount, bootstrap, tick.OrderEvidence, basis,
                particles, flags, records, portableBoundaries, data.ManifestSha256);
        }

        private void Run(Process p)
        {
            switch (p.Entry)
            {
                case SourceFireEntry.PlayerBurst:
                    if (p.State != 0 && (host.ReadRawGameMode() == SourceFireIds.InPlay || ++p.Cursor >= p.Args.A1) ||
                        p.State == 0 && p.Args.A1 == 0)
                    { Retire(p, "ReturnDie", Site(4558, p.Args.A0), 1); return; }
                    p.State = 1;
                    SpawnPuff(p.Args.A0, 7, 4533); SpawnPuff(p.Args.A0, 11, 4534);
                    SpawnPuff(p.Args.A0, 4, 4541); SpawnPuff(p.Args.A0, 2, 4542);
                    SpawnPuff(p.Args.A0, 5, 4547); SpawnPuff(p.Args.A0, 14, 4548);
                    Sleep(p, p.Args.A2, 4552);
                    break;
                case SourceFireEntry.PlayerPuff: RunPlayerPuff(p); break;
                case SourceFireEntry.BallPuff: RunBallPuff(p); break;
                case SourceFireEntry.FlashPlayerWhite:
                    if (p.State == 0 && p.Args.A1 == 0 || p.State == 2 && ++p.Cursor >= p.Args.A1)
                    { Retire(p, "ReturnDie", Site(9856, p.Args.A0), 1); return; }
                    bool set = p.State != 1;
                    WriteFlash(p.Args.A0, set, Site(set ? 9851 : 9853, p.Args.A0));
                    p.State = set ? 1 : 2; Sleep(p, p.Args.A2, set ? 9852 : 9854);
                    break;
                default: throw new SourceFireBoundaryException("Inactive original fire entry is not implemented.");
            }
        }

        private void RunPlayerPuff(Process p)
        {
            if (p.State == 0)
            {
                SourceFireCachedPlayerFrame cache = host.ReadCachedPlayer(p.Args.A0);
                SourceFireGuard.Require(cache != null && cache.ActorId == p.Args.A0 && cache.Camera.SourceTick <= tick.SourceTick,
                    "Missing actual first-wake cached player jpos15/cambot pair; no live re-pose or identity camera.");
                SourceFireGuard.Require(cache.CacheSourceTick >= tick.SourceTick - 1 || cache.RetentionEvidence != null,
                    "An older paired jpos/camera cache needs explicit producer-suspension/retention evidence.");
                records.Add(new SourceFireRecord("PlayerEntryCache", wakeSite.At(wakeSite.Function, 9529, wakeCall, p.Args.A0), p.Serial, 0, p.Args.A1,
                    "cache-tick=" + cache.CacheSourceTick + "|" + cache.CacheProvenance + "|" + cache.Camera.Provenance + "|" + cache.RetentionEvidence));
                SourceVector3 j = cache.CameraJoints[p.Args.A1], c = cache.Camera.Position;
                ReadOnlyCollection<float> m = cache.Camera.Transform;
                float x = j.X * m[0] + j.Y * m[4] + j.Z * m[8] + c.X;
                float y = j.X * m[1] + j.Y * m[5] + j.Z * m[9] + c.Y;
                float z = j.X * m[2] + j.Y * m[6] + j.Z * m[10] + c.Z;
                x += Draw(5, Site(9540, p.Args.A0)); y += Draw(3, Site(9541, p.Args.A0));
                Puff obj = CreateObject(p, new SourceVector3(x, y, z), 9547);
                obj.VelocityY = .40f + Draw(10000, Site(9549, p.Args.A0)) / 15000f;
                p.Cursor = Draw(4, Site(9552, p.Args.A0)); p.End = 17; p.State = 1;
                obj.Frame = p.Cursor; Sleep(p, 1, 9561);
                return;
            }
            Puff current = RequireObject(p);
            if (p.State == 1)
            {
                current.Position = Rise(current.Position, current.VelocityY);
                if (Draw(100, Site(9568, p.Args.A0)) < 50)
                { p.State = 2; Sleep(p, 1, 9569); return; }
            }
            p.Cursor++;
            if (p.Cursor == p.End) { DeleteAndReturn(p, 9571); return; }
            current.Frame = p.Cursor; p.State = 1; Sleep(p, 1, 9561);
        }

        private void RunBallPuff(Process p)
        {
            if (p.State == 0)
            {
                SourceFireBallWorldSample ball = host.ReadBallWorld();
                SourceFireGuard.Require(ball.SourceTick == tick.SourceTick, "Ball puff requires live world ball point at its FIRST wake.");
                SourceFireGuard.Identity(ball.Provenance, "live ball provenance");
                records.Add(new SourceFireRecord("BallEntrySample", wakeSite.At(wakeSite.Function, 9432, wakeCall, -1), p.Serial, 0, 0, ball.Provenance));
                SourceVector3 b = ball.WorldPosition;
                Puff obj = CreateObject(p, new SourceVector3(b.X - Draw(p.Args.A0, Site(9432, -1)), b.Y + p.Args.A1, b.Z + p.Args.A2), 9436);
                obj.VelocityY = .35f;
                p.End = 17 - Draw(5, Site(9441, -1));
                p.Cursor = Draw(5, Site(9442, -1)); p.State = 1;
            }
            else p.Cursor++;
            if (p.Cursor >= p.End) { DeleteAndReturn(p, 9452); return; }
            Puff current = RequireObject(p); current.Frame = p.Cursor;
            current.Position = Rise(current.Position, current.VelocityY); Sleep(p, 1, 9450);
        }

        public void MaybePlayerFire(SourceFireSite site, SourceFirePlayerEmitterInput input)
        {
            RequireSite(site);
            if ((input.RawFlags & 0x00100000) != 0 || !input.TeamFire || input.Station == -1 ||
                input.NativeVelocityX == 0 && input.NativeVelocityZ == 0 && !input.LowerModeReacting) return;
            int divisor = Draw(4, site.At("maybe_plyr_fire", 4481, site.WithinWakeCallOrdinal, input.ActorId)) + 1;
            if (site.OriginalPcount % divisor != 0) return;
            int ordinal = site.WithinWakeCallOrdinal + 1;
            if (input.Offense)
                QueueAt(site, ref ordinal, SourceFireEntry.PlayerPuff, new SourceFireArgs(input.ActorId,
                    input.QuarterbackMode && (input.RawFlags & 1) != 0 ? 7 : 4, 0), "maybe_plyr_fire",
                    input.QuarterbackMode ? ((input.RawFlags & 1) != 0 ? 4489 : 4491) : 4499);
            else
            {
                QueueAt(site, ref ordinal, SourceFireEntry.PlayerPuff, new SourceFireArgs(input.ActorId, 14, 0), "maybe_plyr_fire", 4509);
                QueueAt(site, ref ordinal, SourceFireEntry.PlayerPuff, new SourceFireArgs(input.ActorId, 11, 0), "maybe_plyr_fire", 4510);
                QueueAt(site, ref ordinal, SourceFireEntry.PlayerPuff, new SourceFireArgs(input.ActorId, 0, 0), "maybe_plyr_fire", 4511);
            }
        }

        public void WeaponPreSnapTail(SourceFireSite site, int actorId, int lastReceiver, int consecutiveCompletions,
            bool teamFire, bool onsideKick, bool kickoff)
        {
            RequireSite(site); SourceFireGuard.Actor(actorId);
            int ordinal = site.WithinWakeCallOrdinal;
            if (lastReceiver == actorId && consecutiveCompletions == 2 && !onsideKick && !kickoff && !AlmostFireFlash)
            {
                QueueAt(site, ref ordinal, SourceFireEntry.FlashPlayerWhite, new SourceFireArgs(actorId, 10, 3), "_mode_wpnps", 2160);
                PlayerSound(site, ref ordinal, 193, "_mode_wpnps", 2162, actorId); AlmostFireFlash = true;
            }
            if (lastReceiver == actorId && consecutiveCompletions >= 3 && !onsideKick && !kickoff && !FireFlash)
            {
                QueueAt(site, ref ordinal, SourceFireEntry.PlayerBurst, new SourceFireArgs(actorId, 5, 25), "_mode_wpnps", 2173);
                FireFlash = true;
            }
            if (teamFire && lastReceiver == actorId && !kickoff && !onsideKick && !FireFlash)
            {
                QueueAt(site, ref ordinal, SourceFireEntry.PlayerBurst, new SourceFireArgs(actorId, 5, 25), "_mode_wpnps", 2184);
                FireFlash = true;
            }
        }

        public void DefensivePreSnapTail(SourceFireSite site, int actorId, bool teamFire)
        {
            RequireSite(site); SourceFireGuard.Actor(actorId);
            if (!teamFire || DefensiveFlashCount >= 4) return;
            int ordinal = site.WithinWakeCallOrdinal;
            QueueAt(site, ref ordinal, SourceFireEntry.PlayerBurst, new SourceFireArgs(actorId, 2, 4), "_mode_dbps", 2836);
            DefensiveFlashCount++;
        }

        public void SuccessfulOffensiveCatch(SourceFireSite site, int actorId, int lastReceiver,
            int consecutiveCompletions, int rawTeamFire, bool fieldXAtOrBeyondLos)
        {
            RequireSite(site); SourceFireGuard.Actor(actorId);
            if (lastReceiver != actorId || consecutiveCompletions != 2 || rawTeamFire == 1 ||
                !fieldXAtOrBeyondLos || host.ReadRawGameMode() == SourceFireIds.PlayOver) return;
            int ordinal = site.WithinWakeCallOrdinal;
            PlayerSound(site, ref ordinal, 192, "standard_catch", 3604, actorId);
            PlayerSound(site, ref ordinal, 192, "standard_catch", 3605, actorId);
            QueueAt(site, ref ordinal, SourceFireEntry.PlayerBurst, new SourceFireArgs(actorId, 6, 4), "standard_catch", 3606);
        }

        public void DefensiveSecondLossFireSite(SourceFireSite site, string actualAnnouncerBank, int? selectedQbHitPartner,
            int? resolvedLevel4Priority)
        {
            RequireSite(site);
            var announcerSite = new SourceFireSite(site.SourceTick, site.OriginalPcount, site.NativeProcessSerial,
                site.NativeRunLevel, site.WakeOrdinal, site.WithinWakeCallOrdinal, -1, "nfl.c", "game_proc", 1474);
            Apply(SourceFireCommand.DirectSound(new SourceFireDirectSound(announcerSite, actualAnnouncerBank, 185, 255, 127,
                "SND_PRI_SET_NOINT|SND_PRI_TRACK5|PRIORITY_LVL4", resolvedLevel4Priority)));
            SourceFireGuard.Require(selectedQbHitPartner.HasValue,
                "Only the conditional second-loss burst is blocked: selected player3-or10 pobj_hit is unknown, not known NULL.");
            if (selectedQbHitPartner.Value == -1) return;
            SourceFireGuard.Actor(selectedQbHitPartner.Value);
            int ordinal = site.WithinWakeCallOrdinal + 1;
            PlayerSound(site, ref ordinal, 192, "game_proc", 1485, selectedQbHitPartner.Value, "nfl.c");
            PlayerSound(site, ref ordinal, 192, "game_proc", 1486, selectedQbHitPartner.Value, "nfl.c");
            SourceFireSite burst = new SourceFireSite(site.SourceTick, site.OriginalPcount, site.NativeProcessSerial,
                site.NativeRunLevel, site.WakeOrdinal, ordinal, selectedQbHitPartner.Value, "nfl.c", "game_proc", 1488);
            Apply(SourceFireCommand.Create(burst, SourceFireEntry.PlayerBurst, new SourceFireArgs(selectedQbHitPartner.Value, 6, 4)));
        }

        public SourceFireBallEffect BallFreeOrPhantomStep(SourceFireSite site, SourceFireBallEmitterInput input)
        {
            RequireSite(site);
            switch (input.Kind)
            {
                case SourceBallKind.Pass: BallEffect = input.FirePass ? SourceFireBallEffect.Fire :
                    input.TurboPass ? SourceFireBallEffect.PlayerCloud : SourceFireBallEffect.BallTrail; break;
                case SourceBallKind.Bobble: break;
                case SourceBallKind.Lateral:
                case SourceBallKind.Fumble: BallEffect = SourceFireBallEffect.None; break;
                case SourceBallKind.Kickoff:
                case SourceBallKind.Punt: BallEffect = host.ReadRawGameMode() != SourceFireIds.InPlay ? SourceFireBallEffect.None :
                    input.Bounce ? SourceFireBallEffect.Blink : SourceFireBallEffect.BallTrail; break;
                case SourceBallKind.FieldGoal: BallEffect = input.Bounce ? SourceFireBallEffect.Blink :
                    input.OffenseTeamFire ? SourceFireBallEffect.Fire : SourceFireBallEffect.None; break;
                default: throw new SourceFireBoundaryException("Map SourceBallKind by NAME, never cast it to original LB_ numbers.");
            }
            if (host.ReadRawGameMode() == SourceFireIds.PlayOver) BallEffect = SourceFireBallEffect.None;
            if (BallEffect == SourceFireBallEffect.Fire && input.PostIncrementFlightTime % 2 == 0)
            {
                int ordinal = site.WithinWakeCallOrdinal;
                QueueAt(site, ref ordinal, SourceFireEntry.BallPuff, new SourceFireArgs(4, 2, 0), "ball_proc", 10367);
                QueueAt(site, ref ordinal, SourceFireEntry.BallPuff, new SourceFireArgs(3, -1, 2), "ball_proc", 10368);
                QueueAt(site, ref ordinal, SourceFireEntry.BallPuff, new SourceFireArgs(3, -1, -2), "ball_proc", 10369);
            }
            // Nonfire trail/blink selection is returned to the ball owner, not silently replaced by fire.
            return BallEffect;
        }

        private void QueueAt(SourceFireSite site, ref int ordinal, SourceFireEntry entry, SourceFireArgs args, string function, int line)
        { Apply(SourceFireCommand.Create(site.At(function, line, ordinal++, entry == SourceFireEntry.BallPuff ? -1 : args.A0), entry, args)); }
        private void PlayerSound(SourceFireSite site, ref int ordinal, int call, string function, int line, int actor, string path = "player.c")
        {
            var soundSite = new SourceFireSite(site.SourceTick, site.OriginalPcount, site.NativeProcessSerial, site.NativeRunLevel,
                site.WakeOrdinal, ordinal++, actor, path, function, line);
            Apply(SourceFireCommand.DirectSound(new SourceFireDirectSound(soundSite, "players", call, 235, 127, "PRIORITY_LVL5", 0)));
        }
        private void SpawnPuff(int actor, int joint, int line)
        { Apply(SourceFireCommand.Create(Site(line, actor), SourceFireEntry.PlayerPuff, new SourceFireArgs(actor, joint, 0))); }
        private int Draw(int bound, SourceFireSite site)
        {
            int value = host.Draw(new SourceFireRandomRequest(site, bound));
            SourceFireGuard.Require(value >= 0 && value < bound, "Host randrng result outside original exclusive bound.");
            records.Add(new SourceFireRecord("Rng", site, site.NativeProcessSerial, 0, value, "randrng(" + bound + ")"));
            return value;
        }
        private void WriteFlash(int actor, bool set, SourceFireSite site)
        {
            int raw = host.ReadRawActorFlags(actor);
            int value = set ? raw | SourceFireIds.FlashWhite : raw & ~SourceFireIds.FlashWhite;
            host.WriteRawActorFlags(actor, value, site);
            records.Add(new SourceFireRecord("ActorFlags", site, site.NativeProcessSerial, 0, value, set ? "OR0x400" : "AND~0x400"));
        }
        private void Sleep(Process p, int delay, int line)
        { p.Sleep |= (ushort)delay; Record("Sleep", Site(line, p.Entry == SourceFireEntry.BallPuff ? -1 : p.Args.A0), p, p.ObjectSerial, delay, "cooperative"); }
        private Puff CreateObject(Process p, SourceVector3 position, int line)
        {
            var obj = new Puff { Serial = nextObject++, Owner = p.Serial, Position = position, Frame = 0 };
            objects.Add(obj); // Every active puff has the same priority: new equals follow existing nodes.
            p.ObjectSerial = obj.Serial; RecordObject("CreateObject", Site(line, p.Entry == SourceFireEntry.BallPuff ? -1 : p.Args.A0), obj);
            return obj;
        }
        private Puff FindObject(long serial)
        { foreach (Puff obj in objects) if (obj.Serial == serial) return obj; return null; }
        private Puff RequireObject(Process p)
        { Puff obj = FindObject(p.ObjectSerial); SourceFireGuard.Require(obj != null, "Deleted original object-node access."); return obj; }
        private void DeleteAndReturn(Process p, int line)
        {
            Puff obj = RequireObject(p); SourceFireSite site = Site(line, p.Entry == SourceFireEntry.BallPuff ? -1 : p.Args.A0);
            RecordObject("DeleteObject", site, obj); objects.Remove(obj); Retire(p, "ReturnDie", site, 1);
        }
        private void Retire(Process p, string kind, SourceFireSite site, int cause)
        {
            Record(kind, site, p, p.ObjectSerial, cause, kind == "KillProcess" ?
                "PD_WIPEOUT-symbolic-cause;no-automatic-object-delete-or-flash-cleanup" : "no-automatic-object-delete-or-flash-cleanup");
            LinkedListNode<Process> node = bySerial[p.Serial]; processes.Remove(node); bySerial.Remove(p.Serial);
        }
        private void RequireSite(SourceFireSite site)
        { SourceFireGuard.Require(tick != null && host != null && !faulted && site.SourceTick == tick.SourceTick &&
            site.OriginalPcount == tick.OriginalPcount && site.SourcePath != null, "Apply at its actual executing source site/pcount, never a later render callback."); }
        private SourceFireSite Site(int line, int actor)
        { return wakeSite.At(wakeSite.Function, line, wakeCall++, actor); }
        private void Record(string kind, SourceFireSite site, Process p, long obj, int value, string detail)
        { records.Add(new SourceFireRecord(kind, site, p.Serial, obj, value, detail)); }
        private void RecordObject(string kind, SourceFireSite site, Puff obj)
        { records.Add(new SourceFireRecord(kind, site, obj.Owner, obj.Serial, obj.Frame, obj.Hidden ? "hidden" : "visible")); }
        private void Boundary(string value)
        { if (!portableBoundaries.Contains(value)) portableBoundaries.Add(value); }
        private void ReorderKnownEffects(IReadOnlyList<long> order)
        {
            var unique = new HashSet<long>();
            SourceFireGuard.Require(order.Count == bySerial.Count, "Known list-order projection must contain every existing effect exactly once.");
            foreach (long serial in order)
                SourceFireGuard.Require(bySerial.ContainsKey(serial) && unique.Add(serial), "Unknown/duplicate effect in list-order projection.");
            var nodes = new List<LinkedListNode<Process>>();
            foreach (long serial in order) nodes.Add(bySerial[serial]);
            processes.Clear();
            foreach (LinkedListNode<Process> node in nodes) processes.AddLast(node);
        }
        private static SourceVector3 Rise(SourceVector3 position, float vy)
        { return new SourceVector3(position.X, position.Y + vy, position.Z); }
        private static string Function(SourceFireEntry entry)
        { return entry == SourceFireEntry.PlayerBurst ? "player_burst_into_flames" : entry == SourceFireEntry.PlayerPuff ?
            "plyr_fire_trail_proc" : entry == SourceFireEntry.BallPuff ? "ball_fire_trail_proc" : "flash_plyr_proc"; }
        private static int EntryLine(SourceFireEntry entry)
        { return entry == SourceFireEntry.PlayerBurst ? 4523 : entry == SourceFireEntry.PlayerPuff ? 9518 :
            entry == SourceFireEntry.BallPuff ? 9422 : 9838; }
        private static bool IsWitness(string value)
        {
            if (!value.StartsWith("sha256:", StringComparison.Ordinal) || value.Length != 71) return false;
            for (int i = 7; i < value.Length; i++) if (!(value[i] >= '0' && value[i] <= '9') && !(value[i] >= 'a' && value[i] <= 'f')) return false;
            return true;
        }
    }
}
