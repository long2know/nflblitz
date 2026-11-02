using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Blitz.Contracts;

namespace Blitz.Presentation
{
    public enum SourceCabinetMenuStage { Joining, Teams, Rosters, Versus, Playing, NextMatch, Records }

    public sealed class SourceCabinetMenuSide
    {
        public string Name { get; }
        public string City { get; }
        public string Stations { get; }
        public ReadOnlyCollection<string> Roster { get; }
        public int SelectedSlot { get; }
        public bool Ready { get; }
        public bool Editing { get; }

        public SourceCabinetMenuSide(string name, string city, string stations, string[] roster,
            int selectedSlot, bool ready, bool editing)
        {
            Name = name; City = city; Stations = stations;
            Roster = Array.AsReadOnly((string[])roster.Clone());
            SelectedSlot = selectedSlot; Ready = ready; Editing = editing;
        }
    }

    public sealed class SourceCabinetMenuView
    {
        public SourceCabinetMenuStage Stage { get; }
        public int ParticipatingStationMask { get; }
        public bool WaitingForNeutral { get; }
        public SourceCabinetMenuSide Home { get; }
        public SourceCabinetMenuSide Away { get; }
        public ReadOnlyCollection<string> Settings { get; }
        public int SelectedSetting { get; }
        public bool DefaultAudiblesEnabled { get; }
        public SourceCabinetStatistics PreviousMatch { get; }

        public SourceCabinetMenuView(SourceCabinetMenuStage stage, int participants, bool waitingForNeutral,
            SourceCabinetMenuSide home, SourceCabinetMenuSide away, string[] settings, int selectedSetting,
            bool defaultAudiblesEnabled, SourceCabinetStatistics previousMatch)
        {
            Stage = stage; ParticipatingStationMask = participants; WaitingForNeutral = waitingForNeutral;
            Home = home; Away = away; Settings = Array.AsReadOnly((string[])settings.Clone());
            SelectedSetting = selectedSetting; DefaultAudiblesEnabled = defaultAudiblesEnabled;
            PreviousMatch = previousMatch;
        }
    }

    public sealed class SourceCabinetStatRow
    {
        public string Key { get; }
        public string Label { get; }
        public string Home { get; }
        public string Away { get; }
        internal SourceCabinetStatRow(string key, string label, string home, string away)
        { Key = key; Label = label; Home = home; Away = away; }
    }

    // A read-only projection of the existing producers, never a second set of counters.
    public sealed class SourceCabinetStatistics
    {
        public SourceMatchView Match { get; }
        public int HomeScore { get; }
        public int AwayScore { get; }
        public int CompletedQuarterIndex { get; }
        public TeamId? Winner { get { return HomeScore == AwayScore ? (TeamId?)null :
            HomeScore > AwayScore ? TeamId.Home : TeamId.Away; } }
        public ReadOnlyCollection<SourceCabinetStatRow> Rows { get; }
        public const string Unavailable = "--";

