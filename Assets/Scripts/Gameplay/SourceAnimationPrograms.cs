using System;
using System.Collections.Generic;

namespace Blitz.Gameplay
{
    internal static class SourceAnimationModes
    {
        internal const uint End = 0x0001;
        internal const uint Uninterruptible = 0x0002;
        internal const uint TwoPart = 0x0004;
        internal const uint Streaming = 0x0008;
        internal const uint PrepareTransition = 0x0010;
        internal const uint RotationOnly = 0x0020;
        internal const uint Reacting = 0x0080;
        internal const uint NoCatch = 0x0800;
        internal const uint AnchorY = 0x00010000;
        internal const uint AnchorXZ = 0x00020000;
        internal const uint Anchor = AnchorY | AnchorXZ;
        internal const uint Secondary = 0x00040000;
        internal const uint Interpolating = 0x00080000;
        internal const uint TurnHack = 0x00200000;
    }

    internal enum SourceAnimationOp
    {
        SetMode, NewSequence, NextFrame, SkipFrames, RunToFrame, RunToEnd,
        Interpolate, Stream, StreamToEnd, Jump, JumpIfNotSequence, ChangeScript,
        ZeroVelocity, SetVelocity, SetYVelocity, FacingVelocity, Friction,
        RotateHeading, PrepareTransition, Callback, WaitCondition, BranchIfHasBall, End
    }

    [Flags]
    internal enum SourceAnimationSignal
    {
        None = 0,
        ReleaseBall = 1,
        GeneralRun = 2,
        PlayerDown = 4,
        GroundContact = 8
    }

    internal readonly struct SourceAnimationInstruction
    {
        internal readonly SourceAnimationOp Op;
        internal readonly string Text;
        internal readonly int A;
        internal readonly int B;
        internal readonly float X;
        internal readonly float Y;
        internal readonly float Z;

        internal SourceAnimationInstruction(SourceAnimationOp op, int a = 0, int b = 0,
            string text = null, float x = 0f, float y = 0f, float z = 0f)
        {
            Op = op;
            Text = text;
            A = a;
            B = b;
            X = x;
            Y = y;
            Z = z;
        }
    }

    internal sealed class SourceAnimationProgram
    {
        internal readonly string Symbol;
        internal readonly SourceAnimationInstruction[] Instructions;

        internal SourceAnimationProgram(string symbol, params SourceAnimationInstruction[] instructions)
        {
            Symbol = symbol;
            Instructions = instructions;
        }
    }

    // Programs refer to selected SEQ ordinals, not WAN authoring frame numbers.
    // Evidence: plyrseq.c 28cef0d1082f2b0c9241930fdc8886de4c8f5438.
    internal static class SourceAnimationPrograms
    {
        private static readonly Dictionary<string, SourceAnimationProgram> Programs = Build();

        internal static SourceAnimationProgram Get(string symbol)
        {
            SourceAnimationProgram program;
            if (!Programs.TryGetValue(symbol, out program))
                throw new NotSupportedException("Unported source animation script: " + symbol);
            return program;
        }

        private static SourceAnimationInstruction Mode(uint bits)
        {
            return new SourceAnimationInstruction(SourceAnimationOp.SetMode, unchecked((int)bits));
        }

        private static SourceAnimationInstruction Seq(string name)
        {
            return new SourceAnimationInstruction(SourceAnimationOp.NewSequence, text: name);
        }

        private static SourceAnimationInstruction Run(int ticks = 1)
        {
            return new SourceAnimationInstruction(SourceAnimationOp.RunToEnd, ticks);
        }

        private static SourceAnimationInstruction To(int frame, int ticks = 1)
        {
            return new SourceAnimationInstruction(SourceAnimationOp.RunToFrame, frame, ticks);
        }

        private static SourceAnimationInstruction Int(string sequence, int ticks, int frame = 0)
        {
            return new SourceAnimationInstruction(SourceAnimationOp.Interpolate, ticks, frame, sequence);
        }

        private static SourceAnimationInstruction Stream(int frames, int ticks)
        {
            return new SourceAnimationInstruction(SourceAnimationOp.Stream, frames, ticks);
        }

        private static SourceAnimationInstruction Jump(int pc)
        {
            return new SourceAnimationInstruction(SourceAnimationOp.Jump, pc);
        }

        private static SourceAnimationInstruction Call(SourceAnimationSignal signal)
        {
            return new SourceAnimationInstruction(SourceAnimationOp.Callback, (int)signal);
        }

        private static SourceAnimationInstruction Op(SourceAnimationOp op)
        {
            return new SourceAnimationInstruction(op);
        }

