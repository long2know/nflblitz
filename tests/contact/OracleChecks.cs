using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static partial class Program
{
    private const int ContactOracleCases = 1152;
    private const int LineOracleCases = 1024;
    private sealed class SelectorBoundary : ISourceContactBoundary
    {
        private readonly GameSimulation game;
        internal readonly List<string> Observed = new List<string>();
        internal SelectorBoundary(GameSimulation game) { this.game = game; }
        public int Draw(int bound, string function, int line)
        { return Bind<Func<int, int>>(game, "SourceDraw")(bound); }
        public void Collision(ActorState a, ActorState d, int mode, int dir4, int dir8)
        { Observed.Add("hit:" + a.Id + ":" + d.Id + ":" + mode); }
        public void Change(ActorState actor, string script)
        {
            Observed.Add("anim:" + actor.Id + ":" + (actor.SourceHitPartnerId ?? -1) + ":" + script);
            JsonElement first = programs[script].First(row => row.GetProperty("op").GetString() == "SetMode");
            JsonElement value;
            uint mode = first.TryGetProperty("a", out value) ? value.GetUInt32() : 0;
            actor.AnimationState.Primary.SetMovementMode((actor.AnimationState.Primary.Mode & 0xffff0000u) | mode);
            actor.AnimationState.Secondary.ClearLowModes();
        }
        public void Link(ActorState first, ActorState second)
        { first.SourcePuppetLinkId = second.Id; second.SourcePuppetLinkId = first.Id; }
        public void StopCarrier(ActorState victim, ActorState attacker, int bobble, int fumble,
            string function, int cameraLine, bool camera)
        {
            SimulationState m = game.Model;
            if (m.CarrierId != victim.Id || m.Phase != GamePhase.LiveBall
                || (m.SourceFlags & SourceGameFlags.PlayEnding) != 0) return;
            int high;
            SourceBallLoss loss = SourceContactDriver.FumbleOrBobble(m, victim, attacker, bobble, fumble,
                bound => Draw(bound, function, 0), out high);
            if (loss == SourceBallLoss.Fumble)
            {
                Observed.Add("fumble:" + victim.Id + ":-1:-");
                m.CarrierId = -1; m.SourceFumblesOnPlay++;
                return;
            }
            if (loss == SourceBallLoss.Bobble)
            {
                Draw(10000, "fumble_or_bobble", 0);
                Observed.Add("bobble:" + victim.Id + ":-1:-");
                m.CarrierId = -1;
                return;
            }
            m.SourceFlags |= SourceGameFlags.PlayEnding;
            if (camera) Draw(8, function, cameraLine);
        }
        public void PopHelmet(ActorState actor) { Bind<Action<ActorState>>(game, "SourceMaybePopHelmet")(actor); }
        public void Sound(ActorState actor, string function, int line, params string[] args) { }
    }

    private static GameSimulation OracleFixture(int index, out int[] tape, out int[] cursor)
    {
        var random = new Random(1700 + index);
        int[] prescribed = Enumerable.Range(0, 64).Select(i => random.Next(0, 0x1000000)).ToArray();
        if (index % 4 == 0) for (int i = 0; i < prescribed.Length; i++) prescribed[i] = 0;
        if (index % 4 == 1) for (int i = 0; i < prescribed.Length; i++) prescribed[i] = 99;
        int[] offset = { 0 };
        GameSimulation game = Create(site => site.SourceTick < 100 ? random.Next(0, 0x1000000) : prescribed[offset[0]++],
            new SourceMovementSettings(0, 0, false, 0, 0, index % 6 == 0,
                index / 64 % 4, index / 256 % 4));
        int first = index >= 1148 || index % 13 == 0 ? 3 : 4, second = index % 17 == 0 ? first + 1 : 11;
        if (index >= 1148) second = 11;
        float[] separations = { 0f, 4.5f, 9.999f, 10f, 10.001f, 23.999f, 24f, 24.001f };
        GamePhase[] phases = { GamePhase.LiveBall, GamePhase.DeadBall, GamePhase.PreSnap };
        PairFixture(game, first, second, separations[index % separations.Length], phases[index / 8 % phases.Length]);
        SimulationState m = game.Model;
        m.SourceOffensivePlays[0] = m.SourceOffensivePlays[1] = 10;
        m.LiveTicks = 0;
        m.SourceClock = SourceClock.Create(playTime: index / 24 % 8);
        m.CarrierId = index % 5 == 0 ? -1 : first;
        m.BallKind = SourceBallKind.Pass;
        m.SourceQbEvaded = index % 31 == 0;
        m.SourceFlags = index % 7 == 0 ? SourceGameFlags.PlayEnding : SourceGameFlags.None;
        m.SourceSpinCount[0] = index / 7 % 6; m.SourceSpinCount[1] = index / 11 % 6;
        foreach (ActorState actor in m.Players) actor.SourceHandicap = index / 9 % 12;
        ActorState a = m.Players[first], d = m.Players[second];
        if (first == 3) a.SourceMode = SourceActorMode.Quarterback;
        if (second < 7) { d.SourceControllerStation = -1; m.SourceStationPlayers[1] = -1; }
        if (index % 3 == 0) a.SourceControllerStation = -1;
        if (index % 4 == 0) d.SourceControllerStation = -1;
        a.SourceJob = index % 2 == 0 ? SourceActorJob.Block : SourceActorJob.None;
        d.SourceJob = index % 3 == 0 ? SourceActorJob.Block : SourceActorJob.None;
        a.Facing = (index * 91) & 1023; d.Facing = (index * 37 + 512) & 1023;
        a.SourceVelocity = new FieldVector((index % 5 - 2) * .7f, (index % 7 - 3) * .4f);
        d.SourceVelocity = new FieldVector((index % 3 - 1) * 1.3f, (index % 4 - 2) * .7f);
        a.SourceBlockCount = index / 4 % 6; d.SourceBlockCount = index / 6 % 6;
        if (index % 9 == 0)
            a.AnimationState.FullBody("spin_anim", a, 100, bound => 0, AnimationSelectionEvidence.SourceVerified);
        uint[] modes = { 0u, 2u, 0x80u, 0x100u, 0x102u, 0x200u, 0x400000u, 0x800000u };
        a.AnimationState.Primary.SetMovementMode(modes[index / 9 % modes.Length]);
        d.AnimationState.Primary.SetMovementMode(modes[index / 18 % modes.Length]);
        if (((a.AnimationState.Primary.Mode | d.AnimationState.Primary.Mode) & 0x00c00000u) != 0)
        {
            a.AnimationState.Primary.SetMovementMode((a.AnimationState.Primary.Mode & ~0x00c00000u) | 0x00400000u);
            d.AnimationState.Primary.SetMovementMode((d.AnimationState.Primary.Mode & ~0x00c00000u) | 0x00800000u);
            a.SourcePuppetLinkId = d.Id; d.SourcePuppetLinkId = a.Id;
        }
        a.AnimationState.Secondary.SetMovementMode(index % 11 == 0 ? 0x400u : 0);
        if (index % 19 == 0) a.SourceRawFlags |= 0x20;
        if (index % 23 == 0) d.SourceRawFlags |= 0x20;
        a.Turbo = index % 2 != 0; d.Turbo = index % 3 != 0;
        d.SourceLateHittable = index % 29 == 0;
        a.SourceUnblockable = index % 13 == 0; d.SourceUnblockable = index % 14 == 0;
        a.SourceBackpedal = index % 5 == 0; d.SourceBackpedal = index % 6 == 0;
        a.SourceAttackMode = index % 9; d.SourceAttackMode = index / 9 % 9;
        a.SourceAttackTime = d.SourceAttackTime = 17;
        a.SourceAttackJointA = d.SourceAttackJointA = 8;
        a.SourceAttackJointB = d.SourceAttackJointB = 0;
        a.SourceAttackRadii = d.SourceAttackRadii = new SourceVector3(1f, 1f, index % 7);
        if (index % 16 == 0) SourcePlayerDriver.SetSourcePosition(a,
            new SourceVector3(a.SourcePosition.X, 24f, a.SourcePosition.Z));
        if (index % 32 == 0) SourcePlayerDriver.SetSourcePosition(d,
            new SourceVector3(d.SourcePosition.X, 15f, d.SourcePosition.Z));
        if (index >= 1148)
        {
            int clockCase = index - 1148;
            a.AnimationState.FullBody("qb_stance_anim", a, 100, bound => 0, AnimationSelectionEvidence.SourceVerified);
            a.SourceMode = SourceActorMode.Quarterback; a.SourceControllerStation = clockCase < 2 ? -1 : 0;
            a.SourceRawFlags &= ~0x20; d.SourceRawFlags &= ~0x20;
            a.SourcePuppetLinkId = d.SourcePuppetLinkId = -1;
            a.AnimationState.Primary.SetMovementMode(0); d.AnimationState.Primary.SetMovementMode(0);
            a.SourceJob = d.SourceJob = SourceActorJob.None;
            a.SourceAttackMode = d.SourceAttackMode = 0;
            a.SourceAttackTime = d.SourceAttackTime = 0;
            a.Facing = 256; d.Facing = 768; a.SourceHandicap = 5;
            m.CarrierId = a.Id; m.Phase = GamePhase.LiveBall; m.SourceFlags = SourceGameFlags.None;
            m.SourceQbEvaded = false; m.SourceSpinCount[0] = m.SourceSpinCount[1] = 0;
            m.SourceClock = SourceClock.Create(playTime: new[] { 2, 3, 5, 6 }[clockCase]);
            if (clockCase >= 2)
            {
                d.AnimationState.FullBody("t_sprhed_anim", d, 100, bound => 0, AnimationSelectionEvidence.SourceVerified);
                d.AnimationState.Primary.SetMovementMode(2);
                prescribed[0] = 99; prescribed[1] = 0;
            }
            else for (int i = 0; i < prescribed.Length; i++) prescribed[i] = 0;
            SourcePlayerDriver.SetSourcePosition(a, new SourceVector3(0f, 0f, 0f));
            SourcePlayerDriver.SetSourcePosition(d, new SourceVector3(8f, clockCase >= 2 ? 6f : 0f, 0f));
        }
        Cache(game);
        offset[0] = 0;
        Bind<Action<string, int, int>>(game, "BeginSourceProcess")("detect_proc", -1, 0x500);
        tape = prescribed; cursor = offset;
        return game;
    }

    private static IEnumerable<string> InputRow(GameSimulation game, int index, int[] tape)
    {
        SimulationState m = game.Model;
        var row = new List<string> { "D", index.ToString(), GameSnapshot.RawMovementPhase(m).ToString(),
            ((int)m.Possession).ToString(), m.SourceLastPossession.ToString(), m.CarrierId.ToString(),
            ((uint)m.SourceFlags).ToString(), ((int)m.SourceRules.PlayType).ToString(),
            m.SourceClock.PlayTime.ToString(), m.SourceTick.ToString(), "3",
            m.SourceMovementSettings.ViolencePowerup ? "1" : "0",
            m.SourceMovementSettings.BlockTeamMask.ToString(), m.SourceMovementSettings.BlitzersTeamMask.ToString(),
            m.SourceTeamFire[0] ? "1" : "0", m.SourceTeamFire[1] ? "1" : "0",
            m.SourceSpinCount[0].ToString(), m.SourceSpinCount[1].ToString(), m.SourceQbEvaded ? "1" : "0",
            m.SourceLastReceiver[0].ToString(), m.SourceLastReceiver[1].ToString(),
            ((uint)m.SourceOffensePlay.Flags).ToString(), ((uint)m.SourceDefensePlay.Flags).ToString(),
            "0", ((int)m.SourceBallFlags).ToString() };
        row.AddRange(tape.Select(v => v.ToString()));
        foreach (ActorState actor in m.Players)
        {
            int sequence = Array.IndexOf(sequenceNames, actor.AnimationState.Primary.SequenceName);
            if (sequence < 0) sequence = Array.IndexOf(sequenceNames, "RUN");
            row.AddRange(new[] { actor.SourceControllerStation.ToString(), actor.SourceHandicap.ToString(),
                ((int)actor.SourceMode).ToString(), ((int)actor.SourceJob).ToString(),
                unchecked((uint)GameSnapshot.SourceActorFlags(actor)).ToString(), actor.AnimationState.Primary.Mode.ToString(),
                actor.AnimationState.Secondary.Mode.ToString(), sequence.ToString(), actor.Facing.ToString() });
            SourceVector3 p = SourcePlayerDriver.WorldPosition(actor);
            row.AddRange(new[] { p.X, p.Y, p.Z, actor.SourceRoot.Y, actor.SourceVelocity.X,
                actor.SourceVerticalVelocity, actor.SourceVelocity.Z }.Select(v => Bits(v).ToString()));
            row.AddRange(new[] { actor.SourceAttackMode.ToString(), actor.SourceAttackTime.ToString(),
                actor.SourceAttackJointA.ToString(), actor.SourceAttackJointB.ToString(),
                Bits(actor.SourceAttackRadii.X).ToString(), Bits(actor.SourceAttackRadii.Y).ToString(),
                Bits(actor.SourceAttackRadii.Z).ToString(), actor.SourceBlockCount.ToString(), actor.SourcePuppetLinkId.ToString() });
            for (int j = 0; j < 15; j++)
            {
                SourceVector3 joint = m.SourceCameraJoints[actor.Id, j];
                row.AddRange(new[] { Bits(joint.X).ToString(), Bits(joint.Y).ToString(), Bits(joint.Z).ToString() });
            }
        }
        for (int i = 0; i < 14; i++)
            for (int j = 0; j < 14; j++) row.Add(m.SourceAngles[i, j].ToString());
        return row;
    }

    private static SourceVector3[] LineInput(int index)
    {
        var r = new Random(4800 + index);
        var v = Enumerable.Range(0, 4).Select(i => new SourceVector3(r.Next(-20, 21),
            r.Next(-20, 21), r.Next(-20, 21))).ToArray();
        if (index % 8 == 0) { v[0] = default(SourceVector3); v[1] = new SourceVector3(0f, 10f, 0f);
            v[2] = new SourceVector3(index % 3 == 0 ? 5f : 5.001f, 0f, 0f);
            v[3] = new SourceVector3(v[2].X, 10f, 0f); }
        return v;
    }
    private static void EmitOracleInputs()
    {
        using (var writer = new StreamWriter(Path.Combine(output, "original-inputs.tsv")))
        {
            for (int index = 0; index < ContactOracleCases; index++)
            {
                int[] tape, cursor;
                GameSimulation game = OracleFixture(index, out tape, out cursor);
                writer.WriteLine(string.Join("\t", InputRow(game, index, tape)));
                emittedDetectorCases++;
            }
            for (int index = 0; index < LineOracleCases; index++)
            {
                SourceVector3[] points = LineInput(index);
                var row = new List<string> { "L", index.ToString() };
                foreach (SourceVector3 point in points) row.AddRange(new[] { Bits(point.X).ToString(),
                    Bits(point.Y).ToString(), Bits(point.Z).ToString() });
                row.Add(Bits(5f).ToString()); writer.WriteLine(string.Join("\t", row));
                emittedSegmentCases++;
            }
        }
    }

    private static void VerifyOracle()
    {
        VerifyPrograms();
        int cases = 0, lineCases = 0;
        foreach (string raw in File.ReadLines(Path.Combine(output, "original-results.tsv")))
        {
            string[] row = raw.Split('\t');
            int index = int.Parse(row[1]);
            if (row[0] == "L")
            {
                SourceVector3[] points = LineInput(index);
                Check(SourceContactDriver.LinesIntersect(points[0], points[1], points[2], points[3], 5f)
                    == (row[2] == "1"), "unchanged C segment/parallel/clamping source quirk " + index);
                lineCases++; continue;
            }
            int[] tape, cursor;
            GameSimulation game = OracleFixture(index, out tape, out cursor);
            var boundary = new SelectorBoundary(game);
            SourceContactDriver.Detect(game.Model, boundary);
            int count = int.Parse(row[2]), at = 3;
            string[] expected = row.Skip(at).Take(count).ToArray(); at += count;
            Check(boundary.Observed.SequenceEqual(expected), "unchanged C ordered attack/body and real selection case " + index
                + " expected=" + string.Join(",", expected) + " actual=" + string.Join(",", boundary.Observed));
            int drawCount = int.Parse(row[at++]);
            int[] bounds = row.Skip(at).Take(drawCount).Select(int.Parse).ToArray(); at += drawCount;
            int[] observedBounds = ((SourceInjectedRandom)game.SourceRandomService).Trace.Where(d => d.Site.SourceTick == 100)
                .Select(d => d.Site.BoundOrModulus).ToArray();
            Check(cursor[0] == drawCount && observedBounds.SequenceEqual(bounds),
                "unchanged C short-circuit RNG call order/bounds case " + index + " expected="
                + string.Join(",", bounds) + " actual=" + string.Join(",", observedBounds));
            Check(game.Model.SourceQbEvaded == (row[at++] == "1"), "unchanged C shared qb_evaded " + index);
            Check(game.Model.SourceAbusingSpin == (row[at++] == "1"), "unchanged C shared abusing_spin " + index);
            Check((uint)game.Model.SourceFlags == uint.Parse(row[at++]), "unchanged C play-ending flags " + index);
            Check(game.Model.CarrierId == int.Parse(row[at++]), "unchanged C carrier after response " + index);
            foreach (ActorState actor in game.Model.Players)
            {
                Check(actor.SourceAttackMode == int.Parse(row[at++]), "unchanged C attack mode clear/retain " + index + "/" + actor.Id);
                Check(actor.SourceAttackTime == int.Parse(row[at++]), "unchanged C attack time clear/retain " + index + "/" + actor.Id);
                Check(actor.SourceBlockCount == int.Parse(row[at++]), "unchanged C rusher block count " + index + "/" + actor.Id);
                Check(actor.AnimationState.Primary.Mode == uint.Parse(row[at++]), "unchanged C initial-mode external VM boundary " + index + "/" + actor.Id);
                Check(unchecked((uint)GameSnapshot.SourceActorFlags(actor)) == uint.Parse(row[at++]),
                    "unchanged C full actor flags " + index + "/" + actor.Id);
                Check(actor.SourceHitPartnerId == int.Parse(row[at++]), "unchanged C bilateral pobj_hit assignment " + index + "/" + actor.Id);
                Check(actor.SourcePuppetLinkId == int.Parse(row[at++]), "unchanged C bilateral puppet assignment " + index + "/" + actor.Id);
                SourceVector3 p = SourcePlayerDriver.WorldPosition(actor);
                foreach (float v in new[] { p.X, p.Y, p.Z, actor.SourceVelocity.X, actor.SourceVerticalVelocity, actor.SourceVelocity.Z })
                    Check(Bits(v) == uint.Parse(row[at++]), "unchanged C binary32 cylinder/attack state " + index + "/" + actor.Id);
            }
            Check(at == row.Length, "complete unchanged C projected actor observation row " + index);
            cases++;
        }
        Check(cases == ContactOracleCases && lineCases == LineOracleCases, "complete original contact and capsule oracle inventory");
        verifiedDetectorCases = cases; verifiedSegmentCases = lineCases;
    }

    private static void VerifyPrograms()
    {
        int count = 0;
        var names = new Dictionary<string, string> { { "op", "Op" }, { "text", "Text" }, { "a", "A" }, { "b", "B" },
            { "c", "C" }, { "x", "X" }, { "y", "Y" }, { "z", "Z" }, { "sourceLine", "SourceLine" },
            { "timer", "Timer" }, { "target", "Target" }, { "branchPc", "BranchPc" },
            { "maximumYVelocity", "MaximumYVelocity" }, { "maximumPlanarVelocity", "MaximumPlanarVelocity" },
            { "minimumPlanarVelocity", "MinimumPlanarVelocity" }, { "alternateSequence", "AlternateSequence" } };
        foreach (KeyValuePair<string, JsonElement[]> pair in programs)
        {
            SourceAnimationProgram actual = SourceAnimationPrograms.Get(pair.Key);
            Check(actual.Instructions.Length == pair.Value.Length, "original active ANI length " + pair.Key);
            for (int i = 0; i < pair.Value.Length; i++)
            {
                foreach (KeyValuePair<string, string> field in names)
                {
                    JsonElement value;
                    bool specified = pair.Value[i].TryGetProperty(field.Key, out value);
                    object observed = typeof(SourceAnimationInstruction).GetField(field.Value,
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(actual.Instructions[i]);
                    string label = "original active ANI operand/order " + pair.Key + "/" + i + "/" + field.Key;
                    if (field.Key == "op") Check(observed.ToString() == value.GetString(), label);
                    else if (field.Key == "text" || field.Key == "target" || field.Key == "alternateSequence")
                        Check((string)observed == (specified ? value.GetString() : null), label);
                    else if (observed is float) Check(Bits((float)observed) == Bits(specified ? value.GetSingle() : 0f), label);
                    else Check(unchecked((uint)(int)observed) == (specified ? value.GetUInt32OrSigned()
                        : field.Key == "branchPc" ? uint.MaxValue : 0u), label);
                }
                count++;
            }
        }
        Check(programs.Count == 131 && count == 2147, "all exact131 C1 source ANI programs/2147 active instructions");
    }
}

internal static class ContactJson
{
    internal static uint GetUInt32OrSigned(this JsonElement value)
    { return unchecked((uint)value.GetInt64()); }
}
