using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Blitz.Gameplay;

internal static class SentinelChecks
{
    private static int assertions;
    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new BallAssertionFailure(label);
    }

    private static SourceAiFrame Frame(SourcePlaybook playbook, SourceRulesState rules)
    {
        var actors = Enumerable.Range(0, 14).Select(id => new SourceAiActor(id,
            new SourcePlanar(id % 7 * 10, id / 7 * 30), default(SourcePlanar), SourceActorMode.Receiver));
        return new SourceAiFrame(actors, playbook.Directions, rules,
            new SourceAiBall(SourceBallType.Pass, default(SourcePlanar), default(SourcePlanar),
                default(SourcePlanar), -1), 1);
    }

    private static void Retains(SourcePlaybook playbook, SourceRulesState rules, int down, string label)
    {
        SourceAiFrame frame = null;
        try { frame = Frame(playbook, rules); }
        catch (ArgumentOutOfRangeException error)
        {
            if (error.ParamName != "rules") throw;
        }
        Check(frame != null, label + " accepted");
        Check(frame.Rules.Down == down && frame.Rules.PlayType == rules.PlayType, label + " retained without clamping");
    }

    private static void Rejects(SourcePlaybook playbook, int down, SourcePlayType type)
    {
        bool rejected = false;
        try { Frame(playbook, new SourceRulesState(0, 0, down, 20, 50, type)); }
        catch (ArgumentOutOfRangeException error) { rejected = error.ParamName == "rules"; }
        Check(rejected, "SourceAiFrame rejects " + type + " down=" + down);
    }

    internal static int Run(FullKernelInputs real, string oracleRoot)
    {
        int originalDown = int.Parse(File.ReadAllText(Path.Combine(oracleRoot, "pat-sentinel.tsv")).Trim(),
            CultureInfo.InvariantCulture);
        Check(originalDown == -1, "unchanged pinned nfl.c PR_TOUCHDOWN down assignment is -1");
        foreach (int side in new[] { 0, 1 })
        {
            SourcePlayResolution touchdown = SourceMatchRules.Resolve(new SourcePlayEndInput(
                new SourceRulesState(side, side, 1, 20, 50), SourcePlayEndCause.CarrierTouchdown,
                SourceBallType.Pass, SourceGameFlags.None, side, side == 0 ? 391f : -391f));
            Check(touchdown.State.Down == originalDown && touchdown.State.PlayType == SourcePlayType.ExtraPoint,
                "actual Resolve preserves original touchdown/PAT sentinel side " + side);
            Retains(real.Playbook, touchdown.State, originalDown, "SourceAiFrame original PAT down=-1 side " + side);
            SourcePatDecision pat = SourceMatchFlow.SelectPat(new SourcePatInput(
                touchdown.State, 0, 0x10000, 6, 0, true, SourcePatChoice.FreeKick));
            Check(pat.State.Down == originalDown && pat.State.PlayType == SourcePlayType.Kickoff,
                "actual free PAT keeps original -1 into kickoff side " + side);
            Retains(real.Playbook, pat.State, originalDown, "SourceAiFrame original kickoff down=-1 side " + side);
        }
        for (int down = 0; down <= 4; down++)
            Retains(real.Playbook, new SourceRulesState(0, 0, down, 20, 50), down,
                "existing accepted normal down range " + down);
        foreach (SourcePlayType type in new[] { SourcePlayType.Normal, SourcePlayType.Kickoff,
            SourcePlayType.ExtraPoint, SourcePlayType.Punt, SourcePlayType.FieldGoal })
        {
            Rejects(real.Playbook, -2, type);
            Rejects(real.Playbook, 5, type);
            if (type != SourcePlayType.Kickoff && type != SourcePlayType.ExtraPoint)
                Rejects(real.Playbook, -1, type);
        }
        return assertions;
    }
}
