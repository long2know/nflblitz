using System;
using Blitz.AssetImport;

namespace Blitz.Gameplay
{
    // Raw handicap.c globals/powerups, not a replacement for the imported tables.
    public sealed class SourceHandicapSettings
    {
        public int UserBaseAdjustment { get; }
        public int HumanWinStreak { get; }
        public bool RandomDrones { get; }
        public int OffenseTeamMask { get; }
        public int DefenseTeamMask { get; }
        public int DronesTeamMask { get; }
        public bool NoAssist { get; }
        public bool SuperDrones { get; }

        public SourceHandicapSettings(int userBaseAdjustment = 0, int humanWinStreak = 0,
            bool randomDrones = false, int offenseTeamMask = 0, int defenseTeamMask = 0,
            int dronesTeamMask = 0, bool noAssist = false, bool superDrones = false)
        {
            if (humanWinStreak < 0) throw new ArgumentOutOfRangeException(nameof(humanWinStreak));
            if ((offenseTeamMask & ~3) != 0) throw new ArgumentOutOfRangeException(nameof(offenseTeamMask));
            if ((defenseTeamMask & ~3) != 0) throw new ArgumentOutOfRangeException(nameof(defenseTeamMask));
            if ((dronesTeamMask & ~3) != 0) throw new ArgumentOutOfRangeException(nameof(dronesTeamMask));
            UserBaseAdjustment = userBaseAdjustment;
            HumanWinStreak = humanWinStreak;
            RandomDrones = randomDrones;
            OffenseTeamMask = offenseTeamMask;
            DefenseTeamMask = defenseTeamMask;
            DronesTeamMask = dronesTeamMask;
            NoAssist = noAssist;
            SuperDrones = superDrones;
        }
    }

    public sealed partial class GameSimulation
    {
        // player.c:4611-4617: reset all four fields, not just the fire bit.
        private void ResetSourceTeamFire(int side)
        {
            Model.SourceTeamFire[side] = false;
            Model.SourceConsecutiveCompletions[side] = 0;
            Model.SourceLastReceiver[side] = -1;
            Model.SourceConsecutiveLosses[side] = 0;
        }

        private void ApplySourceFirePlayEnd(SourcePlayEndInput input, SourcePlayResolution resolution)
        {
            int side = resolution.Classification.OffenseSide;
            int defense = 1 - side;
            SourcePlayResult result = resolution.Classification.Result;
            // nfl.c:1445-1503: use classified/clamped fpend and the OLD los/side.
            if ((input.State.PlayType == SourcePlayType.Normal || input.State.PlayType == SourcePlayType.ExtraPoint)
                && result != SourcePlayResult.Incomplete && result != SourcePlayResult.FieldGoal
                && side == input.State.InitialOffenseSide && resolution.FieldEnd < input.State.LineOfScrimmage)
            {
                ResetSourceTeamFire(side);
                Model.SourceConsecutiveLosses[defense] = checked(Model.SourceConsecutiveLosses[defense] + 1);
                if (Model.SourceConsecutiveLosses[defense] == 2 && !Model.SourceTeamFire[defense])
                {
                    Model.SourceTeamFire[defense] = true;
                    Model.SourceLastReceiver[defense] = 4 + 7 * defense;
                }
            }
            else if (!Model.SourceTeamFire[defense])
                Model.SourceConsecutiveLosses[defense] = 0;

            // These occur inside the result switch, AFTER the loss-yard check.
            if (resolution.JustGotFirstDown || result == SourcePlayResult.Touchdown)
                ResetSourceTeamFire(defense);
            else if (resolution.TurnoverOnDowns)
            {
                // PR_INCOMPLETE restores off_init before these resets (nfl.c:1694-1712).
                int resetSide = result == SourcePlayResult.Incomplete ? input.State.InitialOffenseSide : side;
                ResetSourceTeamFire(resetSide);
                if (!Model.SourceTeamFire[1 - resetSide]) ResetSourceTeamFire(1 - resetSide);
            }
            else if (result == SourcePlayResult.FailedConversion)
            {
                int original = input.State.InitialOffenseSide;
                if (Model.SourceTeamFire[original]) ResetSourceTeamFire(original);
            }
            else if (result == SourcePlayResult.FieldGoal)
                ResetSourceTeamFire(1 - input.State.InitialOffenseSide);
            else if (result == SourcePlayResult.MissedKick)
                ResetSourceTeamFire(side);
        }

