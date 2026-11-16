#!/usr/bin/env python3
"""Deliver only the closed, freshly compiled 270c74c public AV306 image."""
import argparse
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import time
from types import SimpleNamespace
from urllib.error import HTTPError
from urllib.request import Request, build_opener

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
spec = importlib.util.spec_from_file_location("released_view_helpers", HERE / "view_controls_image.py")
ui = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ui)
cache, frozen, require = ui.cache, ui.frozen, ui.require
IMAGE = "ghcr.io/long2know/nflblitz"
PINS = HERE / "fresh_main_image_pins.json"
WORKFLOW = "fresh-main-image.yml"


def pins():
    return json.loads(PINS.read_bytes())


def transfer_values(env):
    values = dict(pins()["transfer"])
    values.update({name: env.get("TRANSFER_" + name.upper(), "")
                   for name in ("release_tag", "secret_name", "cipher_sha256")})
    return frozen.validate_inputs(values)


def source_guard(build=None):
    p = pins()
    head = os.environ.get("GITHUB_SHA", "")
    require(re.fullmatch(r"[a-f0-9]{40}", head or "") and
            frozen.run(["git", "rev-parse", "HEAD"]).decode().strip() == head,
            "The actual committed main workflow checkout is required.")
    require(subprocess.run(["git", "merge-base", "--is-ancestor", p["transfer"]["source_revision"], head],
                           cwd=ROOT, capture_output=True).returncode == 0,
            "Compiled270 source must already be merged into workflow main.")
    require(subprocess.run(["git", "diff", "--quiet", "HEAD", "--"], cwd=ROOT).returncode == 0,
            "Uncommitted release-source replacements are forbidden.")
    for row in p["helperSources"]:
        require(frozen.sha(ROOT / row["path"]) == row["sha256"],
                "Reviewed release helper changed: " + row["path"])
    if build is not None:
        require(build.get("sourceRootDigest") == p["transfer"]["source_digest"] and
                hashlib.sha256(json.dumps(build["sources"], sort_keys=True).encode()).hexdigest() ==
                p["transfer"]["source_digest"] and len(build["sources"]) == 163,
                "The actual compiled163-source inventory changed.")
        for row in build["sources"]:
            name = cache.package.relative(row["path"])
            require(frozen.sha(ROOT / name) == row["sha256"] and
                    hashlib.sha256(frozen.run(["git", "show",
                        p["transfer"]["source_revision"] + ":" + name])).hexdigest() == row["sha256"],
                    "Compiled source differs from270 or actual main: " + name)


def validate_build(build):
    p = pins()
    require(build.get("profile") == "original-teams-complete-av306" and
            build.get("factory") == "SourceC2Composition.CreateCompleteAv306Simulation" and
            build.get("dataManifestSha256") == p["transfer"]["data_manifest_sha256"] and
            build.get("execution") == p["execution"] and build.get("assemblies") == p["assemblies"] and
            "compiledAotReuse" not in build.get("execution", {}) and
            len(build.get("verifiedRuntimeResources", {}).get("files", [])) == 48,
            "Only fresh270 rooted-AOT/native/modules and original1153 product are authorized.")


def manifest(reference, *, missing=False):
    immutable = "sha256-" + pins()["transfer"]["image_id"][7:]
    require(reference in ("latest", immutable) or re.fullmatch(r"sha256:[a-f0-9]{64}", reference),
            "No arbitrary registry target or tag is authorized.")
    request = Request(cache.base.REGISTRY + reference, headers={
        "Authorization": "Bearer " + cache.base.registry_token(), "Accept": cache.base.MEDIA_TYPE})
    try:
        with build_opener(frozen.NoRedirect()).open(request, timeout=30) as response:
            raw = response.read(4 * 1024 * 1024)
            digest = response.headers.get("Docker-Content-Digest")
            media = response.headers.get_content_type()
    except HTTPError as error:
        with error:
            status, body = error.code, json.loads(error.read(8192))
        if missing and reference == immutable and status == 404:
            require(body.get("errors") and all(x.get("code") == "MANIFEST_UNKNOWN" for x in body["errors"]),
                    "Unknown immutable-tag404 response.")
            return None
        raise ValueError("Anonymous manifest request failed HTTP" + str(status)) from None
    require(media == cache.base.MEDIA_TYPE and
            digest == "sha256:" + hashlib.sha256(raw).hexdigest(),
            "Anonymous manifest content digest/type mismatch.")
    value = json.loads(raw)
    require(value.get("schemaVersion") == 2 and "manifests" not in value and value.get("layers"),
            "Only a single linux/amd64 image manifest is authorized.")
    return {"raw": raw, "digest": digest, "config": value["config"]["digest"], "layers": value["layers"]}


