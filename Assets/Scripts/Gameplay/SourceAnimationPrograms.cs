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
        RotateHeading, PrepareTransition, Callback, WaitCondition, BranchIfHasBall,
        SetFlip, SetLateHittable, RotateTorso, FaceBall, RotateToBall, LeapTarget,
        SetLowerUninterruptible, SelectScript, RandomBranch, JumpProgram, BranchSlowerSpin,
        BranchIfNotHasBall, CameraCue, ShakerCue, PresentationCallback, End
    }

    [Flags]
    internal enum SourceAnimationSignal
    {
        None = 0,
        ReleaseBall = 1,
        GeneralRun = 2,
        PlayerDown = 4,
        GroundContact = 8,
        HikeBall = 16,
        PuntBall = 32,
        KickBall = 64,
        DropBall = 128,
        SpinBall = 256,
        SpikeBall = 512,
        FlingBall = 1024
    }

    internal readonly struct SourceAnimationInstruction
    {
        internal readonly SourceAnimationOp Op;
        internal readonly string Text;
        internal readonly int A;
        internal readonly int B;
        internal readonly int C;
        internal readonly int SourceLine;
        internal readonly float X;
        internal readonly float Y;
        internal readonly float Z;

        internal SourceAnimationInstruction(SourceAnimationOp op, int a = 0, int b = 0,
            string text = null, float x = 0f, float y = 0f, float z = 0f, int c = 0, int sourceLine = 0)
        {
            Op = op;
            Text = text;
            A = a;
            B = b;
            C = c;
            SourceLine = sourceLine;
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

        private static SourceAnimationInstruction Cue(string function, int line, int a = 0, int b = 0, int c = 0)
        {
            return new SourceAnimationInstruction(SourceAnimationOp.PresentationCallback, a, b, function,
                c: c, sourceLine: line);
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
                Stream(21, 4), Call(SourceAnimationSignal.HikeBall), Stream(23, 8), Mode(SourceAnimationModes.Anchor),
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

            Add(p, "qbset_anim", Mode(0),
                new SourceAnimationInstruction(SourceAnimationOp.RandomBranch, 5, 100, x: 85f),
                new SourceAnimationInstruction(SourceAnimationOp.RandomBranch, 4, 100, x: 35f),
                new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "qbptr_anim"),
                new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "qbptl_anim"),
                Seq("HUT"), To(26, 2), Run(3),
                new SourceAnimationInstruction(SourceAnimationOp.RandomBranch, 1, 2, x: 1f, y: 1f, z: 1f),
                Seq("HUT"), Stream(26, 20), Stream(26, 45),
                new SourceAnimationInstruction(SourceAnimationOp.RandomBranch, 9, 2, x: 1f, y: 1f, z: 1f),
                Seq("HUT"), Stream(26, 10), Stream(26, 35), Jump(1));
            foreach (string side in new[] { "l", "r" })
                Add(p, "qbpt" + side + "_anim", Mode(0), Seq("POINT_" + side.ToUpperInvariant()), Run(), Int("HUT", 6),
                    new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, 6, text: "qbset_anim"));
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
            Add(p, "t_dshoul_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor),
                Seq("T_DSHOUL"), new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, 14),
                new SourceAnimationInstruction(SourceAnimationOp.SetVelocity, z: 0.65f),
                Cue("get_hit_snd", 2448, 57), To(17),
                Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor | SourceAnimationModes.NoCatch),
                new SourceAnimationInstruction(SourceAnimationOp.SetVelocity, y: 0.12f, z: 0.93f),
                new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1), Stream(36, -1),
                Cue("grnd_hit_snd", 2461, -1), Cue("dust_cloud", 2464),
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
                Cue("get_hit_snd", 5544, -1, -1),
                Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor | SourceAnimationModes.Reacting),
                new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 0.75f),
                new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1), Stream(27, -1),
                Cue("grnd_hit_snd", 5555, -1, 0, -1), Cue("dust_cloud", 5558),
                new SourceAnimationInstruction(SourceAnimationOp.ShakerCue, 3, 10, sourceLine: 5560),
                new SourceAnimationInstruction(SourceAnimationOp.Friction, x: 0.055f), To(36),
                new SourceAnimationInstruction(SourceAnimationOp.Callback, (int)SourceAnimationSignal.PlayerDown,
                    text: "player_down", sourceLine: 5573),
                new SourceAnimationInstruction(SourceAnimationOp.BranchIfHasBall, text: "m_wob_fu_anim"),
                new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "get_up_b_anim"));
            Add(p, "fall_04", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor | SourceAnimationModes.Reacting),
                Cue("get_hit_snd", 15955, -1, -1), Seq("FALL_04"),
                new SourceAnimationInstruction(SourceAnimationOp.BranchIfNotHasBall, 5),
                new SourceAnimationInstruction(SourceAnimationOp.CameraCue, 1, text: "csi_rs_suplex", sourceLine: 15969),
                new SourceAnimationInstruction(SourceAnimationOp.FacingVelocity, x: 1.2f),
                new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1),
                new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1),
                Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.AnchorY
                    | SourceAnimationModes.Reacting | SourceAnimationModes.NoCatch),
                new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 1.2f),
                new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1), Stream(9, -1),
                new SourceAnimationInstruction(SourceAnimationOp.Friction, x: 0.08f),
                new SourceAnimationInstruction(SourceAnimationOp.Callback, (int)SourceAnimationSignal.PlayerDown,
                    text: "player_down", sourceLine: 15995),
                Cue("grnd_hit_snd", 15998, -1, 0, -1),
                new SourceAnimationInstruction(SourceAnimationOp.ShakerCue, 2, 10, sourceLine: 16000), Run(),
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
            Add(p, "m_wob_fu_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor),
                Int("M_WOB_FU", 10), new SourceAnimationInstruction(SourceAnimationOp.SetLateHittable, 1),
                Stream(61, 125), Stream(61, 125), Stream(61, 125), Stream(62, 125),
                Seq("M_WOB_FU"), Stream(61, 125), Stream(61, 125), Stream(61, 125),
                Stream(62, 125), Jump(7));
            Add(p, "punt_ball_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor),
                Op(SourceAnimationOp.ZeroVelocity), Seq("M_KICK"), To(4), Call(SourceAnimationSignal.PuntBall),
                new SourceAnimationInstruction(SourceAnimationOp.SetVelocity, z: 0.25f), To(41),
                new SourceAnimationInstruction(SourceAnimationOp.Friction, x: 0.055f), Run(),
                new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 3),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "qbhut_place_ball_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor),
                new SourceAnimationInstruction(SourceAnimationOp.SetFlip, 0), Seq("M_KHHOLD"), Stream(44, 15), Run(3),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "qbhut_unhold_ball_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor),
                Seq("M_KHRELE"), Run(), Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            foreach (string side in new[] { "r", "l" })
                Add(p, "m_lateral" + side + "_anim",
                    Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.AnchorXZ),
                    Op(SourceAnimationOp.ZeroVelocity), Int(side == "l" ? "M_LATERL" : "M_LATERR", 3),
                    Stream(7, 4), new SourceAnimationInstruction(SourceAnimationOp.SetFlip, side == "l" ? 1 : -1),
                    Call(SourceAnimationSignal.ReleaseBall), new SourceAnimationInstruction(SourceAnimationOp.SetFlip, side == "l" ? 0 : -1),
                    Run(), Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            AddCatchPrograms(p);
            AddFormationPrograms(p);
            return p;
        }

        private static void AddCatchPrograms(Dictionary<string, SourceAnimationProgram> p)
        {
            uint anchor = SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor;
            uint air = SourceAnimationModes.Uninterruptible | SourceAnimationModes.AnchorY;
            Add(p, "air_swat_anim", Mode(anchor), Int("SWAT", 3, 39), Stream(6, -1),
                Stream(31, 15), new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 4),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "c_dive_for_ball_anim", Mode(anchor), Seq("C_DIV2H"),
                new SourceAnimationInstruction(SourceAnimationOp.FaceBall, 512), Stream(11, 5),
                new SourceAnimationInstruction(SourceAnimationOp.LeapTarget, x: 2f, y: 0.05f), Mode(air),
                new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 0.75f), To(23),
                Stream(10, -1), new SourceAnimationInstruction(SourceAnimationOp.Friction, x: 0.035f),
                Cue("grnd_hit_snd", 13875, -1), Cue("dust_cloud", 13877),
                Run(), Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "c_jmpint_anim", Mode(anchor), Seq("C_JMPINT"),
                new SourceAnimationInstruction(SourceAnimationOp.RotateToBall, 4), Stream(11, 5),
                new SourceAnimationInstruction(SourceAnimationOp.LeapTarget, x: 1.3f),
                new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 1.4f), Stream(11, 15), Stream(13, -1),
                Call(SourceAnimationSignal.GroundContact), Run(), new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 4),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "c_jmpint3_anim", Mode(anchor), Seq("C_JMPINT"),
                new SourceAnimationInstruction(SourceAnimationOp.RotateToBall, 4), Stream(11, 5),
                new SourceAnimationInstruction(SourceAnimationOp.SetVelocity, y: 1.4f, z: 1.15f),
                Stream(11, 15), Stream(13, -1), Call(SourceAnimationSignal.GroundContact), Run(),
                new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 4),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "c_no_vel1_anim", Mode(anchor), Seq("C_JMPIN3"),
                new SourceAnimationInstruction(SourceAnimationOp.RotateToBall, 4), Stream(13, 5),
                new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 0.75f), Stream(13, 8),
                Stream(11, -1), To(46), new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 4),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "c_jmpint2_anim", Mode(anchor), Seq("C_JMPIN3"),
                new SourceAnimationInstruction(SourceAnimationOp.RotateToBall, 4), Stream(12, 5),
                new SourceAnimationInstruction(SourceAnimationOp.LeapTarget, x: 1.3f),
                new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 1.4f), To(13),
                Stream(20, -1), Call(SourceAnimationSignal.GroundContact), Stream(22, 6), Stream(19, 8),
                new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 4),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "c_run1a_anim", Mode(air), Seq("C_RUN1"), Mode(air), Stream(22, 5),
                new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 1.65f),
                Stream(11, 20), Stream(13, -1), Stream(19, 10), Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            foreach (bool flipped in new[] { false, true })
            {
                string f = flipped ? "f" : "";
                Add(p, "c_jgut1" + f + "_anim", Mode(anchor), Seq(flipped ? "C_JGUT1F" : "C_JGUT1"),
                    Stream(13, 5), Mode(air), new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 1.2f),
                    Stream(8, 15), Stream(24, -1), Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
                Add(p, "c_jmp1" + f + "_anim", Mode(anchor), Seq(flipped ? "C_JMP1F" : "C_JMP1"), Stream(17, 5),
                    Mode(air), new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 1.2f),
                    Stream(8, 15), Stream(20, -1), new SourceAnimationInstruction(SourceAnimationOp.Friction, x: 0.05f),
                    To(52), new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 4),
                    Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
                Add(p, "c_jmp2" + f + "_anim", Mode(anchor), Seq(flipped ? "C_JMP2F" : "C_JMP2"), Stream(13, 5),
                    Mode(air), new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 1.3f),
                    Stream(7, 15), Stream(20, -1), new SourceAnimationInstruction(SourceAnimationOp.Friction, x: 0.06f),
                    Run(), new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 4),
                    Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
                Add(p, "c_sidrol" + f + "_anim", Mode(anchor), Int(flipped ? "C_SROL2F" : "C_SROL2", 3),
                    Stream(11, 5), Mode(air), new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 1.35f),
                    To(25), Stream(12, -1), Call(SourceAnimationSignal.GroundContact),
                    new SourceAnimationInstruction(SourceAnimationOp.Friction, x: 0.04f), Stream(82, 13),
                    Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
                Add(p, "c_splits" + f + "_anim", Mode(anchor), Int(flipped ? "C_SPLITF" : "C_SPLITS", 3),
                    To(5), Mode(air), new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 1.4f),
                    Stream(17, 15), Stream(14, -1), Stream(38, 19),
                    new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 4),
                    Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
                Add(p, "c_sdgut" + f + "_anim", Mode(air), Int(flipped ? "C_SDGUTF" : "C_SDGUT", 4, 9),
                    To(15), Stream(24, 13), Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
                Add(p, "c_spin2" + f + "_anim", Mode(air), Int(flipped ? "C_SPIN2F" : "C_SPIN2", 4),
                    Stream(11, 5), new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 1.4f),
                    Stream(8, 15), Stream(8, -1), Stream(40, 18), Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
                Add(p, "c_bicyc" + f + "_anim", Mode(anchor), Int(flipped ? "C_BICYCF" : "C_BICYC", 3),
                    Stream(15, 5), new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 1.35f),
                    Stream(10, 15), Stream(23, -1), Stream(25, 15), Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            }
            Add(p, "c_roll_anim", Mode(anchor), Seq("C_ROLL"),
                new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, 1),
                new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1), Mode(air),
                new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 1.5f), To(14),
                Stream(21, -1), Call(SourceAnimationSignal.GroundContact), Stream(39, 30),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "c_div2h_anim", Mode(anchor), Int("C_DIV2H", 3), Stream(10, 5), Mode(air),
                new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 1.25f), To(23),
                Stream(10, -1), new SourceAnimationInstruction(SourceAnimationOp.Friction, x: 0.035f),
                Call(SourceAnimationSignal.GroundContact), Stream(55, 30),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Catch99(p, "catch_01", "CATCH_01", 18, 10, 28, 1.2f, 0, 0, false);
            Catch99(p, "catch_02", "CATCH_02", 23, 11, 16, 1.2f, 14, 7, false);
            Catch99(p, "catch_2f", "CATCH_2F", 23, 11, 16, 1.2f, 14, 7, true);
            Catch99(p, "catch_03", "CATCH_03", 10, 23, 10, 1.5f, 11, 6, false);
            Catch99(p, "catch_03f", "CATCH_3B", 10, 23, 10, 1.5f, 11, 6, false);
            Catch99(p, "catch_05", "CATCH_05", 11, 10, 15, 1.5f, 67, 25, false);
            Catch99(p, "catch_05f", "CATCH_5B", 11, 10, 15, 1.5f, 67, 25, false);
            Add(p, "catch_06", Mode(anchor), Seq("CATCH_6B"), Stream(6, 5), Mode(air),
                new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: 0.7f),
                Stream(4, 2), Stream(8, -1), Stream(23, 8),
                new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 4),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            foreach (string name in new[] { "back", "side", "sidef" })
                Add(p, "c2p_" + name + "_anim", Mode(SourceAnimationModes.AnchorXZ | SourceAnimationModes.TwoPart),
                    Seq(name == "back" ? "C_RUN1F" : name == "side" ? "C_SDGUT" : "C_SDGUTF"),
                    Op(SourceAnimationOp.RotateTorso), Stream(name == "back" ? 30 : 15, 8),
                    Stream(name == "back" ? 35 : 25, name == "back" ? 10 : 11),
                    Mode(0), Op(SourceAnimationOp.SetLowerUninterruptible),
                    Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
        }

        private static void Catch99(Dictionary<string, SourceAnimationProgram> p, string symbol, string sequence,
            int first, int second, int ground, float vertical, int finishFrames, int finishTicks, bool flipped)
        {
            Add(p, symbol, Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor), Seq(sequence),
                new SourceAnimationInstruction(SourceAnimationOp.SetFlip, flipped ? 1 : -1), Stream(first, 5),
                Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.AnchorY),
                new SourceAnimationInstruction(SourceAnimationOp.SetYVelocity, x: vertical),
                Stream(second, 15), Stream(ground, -1),
                new SourceAnimationInstruction(SourceAnimationOp.Friction, x: 0.05f),
                finishFrames == 0 ? Run() : Stream(finishFrames, finishTicks),
                new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 4),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
        }

        private static void AddFormationPrograms(Dictionary<string, SourceAnimationProgram> p)
        {
            foreach (string selector in new[] { "lm_random_anim", "db_random_anim", "db_man2man_random_anim",
                "db_zone_random_anim", "qb_random_anim", "qb_random_sg_anim", "wr_random_anim" })
                Add(p, selector, new SourceAnimationInstruction(SourceAnimationOp.SelectScript, text: selector), Op(SourceAnimationOp.End));
            string[] lines = { "PSDLINE1", "PSDLINE2", "PSODLIN1", "PSODLIN2" };
            for (int i = 0; i < lines.Length; i++)
                Add(p, "lm_set" + (i + 1) + "_anim", Seq(lines[i]), Stream(50, 70), Stream(50, 70),
                    Stream(i == 2 ? 54 : i == 3 ? 42 : 49, i == 2 ? 73 : i == 3 ? 65 : 70),
                    Int("3P_C", 10), new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "stance3pt_anim"));
            for (int group = 1; group <= 4; group++)
            {
                string sequence = "DLINE_0" + group;
                for (int variant = 0; variant < 3; variant++)
                {
                    var code = new List<SourceAnimationInstruction>();
                    code.Add(Op(SourceAnimationOp.ZeroVelocity));
                    if (group != 2) code.Add(Mode(group == 1 ? SourceAnimationModes.AnchorXZ : 0));
                    code.Add(Seq(sequence));
                    if (variant > 0) code.Add(new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, variant * 20));
                    int count = group == 1 ? 2 : 4;
                    int frames = group == 1 ? 55 - variant * 10 : group == 2 ? 44 - variant * 5
                        : group == 3 ? variant == 0 ? 36 : variant == 1 ? 31 : 26
                        : variant == 0 ? 34 : variant == 1 ? 29 : 24;
                    int ticks = group == 1 ? variant == 2 ? 102 : 112
                        : group == 2 ? variant == 0 ? 117 : variant == 1 ? 112 : 107
                        : group == 3 ? variant == 0 ? 117 : variant == 1 ? 107 : 100
                        : variant == 0 ? 102 : variant == 1 ? 92 : 77;
                    for (int i = 0; i < count; i++)
                        code.Add(Stream(group == 3 && variant == 0 && i == 3 ? 35 : frames, ticks));
                    int loop = code.Count;
                    code.Add(Mode(SourceAnimationModes.AnchorXZ));
                    code.Add(Int(sequence, 10));
                    int loopCount = group == 1 || group == 4 ? 2 : 4;
                    for (int i = 0; i < loopCount; i++)
                        code.Add(Stream(group == 1 ? 55 : group == 2 ? 44 : group == 3 && i == 3 ? 35
                            : group == 3 ? 36 : 69, group == 1 ? 122 : group == 2 || group == 3 ? 117 : 127));
                    code.Add(Jump(loop));
                    Add(p, "dline_0" + group + (char)('a' + variant), code.ToArray());
                }
            }
            AddShift(p, "shift_01", "SHIFT_01", 36, 68, 36, true);
            AddShift(p, "shift_02", "SHIFT_02", 43, 78);
            AddShift(p, "shift_03", "SHIFT_03", 43, 92, 42);
            AddShift(p, "shift_04", "SHIFT_04", 43, 95, 42);
            AddShift(p, "shift_08", "SHIFT_08", 36, 100, 35);
            AddShift(p, "shift_09", "SHIFT_09", 35, 80, 34);
            AddShift(p, "shift_10", "SHIFT_10", 34, 67);
            Add(p, "qb_claps_set_anim", Seq("PSQB2"), Stream(77, 100), Stream(45, 58), Stream(27, 36),
                Run(), Int("HUT", 8), new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "qbset_anim"));
            Add(p, "qb_rubs_knee_set_anim", Seq("PSQB3"), Stream(53, 75), Stream(53, 75), Stream(39, 50),
                Run(), Int("HUT", 8), new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "qbset_anim"));
            Add(p, "qb_pumps_crowd_set_anim", Seq("PSQB1"), Stream(37, 48), Stream(28, 36), Stream(39, 50), Stream(41, 53),
                Run(), Int("HUT", 8), new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "qbset_anim"));
            for (int i = 1; i <= 4; i++)
                Add(p, "wr_sprint" + i + "_anim", Seq("PSSPRNT" + i), Stream(64, 50), Stream(64, 50),
                    Stream(64, 50), Stream(93, 75), Int("SPT_C", 8),
                    new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: "wr_set_anim"));
            Stance(p, "wr_set2_anim", "SPT_CF", 1, 59);
            Add(p, "m_dblood_anim", Mode(SourceAnimationModes.Anchor), Seq("M_DBLOOD"), Run(), Jump(1));
            Add(p, "m_dblood2_anim", Mode(SourceAnimationModes.Anchor), Seq("M_DBLOOD"), Stream(76, 100), Jump(1));
            Add(p, "m_dblood3_anim", Mode(SourceAnimationModes.Anchor), Seq("M_DBLOOD"), Stream(76, 50), Jump(1));
            Add(p, "juke_c_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor), Seq("JUKE_C"),
                new SourceAnimationInstruction(SourceAnimationOp.SetVelocity, z: 0.28f),
                new SourceAnimationInstruction(SourceAnimationOp.StreamToEnd, x: 0.5f),
                new SourceAnimationInstruction(SourceAnimationOp.PrepareTransition, 4),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "spin_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor),
                new SourceAnimationInstruction(SourceAnimationOp.BranchSlowerSpin, text: "slower_spin_anim"), Int("SPIN", 4),
                new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, 4), Run(),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            Add(p, "slower_spin_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor), Int("SPIN", 4),
                new SourceAnimationInstruction(SourceAnimationOp.SetVelocity, z: 0.9f),
                new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, 4), Run(),
                Call(SourceAnimationSignal.GeneralRun), Op(SourceAnimationOp.End));
            HutLoop(p, "shotgun_anim", "SGUN_2", 28, 39, 45, 35);
            HutLoop(p, "qbhut_knee_anim", "M_KHHUT", 31, 12, 5, 5);
        }

        private static void AddShift(Dictionary<string, SourceAnimationProgram> p, string symbol, string sequence,
            int frames, int ticks, int lastFrames = -1, bool repeat = false)
        {
            Add(p, symbol, Op(SourceAnimationOp.ZeroVelocity), Mode(SourceAnimationModes.AnchorXZ),
                Int(sequence, 10), Stream(frames, ticks), Stream(frames, ticks), Stream(frames, ticks),
                Stream(lastFrames < 0 ? frames : lastFrames, ticks),
                new SourceAnimationInstruction(SourceAnimationOp.ChangeScript, text: repeat ? symbol : "db_random_anim"));
        }

        private static void HutLoop(Dictionary<string, SourceAnimationProgram> p, string symbol, string sequence,
            int first, int second, int middleTicks, int lastTicks)
        {
            Add(p, symbol, Mode(0), Seq(sequence), To(first, 2), Run(3),
                new SourceAnimationInstruction(SourceAnimationOp.RandomBranch, 1, 2, x: 1f, y: 1f, z: 1f),
                Seq(sequence), Stream(first, 20), Stream(second, middleTicks),
                new SourceAnimationInstruction(SourceAnimationOp.RandomBranch, 5, 2, x: 1f, y: 1f, z: 1f),
                Seq(sequence), Stream(first, 10), Stream(second, lastTicks), Jump(1));
        }
    }
}