        private void UpdateSourceHandicaps()
        {
            SourceTeamRosterLibrary roster = sourceRosters as SourceTeamRosterLibrary;
            if (roster == null)
                throw new InvalidOperationException("Source57 handicapping requires the existing strict SourceTeamRosterLibrary "
                    + "with original teaminfo.drone_base and diff_settings; no scalar Difficulty fallback is used.");
            SourceHandicapSettings settings = Model.SourceHandicapSettings;
            int side = (int)Model.Possession;
            bool human0 = SourceContactDriver.IsHumanTeam(Model, Blitz.Contracts.TeamId.Home);
            bool human1 = SourceContactDriver.IsHumanTeam(Model, Blitz.Contracts.TeamId.Away);
            int[] levels = Model.SourceTeamHandicap;
            int diff = checked(Math.Abs(checked(Model.HomeScore - Model.AwayScore)));
            diff = checked(diff + checked(diff * Math.Min(3, Model.Quarter - 1)) / 2);
            int adjustment0 = SourceHandicapAdjustment(settings, 0, side);
            int adjustment1 = SourceHandicapAdjustment(settings, 1, side);
            Model.SourceDronesWinning = false;
            Model.SourceHandicapRepetitionBonus = 0;
            if (human0 && human1)
            {
                levels[0] = 5;
                levels[1] = 5;
                if (diff >= 2 && !Model.SourceNoAssist)
                {
                    int winning = Model.AwayScore > Model.HomeScore ? 1 : 0;
                    levels[winning] = 5 - diff / 4;
                    levels[1 - winning] = 5 + (diff + 2) / 4;
                }
                levels[0] = Math.Max(0, Math.Min(10, levels[0] + adjustment0));
                levels[1] = Math.Max(0, Math.Min(10, levels[1] + adjustment1));
            }
            else if (!human0 && !human1)
            {
                levels[0] = roster.GetTeamData(Model.SourceConfiguration.HomeTeam.TeamIndex).DroneBase;
                levels[1] = roster.GetTeamData(Model.SourceConfiguration.AwayTeam.TeamIndex).DroneBase;
            }
            else
            {
                int human = human0 ? 0 : 1;
                int drone = 1 - human;
                int repeated = 0;
                if (human == side)
                {
                    SourcePlayDefinition[] history = Model.SourceHandicapPlayHistory;
                    for (int i = 0; i < history.Length; i++)
                        if (ReferenceEquals(history[i], Model.SourceOffensePlay)) repeated++;
                    for (int i = history.Length - 1; i > 0; i--) history[i] = history[i - 1];
                    history[0] = Model.SourceOffensePlay;
                }
                Model.SourceHandicapRepetitionBonus = repeated;
                int team = settings.RandomDrones ? 5
                    : drone == 0 ? Model.SourceConfiguration.HomeTeam.TeamIndex : Model.SourceConfiguration.AwayTeam.TeamIndex;
                int basis = checked(roster.GetTeamData(team).DroneBase + settings.UserBaseAdjustment);
                if (settings.HumanWinStreak >= 2) basis = checked(basis + 1);
                if (settings.HumanWinStreak >= 4) basis = checked(basis + 1);
                basis = Math.Min(11, basis);
                if (basis < 3)
                    throw new NotSupportedException("handicap.c:157-159 diff_settings[base-3] is outside the original nine rows; base="
                        + basis + ". No invented lower clamp.");
                SourceRosterDifficulty tuning = roster.Tuning.DifficultySettings[basis - 3];
                Model.SourceDronesLose = basis < 4;
                int winning = Model.AwayScore > Model.HomeScore ? 1 : 0;
                if (winning == human || diff == 0)
                {
                    levels[human] = Math.Max(0, 5 - diff / 4);
                    levels[drone] = Math.Min(11, basis + (diff + 2) / 4);
                    if (diff < tuning.Panic) levels[drone] = Math.Min(10, levels[drone]);
                }
                else
                {
                    Model.SourceDronesWinning = true;
                    levels[human] = Model.SourceDronesLose ? 10 : Math.Min(tuning.HumanMaximum, 5 + (diff + 2) / 4);
                    levels[drone] = Model.SourceDronesLose ? 0 : Math.Max(tuning.DroneMinimum, basis - diff / 4);
                }
                levels[human] -= repeated / 2;
                levels[drone] += (repeated + 1) / 2;
                if (Model.SourceSuperDrones)
                {
                    levels[human] = Math.Max(5, levels[human]);
                    levels[drone] = 11;
                    Model.SourceDronesLose = false;
                }
                levels[human] = Math.Min(10, levels[human] + (human == 0 ? adjustment0 : adjustment1));
            }
            for (int i = 0; i < levels.Length; i++)
            {
                if ((levels[i] < 0 || levels[i] > 11) && Compatibility == Blitz.Contracts.SourceGameplayCompatibility.StrictSource)
                    throw new NotSupportedException("handicap.c:set_handicaps produced team_handicap[" + i + "]="
                        + levels[i] + " outside the original twelve consumer rows. No guessed/clamped table read.");
                Model.SourceEffectiveTeamHandicap[i] = Model.ResolveCompatibilityIndex(
                    Blitz.Contracts.SourceCompatibilityRepairKind.HandicapConsumerIndex, i, levels[i], 12,
                    "handicap.c:set_handicaps -> drone.c:init_drones_prelineup");
            }
            Model.SourceAiParameters = new SourceAiParameters(sourcePlaybook,
                Model.SourceEffectiveTeamHandicap[0], Model.SourceEffectiveTeamHandicap[1]);
        }

        private static int SourceHandicapAdjustment(SourceHandicapSettings settings, int team, int offense)
        {
            int mask = 1 << team;
            int adjustment = (((team == offense ? settings.OffenseTeamMask : settings.DefenseTeamMask) & mask) != 0 ? 2 : 0)
                + ((settings.DronesTeamMask & mask) != 0 ? 2 : 0);
            return Math.Min(3, adjustment);
        }
    }
}
