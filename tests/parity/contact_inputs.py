"""Explicit production Contact272 input identity; historical fixtures are not aliases."""
import hashlib
import json
from pathlib import Path

from fetch_inputs import REVISION


SCHEMA = "source-contact-kernel-real-inputs/v1"
PROFILE = "contact272"
LEGACY_FIXTURE_SHA = "8167196e731762b54296a68108eda40bb3a1077bc691daa94e6cfd08640d8876"
ANIMATION_SHA = "9cae962bbb861bffe4d379bc30919cf34c0238ed35be82e605253d3059cfede9"
SUPPLEMENTAL_SHA = "907e2d368394e5c9db1bebdf218abae62bb3b841230293670cb3d2db48e05fcd"
IMPACT_SHA = "738d712b6f80d8a9456338548e7bbce1fdf88baa46f342a7c45efffd02a8f23a"
PROGRAMS_SHA = "944da0dc45732eec699f96882694a65c3adc1266b1feaa6d1fe7e5296852ffde"
PRODUCTION_PROFILE = {
    "stable_id": "original-teams-contact272",
    "animation_resource_key": "SourceData/local-plyr266-contact272-9cae962b",
    "services_resource_key": "BlitzSourceInterface/original-contact272-soundvm-match-services",
    "animation_sha256": ANIMATION_SHA,
    "clips": 272,
    "selected_frames": 24892,
    "impact_sha256": IMPACT_SHA,
    "contact_programs_sha256": PROGRAMS_SHA,
}

# Only inputs actually required by the active C1 programs and their 115 new clips.
SOURCE_PINS = {
    "include/ani3d.equ": "414c7d5e31cb0f96fe8fc41aea4d15059b566574",
    "include/player.equ": "cd6db29bdb743823347c983de7bd2a00b58d3d4a",
    "include/sndcalls.equ": "0096278ce26295856c57b8dc371937359d71348a",
    "anim/HIT99.WAN": "dd2b76e980bdadfe6cdbd189ca7f1c9f19605da9",
    "anim/misc99_2.wan": "8a7ff0ab4a1b722505bd1695bc5e722267385e9d",
    "anim/pupet99.wan": "33deef47181ca74f13885544f88b069407659f06",
    "anim/pupet992.wan": "f8cd7596590ae3af73fd3ec88b46b3abfd6048e8",
    "anim/puppet.wan": "15d46679f1dade7cb9af93759e0d9b0795dee4b5",
    "anim/puppet2.wan": "ad12f9d4de0b88a1d19b06b9db715b7995ac102a",
}


def profile_metadata(manifest):
    if manifest.get("source_revision") != REVISION:
        raise ValueError("Wrong real-input fixture source revision.")
    schema = manifest.get("schema")
    if schema == "source-kernel-real-inputs/v1":
        if "production_profile" in manifest or "contact_animation" in manifest:
            raise ValueError("A legacy fixture cannot disguise Contact272 as an old library.")
        return "legacy"
    if schema != SCHEMA:
        raise ValueError("Wrong real-input fixture schema/revision.")
    if manifest.get("production_profile") != PRODUCTION_PROFILE:
        raise ValueError("Contact272 requires its exact explicit production profile, keys and pins.")
    from fetch_inputs import input_pins
    if manifest.get("original_source_pins") != input_pins(PROFILE):
        raise ValueError("Contact272 requires the complete case-exact 69-plus-C1 original input closure.")
    return PROFILE


def fixture_profile(manifest_path, requested=None):
    profile = profile_metadata(json.loads(Path(manifest_path).read_bytes()))
    if requested is not None and profile != requested:
        raise ValueError("Explicit input profile " + requested + " differs from fixture " + profile + ".")
    return profile


def animation_files(root, relative, expected_sha, count, frames):
    from prepare_inputs import verified_file
    inventory = verified_file(root, relative, expected_sha)
    entries = json.loads(inventory.read_bytes())["clips"]
    if (len(entries) != count or len({entry["runtime_name"] for entry in entries}) != count
            or sum(entry["selected_frames"] for entry in entries) != frames):
        raise ValueError("Actual distinct animation inventory changed: " + relative)
    paths = [inventory]
    for entry in entries:
        paths.append(verified_file(inventory.parent, entry["path"], entry["sha256"]))
    return paths, entries


def require_disjoint_union(production, movement, supplemental):
    actual = {entry["runtime_name"]: entry for entry in production}
    basis = {entry["runtime_name"]: entry for entry in movement}
    extra = {entry["runtime_name"]: entry for entry in supplemental}
    if (len(actual) != 272 or len(basis) != 157 or len(extra) != 115
            or basis.keys() & extra.keys() or actual.keys() != basis.keys() | extra.keys()):
        raise ValueError("Production272 must contain the exact disjoint real157 and supplemental115 names.")
    for name, reference in {**basis, **extra}.items():
        for field, expected in reference.items():
            if field != "path" and actual[name].get(field) != expected:
                raise ValueError("Production272 differs from unchanged source clip " + name + "." + field)


def fixture_hashes(manifest_path, sound_functions=None):
    from prepare_inputs import fixture_hashes as legacy_hashes, verified_file, sha
    manifest_path = Path(manifest_path).absolute()
    root = manifest_path.parent
    manifest = json.loads(manifest_path.read_bytes())
    if profile_metadata(manifest) != PROFILE:
        raise ValueError("An explicitly prepared production Contact272 fixture is required.")
    legacy = manifest["legacy_fixture"]
    if legacy != {"path": "legacy-fixture.json", "sha256": LEGACY_FIXTURE_SHA}:
        raise ValueError("The original 443-file Sound fixture must remain separately identified.")
    legacy_path = verified_file(root, legacy["path"], LEGACY_FIXTURE_SHA)
    legacy_manifest = json.loads(legacy_path.read_bytes())
    for name, entry in legacy_manifest.items():
        if name != "schema" and manifest.get(name) != entry:
            raise ValueError("Contact272 changed a frozen legacy fixture field: " + name)
    result = legacy_hashes(legacy_path, sound_functions)
    result[str(manifest_path)] = sha(manifest_path)
    for name, relative, pin, count, frames in (
            ("contact_animation", "contact-animation/export-inventory.json", ANIMATION_SHA, 272, 24892),
            ("contact_supplemental_animation", "contact-supplemental/export-inventory.json",
             SUPPLEMENTAL_SHA, 115, 11402)):
        if manifest.get(name) != {"path": relative, "sha256": pin, "clips": count, "selected_frames": frames}:
            raise ValueError("Contact272 input entry differs: " + name)
        paths, entries = animation_files(root, relative, pin, count, frames)
        result.update({str(path.resolve()): sha(path) for path in paths})
        if name == "contact_animation":
            production = entries
        else:
            supplemental = entries
    movement = json.loads((root / manifest["movement_animation"]["path"]).read_bytes())["clips"]
    require_disjoint_union(production, movement, supplemental)
    impact = manifest["contact_impact"]
    if impact != {"path": "contact-impact.json", "sha256": IMPACT_SHA}:
        raise ValueError("Contact272 requires the actual generation-bound impact catalog.")
    impact_path = verified_file(root, impact["path"], IMPACT_SHA)
    result[str(impact_path)] = hashlib.sha256(impact_path.read_bytes()).hexdigest()
    return result