        private static void Add(Dictionary<string, SourceAnimationProgram> programs,
            string symbol, params SourceAnimationInstruction[] instructions)
        {
            programs.Add(symbol, new SourceAnimationProgram(symbol, instructions));
        }

        private static void Stance(Dictionary<string, SourceAnimationProgram> programs,
            string symbol, string sequence, int ticks, int initialRandomRange,
            uint mode = 0, bool zeroVelocity = false)
        {
            if (zeroVelocity)
                Add(programs, symbol, Op(SourceAnimationOp.ZeroVelocity), Mode(mode), Seq(sequence),
                    new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, -1, initialRandomRange),
                    Run(ticks), Seq(sequence), Run(ticks), Jump(5));
            else
                Add(programs, symbol, Mode(mode), Seq(sequence),
                    new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, -1, initialRandomRange),
                    Run(ticks), Seq(sequence), Run(ticks), Jump(4));
        }

        private static void Idle(Dictionary<string, SourceAnimationProgram> programs,
            string symbol, string sequence, int transitionTicks)
        {
            if (transitionTicks == 0)
                Add(programs, symbol, Mode(SourceAnimationModes.AnchorXZ),
                    Op(SourceAnimationOp.ZeroVelocity), Seq(sequence), Run(), Jump(2));
            else
                Add(programs, symbol, Mode(SourceAnimationModes.AnchorXZ),
                    Op(SourceAnimationOp.ZeroVelocity), Int(sequence, transitionTicks),
                    Run(), Seq(sequence), Run(), Jump(4));
        }

        private static void QuarterbackRun(Dictionary<string, SourceAnimationProgram> programs,
            string symbol, string sequence, bool turbo)
        {
            var remaining = turbo
                ? new SourceAnimationInstruction(SourceAnimationOp.StreamToEnd, x: 0.81f)
                : Run();
            Add(programs, symbol, Mode(SourceAnimationModes.TwoPart | SourceAnimationModes.AnchorXZ),
                Int(sequence, -1, -2), remaining, Seq(sequence), remaining, Jump(3));
        }

        private static void FieldRun(Dictionary<string, SourceAnimationProgram> programs,
            string symbol, string sequence, string preceding, int transitionTicks, bool stream,
            bool hasInterpolatedVariant = true)
        {
            var finish = stream
                ? new SourceAnimationInstruction(SourceAnimationOp.StreamToEnd, x: 0.733f)
                : Run();
            var cycle = stream ? Stream(30, 22) : Run();
            Add(programs, symbol, Mode(SourceAnimationModes.AnchorXZ),
                new SourceAnimationInstruction(SourceAnimationOp.JumpIfNotSequence, 6, text: preceding),
                Mode(0), Int(sequence, transitionTicks, -1), finish, Jump(6),
                Seq(sequence), cycle, Jump(6));
            if (hasInterpolatedVariant)
                Add(programs, symbol + "_int", Mode(SourceAnimationModes.AnchorXZ),
                    Int(sequence, -1, -2), finish, Seq(sequence), cycle, Jump(3));
        }

        private static Dictionary<string, SourceAnimationProgram> Build()
        {
            var p = new Dictionary<string, SourceAnimationProgram>(StringComparer.Ordinal);
            Idle(p, "qb_stance_anim", "QBSTANCE", 0);
            Idle(p, "qb_flip_stance_anim", "QBFSTANC", 0);
            Idle(p, "block_ready_anim", "BLK_RDY", 0);
            Idle(p, "breath_anim", "BREATH", 16);
            Idle(p, "breath2_anim", "M_SLOBP2", 10);
            Idle(p, "breath3_anim", "M_SLOBP3", 16);
            Idle(p, "stance_wb_anim_int", "RDY_WB", 12);
            Add(p, "stance_anim", Mode(0), Op(SourceAnimationOp.ZeroVelocity),
                Seq("RUSH_C"), Run(), Jump(0));
            Stance(p, "stance3pt_anim", "3P_C", 1, 60);
            Stance(p, "stance2pt_anim_rnd", "2P_C", 2, 79, zeroVelocity: true);
            Stance(p, "stance2pt2_anim_rnd", "2P_C2", 2, 79, SourceAnimationModes.AnchorXZ, true);
            Add(p, "wr_set_anim", Seq("SPT_C"),
                new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, -1, 60),
                Run(), Seq("SPT_C"), Run(), Jump(3));
            Add(p, "center_set_anim", Seq("CENTER_C"), Stream(58, 116), Jump(0));
            Add(p, "c_standing2_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor),
                Seq("C_STND1"), new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1),
                new SourceAnimationInstruction(SourceAnimationOp.WaitCondition, 1),
                new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 100),
                new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 35),
                Stream(33, 40), Run(), Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "c_standing_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor),
                Seq("C_STND1"), new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1),
                new SourceAnimationInstruction(SourceAnimationOp.WaitCondition, 2), Stream(35, 40),
                Run(), Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "run_hold_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor),
                Seq("RUN"), new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1),
                new SourceAnimationInstruction(SourceAnimationOp.WaitCondition, 2),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "kickball_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor),
                Op(SourceAnimationOp.ZeroVelocity), Seq("M_KICK"),
                new SourceAnimationInstruction(SourceAnimationOp.SetVelocity, z: 0.25f),
                new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, 4),
                Stream(38, 30), new SourceAnimationInstruction(SourceAnimationOp.Friction, x: 0.055f),
                Stream(32, 25), new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 3),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "center_snap_anim", Mode(SourceAnimationModes.Anchor), Seq("C_SNAP"),
                Stream(21, 4), Stream(23, 8), Mode(SourceAnimationModes.Anchor),
                new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "stance3pt_jumpback_anim"));
            Add(p, "stance3pt_jumpback_anim", Mode(SourceAnimationModes.Anchor), Seq("3P_JB"),
                Run(), Mode(SourceAnimationModes.Anchor),
                new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "block_ready_anim_rnd"));
            Add(p, "block_ready_anim_rnd", Mode(SourceAnimationModes.AnchorXZ), Seq("BLK_RDY"),
                Op(SourceAnimationOp.ZeroVelocity),
                new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, 50), Run(),
                Seq("BLK_RDY"), Run(), Jump(5));
            Add(p, "charge_anim", Mode(SourceAnimationModes.AnchorXZ), Op(SourceAnimationOp.ZeroVelocity),
                Int("CHARGE3P", 8), Run(), Op(SourceAnimationOp.End));
            Add(p, "wr_set_anim_int", Int("SPT_C", 4, 1), Run(),
                Seq("SPT_C"), Run(), Jump(2));
            Add(p, "safty2pt_anim_rnd", Mode(SourceAnimationModes.AnchorXZ),
                Int("L_SAFTY", 8), Op(SourceAnimationOp.ZeroVelocity),
                Stream(34, 70), Stream(34, 70), Stream(34, 70), Stream(34, 70),
                Seq("L_SAFTY"), Stream(34, 70), Stream(34, 70), Stream(34, 70),
                Stream(34, 70), Jump(7));

            // The HUT branch is explicit; random pointing/intro selection is not
            // silently represented by a different clip.
            Add(p, "qbset_anim", Mode(0), Seq("HUT"), To(26, 2),
                Run(3), Jump(1));
            string[] qbSymbols = { "qb_run_anim", "qb_runl_anim", "qb_runb_anim",
                "qb_runr_anim", "qb_runl45_anim", "qb_runr45_anim" };
            string[] qbSequences = { "QBRUNF", "QBRUNL", "QBRUNB",
                "QBRUNR", "QBRUNL45", "QBRUNR45" };
            string[] turboSymbols = { "qb_t_run_anim", "qb_t_runl_anim", "qb_t_runb_anim",
                "qb_t_runr_anim", "qb_t_runl45_anim", "qb_t_runr45_anim" };
            string[] turboSequences = { "QBRUNF_T", "QBRUNL_T", "QBRUNB_T",
                "QBRUNR_T", "QBRL45_T", "QBRR45_T" };
            for (int i = 0; i < qbSymbols.Length; i++)
            {
                QuarterbackRun(p, qbSymbols[i], qbSequences[i], false);
                QuarterbackRun(p, turboSymbols[i], turboSequences[i], true);
            }
            FieldRun(p, "run_anim", "RUN", "R_TRB", 2, false);
            FieldRun(p, "run_t_anim", "R_TRB", "RUN", 2, true);
            FieldRun(p, "run_wb_anim", "RUN_WB", "R_TRB_WB", 4, false);
            FieldRun(p, "run_t_wb_anim", "R_TRB_WB", "RUN_WB", 4, false);
            FieldRun(p, "r_huff_anim", "HUFFRUN2", "R_TRB", 3, false, false);
            Add(p, "r_t_huff_anim", Mode(SourceAnimationModes.AnchorXZ), Seq("HUFFRUN"), Run(), Jump(1));
            Add(p, "qb_throw_sr_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.AnchorXZ),
                Seq("THROW_SR"), Op(SourceAnimationOp.ZeroVelocity), To(9),
                Call(SourceAnimationSignal.ReleaseBall), Run(), Call(SourceAnimationSignal.GeneralRun),
                Op(SourceAnimationOp.End));
            Add(p, "qb_throw_sl_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.AnchorXZ),
                Seq("THROW_SL"), Op(SourceAnimationOp.ZeroVelocity), To(9),
                Call(SourceAnimationSignal.ReleaseBall), Run(), Call(SourceAnimationSignal.GeneralRun),
                Op(SourceAnimationOp.End));
            foreach (string side in new[] { "ll", "lr" })
                Add(p, "qb_throw_" + side + "_anim",
                    Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.AnchorXZ),
                    Op(SourceAnimationOp.ZeroVelocity), Seq("THROW_" + side.ToUpperInvariant()),
                    Stream(72, 14), Call(SourceAnimationSignal.ReleaseBall), Stream(114, 39),
                    Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            foreach (string side in new[] { "sl", "sr" })
                Add(p, "qb_rthrow_" + side + "_anim",
                    Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.AnchorXZ | SourceAnimationModes.TwoPart),
                    Int("THROW_" + side.ToUpperInvariant(), 4, 6), To(9),
                    Call(SourceAnimationSignal.ReleaseBall), To(23), Stream(26, 13),
                    Mode(0), Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "c_run1_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.AnchorY),
                Int("C_RUN1", 3), Stream(24, 5), Stream(7, 12), Stream(9, 7),
                Stream(20, 8), Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "c_jgut1_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor), Seq("C_JGUT1"),
                Stream(13, 5), Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.AnchorY),
                new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 1.2f),
                Stream(8, 15), Stream(24, -1), Call(SourceAnimationSignal.GeneralRun),
                Op(SourceAnimationOp.End));
            Add(p, "t_dshoul_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor),
                Seq("T_DSHOUL"), new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, 14),
                new SourceAnimationInstruction(SourceAnimationOp.SetVelocity, z: 0.65f), To(17),
                Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor | SourceAnimationModes.NoCatch),
                new SourceAnimationInstruction(SourceAnimationOp.SetVelocity, y: 0.12f, z: 0.93f),
                new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1), Stream(36, -1),
                Call(SourceAnimationSignal.GroundContact),
                new SourceAnimationInstruction(SourceAnimationOp.Friction, x: 0.054f), To(90),
                Int("M_GU_C2", 4),
                new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "get_up_c_anim"));
            Add(p, "t_stshld_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor),
                Seq("T_STSHLD"), Op(SourceAnimationOp.ZeroVelocity),
                new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, 4), Stream(13, 26),
                new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 4),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "h_front1_anim",
                Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor | SourceAnimationModes.Reacting),
                new SourceAnimationInstruction(SourceAnimationOp.RotateHeading, 512),
                new SourceAnimationInstruction(SourceAnimationOp.FacingVelocity, x: 1.2f),
                Seq("H_FRONT1"), new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, 4),
                Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor | SourceAnimationModes.Reacting),
                new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 0.75f),
                new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1), Stream(27, -1),
                Call(SourceAnimationSignal.GroundContact),
                new SourceAnimationInstruction(SourceAnimationOp.Friction, x: 0.055f), To(36),
                Call(SourceAnimationSignal.PlayerDown),
                new SourceAnimationInstruction(SourceAnimationOp.BranchIfHasBall, text: "m_wob_fu_anim"),
                new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "get_up_b_anim"));
            Add(p, "fall_04", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor | SourceAnimationModes.Reacting),
                Seq("FALL_04"), new SourceAnimationInstruction(SourceAnimationOp.FacingVelocity, x: 1.2f),
                new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1),
                new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1),
                Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.AnchorY
                    | SourceAnimationModes.Reacting | SourceAnimationModes.NoCatch),
                new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 1.2f),
                new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1), Stream(9, -1),
                new SourceAnimationInstruction(SourceAnimationOp.Friction, x: 0.08f),
                Call(SourceAnimationSignal.PlayerDown | SourceAnimationSignal.GroundContact), Run(),
                new SourceAnimationInstruction(SourceAnimationOp.RotateHeading, -128),
                new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 4),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "get_up_b_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.AnchorXZ),
                Int("GET_UP_B", 5, 8), Op(SourceAnimationOp.ZeroVelocity), Stream(56, 30),
                new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 4),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "get_up_c_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.AnchorXZ),
                Seq("M_GU_C2"), Op(SourceAnimationOp.ZeroVelocity), Stream(59, 35),
                new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 4),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            return p;
        }
    }
}
