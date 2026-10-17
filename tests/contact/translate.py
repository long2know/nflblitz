"""Translate the pinned contact-only ANI programs and verify the compiled program contract."""
import argparse
import ast
import hashlib
import json
from pathlib import Path
import re

from source import contact_symbols, read, scripts, blob

ROOT = Path(__file__).resolve().parents[2]
OPERANDS = {
    "include/ani3d.equ": "414c7d5e31cb0f96fe8fc41aea4d15059b566574",
    "include/player.equ": "cd6db29bdb743823347c983de7bd2a00b58d3d4a",
    "include/sndcalls.equ": "0096278ce26295856c57b8dc371937359d71348a",
}
PRESENTATION = {"attack_snd", "dust_cloud", "get_hit_snd", "grnd_hit_snd", "tackle_spch", "taunt_speech"}
SIGNALS = {"general_run": 2, "player_down": 4, "drop_ball": 128}
CAMERAS = {"csi_rs_suplex": 1, "csi_rs_suplex_nf": 1, "csi_zm_divetack": 3, "csi_sn_diveattack": 7}


def constants(operand_root):
    values = {}

    def number(expression):
        def walk(node):
            if isinstance(node, ast.Constant) and type(node.value) is int:
                return node.value
            if isinstance(node, ast.Name):
                return values[node.id]
            if isinstance(node, ast.UnaryOp) and isinstance(node.op, (ast.USub, ast.UAdd, ast.Invert)):
                value = walk(node.operand)
                return -value if isinstance(node.op, ast.USub) else ~value if isinstance(node.op, ast.Invert) else value
            if isinstance(node, ast.BinOp) and isinstance(node.op, (ast.BitOr, ast.LShift, ast.Add, ast.Sub)):
                a, b = walk(node.left), walk(node.right)
                if isinstance(node.op, ast.BitOr):
                    return a | b
                if isinstance(node.op, ast.LShift):
                    return a << b
                return a + b if isinstance(node.op, ast.Add) else a - b
            raise ValueError("Unsupported original constant expression " + expression)
        return walk(ast.parse(expression, mode="eval").body)

    for name, pin in OPERANDS.items():
        data = (Path(operand_root) / name).read_bytes()
        if blob(data) != pin:
            raise ValueError("Original ANI operand pin differs: " + name)
        for raw in data.decode("latin1").splitlines():
            line = raw.split("#", 1)[0].strip()
            match = re.fullmatch(r"\.equ\s+(\w+),\s*(.+)", line)
            if match:
                values[match[1]] = number(match[2])
    return number


def selected(source):
    all_scripts = scripts(source["plyrseq.c"])
    needed = set(contact_symbols(source["detect.c"])) - {"change_anim", "rusher_anim", "blocker_anim"}
    needed.discard("taunt_4_anim")  # Only referenced by the disabled update_onfire_stuff body.
    needed.update(("get_up_b_anim", "get_up_c_anim", "get_up_b1_hurt_anim", "get_up_b2_hurt_anim",
                   "get_up_c2_hurt_anim", "get_up_c3_hurt_anim", "m_wob_fu_anim", "m_wob_fd_anim",
                   "m_cnv_fu1_anim", "m_cnv_fd2_anim"))
    if not needed <= all_scripts.keys():
        raise ValueError("Missing precise original contact scripts: " + str(sorted(needed - all_scripts.keys())))
    return {name: all_scripts[name] for name in sorted(needed)}


