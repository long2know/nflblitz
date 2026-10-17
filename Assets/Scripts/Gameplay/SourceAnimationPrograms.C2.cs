using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Gameplay
{
    internal static partial class SourceAnimationPrograms
    {
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