        public SourceCabinetStatistics(SourceMatchView match, int homeScore, int awayScore, int completedQuarterIndex)
        {
            Match = match ?? throw new ArgumentNullException(nameof(match));
            HomeScore = homeScore; AwayScore = awayScore; CompletedQuarterIndex = completedQuarterIndex;
            var rows = new List<SourceCabinetStatRow>();
            var time = match.TimeOfPossession;
            var first = match.ResultProgressionStats;
            var fourth = match.FourthDownAttempts;
            var interceptions = match.Interceptions;
            var completions = match.PassCompletions;
            var passing = match.CompletedPassStats;
            var running = match.PlayEndStats;
            var activity = match.PlayActivityStats;
            var kicks = match.PlaceKickStats;
            var touchdowns = match.TouchdownStats;
            rows.Add(new SourceCabinetStatRow("possession", "TIME OF POSSESSION",
                Clock(time == null ? (int?)null : time.HomeSeconds), Clock(time == null ? (int?)null : time.AwaySeconds)));
            rows.Add(new SourceCabinetStatRow("first-downs", "FIRST DOWNS",
                Number(first == null ? (int?)null : first.HomeFirstDowns), Number(first == null ? (int?)null : first.AwayFirstDowns)));
            rows.Add(new SourceCabinetStatRow("fourth-downs", "4TH DOWN CONV / ATT",
                Pair(first == null ? (int?)null : first.HomeFourthDownConversions, fourth == null ? (int?)null : fourth.HomeFourthDownAttempts),
                Pair(first == null ? (int?)null : first.AwayFourthDownConversions, fourth == null ? (int?)null : fourth.AwayFourthDownAttempts)));
            rows.Add(new SourceCabinetStatRow("interceptions", "INTERCEPTIONS MADE",
                Number(interceptions == null ? (int?)null : interceptions.HomeInterceptionsMade),
                Number(interceptions == null ? (int?)null : interceptions.AwayInterceptionsMade)));
            rows.Add(new SourceCabinetStatRow("turnovers", "LOST: INT / FUMBLE",
                Pair(interceptions == null ? (int?)null : interceptions.AwayInterceptionsMade, activity == null ? (int?)null : activity.HomeLostFumbles),
                Pair(interceptions == null ? (int?)null : interceptions.HomeInterceptionsMade, activity == null ? (int?)null : activity.AwayLostFumbles)));
            rows.Add(new SourceCabinetStatRow("fumbles", "FUMBLES / LOST",
                Pair(activity == null ? (int?)null : activity.HomeFumbles, activity == null ? (int?)null : activity.HomeLostFumbles),
                Pair(activity == null ? (int?)null : activity.AwayFumbles, activity == null ? (int?)null : activity.AwayLostFumbles)));
            rows.Add(new SourceCabinetStatRow("tackles-sacks", "TACKLES / SACKS",
                Pair(activity == null ? (int?)null : activity.HomeTackles, running == null ? (int?)null : running.HomeSacks),
                Pair(activity == null ? (int?)null : activity.AwayTackles, running == null ? (int?)null : running.AwaySacks)));
            rows.Add(new SourceCabinetStatRow("passes", "PASS ATT / COMPLETIONS",
                Pair(activity == null ? (int?)null : activity.HomePassAttempts, completions == null ? (int?)null : completions.HomeCompletions),
                Pair(activity == null ? (int?)null : activity.AwayPassAttempts, completions == null ? (int?)null : completions.AwayCompletions)));
            rows.Add(new SourceCabinetStatRow("passing", "PASS YARDS / AVG",
                Yards(passing == null ? (int?)null : passing.HomePassingYards, completions == null ? (int?)null : completions.HomeCompletions),
                Yards(passing == null ? (int?)null : passing.AwayPassingYards, completions == null ? (int?)null : completions.AwayCompletions)));
            rows.Add(new SourceCabinetStatRow("rushing", "RUSH YARDS / AVG",
                Yards(running == null ? (int?)null : running.HomeRushingYards, running == null ? (int?)null : running.HomeRushes),
                Yards(running == null ? (int?)null : running.AwayRushingYards, running == null ? (int?)null : running.AwayRushes)));
            rows.Add(new SourceCabinetStatRow("returns", "RETURN YARDS / AVG",
                Yards(running == null ? (int?)null : running.HomeReturnYards, running == null ? (int?)null : running.HomeKickReturns),
                Yards(running == null ? (int?)null : running.AwayReturnYards, running == null ? (int?)null : running.AwayKickReturns)));
            rows.Add(new SourceCabinetStatRow("total-yards", "TOTAL YARDS",
                Number(passing == null ? (int?)null : passing.HomeTotalYards), Number(passing == null ? (int?)null : passing.AwayTotalYards)));
            rows.Add(new SourceCabinetStatRow("field-goals", "FIELD GOALS MADE / ATT",
                Pair(kicks == null ? (int?)null : kicks.HomeFieldGoals, kicks == null ? (int?)null : kicks.HomeFieldGoalAttempts),
                Pair(kicks == null ? (int?)null : kicks.AwayFieldGoals, kicks == null ? (int?)null : kicks.AwayFieldGoalAttempts)));
            rows.Add(new SourceCabinetStatRow("touchdowns", "TOUCHDOWNS / PASS TD",
                Pair(touchdowns == null ? (int?)null : touchdowns.HomeTouchdowns, passing == null ? (int?)null : passing.HomePassTouchdowns),
                Pair(touchdowns == null ? (int?)null : touchdowns.AwayTouchdowns, passing == null ? (int?)null : passing.AwayPassTouchdowns)));
            rows.Add(new SourceCabinetStatRow("sack-yards", "SACK YARDS",
                Number(running == null ? (int?)null : running.HomeSackYards), Number(running == null ? (int?)null : running.AwaySackYards)));
            rows.Add(new SourceCabinetStatRow("rush-return-count", "RUSHES / KICK RETURNS",
                Pair(running == null ? (int?)null : running.HomeRushes, running == null ? (int?)null : running.HomeKickReturns),
                Pair(running == null ? (int?)null : running.AwayRushes, running == null ? (int?)null : running.AwayKickReturns)));
            Rows = rows.AsReadOnly();
        }