def latest_guard(candidate=None):
    actual = manifest("latest")
    p = pins()
    require((actual["digest"], actual["config"]) == (p["priorManifest"], p["priorConfig"]) or
            candidate is not None and actual == candidate,
            "Unexpected concurrent latest delivery; stop without overwriting.")
    return actual


def candidate_manifest(reference):
    value = manifest(reference)
    require(value["config"] == pins()["transfer"]["image_id"] and len(value["layers"]) == 12,
            "Immutable public manifest does not link exact fresh image/all12layers.")
    return value


def verify_image(docker, work, values):
    p = pins()
    actual = json.loads(docker(["image", "inspect", values["source_tag"]]))[0]
    require(actual["Id"] == values["image_id"] and actual["RootFS"]["Layers"] == p["rootfsDiffIds"],
            "Loaded image config or exact12 rootfs identities changed.")
    frozen.image_config({"os": actual["Os"], "architecture": actual["Architecture"],
                         "config": actual["Config"]}, values)
    archive = work / "loaded-image.tar"
    docker(["image", "save", "--output", str(archive), values["source_tag"]])
    frozen.inspect_archive(archive, values)
    site, inventory_path = cache.extract_new_site(docker, values["source_tag"], work)
    require(frozen.sha(inventory_path) == p["inventoryFileSha256"],
            "Exact immutable1219 bundle receipt changed.")
    build, rows = cache.package.inspect_bundle(site, values["build_sha256"], values["source_digest"])
    validate_build(build)
    source_guard(build)
    inventory = json.loads(inventory_path.read_bytes())
    require(len(rows) == 1219 and sum(x["bytes"] for x in rows) == 338205172 and
            rows == inventory["files"] and inventory["sourceBaseCommit"] == values["source_revision"],
            "Every actual1219 payload and honest compiled270 identity must match.")
    seen = set()
    with tarfile.open(archive) as outer:
        record = json.load(outer.extractfile("manifest.json"))[0]
        for name in record["Layers"]:
            with tarfile.open(fileobj=outer.extractfile(name), mode="r|") as layer:
                for entry in layer:
                    name = entry.name.removeprefix("./")
                    if name == "opt/nflblitz/www" or name.startswith("opt/nflblitz/www/"):
                        require(entry.uid == entry.gid == 0 and
                                (entry.isdir() and entry.mode == 0o755 or
                                 entry.isfile() and entry.mode == 0o444),
                                "Actual image app ownership/modes differ: " + name)
                        if entry.isfile():
                            seen.add(name.removeprefix("opt/nflblitz/www/"))
    require(seen == {x["path"] for x in rows}, "Saved image app inventory differs.")
    archive.unlink()
    return site, inventory_path


