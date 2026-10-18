using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal interface ISourceContactBoundary
    {
        int Draw(int bound, string function, int line);
        void Collision(ActorState attacker, ActorState defender, int mode, int direction4, int direction8);
        void Change(ActorState actor, string script);
        void Link(ActorState first, ActorState second);
        void StopCarrier(ActorState victim, ActorState attacker, int bobble, int fumble,
            string function, int cameraLine, bool selectCamera);
        void PopHelmet(ActorState actor);
        void Sound(ActorState actor, string function, int line, params string[] arguments);
    }

    // detect.c 517-1099, 1102-2541, 2986-3254 at 89f840d58d152a2a9f230cb8d0cdffbd554c8ded.
    internal static partial class SourceContactDriver
    {
        private const uint PuppetModes = 0x00c00000;
        private const uint BusyModes = PuppetModes | SourceAnimationModes.Reacting;
        private const int Dragging = 0x00100000;
        private static readonly int[] AvoidSpin = { 60, 40, 20, 5 };
        private static readonly int[] AvoidRedZoneSpin = { 23, 14, 5, 3 };
        private static readonly int[] QuarterbackDuck = { 8, 8, 8, 8, 8, 8, 8, 10, 20, 35, 45, 45 };
        private static readonly int[] OffensiveLineBlock = { -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5, 5 };
        private static readonly string[] Flail = { "h_flailm_anim", "r_flaild_anim", "h_flailm_anim" };
        private static readonly string[] StiffArmFlail = {
            "h_stiff_arm_react_anim", "h_flailm_anim", "h_stiff_arm_react2_anim",
            "h_stiff_arm_react3_anim", "h_flailm_anim"
        };
        private static readonly string[] CarrierFlail = { "r_flaild_carrier_anim", "h_flailm_carrier_anim" };
        private static readonly string[] Stumble = { "r_stmbl_anim", "r_flailnd_anim" };
        private static readonly string[] DragHit = {
            "h_side1_anim", "h_falnk1_anim", "h_blindl_anim",
            "h_bsidel_anim", "h_bsider_anim", "h_bsidef_anim"
        };
        private static readonly string[][] StandardHit = {
            new[] { "h_spin_anim", "h_falnk1_anim", "h_front1_anim", "h_front4_anim",
                "h_bsidef_anim", "fall_10", "fall_12" },
            new[] { "h_spin_anim", "h_back5_anim", "h_falnk1_anim", "h_back6_anim", "h_front4_anim",
                "h_bsidel_anim", "fall_04", "fall_10" },
            new[] { "h_back_anim", "h_back1_anim", "h_back5_anim", "h_back2_anim", "h_back4_anim",
                "h_back5_anim", "h_back6_anim", "fall_13", "fall_16", "fall_18" },
            new[] { "h_back5_anim", "h_back6_anim", "h_back_anim", "h_spinf_anim", "h_falnk1_anim",
                "h_front4_anim", "h_bsider_anim", "fall_04f", "fall_10" }
        };
        private static readonly string[][] TurboHit = {
            new[] { "h_side1_anim", "h_falnk1_anim", "h_spin_anim", "h_blindl_anim", "h_bsidef_anim",
                "h_front4_anim", "h_front5_anim", "h_heli1_anim", "h_blindl_anim", "fall_10", "fall_12" },
            new[] { "h_spinf_anim", "h_blindl_anim", "h_bsidel_anim", "fall_04", "fall_10" },
            new[] { "h_back6_anim", "h_back5_anim", "h_spinf_anim", "fall_13", "fall_16", "fall_18" },
            new[] { "h_spin_anim", "h_blindr_anim", "h_bsider_anim", "fall_04f", "fall_10" }
        };
        private static readonly string[][] PuppetPairs = {
            new[] { "p_clothes_line", "p_flip", "p_ftac", "p_tkdn", "p_thrw", "p_frol",
                "p_flip", "p_clothes_line", "p_ftac", "p_fslm", "p_spin", "p_armg3" },
            new[] { "p_hip_toss", "p_flip", "p_csid", "p_tkdn", "p_thrw", "p_flip",
                "p_grab_jersey", "p_fslm", "p_spin", "p_flip", "p_armg", "p_tbak",
                "p_hip_toss", "p_armg2", "p_armg3" },
            new[] { "p_suplb", "p_grab_jersey", "p_hip_toss", "p_csid", "p_tbak", "p_tkdn",
                "p_grab_jersey", "p_hip_toss", "p_hip_toss", "p_btac", "p_tbak" },
            new[] { "p_hip_toss", "p_csid", "p_flip", "p_tkdn", "p_grab_jersey",
                "p_spin", "p_thrw", "p_flip", "p_fslm", "p_spin", "p_armg", "p_hip_toss",
                "p_tbak", "p_armg2", "p_armg3" }
        };
        private static readonly string[][] ViolentPuppetPairs = {
            new[] { "p_ftac", "p_ftac", "p_tkdn", "p_spin", "p_armg", "p_frol", "p_armg2" },
            new[] { "p_csid", "p_tkdn", "p_face", "p_fslm", "p_spin", "p_armg", "p_tbak", "p_armg2" },
            new[] { "p_csid", "p_tkdn", "p_tbak", "p_btac", "p_suplb", "p_btac", "p_spin", "p_tbak" },
            new[] { "p_csid", "p_tkdn", "p_face", "p_tbak", "p_fslm", "p_spin", "p_armg", "p_tbak", "p_armg2" }
        };

        internal static void RecordCollision(ActorState attacker, ActorState defender)
        {
            defender.SourceHitPartnerId = attacker.Id;
            attacker.SourceHitPartnerId = defender.Id;
        }

        internal static void Detect(SimulationState model, ISourceContactBoundary boundary)
        {
            if (model.SourceCameraJointTick != model.SourceTick)
                throw new InvalidOperationException("detect.c requires the actual current post-player fifteen-joint camera cache.");
            int count = model.Players.Length;
            var low = new SourceVector3[count];
            var distances = new float[count, count];
            var angles = new int[count, count];
            for (int i = 0; i < count; i++)
            {
                SourceVector3 pi = SourcePlayerDriver.WorldPosition(model.Players[i]);
                for (int j = i + 1; j < count; j++)
                {
                    SourceVector3 pj = SourcePlayerDriver.WorldPosition(model.Players[j]);
                    float x = pi.X - pj.X, y = pi.Y - pj.Y, z = pi.Z - pj.Z;
                    distances[i, j] = distances[j, i] = (float)Math.Sqrt(x * x + y * y + z * z);
                    angles[i, j] = model.SourcePlaybook.Directions.Heading(
                        new SourcePlanar(pi.X, pi.Z), new SourcePlanar(pj.X, pj.Z));
                    angles[j, i] = (angles[i, j] + 512) % 1024;
                }
                SourceVector3 pelvis = model.SourceCameraJoints[i, 0];
                SourceVector3 right = model.SourceCameraJoints[i, 10], left = model.SourceCameraJoints[i, 13];
                low[i] = new SourceVector3((pelvis.X + right.X + left.X) / 3f,
                    (pelvis.Y + right.Y + left.Y) / 3f, (pelvis.Z + right.Z + left.Z) / 3f);
            }
            model.SourceDistances = distances;
            model.SourceAngles = angles;
            var e = new ContactExecution(model, boundary);
            for (int i = 0; i < count; i++)
            {
                ActorState a = model.Players[i];
                if ((Flags(a) & 0x20) != 0) continue;
                for (int j = i + 1; j < count; j++)
                {
                    ActorState d = model.Players[j];
                    if (distances[i, j] > 24f || (Flags(d) & 0x20) != 0) continue;
                    if (((Modes(a) | Modes(d)) & PuppetModes) == 0
                        || ((Flags(a) | Flags(d)) & Dragging) != 0)
                    {
                        if (a.SourceAttackMode != 0 && AttackIntersects(model, a, d, low[j]))
                            e.Collide(a, d, a.SourceAttackMode);
                        // Re-read j's attack state: i's immediate animation may have cleared or replaced it.
                        if (d.SourceAttackMode != 0 && AttackIntersects(model, d, a, low[i]))
                            e.Collide(d, a, d.SourceAttackMode);
                    }
                    if (((Modes(a) | Modes(d)) & 0x0200u) == 0 && distances[i, j] < 10f)
                        e.Collide(a, d, 0);
                }
            }
        }

        private static bool AttackIntersects(SimulationState model, ActorState a, ActorState d, SourceVector3 low)
        {
            if (a.SourceAttackJointA == a.SourceAttackJointB)
                throw new NotSupportedException("detect.c:576/605 spherical attack zone enters the original lockup; actor=" + a.Id);
            SourceVector3 first, second;
            ExtendLine(model.SourceCameraJoints[a.Id, a.SourceAttackJointA],
                model.SourceCameraJoints[a.Id, a.SourceAttackJointB],
                a.SourceAttackRadii.X, a.SourceAttackRadii.Y, out first, out second);
            return LinesIntersect(first, second, model.SourceCameraJoints[d.Id, 0], low,
                5f + a.SourceAttackRadii.Z);
        }

        private static uint Modes(ActorState actor) { return actor.AnimationState.Primary.Mode; }
        private static int Flags(ActorState actor) { return GameSnapshot.SourceActorFlags(actor); }
        private static float Height(ActorState actor) { return SourcePlayerDriver.WorldPosition(actor).Y + actor.SourceRoot.Y; }
        private static void ClearAttack(ActorState actor) { actor.SourceAttackMode = actor.SourceAttackTime = 0; }
        private static SourceVector3 Minus(SourceVector3 a, SourceVector3 b)
        { return new SourceVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        private static SourceVector3 Along(SourceVector3 a, SourceVector3 b, float t)
        { return new SourceVector3(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y), a.Z + t * (b.Z - a.Z)); }
        private static float Dot(SourceVector3 a, SourceVector3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        private static float Clamp(float t) { return t < 0f ? 0f : t > 1f ? 1f : t; }
        private static float PointToLine(SourceVector3 a, SourceVector3 b, SourceVector3 point)
        { SourceVector3 v = Minus(b, a); return Dot(Minus(point, a), v) / Dot(v, v); }

        private static void ExtendLine(SourceVector3 a, SourceVector3 b, float x1, float x2,
            out SourceVector3 first, out SourceVector3 second)
        {
            SourceVector3 v = Minus(b, a);
            float m = 1f / (float)Math.Sqrt(Dot(v, v));
            v = new SourceVector3(v.X * m, v.Y * m, v.Z * m);
            first = new SourceVector3(a.X - v.X * x1, a.Y - v.Y * x1, a.Z - v.Z * x1);
            second = new SourceVector3(b.X + v.X * x2, b.Y + v.Y * x2, b.Z + v.Z * x2);
        }

        private static float Determinant(SourceVector3 a, SourceVector3 b, SourceVector3 c)
        {
            return a.X * (b.Y * c.Z - b.Z * c.Y)
                - a.Y * (b.X * c.Z - b.Z * c.X) + a.Z * (b.X * c.Y - b.Y * c.X);
        }

        // Preserve the original parallel endpoint case and bad_t/bad_s 'tt = CLAMP(t)' quirk.
        internal static bool LinesIntersect(SourceVector3 a1, SourceVector3 a2,
            SourceVector3 b1, SourceVector3 b2, float radius)
        {
            SourceVector3 v1 = Minus(a2, a1), v2 = Minus(b2, b1);
            SourceVector3 cross = new SourceVector3(v1.Y * v2.Z - v1.Z * v2.Y,
                v1.Z * v2.X - v1.X * v2.Z, v1.X * v2.Y - v1.Y * v2.X);
            float magnitude = (float)Math.Sqrt(Dot(cross, cross));
            if (magnitude == 0f)
            {
                SourceVector3 delta = Minus(Along(b1, b2, PointToLine(b1, b2, a1)), a1);
                return Dot(delta, delta) <= radius * radius;
            }
            SourceVector3 start = Minus(b1, a1);
            float t = Determinant(start, v2, cross) / (magnitude * magnitude);
            float s = Determinant(start, v1, cross) / (magnitude * magnitude);
            bool badT = t > 1f || t < 0f, badS = s > 1f || s < 0f;
            SourceVector3 p1 = a1, p2 = b1;
            if (t > 1f) { t = 1f; p1 = a2; }
            if (t < 0f) { t = 0f; p1 = a1; }
            if (s > 1f) { s = 1f; p2 = b2; }
            if (s < 0f) { s = 0f; p2 = b1; }
            if (badT && badS)
            {
                SourceVector3 delta = Minus(p2, Along(a1, a2, Clamp(t)));
                if (Dot(delta, delta) <= radius * radius) return true;
                delta = Minus(p1, Along(b1, b2, Clamp(PointToLine(b1, b2, p1))));
                return Dot(delta, delta) <= radius * radius;
            }
            if (badT) p2 = Along(b1, b2, Clamp(PointToLine(b1, b2, p1)));
            else if (badS) p1 = Along(a1, a2, Clamp(PointToLine(a1, a2, p2)));
            else { p1 = Along(a1, a2, t); p2 = Along(b1, b2, s); }
            SourceVector3 difference = Minus(p2, p1);
            return Dot(difference, difference) <= radius * radius;
        }

        private sealed class ContactExecution
        {
            private readonly SimulationState m;
            private readonly ISourceContactBoundary b;
            internal ContactExecution(SimulationState model, ISourceContactBoundary boundary) { m = model; b = boundary; }
            private bool Ending { get { return (m.SourceFlags & SourceGameFlags.PlayEnding) != 0; } }
            private int Field(ActorState actor)
            { return SourceMatchRules.FieldPosition(SourcePlayerDriver.WorldPosition(actor).X, (int)m.Possession); }
            private string Pick(string[] table, string function, int line)
            { return table[b.Draw(table.Length, function, line)]; }
            private int Bearing(ActorState first, ActorState second)
            {
                int value = (1024 + first.Facing - m.SourceAngles[first.Id, second.Id]) % 1024;
                return value > 512 ? 1024 - value : value;
            }
            private void Pair(ActorState attacker, ActorState victim, string name, bool violentArm = false)
            {
                if (attacker.SourcePuppetLinkId != victim.Id || victim.SourcePuppetLinkId != attacker.Id)
                    b.Link(attacker, victim);
                b.Change(attacker, name + "_a_anim");
                b.Change(victim, (violentArm ? "p_armg" : name) + "_v_anim");
            }
            private bool SpinAvoids(ActorState victim, string function, int redLine, int normalLine = 0)
            {
                if (victim.AnimationState.Primary.SequenceName != "SPIN") { m.SourceAbusingSpin = false; return false; }
                int count = m.SourceSpinCount[(int)victim.Team];
                if (count >= 3) { m.SourceAbusingSpin = true; count = 3; }
                bool red = Field(victim) > 80;
                return b.Draw(100, function, red ? redLine : normalLine == 0 ? redLine + 5 : normalLine)
                    < (red ? AvoidRedZoneSpin[count] : AvoidSpin[count]);
            }
            private bool Dragged(ActorState actor)
            { return actor.SourcePuppetLinkId >= 0 && (Flags(m.Players[actor.SourcePuppetLinkId]) & Dragging) != 0; }
            private void BreakDrag(ActorState carrier)
            {
                if (carrier.SourcePuppetLinkId < 0)
                    throw new InvalidOperationException("detect.c dragging flag requires an actual bilateral puppet link.");
                ActorState other = m.Players[carrier.SourcePuppetLinkId];
                carrier.SourceRawFlags &= ~Dragging;
                carrier.SourcePuppetReleaseTick = m.SourceTick;
                carrier.AnimationState.Primary.RemoveMode(BusyModes | 0x0200u);
                other.AnimationState.Primary.RemoveMode(BusyModes | 0x0200u);
                b.Change(other, "h_bsider_anim");
                other.SourcePuppetLinkId = carrier.SourcePuppetLinkId = -1;
            }
            private bool QuarterbackEvades(ActorState victim, bool duck, string function, int slapLine, int duckLine)
            {
                bool quarterback = victim.SourceMode == SourceActorMode.Quarterback;
                int seconds = m.SourceClock.PlayTime;
                if (quarterback && b.Draw(100, function, slapLine) < 90 && seconds <= 2 && !m.SourceQbEvaded
                    && victim.SourceControllerStation == -1 && m.SourceRules.PlayType == SourcePlayType.Normal
                    && (Modes(victim) & SourceAnimationModes.Uninterruptible) == 0)
                {
                    m.SourceQbEvaded = true;
                    b.Change(victim, victim.Flipped ? "stiff_arm2_f_anim" : "stiff_arm2_anim");
                    b.Sound(victim, "snd_scall_bank", slapLine + (function == "att_push" || function == "evade_vs_tackle" ? 10 : 12),
                        "plyr_bnk_str", "OUTTA_MY_FACE_SP", "VOLUME3", "127", "LVL3");
                    victim.SourceThrowing = false;
                    victim.SourceRawFlags &= ~0x80000;
                    return true;
                }
                if (duck && quarterback && b.Draw(100, function, duckLine) < QuarterbackDuck[victim.SourceHandicap]
                    && seconds < 6 && m.SourceRules.PlayType == SourcePlayType.Normal
                    && (Modes(victim) & SourceAnimationModes.Uninterruptible) == 0 && !m.SourceQbEvaded)
                {
                    m.SourceQbEvaded = true;
                    b.Change(victim, victim.Flipped ? "duck_flip_anim" : "duck_anim");
                    b.Sound(victim, "snd_scall_bank", duckLine + 11, "plyr_bnk_str", "OUTTA_MY_FACE_SP", "VOLUME3", "127", "LVL3");
                    victim.SourceThrowing = false;
                    victim.SourceRawFlags &= ~0x80000;
                    b.Sound(victim, "qb_duck_speech", duckLine + 13);
                    return true;
                }
                return false;
            }
            private bool FireDrag(ActorState a, ActorState d, string function, int line)
            {
                if (d.Id != m.CarrierId || d.Id != m.SourceLastReceiver[(int)d.Team]
                    || d.SourceMode == SourceActorMode.Quarterback || (Flags(d) & (0x100 | Dragging)) != 0
                    || m.Phase != GamePhase.LiveBall || !m.SourceTeamFire[(int)d.Team]) return false;
                b.Link(a, d);
                d.SourceRawFlags |= Dragging;
                bool monkey = b.Draw(100, function, line) < 50;
                Pair(a, d, monkey ? "p_run_drag2" : "p_run_drag");
                b.Sound(d, "monkey_on_back_sp", line + (monkey ? 4 : 10));
                return true;
            }

            internal void Collide(ActorState a, ActorState d, int mode)
            {
                int relative = (a.Facing - d.Facing) % 1024;
                if (relative < 0) relative += 1024;
                int dir1 = ((relative + 1536 + 128) % 1024) / 256;
                int dir2 = ((relative + 1536 + 64) % 1024) / 128;
                RecordCollision(a, d);
                m.SourceCollisionVelocity = new FieldVector((a.SourceVelocity.X + d.SourceVelocity.X) / 2f,
                    (a.SourceVelocity.Z + d.SourceVelocity.Z) / 2f);
                m.SourceVictimSequence = d.AnimationState.Primary.SequenceName;
                m.SourceVictimHeight = SourcePlayerDriver.WorldPosition(d).Y;
                b.Collision(a, d, mode, dir1, dir2);
                switch (mode)
                {
                    case 0: Body(a, d, dir1, dir2); break;
                    case 1: Push(a, d, dir1); break;
                    case 2: Dive(a, d); break;
                    case 3: Kick(a, d); break;
                    case 4: LateHit(a, d); break;
                    case 5: Spear(a, d, dir1); break;
                    // Both bodies are compiled out in this original release.
                    case 6: case 8: break;
                    case 7: Head(a, d, dir1); break;
                    default: throw new NotSupportedException("detect.c invalid attack mode " + mode);
                }
            }

            private void Body(ActorState a, ActorState d, int dir1, int dir2)
            {
                SourceVector3 pa = SourcePlayerDriver.WorldPosition(a), pd = SourcePlayerDriver.WorldPosition(d);
                if (Math.Abs(pa.Y - pd.Y) > 30f || a.SourcePuppetLinkId == d.Id) return;
                if (m.Phase != GamePhase.DeadBall && a.Team == m.Possession || (int)a.Team == m.SourceLastPossession)
                { ActorState swap = a; a = d; d = swap; pa = SourcePlayerDriver.WorldPosition(a); pd = SourcePlayerDriver.WorldPosition(d); }
                m.SourceVictimSequence = d.AnimationState.Primary.SequenceName;
                m.SourceVictimHeight = pd.Y;
                if (a.Team == d.Team)
                {
                    if (d.SourceLateHittable && ((Modes(d) | Modes(a)) & BusyModes) == 0)
                    { Pair(a, d, "p_hlpup_back"); return; }
                }
                else if (d.Id == m.CarrierId || m.Phase == GamePhase.DeadBall)
                {
                    string sequence = a.AnimationState.Primary.SequenceName;
                    if (sequence != "T_CRZDIV" && sequence != "T_SPRHED" && Evade(d, a, dir1)) return;
                }
                else if (d.SourceJob == SourceActorJob.Block && Block(d, a)) return;
                var c = new FieldVector(pd.X - pa.X, pd.Z - pa.Z);
                float magnitude = c.X * c.X + c.Z * c.Z;
                c = magnitude < .001f ? new FieldVector(1f, 0f) : PlayerSimulation.Scale(c, 1f / (float)Math.Sqrt(magnitude));
                float da = Math.Abs(a.SourceVelocity.X * c.X + a.SourceVelocity.Z * c.Z);
                float dd = Math.Abs(d.SourceVelocity.X * c.X + d.SourceVelocity.Z * c.Z);
                if (Math.Abs(da) < .001 && Math.Abs(dd) < .001) da = dd = 1f;
                if (d.SourceMode == SourceActorMode.Receiver && d.SourceJob != SourceActorJob.Block) da += 6f;
                if ((Modes(d) & 2u) != 0) da += 6f;
                if ((Modes(a) & 2u) != 0) dd += 6f;
                if ((Modes(d) & PuppetModes) != 0) { dd = 0f; da = 1f; }
                else if ((Modes(d) & SourceAnimationModes.Reacting) != 0 && !Ending && pd.Y > 6.5f) { da = 0f; dd = 1f; }
                if ((Modes(a) & PuppetModes) != 0) { da = 0f; dd = 1f; }
                else if ((Modes(a) & SourceAnimationModes.Reacting) != 0 && !Ending && pa.Y > 6.5f) { dd = 0f; da = 1f; }
                if (d.SourceMode == SourceActorMode.LinePreSnap) { da = 1f; dd = 0f; }
                if (a.SourceMode == SourceActorMode.LinePreSnap) { da = 0f; dd = 1f; }
                float response = 1f / (da + dd);
                response *= 10f - m.SourceDistances[a.Id, d.Id];
                da *= response; dd *= response;
                SourcePlayerDriver.MoveSource(a, new FieldVector(-da * c.X, -da * c.Z));
                SourcePlayerDriver.MoveSource(d, new FieldVector(dd * c.X, dd * c.Z));
            }

            private bool Evade(ActorState carrier, ActorState tackler, int direction)
            {
                if ((Modes(tackler) & 2u) != 0) return false;
                if ((Flags(carrier) & Dragging) != 0)
                { BreakDrag(carrier); b.Change(carrier, Pick(DragHit, "evade_vs_tackle", 2234)); return true; }
                if ((Modes(carrier) & BusyModes) != 0) return false;
                ClearAttack(tackler); ClearAttack(carrier);
                if (SpinAvoids(carrier, "evade_vs_tackle", 2261, 2269))
                { b.Change(tackler, Pick(Flail, "evade_vs_tackle", Field(carrier) > 80 ? 2263 : 2271)); return true; }
                if (QuarterbackEvades(carrier, false, "evade_vs_tackle", 2287, 0)) return true;
                if (Height(carrier) < 2f && !carrier.SourceLateHittable) return false;
                if (FireDrag(tackler, carrier, "evade_vs_tackle", 2325)) return true;
                if (carrier.SourceLateHittable)
                {
                    if (tackler.SourceControllerStation >= 0)
                        b.Change(tackler, Pick(m.SourceMovementSettings.ViolencePowerup
                            ? new[] { "elbow_drp_anim", "belly_flop_anim", "butt_drp_anim", "m_nostomp" }
                            : new[] { "butt_drp_anim", "belly_flop_anim", "m_nostomp" }, "evade_vs_tackle",
                            m.SourceMovementSettings.ViolencePowerup ? 2347 : 2349));
                    return true;
                }
                if (Bearing(tackler, carrier) >= 180) return false;
                int chance = 20 + (carrier.SourceMode == SourceActorMode.Quarterback ? 35 : 0);
                if (Field(carrier) >= 95) chance = 75;
                if ((Flags(carrier) & 0x4000) != 0) chance = 90;
                if (b.Draw(100, "evade_vs_tackle", 2376) < chance)
                {
                    carrier.SourceRawFlags &= ~0x4000;
                    b.Link(tackler, carrier);
                    if (m.Phase == GamePhase.DeadBall && b.Draw(100, "evade_vs_tackle", 2389) < 40)
                        Pair(tackler, carrier, "p_argue");
                    else
                    {
                        bool violent = m.SourceMovementSettings.ViolencePowerup;
                        string name = Pick((violent ? ViolentPuppetPairs : PuppetPairs)[direction], "evade_vs_tackle", 2406);
                        Pair(tackler, carrier, name, violent && name == "p_armg2");
                    }
                }
                else if (b.Draw(100, "evade_vs_tackle", 2413) < 10 && Height(carrier) < 12f && !Ending)
                { b.Change(carrier, Pick(CarrierFlail, "evade_vs_tackle", 2417)); return true; }
                else if (tackler.Turbo)
                {
                    string reaction = Pick(TurboHit[direction], "evade_vs_tackle", 2429);
                    b.Change(tackler, b.Draw(100, "evade_vs_tackle", 2432) < 65 ? "t_dshoul_anim" : "m_swipe_anim");
                    b.Change(carrier, reaction);
                }
                else
                {
                    string reaction = Pick(StandardHit[direction], "evade_vs_tackle", 2449);
                    b.Change(tackler, "t_stshld_anim");
                    b.Change(carrier, reaction);
                }
                string selected = carrier.AnimationState.Primary.SequenceName;
                b.StopCarrier(carrier, tackler, 50, 290, "evade_vs_tackle", 2494,
                    selected != "P_FLIP_V" && selected != "JERSY_V" && selected != "CLOTHS_V");
                return true;
            }

            private void Push(ActorState a, ActorState d, int direction)
            {
                if (Height(d) < 2f || a.Team == d.Team || (Modes(d) & BusyModes) != 0 || Dragged(d)
                    || (Flags(d) & Dragging) != 0) return;
                ClearAttack(a);
                if (a.Team != m.Possession && d.SourceJob == SourceActorJob.Block && b.Draw(2, "att_push", 1148) != 0) return;
                if (SpinAvoids(d, "att_push", 1162)) return;
                if (b.Draw(100, "att_push", 1175) < 55 && (d.SourceAttackMode == 2 || d.SourceAttackMode == 5)) return;
                ClearAttack(d);
                if (QuarterbackEvades(d, false, "att_push", 1225, 0)) return;
                if (b.Draw(100, "att_push", 1242) < 50 && d.Id != m.CarrierId && !Ending)
                {
                    bool stiff = (a.AnimationState.Secondary.Mode & 0x0400u) != 0;
                    b.Change(d, Pick(stiff ? StiffArmFlail : Flail, "att_push", stiff ? 1247 : 1249));
                    b.Sound(d, "crowd_tackle_snds", 1250);
                    b.Sound(d, "snd_scall_bank", 1251, "plyr_bnk_str", "P_HARD_HIT2_SND", "VOLUME4", "127", "PRIORITY_LVL4");
                }
                else
                {
                    b.Change(d, new[] { "h_front1_anim", "h_side1f_anim", "h_back1_anim", "h_side1_anim" }[direction]);
                    b.Sound(d, "snd_scall_bank", 1257, "plyr_bnk_str", "P_HARD_HIT2_SND", "VOLUME4", "127", "PRIORITY_LVL4");
                }
                b.StopCarrier(d, a, 70, 430, "att_push", 1293, true);
            }

            private void Dive(ActorState a, ActorState d)
            {
                if (a.Team == d.Team) return;
                ClearAttack(a);
                if (SpinAvoids(d, "att_dive", 1339)) return;
                ClearAttack(d);
                if (Height(d) < 2f || Dragged(d)) return;
                if ((Flags(d) & Dragging) != 0)
                {
                    BreakDrag(d);
                    if (b.Draw(100, "att_dive", 1389) > 10)
                    { b.Change(d, Pick(DragHit, "att_dive", 1391)); return; }
                }
                if ((Modes(d) & BusyModes) != 0) return;
                int bearing = Bearing(d, a);
                if (SourcePlayerDriver.WorldPosition(a).Y <= 5f)
                {
                    if (b.Draw(100, "att_dive", 1421) < 15
                        && SourceMovement.Speed(new SourceMovementState(a.Id, -1, SourceActorMode.Dead) {
                            VelocityX = d.SourceVelocity.X, VelocityZ = d.SourceVelocity.Z }) > 1.20f
                        && !d.SourceBackpedal)
                    {
                        b.Change(d, Pick(Stumble, "att_dive", 1425));
                        b.Sound(d, "crowd_cheer_snds", 1426); return;
                    }
                    if (bearing >= 180)
                    {
                        b.Link(a, d);
                        bool ankle = b.Draw(100, "att_dive", 1434) < 50;
                        Pair(a, d, ankle ? "p_ankl" : "p_ank2");
                        b.Sound(d, "crowd_tackle_snds", ankle ? 1438 : 1444);
                    }
                    else { b.Change(d, "h_frflip_anim"); b.Sound(d, "crowd_tackle_snds", 1451); }
                }
                else
                {
                    if (FireDrag(a, d, "att_dive", 1472) || QuarterbackEvades(d, true, "att_dive", 1489, 1507)) return;
                    if (bearing >= 180)
                    {
                        b.Link(a, d);
                        int chosen = b.Draw(8, "att_dive", 1529) + 1;
                        b.Sound(d, "crowd_tackle_snds", 1532);
                        if (chosen == 7) Pair(a, d, "p_wtac");
                        Pair(a, d, new[] { "p_hip_toss", "p_spin", "p_armg3", "p_head_grab",
                            "p_dive", "p_frol", "p_waist_drop", "p_waist_drop" }[chosen - 1]);
                    }
                    else
                    {
                        b.Link(a, d);
                        Pair(a, d, b.Draw(100, "att_dive", 1586) < 65 ? "p_ftac" : "p_clothes_line");
                        b.Sound(d, "crowd_tackle_snds", 1596);
                    }
                }
                string selected = d.AnimationState.Primary.SequenceName;
                b.StopCarrier(d, a, 60, 560, "att_dive", 1637,
                    selected != "P_FLIP_V" && selected != "WSTDROPA" && selected != "CLOTHS_V");
            }

            private void Kick(ActorState a, ActorState d)
            {
                if (a.Team == d.Team) return;
                ClearAttack(a);
                a.SourceVelocity = PlayerSimulation.Scale(a.SourceVelocity, .5f);
                if (SpinAvoids(d, "att_kick", 1682)) return;
                ClearAttack(d);
                if (Height(d) < 2f) return;
                b.StopCarrier(d, a, 80, 430, "att_kick", 1734, true);
                b.Change(d, "h_front4_anim");
            }

            private void LateHit(ActorState a, ActorState d)
            {
                if (a.Team == d.Team) return;
                ClearAttack(a); ClearAttack(d);
                b.Sound(d, "crowd_tackle_snds", 1770); b.Sound(d, "late_hit_speech", 1771);
                if (d.AnimationState.Primary.SequenceName == "M_WOB_FU") b.Change(d, "m_cnv_fu1_anim");
                else if (d.AnimationState.Primary.SequenceName == "M_WOB_FD") b.Change(d, "m_cnv_fd2_anim");
            }

            private void Spear(ActorState a, ActorState d, int direction)
            {
                if (a.Team == d.Team) return;
                ClearAttack(a);
                a.SourceVelocity = PlayerSimulation.Scale(a.SourceVelocity, .7f);
                if (SpinAvoids(d, "att_spear_head", 1821)) return;
                ClearAttack(d);
                if (Height(d) < 2f || Dragged(d)) return;
                if ((Flags(d) & Dragging) != 0)
                {
                    BreakDrag(d);
                    if (b.Draw(100, "att_spear_head", 1869) > 10)
                    { b.Change(d, Pick(DragHit, "att_spear_head", 1871)); return; }
                }
                if ((Modes(d) & BusyModes) != 0) return;
                float speed = (float)Math.Sqrt(d.SourceVelocity.X * d.SourceVelocity.X + d.SourceVelocity.Z * d.SourceVelocity.Z);
                if (SourcePlayerDriver.WorldPosition(a).Y <= 5f && speed > 1.20f
                    && b.Draw(100, "att_spear_head", 1883) < 15 && !d.SourceBackpedal && !Ending)
                { b.Change(d, Pick(Stumble, "att_spear_head", 1887)); b.Sound(d, "crowd_cheer_snds", 1888); return; }
                if (FireDrag(a, d, "att_spear_head", 1907)
                    || QuarterbackEvades(d, true, "att_spear_head", 1924, 1942)) return;
                b.PopHelmet(d);
                string reaction = Pick(TurboHit[direction], "att_spear_head", 1968);
                if (d.Id == m.TargetPlayerId && (m.SourceBallFlags & SourceBallFlags.Bounce) == 0
                    && m.Phase == GamePhase.LiveBall && m.BallKind == SourceBallKind.Pass)
                    b.Sound(d, "hit_intended_receiver_sp", 1976);
                b.Change(d, reaction);
                b.StopCarrier(d, a, 65, 560, "att_spear_head", 2014, true);
            }

            private void Head(ActorState a, ActorState d, int direction)
            {
                if (Height(d) < 2f || (Modes(d) & BusyModes) != 0 || Dragged(d)) return;
                ClearAttack(d);
                if (!m.SourcePlowSoundPending)
                {
                    m.SourcePlowSoundPending = true;
                    b.Sound(a, "snd_scall_bank", 2124, "plyr_bnk_str", "BOWLING_PIN_SND", "VOLUME4", "127", "PRIORITY_LVL4");
                    b.Sound(a, "qcreate", 2125, "plow", "0", "delay_plow_proc", "0", "0", "0", "0");
                }
                if (b.Draw(100, "att_head", 2128) < 55 && !Ending)
                    b.Change(d, Pick(Flail, "att_head", 2130));
                else b.Change(d, new[] { "h_heli1_anim", "h_blindr_anim", "h_back_anim", "h_blindl_anim" }[direction]);
            }

            private bool Block(ActorState blocker, ActorState rusher)
            {
                if ((Modes(blocker) & 2u) != 0 && (Modes(blocker) & 0x0100u) == 0
                    || (Modes(rusher) & BusyModes) != 0) return false;
                SourceVector3 rb = Minus(SourcePlayerDriver.WorldPosition(blocker), SourcePlayerDriver.WorldPosition(rusher));
                float dx = rb.X, dz = rb.Z, vx = rusher.SourceVelocity.X, vz = rusher.SourceVelocity.Z;
                Normalize(ref dx, ref dz); Normalize(ref vx, ref vz);
                if (dx * vx + dz * vz < .5f && blocker.SourceControllerStation == -1
                    && !(vx == 0f && vz == 0f) && !(dx == 0f && dz == 0f)) return false;
                int[] probability = rusher.SourceControllerStation >= 0 ? new[] { 0, 0, 2, 2, 2, 3 }
                    : rusher.Id % 7 < 3 ? blocker.Id % 7 < 3 ? new[] { 2, 5, 4, 2, 1, 0 } : new[] { 1, 2, 3, 6, 3, 1 }
                    : blocker.Id % 7 < 3 ? new[] { 2, 6, 5, 2, 1, 0 } : new[] { 1, 2, 3, 6, 3, 1 };
                int column = Weighted(probability, "block_vs_rush", 3134);
                // Strict preserves the original undefined boundary; Portable explicitly uses the last existing entry.
                if (rusher.SourceBlockCount < 0 || rusher.SourceBlockCount >= 6
                    && m.SourceCompatibility == SourceGameplayCompatibility.StrictSource)
                    throw new NotSupportedException("detect.c:3140 bc_adjust[6] is undefined original C; block_count=" + rusher.SourceBlockCount);
                int[] adjustments = { -1, 1, 1, 2, 3, 4 };
                int blockIndex = m.ResolveCompatibilityIndex(SourceCompatibilityRepairKind.BlockCountConsumerIndex,
                    rusher.Id, rusher.SourceBlockCount, adjustments.Length, "detect.c:block_vs_rush:3140");
                int adjustment = adjustments[blockIndex];
                column += rusher.SourceControllerStation < 0 ? adjustment : Math.Max(0, adjustment);
                if (m.SourceDronesLose && m.SourceDronesWinning) column += IsHumanTeam(m, blocker.Team) ? -3 : 3;
                if (rusher.SourceControllerStation >= 0)
                    column -= OffensiveLineBlock[m.Players[(int)m.Possession * 7].SourceHandicap];
                SourceMovementSettings settings = m.SourceMovementSettings;
                if ((settings.BlockTeamMask & (1 << (int)m.Possession)) != 0 || m.SourceTeamFire[(int)m.Possession])
                    column -= 1 + b.Draw(2, "block_vs_rush", 3177);
                if ((settings.BlitzersTeamMask & (1 << (1 - (int)m.Possession))) != 0 || m.SourceTeamFire[1 - (int)m.Possession])
                    column += 1 + b.Draw(2, "block_vs_rush", 3184);
                if (Field(rusher) >= 75) column += 2;
                if (Field(blocker) <= 25) column -= 2;
                column = Math.Max(0, Math.Min(5, column));
                if (rusher.SourceUnblockable)
                    column = Math.Max(column, (m.SourceDefensePlay.Flags & SourcePlayFlags.RedZone) != 0
                        ? b.Draw(2, "block_vs_rush", 3202) != 0 ? 2 : 4 : b.Draw(2, "block_vs_rush", 3204) != 0 ? 4 : 5);
                BlockResult[] table = BlockTables[column];
                var weights = new int[table.Length];
                for (int i = 0; i < weights.Length; i++) weights[i] = table[i].Weight;
                BlockResult result = table[Weighted(weights, "block_vs_rush", 3221)];
                if (result.Puppet)
                {
                    if (((Modes(rusher) | Modes(blocker)) & PuppetModes) != 0)
                        throw new InvalidOperationException("detect.c:3233 attempt to link an existing puppet.");
                    b.Link(rusher, blocker);
                }
                if (result.Rusher != null) b.Change(rusher, result.Rusher);
                if (result.Blocker != null) b.Change(blocker, result.Blocker);
                rusher.SourceBlockCount++;
                return true;
            }

            private int Weighted(int[] weights, string function, int line)
            {
                int total = 0;
                foreach (int weight in weights) total += weight;
                int draw = b.Draw(total, function, line), result = -1;
                while (draw >= 0) draw -= weights[++result];
                return result;
            }
            private static void Normalize(ref float x, ref float z)
            {
                float length = (float)Math.Sqrt(x * x + z * z);
                if (length <= .00001f) { x = z = 0f; return; }
                x /= length; z /= length;
            }
        }

        private readonly struct BlockResult
        {
            internal readonly int Weight;
            internal readonly bool Puppet;
            internal readonly string Rusher, Blocker;
            internal BlockResult(int weight, bool puppet, string rusher, string blocker)
            { Weight = weight; Puppet = puppet; Rusher = rusher; Blocker = blocker; }
        }
        private static readonly BlockResult[][] BlockTables = {
            new[] { new BlockResult(3, true, "tie1c_a_anim", "tie1c_v_anim"),
                new BlockResult(3, true, "tie2c_a_anim", "tie2c_v_anim"), new BlockResult(2, true, "tie3c_a_anim", "tie3c_v_anim") },
            new[] { new BlockResult(1, true, "pushlg1v_anim", "pushlg1a_anim"),
                new BlockResult(1, false, "pushbl2_anim", "h_flailm_anim") },
            new[] { new BlockResult(1, false, "pushbl1_anim", "pushbl1_anim"),
                new BlockResult(1, false, "pushbl1_anim", "pushbl2_anim"), new BlockResult(2, false, "pushbl1_anim", "pushbl3_anim"),
                new BlockResult(3, false, "pushbl2_anim", "pushbl1_anim"), new BlockResult(1, false, "pushbl2_anim", "pushbl2_anim"),
                new BlockResult(2, false, "pushbl2_anim", "pushbl3_anim"), new BlockResult(1, false, "pushbl3_anim", "pushbl1_anim"),
                new BlockResult(2, false, "pushbl3_anim", "pushbl2_anim"), new BlockResult(1, false, "pushbl3_anim", "pushbl3_anim"),
                new BlockResult(2, true, "tie1c_a_anim", "tie1c_v_anim"), new BlockResult(2, true, "tie3c_a_anim", "tie3c_v_anim") },
            new[] { new BlockResult(1, false, "pushbl1_anim", "h_flailm_anim"), new BlockResult(1, false, "pushbl2_anim", "r_flaild_anim"),
                new BlockResult(1, false, "pushbl2_anim", "h_flailm_anim"), new BlockResult(1, false, "pushbl3_anim", "r_flaild_anim"),
                new BlockResult(4, true, "pushlg1a_anim", "pushlg1v_anim") },
            new[] { new BlockResult(2, true, "spin_a_anim", "spin_v_anim"), new BlockResult(2, true, "p_asidea_anim", "p_asidev_anim"),
                new BlockResult(2, true, "p_asidfa_anim", "p_asidfv_anim"), new BlockResult(4, false, null, "h_flailm_anim") },
            new[] { new BlockResult(1, false, null, "h_flailm_anim") }
        };
    }
}
