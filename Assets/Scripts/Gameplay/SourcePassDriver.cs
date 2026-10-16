using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal readonly struct SourceCannedCatch
    {
        internal readonly int Weight;
        internal readonly bool TwoPart;
        internal readonly string Script;
        internal readonly int LaunchTime;
        internal readonly float Height;
        internal readonly float Offset;
        internal readonly int ControlTime;
        internal readonly bool ExcludeUninterruptible;

        internal SourceCannedCatch(int weight, bool twoPart, string script, int launchTime,
            float height, float offset, int controlTime, bool excludeUninterruptible = false)
        {
            Weight = weight;
            TwoPart = twoPart;
            Script = script;
            LaunchTime = launchTime;
            Height = height;
            Offset = offset;
            ControlTime = controlTime;
            ExcludeUninterruptible = excludeUninterruptible;
        }
    }

    internal static class SourcePassDriver
    {
        internal static int PickReceiver(SimulationState model, ActorState quarterback, int rawStick)
        {
            var targets = new int[3];
            var forward = new int[3];
            var rear = new int[3];
            int count = 0, forwardCount = 0, rearCount = 0;
            int leftForward = 0, rightForward = 0, leftRear = 0, rightRear = 0;
            float direction = model.Possession == TeamId.Away ? -1f : 1f;
            SourceVector3 origin = SourcePlayerDriver.WorldPosition(quarterback);
            for (int id = (int)quarterback.Team * 7 + 3; id < (int)quarterback.Team * 7 + 7; id++)
            {
                ActorState actor = model.Players[id];
                if (actor.SourceMode != SourceActorMode.Receiver || id == quarterback.Id
                    || actor.SourceJob == SourceActorJob.Block && actor.SourceControllerStation < 0)
                    continue;
                if (count == targets.Length)
                    throw new NotSupportedException("player.c:pick_receiver targets[3] overflow for a quarterback "
                        + "outside the weapon slots is an undefined original C branch.");
                targets[count++] = id;
                SourceVector3 position = SourcePlayerDriver.WorldPosition(actor);
                if (direction * position.X >= direction * origin.X)
                {
                    forward[forwardCount++] = id;
                    if (direction * position.Z > direction * origin.Z) rightForward++;
                    else leftForward++;
                }
                else
                {
                    rear[rearCount++] = id;
                    if (direction * position.Z > direction * origin.Z) rightRear++;
                    else leftRear++;
                }
            }
            if (count == 0) return -1;
            if (count == 1) return targets[0];
            int column = rawStick % 7;
            int side = column < 2 ? 0 : column < 5 ? 1 : 2;
            int row = rawStick / 7;
            int[] selected = targets;
            int selectedCount = count;
            if (row < 2 && (side == 0 ? leftForward : side == 1 ? forwardCount : rightForward) != 0)
            { selected = forward; selectedCount = forwardCount; }
            else if (row >= 5 && (side == 0 ? leftRear : side == 1 ? rearCount : rightRear) != 0)
            { selected = rear; selectedCount = rearCount; }
            if (selectedCount == 2)
            {
                float first = SourcePlayerDriver.WorldPosition(model.Players[selected[0]]).Z - origin.Z;
                float second = SourcePlayerDriver.WorldPosition(model.Players[selected[1]]).Z - origin.Z;
                selected[2] = Math.Abs(first) < Math.Abs(second) ? selected[0] : selected[1];
                selectedCount = 3;
            }
            var keys = new int[selectedCount];
            for (int i = 0; i < selectedCount; i++)
                keys[i] = (int)(direction * SourcePlayerDriver.WorldPosition(model.Players[selected[i]]).Z * 10f);
            for (int i = 1; i < selectedCount; i++)
                for (int j = i; j > 0 && keys[j - 1] > keys[j]; j--)
                {
                    int key = keys[j]; keys[j] = keys[j - 1]; keys[j - 1] = key;
                    int id = selected[j]; selected[j] = selected[j - 1]; selected[j - 1] = id;
                }
            int ordinal = side == 0 ? 0 : side == 2 ? selectedCount - 1 : selectedCount > 1 ? 1 : 0;
            for (int i = 0; i < selectedCount; i++)
                if (keys[i] == keys[ordinal] && selected[i] != selected[ordinal])
                    throw new NotSupportedException("player.c:pick_receiver target_cmp quantized-Z tie needs "
                        + "the unverified original platform libc qsort ordering; no arbitrary actor is substituted.");
            return selected[ordinal];
        }

        private static readonly SourceCannedCatch[] Front = {
            new SourceCannedCatch(3, false, "c_jgut1_anim", 15, 22f, 0f, 35)
        };
        private static readonly SourceCannedCatch[] Right = {
            new SourceCannedCatch(60, true, "c2p_sidef_anim", 8, 14f, 0f, 19),
            new SourceCannedCatch(3, false, "c_jgut1f_anim", 15, 22f, 0f, 35),
            new SourceCannedCatch(3, false, "c_jmp1f_anim", 20, 31f, 3.3f, 35, true),
            new SourceCannedCatch(3, false, "c_jmp2f_anim", 20, 32.3f, 2.2f, 37, true),
            new SourceCannedCatch(3, false, "c_sidrolf_anim", 17, 29f, 7.9f, 41, true),
            new SourceCannedCatch(3, false, "c_splitsf_anim", 23, 21f, 1.1f, 39),
            new SourceCannedCatch(2, false, "c_sdgutf_anim", 10, 13f, 1.1f, 10),
            new SourceCannedCatch(1, false, "c_spin2f_anim", 24, 25f, 1f, 44, true),
            new SourceCannedCatch(3, false, "c_bicycf_anim", 23, 29f, 7.3f, 56, true),
            new SourceCannedCatch(1, false, "c_div2h_anim", 18, 26.3f, 11.1f, 39, true),
            new SourceCannedCatch(2, false, "catch_01", 20, 29f, 0f, 48, true),
            new SourceCannedCatch(2, false, "catch_2f", 20, 29f, 0f, 64, true),
            new SourceCannedCatch(2, false, "catch_03f", 20, 37f, 0f, 48, true),
            new SourceCannedCatch(2, false, "catch_05f", 21, 37f, 0f, 37, true),
            new SourceCannedCatch(2, false, "catch_06", 10, 22f, 0f, 19, true)
        };
        private static readonly SourceCannedCatch[] Behind = {
            new SourceCannedCatch(60, true, "c2p_back_anim", 8, 21f, 0f, 19),
            new SourceCannedCatch(3, false, "c_run1_anim", 20, 26f, 2.5f, 27, true),
            new SourceCannedCatch(4, false, "c_run1a_anim", 25, 40f, 4f, 46, true),
            new SourceCannedCatch(3, false, "c_jgut1f_anim", 20, 22f, 0f, 35),
            new SourceCannedCatch(3, false, "c_sidrol_anim", 17, 29f, 7.9f, 41, true),
            new SourceCannedCatch(2, false, "c_roll_anim", 20, 27f, 1f, 39, true),
            new SourceCannedCatch(2, false, "c_bicycf_anim", 23, 29f, 7.3f, 56, true),
            new SourceCannedCatch(1, false, "c_div2h_anim", 18, 26.3f, 11.1f, 39, true),
            new SourceCannedCatch(2, false, "catch_01", 20, 29f, 0f, 48, true),
            new SourceCannedCatch(2, false, "catch_02", 20, 29f, 0f, 64, true),
            new SourceCannedCatch(2, false, "catch_2f", 20, 29f, 0f, 64, true),
            new SourceCannedCatch(2, false, "catch_03", 20, 37f, 0f, 48, true),
            new SourceCannedCatch(2, false, "catch_03f", 20, 37f, 0f, 48, true),
            new SourceCannedCatch(2, false, "catch_05", 21, 37f, 0f, 37, true),
            new SourceCannedCatch(2, false, "catch_05f", 21, 37f, 0f, 37, true),
            new SourceCannedCatch(2, false, "catch_06", 10, 22f, 0f, 19, true)
        };
        private static readonly SourceCannedCatch[] Left = {
            new SourceCannedCatch(60, true, "c2p_side_anim", 8, 14f, 0f, 19),
            new SourceCannedCatch(2, false, "c_jgut1_anim", 15, 22f, 0f, 35),
            new SourceCannedCatch(2, false, "c_jmp1_anim", 20, 31f, 3.3f, 35, true),
            new SourceCannedCatch(2, false, "c_jmp2_anim", 20, 32.3f, 2.2f, 37, true),
            new SourceCannedCatch(2, false, "c_sidrol_anim", 17, 29f, 7.9f, 41, true),
            new SourceCannedCatch(2, false, "c_splits_anim", 23, 21f, 1.1f, 39),
            new SourceCannedCatch(2, false, "c_sdgut_anim", 10, 13f, 1.1f, 10),
            new SourceCannedCatch(1, false, "c_spin2_anim", 24, 25f, 1f, 44, true),
            new SourceCannedCatch(2, false, "c_bicyc_anim", 23, 29f, 7.3f, 56, true),
            new SourceCannedCatch(1, false, "c_div2h_anim", 18, 26.3f, 11.1f, 39, true),
            new SourceCannedCatch(2, false, "catch_01", 20, 29f, 0f, 48, true),
            new SourceCannedCatch(2, false, "catch_02", 20, 29f, 0f, 64, true),
            new SourceCannedCatch(2, false, "catch_03", 20, 37f, 0f, 48, true),
            new SourceCannedCatch(2, false, "catch_05", 21, 37f, 0f, 37, true),
            new SourceCannedCatch(2, false, "catch_06", 10, 22f, 0f, 19, true)
        };

        internal static void Pass(SimulationState model, ActorState thrower, ActorState target,
            SourceVector3 wrist, Func<int, int> random)
        {
            float speed = thrower.PendingThrowTurbo ? 5.70f : 5.50f;
            float yards = 60f;
            float maximumDivisor = speed;
            if (model.SourceTeamFire[(int)thrower.Team])
            {
                speed = 7.50f;
                yards = 120f;
                // FBALL_YMAX/FTIME_MAX use TBALL_VELOCITY in the active source.
                maximumDivisor = 5.70f;
            }
            float maximumY = -1f * SourceBallDriver.Gravity * yards
                * SourceTiming.SourceUnitsPerYard / (2f * maximumDivisor);
            float timeMaximum = yards * SourceTiming.SourceUnitsPerYard / maximumDivisor;
            SourceVector3 destination = SourcePlayerDriver.WorldPosition(target);
            float vxp = target.SourceVelocity.X, vzp = target.SourceVelocity.Z;
            float time = InterceptTime(wrist, destination, vxp, vzp, speed);
            int total = (int)(time + 0.5f);
            float vx = (destination.X - wrist.X) / time + vxp;
            float vz = (destination.Z - wrist.Z) / time + vzp;
            float vy = (21f - wrist.Y) / time - SourceBallDriver.Gravity / 2f * time;
            bool canned = true;
            if (vy > maximumY)
            {
                vy = maximumY;
                time = timeMaximum;
                total = (int)(time + 0.5f);
                canned = false;
            }
            float tx = wrist.X + time * vx, tz = wrist.Z + time * vz;
            bool clip = false;
            if (Math.Abs(tx) >= SourceBallDriver.HalfLength - SourceTiming.SourceUnitsPerYard)
            {
                tx = tx > 0f ? SourceBallDriver.HalfLength - SourceTiming.SourceUnitsPerYard
                    : SourceTiming.SourceUnitsPerYard - SourceBallDriver.HalfLength;
                clip = true;
            }
            if (Math.Abs(tz) >= SourceBallDriver.HalfWidth - SourceTiming.SourceUnitsPerYard)
            {
                tz = tz > 0f ? SourceBallDriver.HalfWidth - SourceTiming.SourceUnitsPerYard
                    : SourceTiming.SourceUnitsPerYard - SourceBallDriver.HalfWidth;
                clip = true;
            }
            if (clip)
            {
                float dx = tx - wrist.X, dz = tz - wrist.Z;
                time = (float)Math.Sqrt(dx * dx + dz * dz) / speed;
                vx = dx / time;
                vy = (21f - wrist.Y) / time - SourceBallDriver.Gravity / 2f * time;
                vz = dz / time;
                // Source intentionally does not recompute ttt after this clip.
                canned = false;
            }
            if (vxp == 0f && vzp == 0f) canned = false;
            if (SourceMatchRules.FieldPosition(destination.X, (int)model.Possession)
                <= model.SourceRules.LineOfScrimmage) canned = false;
            if (target.SourceControllerStation >= 0) canned = false;
            target.SourcePendingCatch = null;
            if (canned)
            {
                SourceCannedCatch? choice = ChooseCatch(target, destination, vx, vz, vxp, vzp, total, random);
                if (choice.HasValue)
                {
                    SourceCannedCatch selected = choice.Value;
                    vy += (selected.Height - 21f) / time;
                    FieldVector offset = SourceAnimationLayerState.Rotate(0f, selected.Offset, target.Facing, target.SourceDirections);
                    vx += offset.X / time;
                    vz += offset.Z / time;
                    target.SourcePendingCatch = selected.Script;
                    target.SourceCatchLaunchTime = selected.LaunchTime;
                    target.SourceCatchTwoPart = selected.TwoPart;
                }
            }
            Launch(model, wrist, vx, vy, vz, time, total);
        }

        internal static SourceCannedCatch? ChooseCatch(ActorState target, SourceVector3 destination,
            float vx, float vz, float vxp, float vzp, int total, Func<int, int> random)
        {
            bool unint = target.AnimationState.Primary.IsUninterruptible
                || target.AnimationState.TwoPart && target.AnimationState.Secondary.IsUninterruptible;
            float outX = Math.Abs(vxp) < 0.01 ? 10000f : (SourceBallDriver.HalfLength - destination.X) / vxp;
            if (outX < 0f) outX = (-SourceBallDriver.HalfLength - destination.X) / vxp;
            float outZ = Math.Abs(vzp) < 0.01 ? 10000f : (SourceBallDriver.HalfWidth - destination.Z) / vzp;
            if (outZ < 0f) outZ = (-SourceBallDriver.HalfWidth - destination.Z) / vzp;
            float inbounds = Math.Min(outX, outZ);
            float magnitude = (float)Math.Sqrt(vx * vx + vz * vz);
            float nxb = vx / magnitude, nzb = vz / magnitude;
            magnitude = (float)Math.Sqrt(vxp * vxp + vzp * vzp);
            FieldVector np = magnitude > 0.01f ? new FieldVector(vxp / magnitude, vzp / magnitude)
                : SourceAnimationLayerState.Rotate(0f, 1f, target.Facing, target.SourceDirections);
            float dot = nxb * np.X + nzb * np.Z;
            SourceCannedCatch[] table = dot >= 0.7071f ? Behind : dot <= -0.7071f ? Front
                : nzb * np.X - nxb * np.Z > 0f ? Left : Right;
            int weight = 0;
            for (int i = 0; i < table.Length; i++)
            {
                SourceCannedCatch row = table[i];
                if (row.LaunchTime >= total - 2 || row.ExcludeUninterruptible && unint
                    || total - row.LaunchTime + row.ControlTime > (int)inbounds) continue;
                weight += row.Weight;
            }
            if (weight == 0) return null;
            int selection = 1 + random(weight);
            // Selection's exclusions differ from summation in the active code.
            for (int i = 0; i < table.Length; i++)
            {
                SourceCannedCatch row = table[i];
                if (row.LaunchTime >= total || row.ExcludeUninterruptible && unint
                    || row.ControlTime > inbounds) continue;
                selection -= row.Weight;
                if (selection <= 0) return row;
            }
            throw new InvalidOperationException("Source catch table exhausted its selected probability.");
        }

        internal static void Lateral(SimulationState model, ActorState target, SourceVector3 wrist, bool fieldGoal,
            float speed = 2.70f, float targetZOffset = 0f)
        {
            SourceVector3 destination = SourcePlayerDriver.WorldPosition(target);
            destination = new SourceVector3(destination.X, destination.Y, destination.Z + targetZOffset);
            float desiredY = fieldGoal ? 5f : 14f;
            float time = InterceptTime(wrist, destination, target.SourceVelocity.X, target.SourceVelocity.Z, speed);
            int total = (int)(time + 0.5f);
            float vx = (destination.X - wrist.X) / total + target.SourceVelocity.X;
            float vz = (destination.Z - wrist.Z) / total + target.SourceVelocity.Z;
            float vy = (desiredY - wrist.Y) / total + SourceBallDriver.Gravity / -2f * time;
            float maximumY = -1f * SourceBallDriver.Gravity * 20f * 6f * 1.3f / (2f * speed);
            if (vy > maximumY)
            {
                vy = maximumY;
                total = (int)(20f * 6f * 1.3f / speed + 0.5f);
            }
            float tx = wrist.X + time * vx;
            if (Math.Abs(tx) >= SourceBallDriver.HalfLength - SourceTiming.SourceUnitsPerYard)
            {
                tx = tx > 0f ? SourceBallDriver.HalfLength - SourceTiming.SourceUnitsPerYard
                    : SourceTiming.SourceUnitsPerYard - SourceBallDriver.HalfLength;
                time = (tx - wrist.X) / vx;
                total = (int)(time + 0.5f);
                vy = (21f - wrist.Y) / time - SourceBallDriver.Gravity / 2f * time;
            }
            float tz = wrist.Z + time * vz;
            if (Math.Abs(tz) >= SourceBallDriver.HalfWidth - SourceTiming.SourceUnitsPerYard)
            {
                tz = tz > 0f ? SourceBallDriver.HalfWidth - SourceTiming.SourceUnitsPerYard
                    : SourceTiming.SourceUnitsPerYard - SourceBallDriver.HalfWidth;
                time = (tz - wrist.Z) / vz;
                total = (int)(time + 0.5f);
                vy = (21f - wrist.Y) / time - SourceBallDriver.Gravity / 2f * time;
            }
            Launch(model, wrist, vx, vy, vz, total, total);
        }

        private static void Launch(SimulationState model, SourceVector3 wrist, float vx, float vy, float vz,
            float targetTime, int total)
        {
            model.SourceBallOrigin = wrist;
            SourceBallDriver.SetPosition(model, wrist);
            SourceBallDriver.SetTarget(model, wrist.X + targetTime * vx, wrist.Z + targetTime * vz);
            model.SourceBallVelocity = new FieldVector(vx, vz);
            model.SourceBallVerticalVelocity = vy;
            model.PassTicksElapsed = 0;
            model.PassTicksTotal = total;
            SourceBallDriver.Publish(model);
        }

        private static float InterceptTime(SourceVector3 from, SourceVector3 to, float vx, float vz, float speed)
        {
            float dx = to.X - from.X, dz = to.Z - from.Z;
            float a = vx * vx + vz * vz - speed * speed;
            float b = 2f * (vx * dx + vz * dz);
            float c = dx * dx + dz * dz;
            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f) return 150f;
            float squareRoot = (float)Math.Sqrt(discriminant);
            float first = (-1f * b + squareRoot) / (2f * a);
            float second = (-1f * b - squareRoot) / (2f * a);
            if (first <= 0f && second <= 0f) return 150f;
            if (first > 0f && second > 0f) return first <= second ? second : first;
            return first <= 0f ? second : first;
        }
    }
}
