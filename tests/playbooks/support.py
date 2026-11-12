"""Fail-closed identities shared by the P1 runner, extractor and optional fetcher."""
import hashlib
import json
from pathlib import Path


SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[1]
ARTIFACTS = SUITE / ".artifacts"
PINS = json.loads((SUITE / "pins.json").read_text(encoding="utf-8"))
HISTORICAL_CONTRACTS_14 = (
    "GameplayContracts.cs", "SourceAnimationContracts.cs", "SourceCameraContracts.cs", "SourceConfigurationContracts.cs",
    "SourceContactContracts.cs", "SourceFireSpfxContracts.cs", "SourceInputContracts.cs",
    "SourceInstructionContracts.cs", "SourceInstructionPresentationContracts.cs",
    "SourceKickMeterContracts.cs", "SourceKickoffInstructionChildContracts.cs", "SourceMatchContracts.cs",
    "SourcePortableProcessContracts.cs", "SourcePresentationContracts.cs",
)
HISTORICAL_CONTRACTS_13 = tuple(
    name for name in HISTORICAL_CONTRACTS_14 if name != "SourceKickoffInstructionChildContracts.cs")
CURRENT_CONTRACT_ADDITIONS = (
    "SourcePlaySelectionIntroductionContracts.cs", "SourcePreGameSoundContracts.cs", "SourceSavedRecordContracts.cs",
)
CONTRACTS = HISTORICAL_CONTRACTS_14 + CURRENT_CONTRACT_ADDITIONS
PRE_Q11_SOURCE_GENERATION = "complete-original-p1-current/v1"
SOURCE_GENERATION = "reviewed-sdk-interior-p1-current/v1"
PRE_Q11_PRODUCTION_SHA256 = {
    "Assets/Scripts/Gameplay/SourcePlaybook.cs": "8b91191c6e56563bff8696755bc4f5bedcc6acc6167fcb0e0d9df0c3d79b39fc",
    "Assets/Scripts/Gameplay/SourceDroneAI.cs": "cce5dc35460d5ec2cb77d7a0ab5d322fcb7816a60f28d41dc739ecbea70fbc07",
    "Assets/Scripts/Gameplay/SourceRandom.cs": "fb1b5a6690357933f69a30d842620d1aed9f091537444bc807dc3dee2993d1ce",
}
CURRENT_PRODUCTION_SHA256 = dict(PRE_Q11_PRODUCTION_SHA256, **{
    "Assets/Scripts/Gameplay/SourceDroneAI.cs": "1d2de05b0bf065d8ccfa305f7b2dbb061809c2e0bae5fbb70b72f00366a7c2ae",
})
GAMEPLAY = (
    "SourceClock.cs", "SourceMatchRules.cs", "SourceMatchFlow.cs",
    "SourcePlaybook.cs", "SourceDroneAI.cs", "SourceRandom.cs",
)
PRODUCER = (
    "convert.py", "recipe.json", "manifest.schema.json", "test_convert.py", "README.md", ".gitignore",
)


