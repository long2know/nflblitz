using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Gameplay
{
    internal static partial class SourceAnimationPrograms
    {
        internal static IReadOnlyDictionary<string, SourceAnimationProgram> CreateCompleteAv306Extension()
        {
            var programs = new Dictionary<string, SourceAnimationProgram>();
            foreach (KeyValuePair<string, SourceAnimationProgram> entry in CreateCompleteOriginalSoundExtension())
                programs.Add(entry.Key, entry.Value);
            Idle(programs, "qb_ready_anim", "QB_RDY", 0);
            Idle(programs, "qb_ready_f_anim", "QB_RDYF", 0);
            Add(programs, "into_stance_anim", Mode(0), Op(SourceAnimationOp.ZeroVelocity), Seq("RUSH_I"), Run(),
                Mode(SourceAnimationModes.Anchor),
                new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, text: "stance_anim", branchPc: -1));
            return new ReadOnlyDictionary<string, SourceAnimationProgram>(programs);
        }

        internal static IReadOnlyDictionary<string, SourceAnimationProgram> CreateCompleteOriginalSoundExtension()
        {
            var programs = new Dictionary<string, SourceAnimationProgram>();
            foreach (KeyValuePair<string, SourceAnimationProgram> entry in CreateCompleteOriginalExtension())
                programs.Add(entry.Key, entry.Value);
            OriginalHutLoop(programs, "qbhut_knee_anim", "M_KHHUT", 31, 12, 5, 5, 86, 96, 105);
            OriginalHutLoop(programs, "shotgun_anim", "SGUN_2", 28, 39, 45, 35, 164, 173, 182);
            Add(programs, "qbset_anim", Mode(0),
                new SourceAnimationInstruction(SourceAnimationOp.RandomBranch, 5, 100, x: 85f),
                new SourceAnimationInstruction(SourceAnimationOp.RandomBranch, 4, 100, x: 35f),
                new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "qbptr_anim"),
                new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "qbptl_anim"),
                Seq("HUT"), To(26, 2), Cue("hut_snd", 216, 35), Run(3),
                new SourceAnimationInstruction(SourceAnimationOp.RandomBranch, 1, 2, x: 1f, y: 1f, z: 1f),
                Seq("HUT"), Stream(26, 20), Cue("hut_snd", 226, -1), Stream(26, 45),
                new SourceAnimationInstruction(SourceAnimationOp.RandomBranch, 10, 2, x: 1f, y: 1f, z: 1f),
                Seq("HUT"), Stream(26, 10), Cue("hut_snd", 235, -1), Stream(26, 35), Jump(1));
            foreach (string side in new[] { "l", "r" })
                Add(programs, "qbpt" + side + "_anim", Mode(0), Seq("POINT_" + side.ToUpperInvariant()),
                    Cue("hut_snd", side == "l" ? 265 : 290, -2), Run(), Int("HUT", 6),
                    new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, 6, text: "qbset_anim"));
            Add(programs, "qb_pumps_crowd_set_anim", Seq("PSQB1"), Stream(37, 48), Stream(28, 36),
                Stream(39, 50), Cue("hut_snd", 8470, -1), Stream(41, 53), Cue("hut_snd", 8473, -1),
                Run(), Int("HUT", 8), new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "qbset_anim"));
            Add(programs, "qb_claps_set_anim", Seq("PSQB2"), Stream(77, 100), Cue("hut_snd", 8493, -1),
                Stream(45, 58), Cue("hut_snd", 8496, -1), Stream(27, 36),
                Run(), Int("HUT", 8), new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "qbset_anim"));
            Add(programs, "qb_rubs_knee_set_anim", Seq("PSQB3"), Stream(53, 75), Stream(53, 75),
                Cue("hut_snd", 8519, -1), Stream(39, 50), Cue("hut_snd", 8522, -1),
                Run(), Int("HUT", 8), new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "qbset_anim"));
            OriginalInsertedSound(programs, "punt_ball_anim", 4, Cue("attack_snd", 1341, 116));
            OriginalInsertedSound(programs, "kickball_anim", 5, Cue("attack_snd", 1382, 116));
            OriginalInsertedSound(programs, "air_swat_anim", 2, Cue("attack_snd", 1850, -1));
            OriginalGroundSound(programs, "c_jmpint_anim", 13918);
            OriginalGroundSound(programs, "c_jmpint2_anim", 13965);
            OriginalGroundSound(programs, "c_jmpint3_anim", 14008);
            OriginalGroundSound(programs, "c_sidrolf_anim", 14512);
            OriginalGroundSound(programs, "c_roll_anim", 14654);
            OriginalGroundSound(programs, "c_div2h_anim", 14908);
            OriginalFlippedRelease(programs, "qb_rthrow_sl_anim", 587, 593);
            OriginalFlippedRelease(programs, "qb_throw_sl_anim", 728, 734);
            OriginalFlippedRelease(programs, "qb_throw_ll_anim", 787, 793);
            Add(programs, "block_ready_anim_rnd", Mode(SourceAnimationModes.AnchorXZ), Seq("BLK_RDY"),
                Op(SourceAnimationOp.ZeroVelocity), new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, 50),
                Seq("BLK_RDY"), Run(), Jump(4));
            return new ReadOnlyDictionary<string, SourceAnimationProgram>(programs);
        }

        private static void OriginalFlippedRelease(Dictionary<string, SourceAnimationProgram> programs,
            string symbol, int setLine, int clearLine)
        {
            var instructions = new List<SourceAnimationInstruction>(Get(symbol).Instructions);
            int release = instructions.FindIndex(instruction => instruction.Op == SourceAnimationOp.Callback
                && instruction.A == (int)SourceAnimationSignal.ReleaseBall);
            if (release < 0 || instructions.FindLastIndex(instruction => instruction.Op == SourceAnimationOp.Callback
                && instruction.A == (int)SourceAnimationSignal.ReleaseBall) != release)
                throw new System.InvalidOperationException("Original flipped throw requires exactly one release.");
            instructions.Insert(release, new SourceAnimationInstruction(SourceAnimationOp.SetFlip, 1, sourceLine: setLine));
            instructions.Insert(release + 2, new SourceAnimationInstruction(SourceAnimationOp.SetFlip, 0, sourceLine: clearLine));
            programs.Add(symbol, new SourceAnimationProgram(symbol, instructions.ToArray()));
        }

        private static void OriginalInsertedSound(Dictionary<string, SourceAnimationProgram> programs,
            string symbol, int at, SourceAnimationInstruction cue)
        {
            SourceAnimationInstruction[] original = Get(symbol).Instructions;
            var instructions = new List<SourceAnimationInstruction>(original);
            foreach (SourceAnimationInstruction instruction in original)
                if (instruction.Op == SourceAnimationOp.Jump || instruction.Op == SourceAnimationOp.JumpIfNotSequence ||
                    instruction.Op == SourceAnimationOp.RandomBranch || instruction.Op == SourceAnimationOp.BranchIfHasBall ||
                    instruction.Op == SourceAnimationOp.BranchIfNotHasBall)
                    throw new System.InvalidOperationException("The complete-original sound insertion requires a straight-line source program.");
            instructions.Insert(at, cue);
            programs.Add(symbol, new SourceAnimationProgram(symbol, instructions.ToArray()));
        }

        private static void OriginalGroundSound(Dictionary<string, SourceAnimationProgram> programs, string symbol, int line)
        {
            var instructions = (SourceAnimationInstruction[])Get(symbol).Instructions.Clone();
            int replaced = 0;
            for (int pc = 0; pc < instructions.Length; pc++)
                if (instructions[pc].Op == SourceAnimationOp.Callback &&
                    instructions[pc].A == (int)SourceAnimationSignal.GroundContact)
                {
                    instructions[pc] = Cue("grnd_hit_snd", line, -1);
                    replaced++;
                }
            if (replaced != 1)
                throw new System.InvalidOperationException("Original catch landing requires exactly its source ground callback.");
            programs.Add(symbol, new SourceAnimationProgram(symbol, instructions));
        }

        private static void OriginalHutLoop(Dictionary<string, SourceAnimationProgram> programs,
            string symbol, string sequence, int first, int second, int middleTicks, int lastTicks,
            int slowLine, int fastLine, int lastLine)
        {
            Add(programs, symbol, Mode(0), Seq(sequence), To(first, 2), Cue("hut_snd", slowLine, 35), Run(3),
                new SourceAnimationInstruction(SourceAnimationOp.RandomBranch, 1, 2, x: 1f, y: 1f, z: 1f),
                Seq(sequence), Stream(first, 20), Cue("hut_snd", fastLine, -1), Stream(second, middleTicks),
                new SourceAnimationInstruction(SourceAnimationOp.RandomBranch, 6, 2, x: 1f, y: 1f, z: 1f),
                Seq(sequence), Stream(first, 10), Cue("hut_snd", lastLine, -1), Stream(second, lastTicks), Jump(1));
        }
    }
}