def translate(source, operand_root):
    number = constants(operand_root)
    result = {}
    for name, script in selected(source).items():
        rows, output, labels, branches = script["rows"], [], {}, []
        position = 0

        def take(kind):
            nonlocal position
            line, actual, text = rows[position]
            position += 1
            if actual != kind:
                raise ValueError(f"{name}:{line} expected {kind}, not {actual}: {text}")
            return text.split(",")

        def instruction(op, **fields):
            output.append({"op": op, **fields})

        def integers(kind):
            return [number(v.strip()) for v in take(kind)]

        while position < len(rows):
            line, kind, raw = rows[position]
            position += 1
            if kind == "label":
                labels[raw] = len(output)
                continue
            if kind != "byte":
                raise ValueError(f"{name}:{line} missing original ANI byte: {raw}")
            parts = [s.strip() for s in raw.split(",")]
            opcode = parts[0]
            if not opcode.startswith("ANI_"):
                for value in parts:
                    instruction("NextFrame", a=number(value))
                continue
            args = [number(value) for value in parts[1:]]
            if opcode == "ANI_SETMODE":
                instruction("SetMode", a=integers("word")[0])
            elif opcode == "ANI_NEWSEQ":
                sequence = take("word")[0]
                if not sequence.startswith("SEQ_"):
                    raise ValueError("Expected actual source SEQ identity")
                instruction("NewSequence", text=sequence[4:])
            elif opcode == "ANI_INTTOSEQ":
                sequence, frame = take("word")[0], integers("byte")[0]
                instruction("Interpolate", a=args[0], b=frame, text=sequence.removeprefix("SEQ_"))
            elif opcode == "ANI_INTSTREAM":
                if args[0] == -2:
                    instruction("StreamToEnd", x=float(take("float")[0]))
                else:
                    instruction("Stream", a=args[0], b=args[1])
            elif opcode == "ANI_SKIPFRAMES":
                if len(args) != 1 or args[0] < 0:
                    raise ValueError("Unexpected random skip in contact scope")
                instruction("SkipFrames", a=args[0])
            elif opcode in ("ANI_RUNTOFRAME", "ANI_RUNTOEND"):
                instruction("RunToFrame" if opcode == "ANI_RUNTOFRAME" else "RunToEnd",
                            **dict(zip(("a", "b"), args)))
            elif opcode in ("ANI_SETFLAG", "ANI_CLRFLAG"):
                instruction("MovementCode", a=integers("word")[0],
                            text="set_flag" if opcode == "ANI_SETFLAG" else "clear_flag")
            elif opcode in ("ANI_CODE", "ANI_CODE_BNZ", "ANI_CODE_BZ"):
                operands = [s.strip() for s in take("word")]
                function, arguments = operands[0], [number(v) for v in operands[1:4]]
                if opcode != "ANI_CODE":
                    target = operands[4]
                    if function == "plyr_have_ball":
                        instruction("BranchIfHasBall" if opcode == "ANI_CODE_BNZ" else "BranchIfNotHasBall")
                        branches.append((len(output) - 1, target, "ball"))
                    elif function == "plyr_rand":
                        instruction("RandomBranch", a=0, b=arguments[0], x=1.0, y=1.0,
                                    z=1.0 if opcode == "ANI_CODE_BNZ" else 0.0)
                        branches.append((len(output) - 1, target, "random"))
                    else:
                        instruction("MovementBranchNonzero" if opcode == "ANI_CODE_BNZ" else "MovementBranchZero",
                                    text=function, **dict(zip(("a", "b", "c"), arguments)))
                        branches.append((len(output) - 1, target, "code"))
                elif function in SIGNALS:
                    instruction("Callback", a=SIGNALS[function], text=function if function == "player_down" else None,
                                sourceLine=rows[position - 1][0] if function == "player_down" else 0)
                elif function in PRESENTATION:
                    instruction("PresentationCallback", text=function, **dict(zip(("a", "b", "c"), arguments)),
                                sourceLine=rows[position - 1][0])
                else:
                    instruction("MovementCode", text=function, **dict(zip(("a", "b", "c"), arguments)),
                                sourceLine=rows[position - 1][0])
            elif opcode == "ANI_GOTO":
                target = take("word")[0].strip()
                instruction("JumpProgram")
                branches.append((len(output) - 1, target, "goto"))
            elif opcode == "ANI_ZEROVELS":
                instruction("ZeroVelocity")
            elif opcode == "ANI_SETVELS":
                instruction("SetVelocity", **dict(zip(("x", "y", "z"), (float(v) for v in take("float")))))
            elif opcode in ("ANI_SETYVEL", "ANI_SETFRICTION", "ANI_SET_FACE_REL_VEL", "ANI_STREAMTOEND"):
                operation = {"ANI_SETYVEL": "SetYVelocity", "ANI_SETFRICTION": "Friction",
                             "ANI_SET_FACE_REL_VEL": "HitFacingVelocity", "ANI_STREAMTOEND": "StreamToEnd"}[opcode]
                instruction(operation, x=float(take("float")[0]))
            elif opcode == "ANI_TURNHACK":
                instruction("TurnHack", a=integers("word")[0])
            elif opcode == "ANI_PREP_XITION":
                instruction("PrepareTransition", **dict(zip(("a", "b"), integers("word"))))
            elif opcode == "ANI_SETCAMBOT":
                table = take("word")[0].strip()
                instruction("CameraCue", a=CAMERAS[table], text=table, sourceLine=rows[position - 1][0])
            elif opcode == "ANI_SHAKER":
                instruction("ShakerCue", a=args[0], b=integers("word")[0], sourceLine=line)
            elif opcode == "ANI_ATTACK_ON":
                instruction("AttackOn", a=args[0], b=args[1], c=args[2], timer=args[3],
                            **dict(zip(("x", "y", "z"), (float(v) for v in take("float")))))
            elif opcode == "ANI_ATTACK_OFF":
                instruction("AttackOff")
            elif opcode == "ANI_LEAPATT":
                values = integers("word")
                floats = [float(v) for v in take("float")]
                # The source permits each following float group on its own directive.
                while len(floats) < 6:
                    floats.extend(float(v) for v in take("float"))
                instruction("LeapAttack", a=values[0], b=values[1], c=values[2], x=floats[0], y=floats[1],
                            z=floats[2], maximumYVelocity=floats[3], maximumPlanarVelocity=floats[4],
                            minimumPlanarVelocity=floats[5], sourceLine=line)
            elif opcode == "ANI_END":
                instruction("End")
            else:
                raise ValueError(f"{name}:{line} unsupported original opcode {opcode}")
        for pc, target, kind in branches:
            item = output[pc]
            if target in labels:
                if kind in ("ball", "random"):
                    item["a"] = labels[target]
                elif kind == "goto":
                    item.clear()
                    item.update(op="Jump", a=labels[target])
                else:
                    item["branchPc"] = labels[target]
            elif kind == "ball":
                if item["op"] == "BranchIfHasBall":
                    item["text"] = target
                else:
                    item["target"] = target
            elif kind == "random":
                raise ValueError("Unexpected original random branch outside its script")
            else:
                item["text" if kind == "goto" else "target"] = target
                item["branchPc"] = -1
        result[name] = [{k: v for k, v in row.items() if v is not None and (k == "op" or v != 0)}
                        for row in output]
    return result