class GateError(RuntimeError):
    pass


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def blob_sha1(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def verify_inputs(source_root):
    source_root = Path(source_root).resolve()
    if not source_root.is_dir():
        raise GateError("Missing explicit pinned original source root: " + str(source_root))
    contents = {}
    for name, expected in PINS["blobs"].items():
        path = source_root / name
        if path.is_symlink() or not path.resolve().is_relative_to(source_root) or not path.is_file():
            raise GateError("Missing/escaping pinned original input: " + name)
        data = path.read_bytes()
        actual = blob_sha1(data)
        if actual != expected:
            raise GateError("Original input hash mismatch: " + name + "; expected " + expected + "; observed " + actual)
        contents[name] = data
    return contents


def require_recipe(recipe):
    if (recipe.get("schema") != "source-playbook-recipe/v1"
            or recipe.get("source_revision") != PINS["revision"]
            or recipe.get("source_tree") != PINS["tree"]
            or recipe.get("profile") != PINS["profile"]):
        raise GateError("Unsupported source playbook revision/tree/profile; only normal release 57Hz USE_OLD_QB=FALSE is tested.")
    if {value["path"]: value["blob_sha1"] for value in recipe["inputs"].values()} != {
            name: PINS["blobs"][name] for name in (
                "playbook.c", "PlayStuf.c", "playsel.c", "include/PlayStuf.h", "include/player.h",
                "player.c", "handicap.c", "lib/ani3d/TRIGS.H", "drone.c", "include/GAME.H")}:
        raise GateError("Producer recipe input pins differ from regression pins.")


def source_paths(root=ROOT, source_generation=SOURCE_GENERATION):
    root = Path(root)
    if source_generation not in (SOURCE_GENERATION, PRE_Q11_SOURCE_GENERATION, "historical-p1/v1"):
        raise GateError("Select an explicit known P1 source generation.")
    contracts = HISTORICAL_CONTRACTS_13 if source_generation == "historical-p1/v1" else CONTRACTS
    actual_contracts = {path.relative_to(root / "Assets/Scripts/Contracts").as_posix()
                        for path in (root / "Assets/Scripts/Contracts").rglob("*.cs")}
    if actual_contracts != set(contracts):
        missing, unexpected = set(contracts) - actual_contracts, actual_contracts - set(contracts)
        raise GateError("Expected the complete exact named Contracts source generation, never substitute DTOs; "
                        "missing=" + str(sorted(missing)) + "; unexpected=" + str(sorted(unexpected)))
    names = ["Assets/Scripts/Contracts/" + name for name in contracts]
    names += ["Assets/Scripts/Gameplay/" + name for name in GAMEPLAY]
    names += ["tools/playbooks/" + name for name in PRODUCER]
    for name in names:
        path = root / name
        if path.is_symlink() or not path.is_file() or not path.resolve().is_relative_to(root.resolve()):
            raise GateError("Missing/escaping actual source: " + name)
    return names


def live_hashes(root=ROOT, source_generation=SOURCE_GENERATION):
    return {name: sha256((Path(root) / name).read_bytes()) for name in source_paths(root, source_generation)}


def snapshot(root=ROOT, source_generation=SOURCE_GENERATION):
    contents = {name: (Path(root) / name).read_bytes() for name in source_paths(root, source_generation)}
    captured = {name: sha256(data) for name, data in contents.items()}
    if live_hashes(root, source_generation) != captured:
        raise GateError("LiveSources changed while capturing the bounded P1 snapshot.")
    production = (CURRENT_PRODUCTION_SHA256 if source_generation == SOURCE_GENERATION else
                  PRE_Q11_PRODUCTION_SHA256 if source_generation == PRE_Q11_SOURCE_GENERATION else
                  PINS["production_sha256"])
    for name, expected in dict(production, **PINS["producer_sha256"]).items():
        if captured.get(name) != expected:
            raise GateError("Frozen actual-source SHA256 guard failed: " + name + "; generation=" + source_generation)
    return contents, captured


def require_unchanged(before, root=ROOT, source_generation=SOURCE_GENERATION):
    after = live_hashes(root, source_generation)
    if after != before:
        raise GateError("LiveSources inventory/bytes changed during the P1 regression run.")
    return after


def validate_schema(value, schema, document=None, location="$"):
    """Validate the small pinned manifest schema subset; unknown keywords fail."""
    document = schema if document is None else document
    allowed = {
        "$schema", "$defs", "$ref", "title", "type", "additionalProperties", "required", "properties",
        "const", "minProperties", "minimum", "minLength", "minItems", "items", "pattern",
    }
    if set(schema) - allowed:
        raise GateError("Unsupported manifest schema keywords at " + location)
    if "$ref" in schema:
        parts = schema["$ref"].split("/")
        if parts[:2] != ["#", "$defs"] or len(parts) != 3:
            raise GateError("Unsupported manifest schema reference.")
        return validate_schema(value, document["$defs"][parts[2]], document, location)
    if "const" in schema and (type(value) is not type(schema["const"]) or value != schema["const"]):
        raise GateError("Manifest constant mismatch: " + location)
    kind = schema.get("type")
    kinds = {"object": dict, "array": list, "integer": int, "string": str}
    if kind is not None and (kind not in kinds or type(value) is not kinds[kind]):
        raise GateError("Manifest type mismatch: " + location)
    if kind == "object":
        if set(schema.get("required", [])) - set(value) or len(value) < schema.get("minProperties", 0):
            raise GateError("Manifest required fields missing: " + location)
        properties = schema.get("properties", {})
        extra = schema.get("additionalProperties", True)
        for key, field in value.items():
            if key in properties:
                validate_schema(field, properties[key], document, location + "." + key)
            elif extra is False:
                raise GateError("Unexpected manifest field: " + location + "." + key)
            elif isinstance(extra, dict):
                validate_schema(field, extra, document, location + "." + key)
    elif kind == "array":
        if len(value) < schema.get("minItems", 0):
            raise GateError("Empty manifest array: " + location)
        for index, item in enumerate(value):
            validate_schema(item, schema["items"], document, location + "[" + str(index) + "]")
    elif kind == "integer" and value < schema.get("minimum", value):
        raise GateError("Manifest integer below minimum: " + location)
    elif kind == "string":
        import re
        if len(value) < schema.get("minLength", 0) or ("pattern" in schema and re.search(schema["pattern"], value) is None):
            raise GateError("Malformed manifest string: " + location)
