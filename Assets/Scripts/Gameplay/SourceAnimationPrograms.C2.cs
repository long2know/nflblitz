using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Gameplay
{
    internal static partial class SourceAnimationPrograms
    {
        private static SourceAnimationInstruction Celebrate(bool value)
        { return new SourceAnimationInstruction(SourceAnimationOp.SetCelebrate, value ? 1 : 0); }

        private static SourceAnimationInstruction DanceCamera(string table, int line, int count = 1)
        { return new SourceAnimationInstruction(SourceAnimationOp.CameraCue, count, text: table, sourceLine: line); }

        private static SourceAnimationInstruction DanceTurn(int heading)
        { return new SourceAnimationInstruction(SourceAnimationOp.TurnHack, heading); }

        private static void AddDance(Dictionary<string, SourceAnimationProgram> programs, string symbol,
            string sequence, int transition, int frame, bool anchorY, params SourceAnimationInstruction[] body)
        {
            var words = new List<SourceAnimationInstruction> {
                Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.AnchorXZ
                    | (anchorY ? SourceAnimationModes.AnchorY : 0)),
                Op(SourceAnimationOp.ZeroVelocity), Celebrate(true), Int(sequence, transition, frame)
            };
            words.AddRange(body);
            words.Add(new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, text: "breath_anim", branchPc: -1));
            words.Add(Op(SourceAnimationOp.End));
            programs.Add(symbol, new SourceAnimationProgram(symbol, words.ToArray()));
        }

        internal static IReadOnlyDictionary<string, SourceAnimationProgram> CreateCompleteOriginalExtension()
        {
            var programs = new Dictionary<string, SourceAnimationProgram>();
            foreach (KeyValuePair<string, SourceAnimationProgram> entry in CreateC2Extension())
                programs.Add(entry.Key, entry.Value);
            AddDance(programs, "endz_08", "ENDZ_08", 10, 4, true,
                new SourceAnimationInstruction(SourceAnimationOp.BranchIfNotHasBall, 6),
                DanceCamera("csi_rs_suplex_nf", 15723),
                Call(SourceAnimationSignal.DropBall), Cue("taunt_speech", 15731),
                Stream(69, 122), Celebrate(false));
            Add(programs, "endz_06",
                Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor), Celebrate(true),
                Int("ENDZ_06", 10),
                new SourceAnimationInstruction(SourceAnimationOp.BranchIfNotHasBall, 5),
                DanceCamera("csi_rs_suplex", 15763), Op(SourceAnimationOp.ZeroVelocity),
                Call(SourceAnimationSignal.DropBall), Cue("taunt_speech", 15774),
                Stream(40, 65), Stream(40, 65), Stream(40, 65), Stream(40, 65), Celebrate(false),
                new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, text: "breath_anim", branchPc: -1),
                Op(SourceAnimationOp.End));
            AddDance(programs, "endz_20", "ENDZ_20", 10, 0, true,
                new SourceAnimationInstruction(SourceAnimationOp.BranchIfNotHasBall, 6),
                DanceCamera("csi_zm_divetack", 17889, 3),
                Call(SourceAnimationSignal.DropBall), Cue("taunt_speech", 17897),
                Stream(42, 60), Stream(42, 60), Stream(42, 60), Stream(42, 60),
                DanceTurn(256), Celebrate(false));
            AddDance(programs, "end_zone1_anim", "INZONE1", 6, 0, true,
                new SourceAnimationInstruction(SourceAnimationOp.BranchIfNotHasBall, 6),
                DanceCamera("csi_rs_suplex_nf", 7849), Cue("taunt_speech", 7853),
                Stream(100, 80), Stream(100, 80), Cue("taunt_speech", 7860, 2),
                Stream(23, 19), Call(SourceAnimationSignal.SpikeBall), Run(),
                DanceTurn(-128), Celebrate(false));
            AddDance(programs, "end_zone5_anim", "M_TAUNBC", 6, 0, true,
                Cue("taunt_speech", 7896, 18),
                new SourceAnimationInstruction(SourceAnimationOp.StreamToEnd, x: 1.90f), Celebrate(false));
            AddDance(programs, "end_zone6_anim", "M_EZCTHR", 6, 0, true,
                new SourceAnimationInstruction(SourceAnimationOp.BranchIfNotHasBall, 6),
                DanceCamera("csi_rs_suplex", 7981), To(172), Cue("taunt_speech", 7988, 20),
                Call(SourceAnimationSignal.FlingBall), Stream(47, 105),
                DanceTurn(512), Celebrate(false));
            AddDance(programs, "end_zone9_anim", "M_EZCGUN", 6, 0, true,
                new SourceAnimationInstruction(SourceAnimationOp.BranchIfNotHasBall, 6),
                DanceCamera("csi_rs_suplex_nf", 8026), Stream(100, 45),
                Cue("attack_snd", 8033, 147), Stream(16, 11),
                Cue("attack_snd", 8038, 147), Stream(12, 8),
                Cue("attack_snd", 8043, 147), Stream(12, 8),
                Cue("attack_snd", 8048, 147), Stream(10, 8),
                Cue("attack_snd", 8053, 147), Stream(10, 8),
                Cue("attack_snd", 8058, 182), Stream(11, 8),
                Cue("attack_snd", 8063, 182), To(252),
                Cue("attack_snd", 8068, 181), Stream(9, 10),
                Cue("attack_snd", 8073, 183), Run(), Celebrate(false), DanceTurn(-256));
            AddDance(programs, "end_zone3_anim", "INZONE3", 6, 0, true,
                Call(SourceAnimationSignal.DropBall), Stream(100, 90), Stream(100, 90), Stream(100, 90),
                Cue("taunt_speech", 17420, 5), Stream(87, 77), Celebrate(false));
            AddDance(programs, "end_zone2_anim", "INZONE2", 6, 0, false,
                To(31), Cue("taunt_speech", 17372, 3), To(82), Cue("taunt_speech", 17377, 3),
                Run(), DanceTurn(-320), Celebrate(false));
            AddDance(programs, "end_zone4_anim", "M_CELCHE", 6, 0, true,
                To(68), To(120), To(211), Run(), Celebrate(false));
            AddDance(programs, "end_zone7_anim", "M_EZCWHA", 6, 0, true,
                Stream(100, 95), Stream(100, 95), Stream(73, 71), Celebrate(false), DanceTurn(512));
            AddDance(programs, "end_zone8_anim", "M_EZWAV1", 6, 0, true,
                Cue("taunt_speech", 17636, 3), Run(), Celebrate(false));
            AddDance(programs, "end_zone10_anim", "M_EZCSPN", 6, 0, true,
                new SourceAnimationInstruction(SourceAnimationOp.BranchIfNotHasBall, 6),
                DanceCamera("csi_rs_suplex_nf", 17666), Stream(100, 80), Stream(100, 80),
                Cue("taunt_speech", 17675, 19), Stream(100, 80), Stream(24, 18),
                Celebrate(false), DanceTurn(256));
            AddDance(programs, "end_zone11_anim", "M_EZCHRS", 6, 0, true,
                Stream(37, 90), Stream(38, 90), Stream(38, 90), Stream(37, 90), Run(),
                Celebrate(false), DanceTurn(512));
            AddDance(programs, "end_zone13_anim", "M_EZPUSH", 6, 0, true,
                new SourceAnimationInstruction(SourceAnimationOp.BranchIfNotHasBall, 6),
                DanceCamera("csi_rs_suplex_nf", 17494),
                Stream(36, 85), Stream(36, 85), Stream(36, 85), Stream(36, 85),
                DanceTurn(-576), Celebrate(false));
            AddDance(programs, "end_zone14_anim", "M_EZJASN", 6, 0, false,
                To(14), Stream(41, 120), Stream(41, 120), DanceTurn(-576), Celebrate(false));
            AddDance(programs, "end_zone15_anim", "M_EZDNCE", 6, 0, true,
                new SourceAnimationInstruction(SourceAnimationOp.BranchIfNotHasBall, 6),
                DanceCamera("csi_rs_suplex", 17567),
                Stream(46, 105), Stream(46, 105), Stream(46, 105), Stream(46, 105),
                DanceTurn(-768), Celebrate(false));
            AddDance(programs, "end_zone18_anim", "M_EZJON1", 6, 0, true,
                new SourceAnimationInstruction(SourceAnimationOp.BranchIfNotHasBall, 6),
                DanceCamera("csi_rs_suplex", 17279), Call(SourceAnimationSignal.DropBall),
                Cue("taunt_speech", 17289), Stream(37, 85), Stream(37, 85), Stream(37, 85), Stream(37, 85),
                Celebrate(false));
            AddDance(programs, "end_zone19_anim", "M_EZJON2", 6, 0, true,
                new SourceAnimationInstruction(SourceAnimationOp.BranchIfNotHasBall, 6),
                DanceCamera("csi_zm_divetack", 17331, 3), Call(SourceAnimationSignal.DropBall),
                Cue("taunt_speech", 17338), Stream(37, 85), Stream(37, 85), Stream(37, 85), Stream(37, 85),
                Celebrate(false));
            Add(programs, "disapointed1_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor),
                new SourceAnimationInstruction(SourceAnimationOp.Friction, x: .095f), Seq("R_SLO_D1"), Run(),
                DanceTurn(-256), new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, text: "breath_anim", branchPc: -1),
                Op(SourceAnimationOp.End));
            Add(programs, "disapointed2_anim", Mode(SourceAnimationModes.Uninterruptible | SourceAnimationModes.Anchor),
                new SourceAnimationInstruction(SourceAnimationOp.Friction, x: .095f), Seq("R_SLO_D2"), Run(),
                new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, text: "breath_anim", branchPc: -1),
                Op(SourceAnimationOp.End));
            return new ReadOnlyDictionary<string, SourceAnimationProgram>(programs);
        }

        internal static IReadOnlyDictionary<string, SourceAnimationProgram> CreateC2Extension()
        {
            var programs = new Dictionary<string, SourceAnimationProgram>();
            // Pinned normal57 plyrseq.c; unreachable words following ANI_END are not instructions.
            programs["taunt_1_anim"] = new SourceAnimationProgram("taunt_1_anim",
                new SourceAnimationInstruction(SourceAnimationOp.SetMode, a: 131074),
                new SourceAnimationInstruction(SourceAnimationOp.ZeroVelocity),
                new SourceAnimationInstruction(SourceAnimationOp.MovementCode, text: "face_carrier", a: 256, sourceLine: 3134),
                new SourceAnimationInstruction(SourceAnimationOp.Interpolate, a: 10, b: 7, text: "TAUNT_1"),
                new SourceAnimationInstruction(SourceAnimationOp.PresentationCallback, text: "taunt_speech", a: -2, sourceLine: 3144),
                new SourceAnimationInstruction(SourceAnimationOp.RunToEnd, a: 1),
                new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, text: "stance_anim_int", branchPc: -1),
                new SourceAnimationInstruction(SourceAnimationOp.End));
            programs["taunt_2_anim"] = new SourceAnimationProgram("taunt_2_anim",
                new SourceAnimationInstruction(SourceAnimationOp.SetMode, a: 131074),
                new SourceAnimationInstruction(SourceAnimationOp.ZeroVelocity),
                new SourceAnimationInstruction(SourceAnimationOp.MovementCode, text: "face_carrier", a: 256, sourceLine: 3162),
                new SourceAnimationInstruction(SourceAnimationOp.Interpolate, a: 10, text: "TAUNT_2"),
                new SourceAnimationInstruction(SourceAnimationOp.PresentationCallback, text: "taunt_speech", a: -2, sourceLine: 3169),
                new SourceAnimationInstruction(SourceAnimationOp.RunToEnd, a: 1),
                new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, text: "stance_anim_int", branchPc: -1),
                new SourceAnimationInstruction(SourceAnimationOp.End));
            programs["taunt_3_anim"] = new SourceAnimationProgram("taunt_3_anim",
                new SourceAnimationInstruction(SourceAnimationOp.SetMode, a: 131074),
                new SourceAnimationInstruction(SourceAnimationOp.ZeroVelocity),
                new SourceAnimationInstruction(SourceAnimationOp.MovementCode, text: "face_carrier", a: 256, sourceLine: 3187),
                new SourceAnimationInstruction(SourceAnimationOp.Interpolate, a: 14, b: 14, text: "TAUNT_3"),
                new SourceAnimationInstruction(SourceAnimationOp.PresentationCallback, text: "taunt_speech", a: -2, sourceLine: 3194),
                new SourceAnimationInstruction(SourceAnimationOp.RunToEnd, a: 1),
                new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, text: "stance_anim_int", branchPc: -1),
                new SourceAnimationInstruction(SourceAnimationOp.End));
            programs["taunt_4_anim"] = new SourceAnimationProgram("taunt_4_anim",
                new SourceAnimationInstruction(SourceAnimationOp.SetMode, a: 131074),
                new SourceAnimationInstruction(SourceAnimationOp.ZeroVelocity),
                new SourceAnimationInstruction(SourceAnimationOp.MovementCode, text: "face_carrier", a: 512, sourceLine: 3212),
                new SourceAnimationInstruction(SourceAnimationOp.Interpolate, a: 10, b: 14, text: "TAUNT_4"),
                new SourceAnimationInstruction(SourceAnimationOp.PresentationCallback, text: "taunt_speech", a: -1, sourceLine: 3219),
                new SourceAnimationInstruction(SourceAnimationOp.RunToEnd, a: 1),
                new SourceAnimationInstruction(SourceAnimationOp.TurnHack, a: 512),
                new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, text: "stance_anim_int", branchPc: -1),
                new SourceAnimationInstruction(SourceAnimationOp.End));
            programs["m_laugh1_anim"] = new SourceAnimationProgram("m_laugh1_anim",
                new SourceAnimationInstruction(SourceAnimationOp.SetMode, a: 131074),
                new SourceAnimationInstruction(SourceAnimationOp.ZeroVelocity),
                new SourceAnimationInstruction(SourceAnimationOp.MovementCode, text: "face_carrier", a: 256, sourceLine: 3300),
                new SourceAnimationInstruction(SourceAnimationOp.Interpolate, a: 10, text: "M_LAUGH1"),
                new SourceAnimationInstruction(SourceAnimationOp.PresentationCallback, text: "taunt_speech", a: -3, sourceLine: 3307),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 37, b: 80),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 37, b: 80),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 37, b: 80),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 37, b: 80),
                new SourceAnimationInstruction(SourceAnimationOp.RunToEnd, a: 1),
                new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, text: "stance_anim_int", branchPc: -1),
                new SourceAnimationInstruction(SourceAnimationOp.End));
            programs["m_qbwhat_anim"] = new SourceAnimationProgram("m_qbwhat_anim",
                new SourceAnimationInstruction(SourceAnimationOp.SetMode, a: 196610),
                new SourceAnimationInstruction(SourceAnimationOp.ZeroVelocity),
                new SourceAnimationInstruction(SourceAnimationOp.Interpolate, a: 8, b: 2, text: "M_QBWHAT"),
                new SourceAnimationInstruction(SourceAnimationOp.PresentationCallback, text: "taunt_speech", a: 16, sourceLine: 7568),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 43, b: 80),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 43, b: 80),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 43, b: 80),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 43, b: 80),
                new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, text: "breath_anim", branchPc: -1),
                new SourceAnimationInstruction(SourceAnimationOp.End));
            programs["m_qbfite_anim"] = new SourceAnimationProgram("m_qbfite_anim",
                new SourceAnimationInstruction(SourceAnimationOp.SetMode, a: 196610),
                new SourceAnimationInstruction(SourceAnimationOp.ZeroVelocity),
                new SourceAnimationInstruction(SourceAnimationOp.Interpolate, a: 8, b: 2, text: "M_QBFITE"),
                new SourceAnimationInstruction(SourceAnimationOp.PresentationCallback, text: "get_hit_snd", a: 70, sourceLine: 7538),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 41, b: 80),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 41, b: 80),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 41, b: 80),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 41, b: 80),
                new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, text: "breath_anim", branchPc: -1),
                new SourceAnimationInstruction(SourceAnimationOp.End));
            programs["m_mscat1_anim"] = new SourceAnimationProgram("m_mscat1_anim",
                new SourceAnimationInstruction(SourceAnimationOp.SetMode, a: 196610),
                new SourceAnimationInstruction(SourceAnimationOp.ZeroVelocity),
                new SourceAnimationInstruction(SourceAnimationOp.NewSequence, text: "M_MSCAT1"),
                new SourceAnimationInstruction(SourceAnimationOp.PresentationCallback, text: "taunt_speech", a: 47, sourceLine: 7459),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 34, b: 65),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 35, b: 65),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 34, b: 65),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 35, b: 65),
                new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, text: "breath_anim", branchPc: -1),
                new SourceAnimationInstruction(SourceAnimationOp.End));
            programs["m_mscat2_anim"] = new SourceAnimationProgram("m_mscat2_anim",
                new SourceAnimationInstruction(SourceAnimationOp.SetMode, a: 196610),
                new SourceAnimationInstruction(SourceAnimationOp.ZeroVelocity),
                new SourceAnimationInstruction(SourceAnimationOp.NewSequence, text: "M_MSCAT2"),
                new SourceAnimationInstruction(SourceAnimationOp.PresentationCallback, text: "taunt_speech", a: 47, sourceLine: 7484),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 34, b: 65),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 35, b: 65),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 34, b: 65),
                new SourceAnimationInstruction(SourceAnimationOp.Stream, a: 34, b: 65),
                new SourceAnimationInstruction(SourceAnimationOp.JumpProgram, text: "breath_anim", branchPc: -1),
                new SourceAnimationInstruction(SourceAnimationOp.End));
            return new ReadOnlyDictionary<string, SourceAnimationProgram>(programs);
        }
    }
}