def csharp(programs):
    def literal(value):
        if isinstance(value, str):
            return json.dumps(value)
        if isinstance(value, float):
            return repr(value) + "f"
        return str(value) if value <= 2147483647 else "unchecked((int)" + hex(value) + "u)"

    lines = ["using System.Collections.Generic;", "", "namespace Blitz.Gameplay", "{",
             "    internal static partial class SourceAnimationPrograms", "    {",
             "        // Generated from pinned normal57 plyrseq.c by tests/contact/translate.py.",
             "        private static void AddContactPrograms(Dictionary<string, SourceAnimationProgram> programs)", "        {"]
    for name, instructions in programs.items():
        lines.append(f'            programs["{name}"] = new SourceAnimationProgram("{name}",')
        for i, row in enumerate(instructions):
            arguments = ["SourceAnimationOp." + row["op"]] + [
                key + ": " + literal(value) for key, value in row.items() if key != "op"]
            lines.append("                new SourceAnimationInstruction(" + ", ".join(arguments)
                         + ("));" if i == len(instructions) - 1 else "),"))
    lines.extend(("        }", "    }", "}", ""))
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", required=True, type=Path)
    parser.add_argument("--operand-root", required=True, type=Path)
    parser.add_argument("--write-production", action="store_true")
    args = parser.parse_args()
    programs = translate(read(args.source_root), args.operand_root)
    output = ROOT / "Assets/Scripts/Gameplay/SourceAnimationPrograms.Contact.cs"
    expected = csharp(programs)
    if args.write_production:
        output.write_text(expected)
    if output.read_text() != expected:
        raise ValueError("Production contact ANI programs differ from their precise original operands/order.")
    artifacts = ROOT / "tests/contact/.artifacts"
    artifacts.mkdir(exist_ok=True)
    (artifacts / "source-programs.json").write_text(json.dumps(programs, indent=2) + "\n")
    print(json.dumps({"status": "passed", "programs": len(programs),
                      "instructions": sum(map(len, programs.values())),
                      "production_sha256": hashlib.sha256(output.read_bytes()).hexdigest()}))


if __name__ == "__main__":
    main()
