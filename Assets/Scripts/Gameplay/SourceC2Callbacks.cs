using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.AssetImport;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public sealed class SourceC2Bootstrap
    {
        public SourceC2CallbackData Data { get; }
        public SourceC2EntryKind EntryKind { get; }
        public SourceC2InitialState InitialState { get; }
        public string EntryProfile { get { return SourceC2CallbackData.EntryProfile; } }
        public string PreEntryEvidence
        { get { return "Original InitGameProc sound-bank boundary; preceding original boot/load/coin RNG is unverified input, not replayed."; } }

        private SourceC2Bootstrap(SourceC2CallbackData data, SourceC2EntryKind kind, SourceC2InitialState state)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data)); EntryKind = kind; InitialState = state;
        }

        public static SourceC2Bootstrap FromOriginalProgramBootAtSoundBanks(SourceC2CallbackData data)
        { return new SourceC2Bootstrap(data, SourceC2EntryKind.OriginalProgramBootSoundBanks, null); }

        public static SourceC2Bootstrap FromVerifiedOriginalState(SourceC2CallbackData data,
            SourceC2InitialState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (!state.ResultPending.HasValue || !state.DeferredProcessCount.HasValue)
                throw new SourceC2MissingStateException("verified continuation end/process state");
            if (state.ResultPending.Value || state.DeferredProcessCount.Value != 0)
                throw new NotSupportedException("This entry does not restore an in-flight original game/proc snapshot.");
            return new SourceC2Bootstrap(data, SourceC2EntryKind.VerifiedOriginalContinuation, state);
        }

        internal void RequireLibrary(ISourceAnimationLibrary library)
        {
            bool preSnapSwitch = Data.Profile == GameSimulation.SourceDefensivePreSnapSwitchProfile;
            SourceAnimationLibrary actual = library as SourceAnimationLibrary;
            if (actual == null || actual.InventorySha256 != Data.AnimationManifestSha256 ||
                actual.SequenceNames.Count != (preSnapSwitch ? 282 : 281) ||
                actual.FrameCount != (preSnapSwitch ? 26265 : 26230))
                throw new ArgumentException(preSnapSwitch
                    ? "Pre-snap switch requires its actual pinned original282 animation union."
                    : "C2 requires its actual pinned original281 animation union, never the legacy272 alias.");
        }
    }

    public sealed class SourceC2MissingStateException : InvalidOperationException
    {
        public string Field { get; }
        internal SourceC2MissingStateException(string field)
            : base("Original C2 requires actual " + field + "; unknown is not a successful default.")
        { Field = field; }
    }

    internal sealed class SourceC2WhistleProcess
    {
        internal readonly long Id;
        internal readonly long CreatedTick;
        internal long HostSerial;
        internal int SleepRemaining = 1;
        internal bool Started;
        internal SourceC2WhistleProcess(long id, long tick) { Id = id; CreatedTick = tick; }
    }

    internal sealed class SourceC2PendingEnd
    {
        internal readonly SourcePlayEndInput Input;
        internal readonly long QueuedTick;
        internal readonly long Epoch;
        internal readonly int PlayerId;
        internal readonly int AttackerId;
        internal readonly TeamId Offense;
        internal readonly FieldVector Position;
        internal readonly float DisplayLos;
        internal readonly float DisplayFirstDown;
        internal SourceC2PendingEnd(SimulationState model, SourcePlayEndCause cause, int playerId,
            FieldVector position, int attackerId, SourceBallType ballType)
        {
            Input = new SourcePlayEndInput(model.SourceRules, cause, ballType, model.SourceFlags,
                model.SourceLastPossession, model.SourcePlayEndX, model.SourcePlayEndZ,
                model.SourceFumbleX, model.SourceLastResult);
            QueuedTick = model.SourceTick; Epoch = ++model.SourceC2EndEpoch;
            PlayerId = playerId; AttackerId = attackerId; Position = position; Offense = model.Possession;
            DisplayLos = model.LineOfScrimmage; DisplayFirstDown = model.FirstDownLine;
        }
    }

    public sealed partial class GameSimulation
    {
        private readonly SourceC2Bootstrap sourceC2Bootstrap;
        private readonly List<SourceC2Event> sourceC2Events = new List<SourceC2Event>();
        public bool SourceC2Enabled { get { return sourceC2Bootstrap != null; } }
        public ReadOnlyCollection<SourceC2Event> C2Events { get { return Array.AsReadOnly(sourceC2Events.ToArray()); } }
        public SourceC2StateView C2State
        {
            get
            {
                var processes = new SourceC2WhistleView[Model.SourceC2Whistles.Count];
                for (int n = 0; n < processes.Length; n++)
                {
                    SourceC2WhistleProcess p = Model.SourceC2Whistles[n];
                    processes[n] = new SourceC2WhistleView(p.Id, p.CreatedTick, !p.Started, p.SleepRemaining);
                }
                return !SourceC2Enabled ? default(SourceC2StateView)
                    : new SourceC2StateView(sourceC2Bootstrap.EntryProfile, sourceC2Bootstrap.Data.PayloadSha256,
                        Model.SourceTaunted, Model.SourceAnnouncerSwappedBank, Model.SourceTauntBank,
                        Model.SourceC2EndEpoch, Model.SourceC2PendingResult != null, Model.SourceC2Whistles.Count,
                        sourceC2Bootstrap.EntryKind, sourceC2Bootstrap.InitialState == null
                            ? sourceC2Bootstrap.PreEntryEvidence : sourceC2Bootstrap.InitialState.ProvenanceSha256,
                        Model.SourceC2BankEpoch, Array.AsReadOnly(processes));
            }
        }

        private void InitializeSourceC2()
        {
            if (!SourceC2Enabled) return;
            sourceC2Bootstrap.RequireLibrary(sourceAnimations);
            Model.SourceC2Enabled = true;
            Model.SourceC2Programs = SourceAnimationPrograms.CreateC2Extension();
            if (sourceC2Bootstrap.EntryKind == SourceC2EntryKind.OriginalProgramBootSoundBanks)
            {
                Model.SourceTaunted = false; // player.c:293 program BSS, not a per-play reset.
                ReloadSourceC2Banks(true);
            }
            else
            {
                SourceC2InitialState state = sourceC2Bootstrap.InitialState;
                Model.SourceTaunted = state.Taunted;
                Model.SourceAnnouncerSwappedBank = state.AnnouncerBank;
                Model.SourceTauntBank = state.TauntBank;
            }
        }

        private void ReloadSourceC2Banks(bool boot)
        {
            if (!SourceC2Enabled) return;
            string caller = boot ? "InitGameProc" : "start_new_quarter";
            int ancr = SourceDraw(2, "nfl.c:" + caller + ":" + (boot ? "556" : "2662") + "/Game.c:get_ancr_str:161");
            Model.SourceAnnouncerSwappedBank = sourceC2Bootstrap.Data.AnnouncerBanks[ancr];
            int taunt = SourceDraw(2, "nfl.c:" + caller + ":" + (boot ? "560" : "2666") + "/Game.c:get_taunt_str:168");
            Model.SourceTauntBank = sourceC2Bootstrap.Data.TauntBanks[taunt];
            Model.SourceC2BankEpoch++;
            C2Record(boot ? SourceC2EventKind.BootBanksSelected : SourceC2EventKind.QuarterBanksSelected,
                -1, Model.SourceAnnouncerSwappedBank + "/" + Model.SourceTauntBank,
                Model.SourceTaunted, Model.SourceTaunted, sourcePath: "nfl.c", function: caller,
                sourceLine: boot ? 560 : 2666);
        }

        private void C2Record(SourceC2EventKind kind, int actorId, string detail, bool? before, bool? after,
            string sourcePath = null, string function = null, int? sourceLine = null, int? selectedOrdinal = null)
        {
            sourceC2Events.Add(new SourceC2Event(kind, Model.SourceTick, Model.SourceRandomProcess,
                Model.SourceRandomProcessOrder, actorId, Model.SourceC2EndEpoch, detail, before, after,
                sourcePath, function, sourceLine, selectedOrdinal));
        }

        private bool SourceC2Taunted()
        {
            if (!Model.SourceTaunted.HasValue) throw new SourceC2MissingStateException("player.c:293 global taunted");
            return Model.SourceTaunted.Value;
        }

        private bool C2OverFirstDown(ActorState hit)
        {
            SourceVector3 position = SourcePlayerDriver.WorldPosition(hit);
            if (Math.Abs(position.Z) > 182.85f || Math.Abs(position.X) > 468f) return false;
            if (Model.SourceRules.FirstDown >= 100 || (Model.SourceFlags & SourceGameFlags.PossessionChange) != 0) return false;
            return SourceMatchRules.FieldPosition(position.X, (int)Model.Possession) >= Model.SourceRules.FirstDown;
        }

        private void ExecuteSourceC2Void(ActorState actor, string function, int a, int b, int c)
        {
            SourceAnimationRandomContext(actor);
            C2Record(SourceC2EventKind.CallbackEntered, actor.Id,
                function + ";caller=" + actor.AnimationState.Primary.ScriptSymbol +
                ";frame=" + actor.AnimationState.Primary.FrameOrdinal, Model.SourceTaunted, Model.SourceTaunted,
                "player.c", function, function == "maybe_taunt" ? 5774 : function == "maybe_qb_mad" ? 10678 : 10697);
            if (function == "maybe_taunt") SourceMaybeTaunt(actor);
            else if (function == "maybe_qb_mad") SourceMaybeQbMad(actor);
            else if (function == "maybe_receiver_mad") SourceMaybeReceiverMad(actor);
            else throw new NotSupportedException("Unqualified original void C2 callback: " + function);
        }

        private void SourceMaybeTaunt(ActorState actor)
        {
            if (!actor.SourceHitPartnerId.HasValue) throw new SourceC2MissingStateException("pobj_hit");
            int partner = actor.SourceHitPartnerId.Value;
            if (partner == -1) return;
            if (partner < 0 || partner >= Model.Players.Length)
                throw new InvalidOperationException("Original maybe_taunt has an invalid actual pobj_hit.");
            if (SourceC2Taunted()) return;
            if ((Model.SourceFlags & SourceGameFlags.PlayEnding) == 0) return;
            ActorState hit = Model.Players[partner];
            if (hit.Team != Model.Possession) return;
            if (Model.SourceRules.PlayType == SourcePlayType.Kickoff) return;
            if (C2OverFirstDown(hit)) return;
            if (Model.SourceRules.Down == 1) return;
            if ((Model.SourceFlags & SourceGameFlags.PossessionChange) != 0) return;
            if (Model.SourceLastResult == SourcePlayResult.Touchdown || Model.SourceLastResult == SourcePlayResult.Conversion) return;
            int him = Model.CarrierId;
            if (him == -1 && hit.Id == Model.TargetPlayerId) him = hit.Id;
            if (him == -1 && hit.Id != Model.CarrierId && hit.Id != Model.TargetPlayerId
                && Model.TargetPlayerId >= -1 && Model.TargetPlayerId < Model.Players.Length)
            {
                // Explicit portability repair, not emulation of the original negative-index load.
                C2Record(SourceC2EventKind.CallbackEntered, actor.Id,
                    "PORTABILITY_REPAIR:NO_ELIGIBLE_CANDIDATE;policy=Parent-autonomous-source-semantic;user-approval=NOT_CAPTURED;" +
                    "original-bearing(" + actor.Id + ",-1)=SKIPPED;hit=" + hit.Id + ";carrier=" + Model.CarrierId +
                    ";receiver=" + Model.TargetPlayerId + ";draws=" + Model.SourceRandomDraws + ";call=" + Model.SourceRandomCallIndex,
                    Model.SourceTaunted, Model.SourceTaunted, "player.c", "maybe_taunt", 5843);
                return;
            }
            if (him < 0 || him >= Model.Players.Length)
                throw new InvalidOperationException("Original maybe_taunt bearing dereferences an invalid carrier/receiver; no fabricated target.");
            int bearing = (1024 + actor.Facing - Model.SourceAngles[actor.Id, him]) % 1024;
            if (bearing > 512) bearing = 1024 - bearing;
            if (bearing > 256) return;
            if (hit.Id == Model.CarrierId &&
                SourceDraw(100, "player.c:maybe_taunt:5852") < (Model.SourceTeamFire[1 - (int)Model.Possession] ? 70 : 45))
                C2SelectTaunt(actor, 5855);
            else if (hit.Id == Model.TargetPlayerId && (Model.SourceFlags & SourceGameFlags.PassThrown) != 0 &&
                Model.CarrierId == -1 && SourceDraw(100, "player.c:maybe_taunt:5864") < 60)
                C2SelectTaunt(actor, 5867);
        }

        private void C2SelectTaunt(ActorState actor, int sourceLine)
        {
            string[] taunts = { "taunt_1_anim", "taunt_2_anim", "taunt_3_anim", "taunt_4_anim", "m_laugh1_anim" };
            bool? before = Model.SourceTaunted;
            int ordinal = SourceDraw(5, "player.c:maybe_taunt:" + sourceLine);
            string script = taunts[ordinal];
            ChangeContactAnimation(actor, script);
            Model.SourceTaunted = true;
            C2Record(SourceC2EventKind.TauntSelected, actor.Id, script, before, Model.SourceTaunted,
                "player.c", "maybe_taunt", sourceLine, ordinal);
        }

        private void SourceMaybeQbMad(ActorState actor)
        {
            if (Model.Phase == GamePhase.LiveBall) return;
            if (actor.SourceMode != SourceActorMode.Quarterback || actor.Id != Model.CarrierId ||
                Model.Phase != GamePhase.DeadBall || SourceC2Taunted() ||
                SourceDraw(100, "player.c:maybe_qb_mad:10689") >= 40) return;
            bool? before = Model.SourceTaunted;
            Model.SourceTaunted = true;
            string[] mad = { "m_qbwhat_anim", "m_qbwhat_anim", "m_qbfite_anim" };
            int ordinal = SourceDraw(3, "player.c:maybe_qb_mad:10692");
            string script = mad[ordinal];
            ChangeContactAnimation(actor, script);
            C2Record(SourceC2EventKind.QuarterbackMadSelected, actor.Id, script, before, Model.SourceTaunted,
                "player.c", "maybe_qb_mad", 10692, ordinal);
        }

        private void SourceMaybeReceiverMad(ActorState actor)
        {
            if (actor.Id != Model.TargetPlayerId || Model.Phase != GamePhase.DeadBall ||
                Model.BallKind != SourceBallKind.Pass || Model.CarrierId != -1 || SourceC2Taunted() ||
                SourceDraw(100, "player.c:maybe_receiver_mad:10707") >= 50) return;
            bool? before = Model.SourceTaunted;
            Model.SourceTaunted = true;
            string[] mad = { "m_mscat1_anim", "m_mscat2_anim" };
            int ordinal = SourceDraw(2, "player.c:maybe_receiver_mad:10710");
            string script = mad[ordinal];
            ChangeContactAnimation(actor, script);
            C2Record(SourceC2EventKind.ReceiverMadSelected, actor.Id, script, before, Model.SourceTaunted,
                "player.c", "maybe_receiver_mad", 10710, ordinal);
        }

        private void QueueSourceC2PlayEnd(SourcePlayEndCause cause, int playerId, FieldVector position, int attackerId)
        {
            if (Model.SourceC2PendingResult != null) throw new InvalidOperationException("Original play-over epoch already queued.");
            Model.SourceC2PendingResult = new SourceC2PendingEnd(Model, cause, playerId, position, attackerId,
                SourceBallTypeFor(Model.BallKind));
            Model.Phase = GamePhase.DeadBall;
            Model.SourceMatchPhase = SourceMatchPhase.PlayResult;
            foreach (ActorState actor in Model.Players)
            { actor.SourcePlayOver = true; actor.SourceInPlay = false; }
            C2Record(SourceC2EventKind.PlayOverQueued, playerId, cause.ToString(), Model.SourceTaunted, Model.SourceTaunted);
        }

        private void CommitSourceC2PlayEnd()
        {
            SourceC2PendingEnd pending = Model.SourceC2PendingResult;
            if (pending == null || pending.QueuedTick >= Model.SourceTick) return;
            bool? before = Model.SourceTaunted;
            Model.ClockActive = false;
            Model.SourceTaunted = false;
            Model.SourceClock = SourceClock.DiscardFraction(Model.SourceClock);
            ApplySourcePlayEnd(pending.Input.Cause, pending.PlayerId, pending.Position, pending.AttackerId, pending);
            Model.SourceC2PendingResult = null;
            C2Record(SourceC2EventKind.ResultCommitted, pending.PlayerId, pending.Input.Cause.ToString(), before, Model.SourceTaunted,
                "nfl.c", "game_proc", 1381);
        }

        private void SpawnSourceC2Whistle()
        {
            var process = new SourceC2WhistleProcess(++Model.SourceC2WhistleSerial, Model.SourceTick);
            process.HostSerial = ++sourceFireNextSerial;
            Model.SourceProcesses.Create(process.HostSerial, 0, "whistle_proc", 0);
            Model.SourceC2Whistles.Insert(0, process);
            C2Record(SourceC2EventKind.WhistleSpawned, Model.SourceRandomActor, "player.c:5948 qcreate head; id=" + process.Id,
                Model.SourceTaunted, Model.SourceTaunted, "player.c", "player_down", 5948);
        }

        private void DispatchSourceC2Whistles()
        {
            if (!SourceC2Enabled) return;
            foreach (SourceC2WhistleProcess process in Model.SourceC2Whistles.ToArray())
                DispatchSourceC2Whistle(process);
        }

        private void DispatchSourceC2Whistle(SourceC2WhistleProcess process)
        {
            if (!SourceC2Enabled || !Model.SourceC2Whistles.Contains(process)) return;
            if (!EnterSourceProcess(process.HostSerial))
            {
                process.SleepRemaining = Model.SourceProcesses.Require(process.HostSerial).SleepRemaining;
                return;
            }
            process.SleepRemaining = 0;
            try
            {
                BeginSourceProcess("whistle_proc", -1, 0);
                if (!process.Started)
                {
                    int chance = SourceDraw(100, "player.c:whistle_proc:6014");
                    C2Record(SourceC2EventKind.WhistleFirstWake, -1, "id=" + process.Id + ";chance=" + chance,
                        Model.SourceTaunted, Model.SourceTaunted, "player.c", "whistle_proc", 6014);
                    if (chance >= 25)
                    { Model.SourceC2Whistles.Remove(process); Model.SourceProcesses.Kill(process.HostSerial); }
                    else { process.Started = true; process.SleepRemaining = 30; Model.SourceProcesses.Sleep(30); }
                    return;
                }
                ExecuteSourceC2NativeSound(new SourceC2SoundContext(SourceC2SoundFunctionKind.WhistleProc, Model.SourceTick,
                    "whistle_proc", Model.SourceRandomProcessOrder, -1, "player.c", "whistle_proc", 6018,
                    invocationId: process.Id, firstCallIndex: Model.SourceRandomCallIndex));
                C2Record(SourceC2EventKind.WhistleEmission, -1, "player.c:6018;id=" + process.Id,
                    Model.SourceTaunted, Model.SourceTaunted, "player.c", "whistle_proc", 6018);
                Model.SourceC2Whistles.Remove(process);
                Model.SourceProcesses.Kill(process.HostSerial);
            }
            finally { CompleteSourceProcess(); }
        }

        private void SourceC2ImmediateWhistle(ActorState actor)
        {
            int line;
            string function;
            if (actor.SourceMode == SourceActorMode.Quarterback) { line = 1839; function = "_mode_qb"; }
            else if (actor.SourceMode == SourceActorMode.Receiver) { line = 2434; function = "_mode_wpn"; }
            else throw new NotSupportedException("Original immediate OOB whistle is not qualified for actor mode " + actor.SourceMode);
            SourceRandomContext random = SourceAnimationRandomContext(actor);
            ExecuteSourceC2NativeSound(new SourceC2SoundContext(SourceC2SoundFunctionKind.WhistleImmediate,
                Model.SourceTick, random.Process, random.ProcessOrder, actor.Id, "player.c", function, line,
                firstCallIndex: random.FirstCallIndex));
            C2Record(SourceC2EventKind.WhistleEmission, actor.Id, "immediate source whistle",
                Model.SourceTaunted, Model.SourceTaunted, "player.c", function, line);
        }
    }
}
