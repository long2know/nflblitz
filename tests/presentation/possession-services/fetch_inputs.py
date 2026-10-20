#!/usr/bin/env python3
"""Fetch the exact possession/Goose/HUD/FX union from pinned public Git objects, never a local cache."""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent


def require(value, message):
    if not value:
        raise ValueError(message)


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def blob(raw):
    return hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()


def portable(name):
    require(isinstance(name, str) and name and "\\" not in name and ":" not in name and
            not name.startswith("/") and all(p not in ("", ".", "..") for p in name.split("/")),
            "Nonportable original input path: " + str(name))
    return name


def new_output(path):
    path = Path(path).absolute()
    require(path == path.resolve() and ROOT / ".squad/.scratch" in path.parents and not path.exists(),
            "Choose a NEW regular ignored worktree-local output.")
    for parent in path.parents:
        require(not parent.is_symlink(), "Symlink output ancestor is forbidden.")
        if parent == ROOT:
            break
    require(subprocess.run(["git", "check-ignore", "-q", str(path / "receipt.json")], cwd=ROOT).returncode == 0,
            "Original material and derived data must remain ignored.")
    return path


def catalog():
    recipe = json.loads((HERE / "inputs.json").read_bytes())
    require(recipe["schema"] == "possession-service-ci-inputs/v1" and len(recipe["authority"]) == 13,
            "Retain the exact13 original source/Goose authority inputs.")
    documents = {}
    for name, expected in recipe["recipes"].items():
        raw = (ROOT / portable(name)).read_bytes()
        require(digest(raw) == expected, "Inherited source input recipe changed: " + name)
        documents[name] = json.loads(raw)
    inputs = {}

    def add(repo, path, expected):
        key = (repo, portable(path))
        require(repo in recipe["repositories"], "Unknown pinned repository.")
        record = inputs.setdefault(key, {})
        for field in ("gitBlobSha1", "bytes", "sha256"):
            if field in expected:
                require(field not in record or record[field] == expected[field], "Conflicting original pin: " + path)
                record[field] = expected[field]

    for record in recipe["authority"]:
        add(record["repository"], record["path"], record)
    hud = documents["tools/interface/pins.json"]
    for group, repo in (("source", "nfl"), ("tools", "midway")):
        for path, expected in hud[group].items():
            add(repo, path, {"gitBlobSha1": expected})
    fire = documents["tools/fire-spfx/inputs.json"]
    for record in fire["inputs"] + fire["dustInputs"]:
        add("nfl", record["path"], record)
    add("nfl", recipe["helmet"]["path"], recipe["helmet"])
    require(len(inputs) == 76 and sum(repo == "nfl" for repo, _ in inputs) == 65,
            "Retain exact76-input source/Goose/HUD/v3-FX union; no omitted operands.")
    return recipe, inputs


def verify_bytes(raw, expected, name):
    require(blob(raw) == expected["gitBlobSha1"], "Actual Git blob bytes changed: " + name)
    require("bytes" not in expected or len(raw) == expected["bytes"], "Original byte length changed: " + name)
    require("sha256" not in expected or digest(raw) == expected["sha256"], "Original SHA256 changed: " + name)
    return dict(bytes=len(raw), gitBlobSha1=blob(raw), sha256=digest(raw))


def verify_directory(directory):
    directory = Path(directory).absolute()
    recipe, inputs = catalog()
    expected = {repo + "/" + path for repo, path in inputs} | {"receipt.json"}
    actual = {p.relative_to(directory).as_posix() for p in directory.rglob("*") if p.is_file() or p.is_symlink()}
    require(actual == expected, "Exact possession CI source inventory differs: missing=" +
            repr(sorted(expected - actual)) + "; unexpected=" + repr(sorted(actual - expected)))
    receipt = json.loads((directory / "receipt.json").read_bytes())
    require(receipt["schema"] == "possession-service-ci-acquisition/v1" and
            receipt["repositories"] == recipe["repositories"] and len(receipt["files"]) == 76,
            "Authenticated acquisition receipt identity/inventory changed.")
    records = {}
    for (repo, name), pin in sorted(inputs.items()):
        path = directory / repo / name
        require(path.is_file() and not path.is_symlink() and path.resolve() == path,
                "Missing/escaping regular original input: " + name)
        records[repo + "/" + name] = verify_bytes(path.read_bytes(), pin, repo + "/" + name)
    require(records == receipt["files"], "Acquired original receipt bytes/inventory changed.")
    return recipe, records


def github_json(endpoint):
    command = ["gh", "api", endpoint]
    for attempt in range(3):
        try:
            result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=90, check=True)
            return json.loads(result.stdout)
        except (subprocess.SubprocessError, json.JSONDecodeError):
            if attempt == 2:
                raise
            time.sleep(2 ** attempt)


def fetch(output):
    output = new_output(output)
    recipe, inputs = catalog()
    started = time.monotonic()
    trees = {}
    for repo, identity in recipe["repositories"].items():
        prefix = "repos/" + identity["name"] + "/git/"
        commit = github_json(prefix + "commits/" + identity["revision"])
        require(commit["sha"] == identity["revision"] and commit["tree"]["sha"] == identity["tree"],
                "Pinned original revision/tree relationship changed: " + repo)
        tree = github_json(prefix + "trees/" + identity["tree"] + "?recursive=1")
        require(tree["sha"] == identity["tree"] and not tree.get("truncated"),
                "Incomplete or wrong pinned original tree: " + repo)
        trees[repo] = {entry["path"]: entry for entry in tree["tree"]}
    payloads, records, downloaded = {}, {}, {}
    for (repo, name), expected in sorted(inputs.items()):
        identity = recipe["repositories"][repo]
        entry = trees[repo].get(name)
        require(entry is not None and entry["type"] == "blob" and entry["sha"] == expected["gitBlobSha1"],
                "Pinned tree/path/blob identity changed: " + repo + "/" + name)
        key = (repo, expected["gitBlobSha1"])
        if key not in downloaded:
            response = github_json("repos/" + identity["name"] + "/git/blobs/" + expected["gitBlobSha1"])
            require(response["sha"] == expected["gitBlobSha1"] and response["encoding"] == "base64",
                    "Unexpected Git blob API response: " + name)
            raw = base64.b64decode(response["content"])
            require(len(raw) == response["size"] == entry["size"], "Git blob API/tree length changed: " + name)
            downloaded[key] = raw
        raw = downloaded[key]
        record = verify_bytes(raw, dict(expected, bytes=entry["size"]), repo + "/" + name)
        relative = repo + "/" + name
        payloads[relative], records[relative] = raw, record
        print("AUTHENTICATED", relative, record["gitBlobSha1"], record["bytes"], flush=True)
    output.mkdir(parents=True)
    for name, raw in payloads.items():
        path = output / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
    receipt = dict(schema="possession-service-ci-acquisition/v1", repositories=recipe["repositories"],
                   files=records, downloadedGitBlobs=len(downloaded), localCacheUsed=False,
                   seconds=round(time.monotonic() - started, 3), originalMaterialIgnored=True,
                   command=sys.argv)
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    verify_directory(output)
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        receipt = fetch(args.output)
        print(json.dumps(dict(status="authenticated", inputs=len(receipt["files"]), seconds=receipt["seconds"])))
        return 0
    except (OSError, ValueError, KeyError, subprocess.SubprocessError) as error:
        print("Possession CI acquisition rejected: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