def runtime(docker, work, image, inventory):
    name = "fresh270-" + os.environ["GITHUB_RUN_ID"] + "-" + os.environ["GITHUB_RUN_ATTEMPT"]
    require(re.fullmatch(r"fresh270-[0-9]+-[0-9]+", name), "Run-owned container name required.")
    created = False
    try:
        docker(["create", "--name", name, "--platform", "linux/amd64", "--pull=never",
                "--user", "1000:1000", "--env", "BASE_URL=/nflblitz",
                "--publish", "127.0.0.1:8920:8080/tcp", image])
        created = True
        docker(["start", name])
        deadline = time.monotonic() + 100
        while True:
            actual = json.loads(docker(["inspect", name]))[0]
            health = actual["State"].get("Health", {}).get("Status")
            if health == "healthy":
                break
            require(actual["State"]["Running"] and health != "unhealthy" and time.monotonic() < deadline,
                    "Ordinary no-tmpfs UID1000 prefix container did not become healthy.")
            time.sleep(2)
        host = actual["HostConfig"]
        require(actual["Image"] == pins()["transfer"]["image_id"] and
                actual["Config"]["User"] == "1000:1000" and not host["ReadonlyRootfs"] and
                not host["Privileged"] and not host.get("Tmpfs") and not host.get("Binds") and
                not host.get("SecurityOpt"), "No runtime mounts or security weakening are allowed.")
        kernel = docker(["exec", name, "sh", "-c",
            "id -u; id -g; awk '/^CapEff:/ {print $2} /^Seccomp:/ {print $2}' /proc/1/status; "
            "stat -c '%u:%a' /tmp/nflblitz-1000 /opt/nflblitz/www/build-info.json"]).decode().splitlines()
        require(kernel == ["1000", "1000", "0000000000000000", "2", "1000:700", "0:444"],
                "Real UID/GID, capabilities, seccomp, private runtime and app modes differ.")
        url = "http://127.0.0.1:8920/nflblitz/"
        env = dict(os.environ, NFLBLITZ_TEST_URL=url, NFLBLITZ_TEST_INVENTORY=str(inventory))
        frozen.run(["python3", "-B", str(ROOT / "tools/source-player/deploy/test_deployment.py"),
                    "ImageHttpTests", "-q"], env=env)
        browser = work / "cache-browser.json"
        frozen.run(["node", str(ROOT / "tools/source-player/verified-cache.browser.mjs"), url,
                    str(browser), str(work / "chrome"), "--image-smoke"], env=env)
        proof = json.loads(browser.read_bytes())
        ui.validate_cache_browser(proof)
        view = ui.ui_browser(url, work)
        frozen.run(["node", str(HERE / "fresh_main_image.browser.mjs"), url,
                    str(work / "audio-browser.json"), str(work / "audio")])
        audio = json.loads((work / "audio-browser.json").read_bytes())
        require(view["accepted"] and audio["accepted"] is True and audio["normalSandbox"] is True,
                "Actual view controls and trusted original audio/WebGL2 must pass.")
        return {"ordinaryUid1000NoTmpfsPrefixHealthFullHttp": True, "kernel": kernel,
                "cacheBrowserSha256": frozen.sha(browser), "cacheStages": proof["stages"],
                "view": view, "audio": audio}
    finally:
        if created:
            docker(["rm", "--force", name])


def promote(candidate, token, work):
    cache.base.public_package(token)
    require(candidate_manifest(candidate["digest"]) == candidate, "Immutable candidate changed.")
    current = latest_guard(candidate)
    if current == candidate:
        return
    bearer = cache.base.registry_token(token)
    require(latest_guard(candidate) == current, "Latest raced immediately before conditional promotion.")
    cache.alias_put(candidate, bearer, current, work)
    require(candidate_manifest("latest") == candidate, "Anonymous latest digest differs after promotion.")
    cache.base.public_package(token)


def publish(work, docker, token, key, values):
    cache.base.public_package(token)
    latest_guard()
    release = frozen.api("/repos/long2know/nflblitz/releases/tags/" + values["release_tag"], token)
    require(isinstance(release, dict) and release.get("tag_name") == values["release_tag"] and
            release.get("prerelease") is True and release.get("draft") is False and
            [x.get("name") for x in release.get("assets", [])] == [frozen.ASSET],
            "Only the unique ciphertext-only prerelease may be downloaded.")
    frozen.run(["gh", "release", "download", values["release_tag"], "--repo", "long2know/nflblitz",
                "--pattern", frozen.ASSET, "--dir", str(work)], env=dict(os.environ, GH_TOKEN=token))
    frozen.receive_and_load(work / frozen.ASSET, work / "image.tar.gz", values, key, docker)
    _, inventory = verify_image(docker, work, values)
    proof = runtime(docker, work, values["source_tag"], inventory)
    cache.base.public_package(token)
    latest_guard()
    immutable = IMAGE + ":sha256-" + values["image_id"][7:]
    existing = manifest(immutable.rsplit(":", 1)[1], missing=True)
    if existing is not None:
        candidate_manifest(existing["digest"])
    docker(["login", "ghcr.io", "--username", "long2know", "--password-stdin"], input=(token + "\n").encode())
    docker(["tag", values["source_tag"], immutable])
    docker(["push", immutable])
    candidate = candidate_manifest(immutable.rsplit(":", 1)[1])
    promote(candidate, token, work)
    return {"imageId": values["image_id"], "manifestDigest": candidate["digest"],
            "immutable": immutable, "runtime": proof, "published": True}


