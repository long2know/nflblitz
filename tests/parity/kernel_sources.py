"""Exact pure-kernel compilation and canonical281 input closures for QA runners."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
C2_ANIMATION_SHA = "e87401c8cf3e348cc2d9c7255a1f18f43e41b1694ff7dff45c4bf025a5ecce4f"
C2_DATA_SHA = "9d2cb7ac5d92c3c2178fc1970a68ccad778fb7d319513b1787fe3ba3fe905f99"
C2_M1_DATA_SHA = "b0c02e5516f4f932625e1f8ccb3ff0fd53ae222657f899eb8c9d0b0c2e74460a"
C2_HIT_SHA = "bb978454ab57b09357e422f048e4035bea01a4d5932f453dd89be39838c6e904"
PRESNAP_ANIMATION_SHA = "50aa432ff9b332ec9087133f5bccb43ae18e1ca7c0bf7830cbdd675d3e024046"
PRESNAP_DATA_SHA = "e9a2ccbfffda0e83ae9ba409e4fbfd6f6c6f074ebd024aa30367d9d06c29c653"
PRESNAP_IMPACT_SHA = "456d525aa75ba343942613793cc437d6732f0b16d972a981537929daece9f3d7"
PRESNAP_MOVEMENT_SHA = "2f2d888ff89a6a89d9b72d19590115a71cce8ff5c7c7d34b8c313205cf0358ef"
PRESNAP_HOP_SHA = "df5e69344d049646e07143c4cbfec0d577e442fe63152099967436fa91c4e2bb"
PRESENTATION = ("SourceCinematicModel.cs", "SourceAudioFunctions.cs", "SourceC2SoundFunctions.cs")
COMPOSITIONS = ("SourceContactComposition.cs", "SourceC2Composition.cs")


def production_paths(root=ROOT):
    root = Path(root)
    return [path for folder in ("Contracts", "Gameplay", "AssetImport")
            for path in sorted((root / "Assets/Scripts" / folder).glob("*.cs"))] + [
                root / "Assets/Scripts/Presentation" / name for name in PRESENTATION] + [
                root / "Assets/Scripts/Runtime" / name for name in COMPOSITIONS]


def canonical_hashes(directory, profile="contact281-c2"):
    if profile not in ("contact281-c2", "contact281-c2-m1"):
        raise ValueError("Select exactly one explicit canonical callback generation.")
    directory = Path(directory).absolute()
    if not directory.is_relative_to(ROOT) or directory.is_symlink():
        raise ValueError("Canonical281 requires an explicit regular local input directory.")
    files = {p.relative_to(directory).as_posix(): p for p in directory.rglob("*") if p.is_file()}
    animation = "animation281/export-inventory.json"
    data_sha = C2_M1_DATA_SHA if profile == "contact281-c2-m1" else C2_DATA_SHA
    required = {animation: C2_ANIMATION_SHA, "c2-callback-data.json": data_sha,
                "m-qbfite-hit-extension.safn": C2_HIT_SHA}
    for name, pin in required.items():
        if name not in files or files[name].is_symlink() or hashlib.sha256(files[name].read_bytes()).hexdigest() != pin:
            raise ValueError("Canonical281 actual production input pin differs: " + name)
    inventory = json.loads(files[animation].read_bytes())
    clips = inventory.get("clips", [])
    if (inventory.get("profile") != "original-teams-contact281-c2" or len(clips) != 281
            or len({entry["runtime_name"] for entry in clips}) != 281
            or sum(entry["selected_frames"] for entry in clips) != 26230):
        raise ValueError("Canonical281 clip identities/counts/frames/profile differ; no272 alias.")
    for entry in clips:
        name = "animation281/" + entry["path"]
        if (Path(name).is_absolute() or ".." in Path(name).parts or name not in files
                or files[name].is_symlink() or hashlib.sha256(files[name].read_bytes()).hexdigest() != entry["sha256"]):
            raise ValueError("Canonical281 clip path/content differs: " + name)
    expected = {animation, animation + ".sha256", "c2-callback-data.json", "c2-callback-data.json.sha256",
                "m-qbfite-hit-extension.safn", "m-qbfite-hit-extension.safn.sha256",
                "m-qbfite-hit-extension-source.json"}
    expected.update("animation281/" + entry["path"] for entry in clips)
    if set(files) != expected or len(files) != 288:
        raise ValueError("Canonical281 requires its exact288-file producer closure, without missing/extra outputs.")
    for name, pin in ((animation + ".sha256", C2_ANIMATION_SHA),
                      ("c2-callback-data.json.sha256", data_sha),
                      ("m-qbfite-hit-extension.safn.sha256", C2_HIT_SHA)):
        if files[name].read_text().strip() != pin:
            raise ValueError("Canonical281 detached production pin differs: " + name)
    return {str(files[name]): hashlib.sha256(files[name].read_bytes()).hexdigest() for name in sorted(files)}


def successor_hashes(directory):
    """The separate exact282/15-binding generation; never relax canonical281."""
    directory = Path(directory).absolute()
    if not directory.is_relative_to(ROOT) or directory.resolve() != directory:
        raise ValueError("Pre-snap switch requires its explicit regular checkout-local profile root.")
    files = {p.relative_to(directory).as_posix(): p for p in directory.rglob("*") if p.is_file()}
    if any(p.is_symlink() for p in directory.rglob("*")):
        raise ValueError("Pre-snap switch profile symlinks are forbidden.")
    animation = "animation282/export-inventory.json"
    required = {animation: PRESNAP_ANIMATION_SHA, "c2-callback-data.json": PRESNAP_DATA_SHA,
                "m-qbfite-hit-extension.safn": C2_HIT_SHA,
                "animation282/clips/M_HOPCLP.json": PRESNAP_HOP_SHA}
    for name, pin in required.items():
        if name not in files or hashlib.sha256(files[name].read_bytes()).hexdigest() != pin:
            raise ValueError("Pre-snap switch actual production input pin differs: " + name)
    inventory = json.loads(files[animation].read_bytes())
    clips = inventory["clips"]
    if (inventory.get("profile") != "original-teams-contact282-c2-m1-presnap-switch" or len(clips) != 282
            or len({entry["runtime_name"] for entry in clips}) != 282
            or sum(entry["selected_frames"] for entry in clips) != 26265):
        raise ValueError("Pre-snap switch requires exact282/26265 identities, not a281 alias.")
    for entry in clips:
        name = "animation282/" + entry["path"]
        if (Path(name).is_absolute() or ".." in Path(name).parts or name not in files
                or hashlib.sha256(files[name].read_bytes()).hexdigest() != entry["sha256"]):
            raise ValueError("Pre-snap switch clip path/content differs: " + name)
    expected = {animation, animation + ".sha256", "c2-callback-data.json", "c2-callback-data.json.sha256",
                "m-qbfite-hit-extension.safn", "m-qbfite-hit-extension.safn.sha256",
                "m-qbfite-hit-extension-source.json"}
    expected.update("animation282/" + entry["path"] for entry in clips)
    if set(files) != expected or len(files) != 289:
        raise ValueError("Pre-snap switch requires its exact289-file producer closure.")
    for name, pin in ((animation + ".sha256", PRESNAP_ANIMATION_SHA),
                      ("c2-callback-data.json.sha256", PRESNAP_DATA_SHA),
                      ("m-qbfite-hit-extension.safn.sha256", C2_HIT_SHA)):
        if files[name].read_text().strip() != pin:
            raise ValueError("Pre-snap switch detached production pin differs: " + name)
    return {str(files[name]): hashlib.sha256(files[name].read_bytes()).hexdigest() for name in sorted(files)}
