"""Verified original contact input and animation-script extraction; no source payloads in Git."""
import hashlib
import re
from pathlib import Path

REVISION = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded"
PINS = {
    "detect.c": "68c239fa2e617988107fbebeb4dfeff5bbfe9233",
    "player.c": "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c",
    "plyrseq.c": "28cef0d1082f2b0c9241930fdc8886de4c8f5438",
    "pmisc.c": "50426d40ecd03515cecd295fb80a63ffa0cb0683",
    "handicap.c": "871424596fb9786af3602a49cc9d20c466df8ca1",
    "include/player.h": "a86b1a5b52ba842b206cd7dd0bbb26521db6dcb8",
    "include/ani3d.h": "826c6322467fcfe503b8402b540e4731671f9b6b",
}


def blob(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()


def read(root):
    result = {}
    for name, pin in PINS.items():
        data = (Path(root) / name).read_bytes()
        if blob(data) != pin:
            raise ValueError("Original contact source pin differs: " + name)
        result[name] = data
    return result


def closing(data, start):
    depth = 0
    for match in re.finditer(rb'/\*.*?\*/|//[^\r\n]*|"(?:\\.|[^"\\])*"|[{}]', data[start:], re.S):
        if match.group() == b"{":
            depth += 1
        elif match.group() == b"}":
            depth -= 1
            if depth == 0:
                return start + match.end()
    raise ValueError("Unbalanced original C")


def function(data, name):
    found = list(re.finditer(rb"(?m)^(?:static\s+)?(?:void|int|float)\s+" + name.encode() + rb"\s*\([^;{}]*\)\s*\{", data))
    if len(found) != 1:
        raise ValueError("Expected one original C body: " + name)
    start = found[0].start()
    # Pinned functions close at column zero; disabled alternative branches have unequal braces.
    end = data.index(b"\n}", found[0].end()) + 2
    return start, end, data[start:end]


def scripts(data):
    """Interpret only the source's normal GETUPAGAIN=1/DOSETCAM=1 assembly guards."""
    result, current, pending, conditions = {}, None, False, []
    for line, raw in enumerate(data.decode("latin1").splitlines(), 1):
        text = raw.split("#", 1)[0].strip()
        if not text:
            continue
        if text.startswith(".if "):
            name = text[4:].strip()
            if name not in ("GETUPAGAIN", "DOSETCAM"):
                raise ValueError("Unsupported original assembly input: " + name)
            conditions.append(True)
            continue
        if text == ".else":
            if not conditions:
                raise ValueError("Unmatched original .else")
            conditions[-1] = not conditions[-1]
            continue
        if text == ".endif":
            if not conditions:
                raise ValueError("Unmatched original .endif")
            conditions.pop()
            continue
        if not all(conditions):
            continue
        if re.fullmatch(r"\.byte\s+SCRIPT", text):
            pending, current = True, None
            continue
        label = re.fullmatch(r"(\w+):", text)
        if label:
            if pending:
                current = label[1]
                if current in result:
                    raise ValueError("Duplicate original script: " + current)
                result[current] = {"line": line, "rows": []}
                pending = False
            elif current:
                result[current]["rows"].append((line, "label", label[1]))
            continue
        row = re.fullmatch(r"\.(byte|word|float)\s+(.+)", text)
        if row and current:
            result[current]["rows"].append((line, row[1], row[2].strip()))
    if conditions:
        raise ValueError("Unclosed original assembly guard")
    return result


def contact_symbols(detect):
    active = re.sub(rb"//[^\r\n]*|/\*.*?\*/", b"", detect, flags=re.S)
    # The two fully disabled attack bodies contain no active script references.
    active = re.sub(rb"#if 0\b.*?#endif", b"", active, flags=re.S)
    symbols = {x.decode() for x in re.findall(rb"\b(?:[a-z]\w*_anim|fall_\d+f?|m_nostomp)\b", active)}
    for prefix in ("p_hlpup_back", "p_ankl", "p_ank2", "p_run_drag", "p_run_drag2",
                   "p_hip_toss", "p_spin", "p_armg3", "p_head_grab", "p_dive", "p_frol",
                   "p_wtac", "p_waist_drop", "p_argue"):
        symbols.update((prefix + "_a_anim", prefix + "_v_anim"))
    return sorted(symbols)