def anonymous(work, docker):
    digest = os.environ.get("FRESH_EXPECTED_MANIFEST", "")
    require(re.fullmatch(r"sha256:[a-f0-9]{64}", digest or ""), "Publisher's real immutable digest required.")
    require(candidate_manifest("latest")["digest"] == digest, "Latest changed before anonymous cold pull.")
    require(not docker(["image", "ls", "--quiet", IMAGE]).strip(), "Fresh anonymous runner required.")
    docker(["pull", "--platform", "linux/amd64", IMAGE + "@" + digest])
    values = transfer_values(os.environ)
    docker(["tag", IMAGE + "@" + digest, values["source_tag"]])
    _, inventory = verify_image(docker, work, values)
    proof = runtime(docker, work, values["source_tag"], inventory)
    require(candidate_manifest("latest")["digest"] == digest, "Latest raced during anonymous acceptance.")
    config = work / "docker/config.json"
    credentials = json.loads(config.read_bytes()) if config.exists() else {}
    require(not any(credentials.get(x) for x in ("auths", "credsStore", "credHelpers")),
            "Anonymous consumer acquired registry credentials.")
    return {"imageId": pins()["transfer"]["image_id"], "manifestDigest": digest,
            "freshAnonymousPull": True, "noRegistryCredentials": True, "runtime": proof}


def prepare(args):
    require(args.qa_approved, "Actual independent Quality approval is required; never infer it from selfchecks.")
    main = frozen.run(["gh", "api", "repos/long2know/nflblitz/git/ref/heads/main",
                       "--jq", ".object.sha"]).decode().strip()
    frozen.run(["gh", "api", "repos/long2know/nflblitz/contents/.github/workflows/" +
                WORKFLOW + "?ref=" + main, "--jq", ".sha"])
    with contextlib.redirect_stdout(io.StringIO()):
        frozen.prepare(SimpleNamespace(qa_approved=True, archive=args.archive, output_dir=args.work_dir,
                                       **pins()["transfer"]))
    path = args.work_dir / "receipt.json"
    receipt = json.loads(path.read_bytes())
    receipt["workflow"] = WORKFLOW
    (args.work_dir / "workflow-inputs.json").write_text(json.dumps({
        k: receipt["inputs"][k] for k in ("release_tag", "secret_name", "cipher_sha256")}, indent=2) + "\n")
    path.write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps(receipt, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("prepare", "publish", "anonymous"))
    parser.add_argument("--work-dir", required=True, type=Path)
    parser.add_argument("--archive", type=Path)
    parser.add_argument("--qa-approved", action="store_true")
    args = parser.parse_args()
    os.umask(0o077)
    os.environ["GH_HOST"] = "github.com"
    if args.action == "prepare":
        prepare(args)
        return
    cache.base.identity.context(os.environ)
    source_guard()
    require(not os.environ.get("DOCKER_AUTH_CONFIG") and not os.environ.get("GITHUB_TOKEN"),
            "Inherited registry credentials are forbidden.")
    token, key = os.environ.pop("GH_TOKEN", ""), os.environ.pop("REGISTRY_TRANSFER_KEY", "")
    require(bool(token and key) if args.action == "publish" else not token and not key,
            "Only publisher may receive job token/transfer key.")
    values = transfer_values(os.environ) if args.action == "publish" else None
    work = frozen.private_directory(args.work_dir)
    env = dict(os.environ, DOCKER_CONFIG=str(work / "docker"))

    def docker(command, **kwargs):
        require(command[0] not in ("build", "manifest") and
                (args.action == "publish" or command[0] not in ("login", "push")),
                "Rebuilding or anonymous authentication/publication is forbidden.")
        if command[0] == "push":
            require(command == ["push", IMAGE + ":sha256-" + pins()["transfer"]["image_id"][7:]],
                    "Only exact immutable candidate receives image blobs.")
        return frozen.run(["docker"] + command, env=env, **kwargs)

    try:
        receipt = publish(work, docker, token, key, values) if args.action == "publish" else anonymous(work, docker)
        receipt.update(sourceRevision=pins()["transfer"]["source_revision"],
                       compiledSourceDigest=pins()["transfer"]["source_digest"],
                       buildInfoSha256=pins()["transfer"]["build_sha256"],
                       independentArtifactQuality=pins()["independentArtifactQuality"],
                       publisherRevision=os.environ["GITHUB_SHA"])
        text = json.dumps(receipt, indent=2, sort_keys=True)
        print(text)
        with Path(os.environ["GITHUB_STEP_SUMMARY"]).open("a") as summary:
            summary.write("## Exact fresh270 public image " + args.action + "\n\n```json\n" + text + "\n```\n")
        if args.action == "publish":
            with Path(os.environ["GITHUB_OUTPUT"]).open("a") as out:
                out.write("manifestDigest=" + receipt["manifestDigest"] + "\n")
    finally:
        try:
            if token:
                docker(["logout", "ghcr.io"])
        finally:
            shutil.rmtree(work)


if __name__ == "__main__":
    main()