        public string QuarterScore(TeamId side, int quarter)
        {
            if (quarter > CompletedQuarterIndex) return Unavailable;
            foreach (SourceQuarterScore score in Match.QuarterScores)
                if (score.IsSpecified && score.QuarterIndex == quarter)
                    return Number(side == TeamId.Home ? score.HomeScore : score.AwayScore);
            return Unavailable;
        }

        private static string Number(int? value) { return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : Unavailable; }
        private static string Pair(int? first, int? second) { return Number(first) + "/" + Number(second); }
        private static string Clock(int? seconds) { return seconds.HasValue ?
            Number(seconds.Value / 60) + ":" + (seconds.Value % 60).ToString("00", CultureInfo.InvariantCulture) : Unavailable; }
        private static string Yards(int? yards, int? count)
        {
            int? average = yards.HasValue && count.HasValue ? (count.Value == 0 ? 0 : (int)(0.49f + (float)yards.Value / count.Value)) : (int?)null;
            return Pair(yards, average);
        }
    }

    public static class SourceCabinetCoachingText
    {
        // lib/select/select.c:coaching_tip_scrn hint_list, in its original ordinal order.
        // The game_proc owns both random draws. No draw or selection occurs here.
        private static readonly string[] Tips = {
            "QB JUMP PASS|RUN AWAY FROM BLITZING|DEFENDERS, HIT TURBO & JUMP.|WHILE IN THE AIR, CHOOSE A|RECEIVER AND PRESS THE|PASS BUTTON!|HELPS AVOID QB SACKS!",
            "TURBO BUTTON|USE THE TURBO BUTTON TO MAKE|YOUR PLAYER RUN FASTER, JUMP|HIGHER, AND HIT HARDER!|HARD HITS WILL CAUSE THE BALL|CARRIER TO FUMBLE MORE OFTEN!|USE YOUR TURBO WISELY!",
            "SPIN MOVE|QUICKLY TAP THE TURBO BUTTON|TO PERFORM A SPIN MOVE.|SPIN MOVES CAUSE THE DEFENDER|TO MISS HIS TACKLE!|TURBO METER CAN'T BE EMPTY!|USE YOUR TURBO WISELY!",
            "STIFF ARM|WHILE CARRYING THE BALL BEYOND|THE LINE OF SCRIMMAGE, HOLD|TURBO, THEN PRESS THE PASS|BUTTON.  THIS WILL KNOCK|DOWN A WOULD BE TACKLER!",
            "DIVE FORWARD|WHEN ON OFFENSE, DIVE FOR A|FEW EXTRA YARDS BY HOLDING|TURBO AND QUICKLY TAPPING|THE JUMP BUTTON.",
            "PUSH DOWNS|WHEN ON DEFENSE, HOLD TURBO|AND PRESS THE CHANGE PLAYER|BUTTON TO KNOCK DOWN YOUR|OPPONENT.  KNOCK DOWN A WIDE|RECEIVER RUNNING HIS ROUTE!",
            "SPEED THROUGH|PRESS AND HOLD ALL 3 BUTTONS|AT THE END OF ANY PLAY TO|QUICKLY ADVANCE TO THE NEXT|SCREEN.",
            "HIGH HURDLE|WHEN ON OFFENSE, HOLD TURBO|AND PRESS THE JUMP BUTTON|TO LEAP OVER WOULD BE|TACKLERS!  HOWEVER, FUMBLES|HAPPEN MORE ON HURDLES!",
            "STRONG TACKLE|HOLD TURBO AND PRESS THE|TACKLE BUTTON TO PERFORM AN|AGGRESSIVE TACKLE.  STRONG|TACKLES WILL CAUSE MORE|FUMBLES TO HAPPEN!",
            "HURDLE LINE|WHEN ON DEFENSE, HOLD|TURBO AND PRESS TACKLE TO|HURDLE OVER THE LINE OF|SCRIMMAGE AND QUICKLY SACK|THE QUARTERBACK!",
            "ONSIDE KICK|AFTER ANY SCORE, PRESS UP|ON THE STICK AND PRESS ALL|3 BUTTONS.  THIS WILL CAUSE|THE KICKING TEAM TO PERFORM|A DANGEROUS ONSIDE KICK!",
            "MAN IN MOTION|BEFORE THE SNAP, PUSH STICK|LEFT OR RIGHT TO REPOSITION|ONE RECEIVER.  HIKE THE BALL|WITH BOTH JUMP AND PASS|BUTTONS TO MAKE HIM BLOCK!",
            "EXTRA BLOCKER|HIKE THE BALL WITH BOTH THE|JUMP AND PASS BUTTONS TO|CAUSE MAN IN MOTION TO BLOCK|ONCE YOU HIKE THE BALL!",
            "LOOSE BALL|ON BOBBLES OR FUMBLES, HOLD|THE TURBO BUTTON AND PRESS|THE JUMP BUTTON TO LEAP AT|AND RECOVER THE LOOSE BALL.",
            "JUMP CATCH|IF YOU PRESS THE JUMP|BUTTON WHEN THE PASS IS NEAR|THE TARGET WIDE RECEIVER, HE|WILL JUMP UP TO CATCH IT.|IT SURPRISES THE DEFENDER!",
            "INTERCEPTIONS|IF YOU PRESS THE JUMP|BUTTON WHEN THE PASS IS NEAR|YOUR DEFENDER, HE WILL JUMP|UP TO INTERCEPT IT!|DON'T MISS IT THOUGH!",
            "HIDDEN CURSOR|PUSH STICK UP TWICE ON THE|PLAY SELECT SCREEN TO HIDE|YOUR CURSOR.  THIS KEEPS|YOUR OPPONENT FROM SEEING|WHICH PLAY YOU PICK.",
            "RANDOM PLAY|PLAYERS CAN HAVE THE|CPU CHOOSE A RANDOM|PLAY ON THE PLAY|SELECT PAGE.",
            "SECRET PLAYS|TRY DIFFERENT STICK AND|BUTTON COMBINATIONS TO|FIND HIDDEN PLAYS!",
            "PASS LIMIT|NO QUARTERBACK CAN PASS|THE BALL FURTHER THAN|60 YARDS!|EACH QUARTERBACK HAS HIS|OWN PASS RATING!",
            "SPIN FUMBLES|IF A PLAYER PERFORMS|MULTIPLE SPINS DURING|ONE PLAY, HE IS MORE|LIKELY TO FUMBLE WHEN HE|GETS TACKLED!",
            "FIRE RULES|IF THE DEFENSIVE TEAM|TACKLES THE BALL CARRIER|2 TIMES FOR A LOSS OF|YARDAGE DURING ANY ONE|POSSESSION, HE IS ON FIRE!",
            "EXPERIENCE|YOU NEED TO PLAY AT|LEAST 5 GAMES BEFORE YOUR|NAME WILL APPEAR ON THE|HIGH SCORE PAGES.  EXCEPT|FOR THE WORLD RECORDS PAGE.",
            "EASY PASSING|POINT STICK TO LEFT TO|CHOOSE LEFT MOST RECEIVER.|POINT STICK TO RIGHT TO|CHOOSE RIGHT MOST RECEIVER.|UP CHOOSES MIDDLE RECEIVER.",
            "CHANGE PLAYER|WHEN ON DEFENSE, THE CHANGE|PLAYER BUTTON WILL CAUSE|YOU TO TAKE CONTROL OF THE|DEFENDER NEAREST THE BALL|CARRIER.",
            "SWAT BALL|WHEN ON DEFENSE, AND THE|BALL IS IN THE AIR, THE JUMP|BUTTON WILL CAUSE YOUR MAN|TO JUMP UP AND SWAT THE BALL|DOWN INTO THE GROUND!",
            "FIELD GOALS|THE LINE OF SCRIMMAGE MUST|BE IN ENEMY TERRITORY BEFORE|A FIELD GOAL CAN BE ATTEMPTED",
            "LATERAL BALL|WHEN CARRYING THE BALL, AND A|TEAMMATE IS NEAR, PRESS THE|PASS BUTTON TO LATERAL THE|BALL TO HIM.|WORKS DOWNFIELD ALSO!",
            "STOP CLOCK|WHEN CARRYING THE BALL, JUMP|OUT OF BOUNDS TO STOP THE|CLOCK.  YOU CAN NOT RUN OUT|OF BOUNDS!",
            "AUDIBLES|DOUBLE TAP TURBO TO SHOW|AUDIBLE MENU. PUSH STICK|UP OR DOWN TO CHANGE YOUR|PLAY AT THE LINE.|CREATE A PLAY ALLOWS YOU|TO PERSONALIZE THE AUDIBLE PLAYS."
        };

        public static ReadOnlyCollection<string> Lines(int ordinal)
        {
            if (ordinal < 0 || ordinal >= Tips.Length) throw new ArgumentOutOfRangeException(nameof(ordinal));
            return Array.AsReadOnly(Tips[ordinal].Split('|'));
        }
    }
}
