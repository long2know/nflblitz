using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public sealed partial class GameSimulation
    {
        private readonly List<SourceContactEvent> sourceContactEvents = new List<SourceContactEvent>();
        private readonly List<SourceContactSoundCue> unresolvedContactSounds = new List<SourceContactSoundCue>();
        public ReadOnlyCollection<SourceContactEvent> ContactEvents { get { return Array.AsReadOnly(sourceContactEvents.ToArray()); } }
        public ReadOnlyCollection<SourceContactSoundCue> UnresolvedContactSoundCalls
        { get { return Array.AsReadOnly(unresolvedContactSounds.ToArray()); } }

        private void SourceContacts()
        {
            try { SourceContactDriver.Detect(Model, new ContactBoundary(this)); }
            catch (InvalidOperationException error) { Model.SourceAnimationFailure = error.Message; throw; }
            catch (NotSupportedException error) { Model.SourceAnimationFailure = error.Message; throw; }
        }

        private void ContactEvent(SourceContactEventKind kind, ActorState actor, int other = -1,
            int mode = 0, int dir4 = 0, int dir8 = 0, string script = null)
        {
            sourceContactEvents.Add(new SourceContactEvent(Model.SourceTick, Model.SourcePresentationOrder++, kind,
                actor.Id, other, SourcePlayerDriver.WorldPosition(actor), mode, dir4, dir8, script));
        }

        private void ContactAnimationProcess(ActorState actor)
        {
            if (Model.SourceRandomActor == actor.Id) return;
            int offset = Model.SourceRandomCallIndex;
            BeginSourceProcess(Model.SourceRandomProcess, actor.Id, Model.SourceRandomProcessOrder);
            Model.SourceRandomCallIndex = offset;
        }

        private void ChangeContactAnimation(ActorState actor, string script)
        {
            ContactAnimationProcess(actor);
            actor.Action = (actor.AnimationState.Primary.Mode & SourceAnimationModes.Reacting) != 0
                || script.StartsWith("h_", StringComparison.Ordinal) || script.StartsWith("fall_", StringComparison.Ordinal)
                || script.EndsWith("_v_anim", StringComparison.Ordinal) ? ActorAction.Fall : ActorAction.Contact;
            actor.AnimationState.FullBody(script, actor, Model.SourceTick, SourceDraw, AnimationSelectionEvidence.SourceVerified);
            ContactEvent(SourceContactEventKind.AnimationChanged, actor,
                actor.SourceHitPartnerId ?? -1, script: script);
        }

        private SourceVector3 ContactCachedWorldJoint(ActorState actor, int joint)
        {
            SourceVector3 point = Model.SourceCameraJoints[actor.Id, joint];
            SourceMovementCameraFrame camera = Model.SourceMovementCamera;
            ReadOnlyCollection<float> matrix = camera.Transform;
            return new SourceVector3(point.X * matrix[0] + point.Y * matrix[4] + point.Z * matrix[8] + camera.Position.X,
                point.X * matrix[1] + point.Y * matrix[5] + point.Z * matrix[9] + camera.Position.Y,
                point.X * matrix[2] + point.Y * matrix[6] + point.Z * matrix[10] + camera.Position.Z);
        }

        private void SourceContactSound(ActorState actor, string function, string caller, int line, params string[] arguments)
        {
            var context = new SourcePresentationContext(Model.SourceTick, Model.SourcePresentationOrder++, actor.Id,
                "detect.c", caller, line, opcodeSymbol: "source-function-call");
            unresolvedContactSounds.Add(new SourceContactSoundCue(context, function, arguments, Model.CarrierId));
        }

        private void StopSourceCarrier(ActorState victim, ActorState attacker, int bobble, int fumble,
            string function, int cameraLine, bool selectCamera)
        {
            if (victim.Id != Model.CarrierId || Model.Phase != GamePhase.LiveBall
                || (Model.SourceFlags & SourceGameFlags.PlayEnding) != 0) return;
            int high;
            SourceBallLoss loss = SourceContactDriver.FumbleOrBobble(Model, victim, attacker, bobble, fumble,
                SourceDrawFor("detect.c:fumble_or_bobble"), out high);
            FieldVector mean = Model.SourceCollisionVelocity;
            if (loss == SourceBallLoss.Bobble)
            {
                float by = SourceDrawFor("detect.c:fumble_or_bobble:high")(10000) > high ? -1f : 0f;
                SourceBallDriver.Bobble(Model, victim, ContactCachedWorldJoint(victim, 7),
                    mean.X / 1.5f, by, mean.Z / 1.5f, SourceDrawFor("player.c:bobble_pass"));
                Model.SourcePendingTackleAttacker = Model.SourcePendingTackleVictim = -1;
                Model.SourceHasContactSpot = false;
                EmitSource(SourceEventType.Bobbled, victim.Id, attacker.Id);
                return;
            }
            if (loss == SourceBallLoss.Fumble)
            {
                SourceBallDriver.Fumble(Model, victim, ContactCachedWorldJoint(victim, 7),
                    mean.X / 2f, mean.Z / 2f, SourceDrawFor("player.c:fumble_ball"));
                Model.SourceFumblesOnPlay++;
                Model.SourcePendingTackleAttacker = Model.SourcePendingTackleVictim = -1;
                Model.SourceHasContactSpot = false;
                EmitSource(SourceEventType.Fumbled, victim.Id, attacker.Id);
                return;
            }
            if (attacker.SourceControllerStation >= 0) Model.SourceTackles[(int)attacker.Team]++;
            SourceVector3 spot = SourcePlayerDriver.WorldPosition(victim);
            SourceVector3 a = SourcePlayerDriver.WorldPosition(attacker);
            if ((OverSourceDefensiveEndZone(Model.SourceBallPosition) || OverSourceDefensiveEndZone(spot))
                && (a.Y < .2f || spot.Y < .2f))
                EndSourcePlay(SourcePlayEndCause.CarrierTouchdown, victim.Id, victim.Position, attacker.Id, spot);
            Model.SourcePendingTackleAttacker = attacker.Id;
            Model.SourcePendingTackleVictim = victim.Id;
            Model.SourceContactPosition = victim.Position;
            Model.SourceContactSourcePosition = spot;
            Model.SourceHasContactSpot = true;
            Model.SourcePlayEndX = spot.X; Model.SourcePlayEndZ = spot.Z;
            Model.SourceFlags |= SourceGameFlags.PlayEnding;
            ContactEvent(SourceContactEventKind.ProgressStopped, victim, attacker.Id);
            if (selectCamera)
            {
                SourceRandomDraw draw = DrawSourceRandom(8, "detect.c:" + function + ":select_csi_item");
                var context = new SourcePresentationContext(Model.SourceTick, Model.SourcePresentationOrder++,
                    victim.Id, "detect.c", function, cameraLine, opcodeSymbol: "source-function-call");
                sourcePresentationCues.Add(new SourceCameraCue(context, SourcePresentationCueKind.ContactCameraSelection,
                    new SourceCameraSelection("csi_fs_endplay", draw.Value, 8, draw.Raw, draw.Site.ToString()), Model.CarrierId));
            }
            if (function == "att_kick" || function == "att_spear_head" || function == "evade_vs_tackle")
                SourceContactSound(victim, "crowd_tackle_snds", function,
                    function == "att_kick" ? 1737 : function == "att_spear_head" ? 2017 : 2498);
        }

        private void SourceMaybePopHelmet(ActorState actor)
        {
            int first = SourceDraw(100, "player.c:maybe_pop_helmet:9177");
            if (Model.Phase != GamePhase.LiveBall) return;
            if (actor.SourceControllerStation >= 0)
            {
                if (sourcePresentation == null)
                    throw new InvalidOperationException("player.c:maybe_pop_helmet requires actual team_head station input.");
                if (sourcePresentation.StationHeads[actor.SourceControllerStation] != -1) return;
            }
            ClearSourceLooseHelmet("player.c", "maybe_pop_helmet", 9188, 9189, actor.Id);
            if (actor.Id != Model.CarrierId) return;
            bool fire = Model.SourceTeamFire[(int)actor.Team] && first < 45;
            if (!fire && SourceDraw(100, "player.c:maybe_pop_helmet:9200") >= 10) return;
            actor.SourceHelmetPopped = true;
            ContactEvent(SourceContactEventKind.HelmetPopped, actor, actor.SourceHitPartnerId ?? -1);
            var context = new SourcePresentationContext(Model.SourceTick, Model.SourcePresentationOrder++, actor.Id,
                "player.c", "maybe_pop_helmet", fire ? 9197 : 9203, opcodeSymbol: "source-function-call");
            unresolvedContactSounds.Add(new SourceContactSoundCue(context, "snd_scall_bank",
                new[] { "ancr_swaped_bnk_str", fire ? "165" : "166", "ANCR_VOL2", "127", "LVL4" }, Model.CarrierId));
            if (!fire)
            {
                context = new SourcePresentationContext(Model.SourceTick, Model.SourcePresentationOrder++, actor.Id,
                    "player.c", "maybe_pop_helmet", 9204, opcodeSymbol: "source-function-call");
                unresolvedContactSounds.Add(new SourceContactSoundCue(context, "snd_scall_bank",
                    new[] { "plyr_bnk_str", "P_HARD_HIT3_SND", "ANCR_VOL2", "127", "PRIORITY_LVL3" }, Model.CarrierId));
            }
            AdmitSourceLooseHelmet(actor, fire ? 9198 : 9205);
        }

        private int SourceContactAnimationCode(ActorState actor, string function, int argument0)
        {
            if (SourceC2Enabled && (function == "maybe_taunt" || function == "maybe_qb_mad"
                || function == "maybe_receiver_mad"))
                throw new InvalidOperationException("Original void C2 callback must execute through the plain action ANI_CODE path, not an int-return adapter.");
            if (function == "rotate_anim")
            {
                if (!actor.SourceHitPartnerId.HasValue)
                    throw new InvalidOperationException("player.c:rotate_anim requires known pobj_hit state.");
                if (actor.SourceHitPartnerId.Value >= 0)
                    actor.Facing = actor.TargetFacing = (Model.Players[actor.SourceHitPartnerId.Value].Facing + argument0 + 1024) % 1024;
            }
            else if (function == "face_attacker")
            {
                if (!actor.SourceHitPartnerId.HasValue || actor.SourceHitPartnerId.Value < 0)
                    throw new InvalidOperationException("player.c:face_attacker dereferences an absent pobj_hit.");
                actor.Facing = actor.TargetFacing = Model.SourceAngles[actor.Id, actor.SourceHitPartnerId.Value];
            }
            else if (function == "face_carrier")
            {
                int target = Model.CarrierId;
                if (target == -1)
                {
                    if (!actor.SourceHitPartnerId.HasValue || actor.SourceHitPartnerId.Value < 0)
                        throw new InvalidOperationException("player.c:face_carrier without a carrier dereferences actual pobj_hit.");
                    if (actor.SourceHitPartnerId.Value != Model.TargetPlayerId) return 1;
                    target = actor.SourceHitPartnerId.Value;
                }
                int bearing = (1024 + actor.Facing - Model.SourceAngles[actor.Id, target]) % 1024;
                if (bearing > 512) bearing = 1024 - bearing;
                if (bearing > argument0) return 1;
                actor.TargetFacing = Model.SourceAngles[actor.Id, target];
            }
            else if (function == "release_puppet")
            {
                int linked = actor.SourcePuppetLinkId;
                if (linked < 0) return 0;
                ActorState other = Model.Players[linked];
                actor.SourcePuppetLinkId = other.SourcePuppetLinkId = -1;
                other.SourcePuppetReleaseTick = Model.SourceTick;
                actor.AnimationState.Primary.RemoveMode(0x00c00000u);
                other.AnimationState.Primary.RemoveMode(0x00c00000u);
                if (argument0 == 0)
                {
                    actor.SourceVelocity = other.SourceVelocity = default(FieldVector);
                    actor.SourceVerticalVelocity = other.SourceVerticalVelocity = 0f;
                }
                ContactEvent(SourceContactEventKind.PuppetReleased, actor, other.Id);
            }
            else if (function == "maybe_pop_helmet") SourceMaybePopHelmet(actor);
            else if (function == "lower_player" || function == "set_plyr_y_pos")
            {
                SourceVector3 position = SourcePlayerDriver.WorldPosition(actor);
                SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(position.X,
                    function == "lower_player" ? -3f : 0f, position.Z));
            }
            else if (function == "maybe_qb_mad")
            {
                if (Model.Phase == GamePhase.LiveBall || actor.SourceMode != SourceActorMode.Quarterback
                    || actor.Id != Model.CarrierId || Model.Phase != GamePhase.DeadBall) return 0;
                throw new NotSupportedException("player.c:10677 maybe_qb_mad requires the C2 taunted/celebration producer; no substitute get-up selection.");
            }
            else if (function == "maybe_receiver_mad")
            {
                if (actor.Id != Model.TargetPlayerId || Model.Phase != GamePhase.DeadBall
                    || Model.BallKind != SourceBallKind.Pass || Model.CarrierId != -1) return 0;
                throw new NotSupportedException("player.c:10697 maybe_receiver_mad requires the C2 taunted/celebration producer.");
            }
            else if (function == "maybe_taunt")
            {
                if (!actor.SourceHitPartnerId.HasValue)
                    throw new InvalidOperationException("player.c:maybe_taunt requires known pobj_hit.");
                int hit = actor.SourceHitPartnerId.Value;
                if (hit < 0 || (Model.SourceFlags & SourceGameFlags.PlayEnding) == 0
                    || Model.Players[hit].Team != Model.Possession || Model.SourceRules.PlayType == SourcePlayType.Kickoff
                    || Model.SourceRules.Down == 1) return 0;
                throw new NotSupportedException("player.c:5774 maybe_taunt requires C2 taunted/original deferred result state.");
            }
            else if (function == "maybe_get_up_b_hurt" || function == "maybe_get_up_c_hurt")
            {
                if (function == "maybe_get_up_c_hurt" && Model.Phase == GamePhase.LiveBall) return 0;
                if (actor.Id == Model.CarrierId && SourceDraw(100, "player.c:" + function) < 30)
                {
                    string[] table = function == "maybe_get_up_b_hurt"
                        ? new[] { "get_up_b1_hurt_anim", "get_up_b1_hurt_anim", "get_up_b2_hurt_anim" }
                        : new[] { "get_up_c2_hurt_anim", "get_up_c3_hurt_anim", "get_up_c3_hurt_anim" };
                    ChangeContactAnimation(actor, table[SourceDraw(3, "player.c:" + function + ":table")]);
                }
            }
            else return -1;
            return 0;
        }

        private void SetSourcePuppetPosition(ActorState actor)
        {
            uint mode = actor.AnimationState.Primary.Mode;
            if ((mode & 0x00c00000u) == 0) return;
            int linked = actor.SourcePuppetLinkId;
            if (linked < 0)
                throw new InvalidOperationException("ANIM.C:set_puppet_pos requires the actual bilateral contact link.");
            ActorState attacker = (mode & 0x00800000u) != 0 ? Model.Players[linked] : actor;
            ActorState victim = attacker == actor ? Model.Players[linked] : actor;
            SourceVector3 root = victim.AnimationState.Primary.SelectedRoot(victim);
            victim.SourceRoot = root;
            victim.Facing = attacker.Facing; victim.TargetFacing = attacker.TargetFacing;
            FieldVector offset = SourceAnimationLayerState.Rotate(root.X - attacker.SourceRoot.X,
                root.Z - attacker.SourceRoot.Z, attacker.Facing, attacker.SourceDirections);
            SourceVector3 position = SourcePlayerDriver.WorldPosition(attacker);
            SourcePlayerDriver.SetSourcePosition(victim, new SourceVector3(position.X + offset.X, position.Y, position.Z + offset.Z));
            victim.SourceVelocity = attacker.SourceVelocity;
            victim.SourceVerticalVelocity = attacker.SourceVerticalVelocity;
        }

        private int SourceContactHitHeading(ActorState actor)
        {
            if (!actor.SourceHitPartnerId.HasValue || actor.SourceHitPartnerId.Value < 0)
                throw new InvalidOperationException("ANIM.C:1948 ANI_SET_FACE_REL_VEL dereferences actual pobj_hit; no own-facing fallback.");
            return Model.Players[actor.SourceHitPartnerId.Value].Facing;
        }

        private int SourceContactGroundTime(ActorState actor)
        {
            int ticks = SourceAnimationLayerState.GroundTicks(actor);
            if ((actor.AnimationState.Primary.Mode & 0x00c00000u) == 0) return ticks;
            if (actor.SourcePuppetLinkId < 0)
                throw new InvalidOperationException("ANIM.C:1755 puppet AA_TOGROUND requires the actual linked actor.");
            return Math.Min(ticks, SourceAnimationLayerState.GroundTicks(Model.Players[actor.SourcePuppetLinkId]));
        }

        private sealed class ContactBoundary : ISourceContactBoundary
        {
            private readonly GameSimulation game;
            internal ContactBoundary(GameSimulation game) { this.game = game; }
            public int Draw(int bound, string function, int line)
            { return game.SourceDraw(bound, "detect.c:" + function + ":" + line); }
            public void Collision(ActorState a, ActorState d, int mode, int dir4, int dir8)
            { game.ContactEvent(SourceContactEventKind.Collision, a, d.Id, mode, dir4, dir8); }
            public void Change(ActorState actor, string script) { game.ChangeContactAnimation(actor, script); }
            public void Link(ActorState first, ActorState second)
            {
                first.SourcePuppetLinkId = second.Id; second.SourcePuppetLinkId = first.Id;
                game.ContactEvent(SourceContactEventKind.PuppetLinked, first, second.Id);
            }
            public void StopCarrier(ActorState victim, ActorState attacker, int bobble, int fumble,
                string function, int cameraLine, bool selectCamera)
            { game.StopSourceCarrier(victim, attacker, bobble, fumble, function, cameraLine, selectCamera); }
            public void PopHelmet(ActorState actor) { game.SourceMaybePopHelmet(actor); }
            public void Sound(ActorState actor, string function, int line, params string[] arguments)
            {
                string caller = line >= 2185 ? "evade_vs_tackle" : line >= 2078 ? "att_head"
                    : line >= 1780 ? "att_spear_head" : line >= 1748 ? "att_late_hit"
                    : line >= 1652 ? "att_kick" : line >= 1301 ? "att_dive" : "att_push";
                game.SourceContactSound(actor, function, caller, line, arguments);
            }
        }
    }
}
