#!/usr/bin/env python3
"""Recover only accepted7419 into the explicitly selected, independently verified private package."""
import argparse
import base64
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import time
from urllib.error import HTTPError
from urllib.parse import urlencode
from urllib.request import Request, build_opener

HERE = Path(__file__).resolve().parent
REPOSITORY = "long2know/nflblitz"
TARGET_NAME = "nflblitz-private"
TARGET_IMAGE = "ghcr.io/long2know/" + TARGET_NAME
TARGET_API = "/users/long2know/packages/container/" + TARGET_NAME
PINS_SHA = "854eb3003396c46a8ca84ac8bdee7a74dbe7d6897a29ef21cabd05886a31433e"
MANIFEST_TYPES = ", ".join((
    "application/vnd.oci.image.index.v1+json",
    "application/vnd.oci.image.manifest.v1+json",
    "application/vnd.docker.distribution.manifest.list.v2+json",
    "application/vnd.docker.distribution.manifest.v2+json",
))
spec = importlib.util.spec_from_file_location("frozen_registry_transfer", HERE / "registry_transfer.py")
frozen = importlib.util.module_from_spec(spec)
spec.loader.exec_module(frozen)
require = frozen.require


def load_pins():
    path = HERE / "private_target_recovery_pins.json"
    require(frozen.sha(path) == PINS_SHA, "The exact approved recovery/source/runtime/Quality pins changed.")
    require(frozen.sha(HERE / "registry_transfer.py") ==
            "c554c818082878d2c1eb33db9ce1b1f8115f1ade78049f0af4219771c4cb267d",
            "Frozen c554 identity/archive/payload/privacy code must remain byte-identical.")
    value = json.loads(path.read_bytes())
    require(value["imageId"] == "sha256:7419b62b728945fa59e90590dfe4e7eeec9a0eed96a9b53daebc225b31fae6cb" and
            value["sourceManifestDigest"] == "sha256:5cace3ee21a0a7f3ecf08e78661aa84b221a3c4c409b390cf6b50996b99b977c" and
            len(value["requiredRemoteMainFiles"]) == 178 and len(value["rootfsDiffIds"]) == 12,
            "Only the exact previously attested7419 image and source graph may be recovered.")
    return value


def context(env):
    require(env.get("GITHUB_ACTIONS") == "true" and env.get("GITHUB_EVENT_NAME") == "workflow_dispatch" and
            env.get("GITHUB_REPOSITORY") == REPOSITORY and env.get("GITHUB_REF") == "refs/heads/main" and
            re.fullmatch(r"[0-9a-f]{40}", env.get("GITHUB_SHA", "")),
            "Recovery/cold pull require the actual repository main manual workflow, never a branch or arbitrary target.")


def github_json(path, token):
    raw = frozen.run(["gh", "api", "repos/" + REPOSITORY + "/" + path],
                     env=dict(os.environ, GH_TOKEN=token))
    return json.loads(raw)


def verify_main(pins, token):
    relation = github_json("compare/" + pins["sourceRevision"] + "..." + os.environ["GITHUB_SHA"], token)
    require(relation["status"] in ("ahead", "identical"), "Accepted source is not merged into actual workflow main.")
    tree_sha = github_json("git/commits/" + os.environ["GITHUB_SHA"], token)["tree"]["sha"]
    tree = github_json("git/trees/" + tree_sha + "?recursive=1", token)
    require(not tree.get("truncated") and tree["sha"] == tree_sha, "Actual main Git tree is incomplete.")
    files = {row["path"]: row for row in tree["tree"] if row["type"] == "blob"}
    for row in pins["requiredRemoteMainFiles"]:
        actual = files.get(row["path"])
        require(actual is not None and actual["sha"] == row["gitBlobSha1"] and
                actual["mode"] in ("100644", "100755"),
                "Actual main147/161/17 bytes differ from the accepted image: " + row["path"])


def package_metadata(token):
    package = frozen.api(TARGET_API, token)
    print(json.dumps({"stage": "selected-private-target-metadata",
                      "endpoint": "https://api.github.com" + TARGET_API,
                      "httpStatus": 404 if package is None else 200,
                      "name": None if package is None else package.get("name"),
                      "visibility": None if package is None else package.get("visibility"),
                      "owner": None if package is None else (package.get("owner") or {}).get("login"),
                      "packageType": None if package is None else package.get("package_type"),
                      "repository": None if package is None else (package.get("repository") or {}).get("full_name")},
                     sort_keys=True), flush=True)
    return package


def assert_private_metadata(package):
    require(isinstance(package, dict) and package.get("visibility") == "private" and
            package.get("package_type") == "container" and package.get("name") == TARGET_NAME and
            (package.get("owner") or {}).get("login") == "long2know" and
            (package.get("owner") or {}).get("type") == "User" and
            (package.get("repository") or {}).get("full_name") == REPOSITORY,
            "Selected new target must actually be private, correctly owned/named/typed and linked; no game blob is admitted.")


def anonymous_denied():
    query = urlencode({"service": "ghcr.io", "scope": "repository:long2know/" + TARGET_NAME + ":pull"})
    try:
        with build_opener(frozen.NoRedirect()).open(
                Request("https://ghcr.io/token?" + query, headers={"Accept": "application/json"}), timeout=30):
            raise ValueError("Anonymous target pull token is available; no original game bytes may be pushed.")
    except HTTPError as error:
        with error:
            require(error.code == 403, "Unknown anonymous target response; failing closed.")
            value = json.loads(error.read(8192))
        require(isinstance(value, dict) and len(value.get("errors", [])) == 1 and
                value["errors"][0].get("code") == "DENIED", "Unknown anonymous target denial.")


def private_target(token):
    assert_private_metadata(package_metadata(token))
    anonymous_denied()


def ensure_private_target(token, docker, work):
    package = package_metadata(token)
    if package is not None:
        assert_private_metadata(package)
        anonymous_denied()
        return
    anonymous_denied()
    run = os.environ.get("GITHUB_RUN_ID", "") + "-" + os.environ.get("GITHUB_RUN_ATTEMPT", "")
    require(re.fullmatch(r"[0-9]+-[0-9]+", run), "A unique actual workflow run identifies the asset-free bootstrap.")
    context_dir = work / "asset-free-bootstrap"
    context_dir.mkdir(mode=0o700)
    (context_dir / "Dockerfile").write_text(
        "FROM scratch\n"
        "LABEL org.opencontainers.image.source=\"https://github.com/long2know/nflblitz\" "
        "io.nflblitz.bootstrap-only=\"no-original-assets-nonexecutable\"\n"
        "USER 101:101\n")
    require([path.name for path in context_dir.iterdir()] == ["Dockerfile"],
            "The bootstrap context must contain no app, data, source or executable.")
    tag = TARGET_IMAGE + ":bootstrap-private-owner-check-" + run
    docker(["build", "--network", "none", "--pull=false", "--tag", tag, str(context_dir)])
    docker(["push", tag])
    for attempt in range(6):
        package = package_metadata(token)
        if package is not None:
            assert_private_metadata(package)
            anonymous_denied()
            return
        time.sleep(2)
    raise ValueError("Bootstrap package metadata remains unknown; no game pull/push is permitted.")


def target_pull_token(token):
    query = urlencode({"service": "ghcr.io", "scope": "repository:long2know/" + TARGET_NAME + ":pull"})
    auth = base64.b64encode(("long2know:" + token).encode()).decode()
    request = Request("https://ghcr.io/token?" + query,
                      headers={"Authorization": "Basic " + auth, "Accept": "application/json"})
    with build_opener(frozen.NoRedirect()).open(request, timeout=30) as response:
        value = json.loads(response.read(65536))
    require(response.status == 200 and isinstance(value.get("token"), str), "No target-scoped registry pull token.")
    return value["token"]


def target_manifest(reference, token, *, absent_ok=False):
    require(re.fullmatch(r"(?:sha256:[0-9a-f]{64}|sha256-[0-9a-f]{64}|latest)", reference),
            "Only the explicitly selected private latest/immutable/digest may be inspected.")
    bearer = target_pull_token(token)

    def read(name, allow_absent):
        request = Request("https://ghcr.io/v2/long2know/" + TARGET_NAME + "/manifests/" + name,
                          headers={"Authorization": "Bearer " + bearer, "Accept": MANIFEST_TYPES})
        try:
            with build_opener(frozen.NoRedirect()).open(request, timeout=30) as response:
                raw, digest = response.read(4 * 1024 * 1024), response.headers.get("Docker-Content-Digest")
        except HTTPError as error:
            with error:
                status, value = error.code, json.loads(error.read(8192))
            if allow_absent and status == 404:
                errors = value.get("errors", [])
                require(errors and all(row.get("code") in ("MANIFEST_UNKNOWN", "NAME_UNKNOWN") for row in errors),
                        "Unknown private tag absence response.")
                return None
            raise ValueError("Private registry manifest check failed HTTP" + str(status)) from None
        require(re.fullmatch(r"sha256:[0-9a-f]{64}", digest or "") and
                "sha256:" + hashlib.sha256(raw).hexdigest() == digest,
                "Private registry manifest bytes/digest differ.")
        return digest, json.loads(raw)

    actual = read(reference, absent_ok)
    if actual is None:
        return None
    digest, manifest = actual
    if "manifests" in manifest:
        children = manifest["manifests"]
        require(manifest.get("schemaVersion") == 2 and len(children) == 1,
                "Private target must retain exactly one accepted image, not a substituted index.")
        platform = children[0].get("platform", {})
        require(platform.get("os", "linux") == "linux" and platform.get("architecture", "amd64") == "amd64",
                "Private target platform differs.")
        child = read(children[0]["digest"], False)
        require(child[0] == children[0]["digest"], "Private index child manifest digest differs.")
        manifest = child[1]
    pins = load_pins()
    require(manifest.get("schemaVersion") == 2 and "manifests" not in manifest and
            manifest.get("config", {}).get("digest") == pins["imageId"] and manifest.get("layers"),
            "Selected private immutable/latest already identifies another image; STOP without overwriting it.")
    return digest


def check_no_other_image(token):
    pins = load_pins()
    return {name: target_manifest(name, token, absent_ok=True)
            for name in ("sha256-" + pins["imageId"][7:], "latest")}


def verify_loaded(pins, docker, work):
    loaded = json.loads(docker(["image", "inspect", pins["sourceTag"]]))[0]
    values = pins["historicalTransferInputsForFrozenArchiveVerificationOnly"]
    frozen.image_config({"os": loaded["Os"], "architecture": loaded["Architecture"], "config": loaded["Config"]}, values)
    require(loaded["RootFS"]["Layers"] == pins["rootfsDiffIds"] and
            loaded["Config"]["Labels"].get("io.nflblitz.compiled-source-commit") == pins["actualCompiledHead"],
            "Recovered image rootfs or actual6ad compilation provenance changed.")
    frozen.inspect_archive(docker(["image", "save", pins["sourceTag"]]), values)
    accepted = frozen.verify_payload(values, work, docker, loaded["Config"])
    require(accepted == {"profile": "original-teams-complete-av306", "deploymentProfile": "runtime-base-url/v1"},
            "Only the qualified GENERAL1153 BASE_URL family is eligible.")
    build = json.loads((work / "build-info.json").read_bytes())
    execution = build["execution"]
    require(execution["nativeRuntimeSha256"] == pins["nativeWasmSha256"] and
            next(row["sha256"] for row in execution["applicationObjects"] if row["assembly"] == "SourcePlayer.Core") ==
            pins["coreAotSha256"] and
            next(row["sha256"] for row in build["assemblies"] if row["path"].endswith("Core.wasm")) ==
            pins["coreWebcilSha256"] and
            loaded["Config"]["Labels"]["io.nflblitz.deployment-sha256"] == pins["deploymentSha256"],
            "Actual new CoreAOT/native/Webcil/deployment receipts differ from accepted7419.")


def verify_existing_images(existing, pins, docker, work):
    for digest in set(value for value in existing.values() if value is not None):
        reference = TARGET_IMAGE + "@" + digest
        directory = work / ("existing-" + digest[7:23])
        directory.mkdir(mode=0o700)
        try:
            docker(["pull", "--platform", "linux/amd64", reference])
            docker(["tag", reference, pins["sourceTag"]])
            verify_loaded(pins, docker, directory)
        finally:
            shutil.rmtree(directory)


def execute(action, directory, expected_digest=None):
    context(os.environ)
    pins = load_pins()
    token = os.environ.pop("GH_TOKEN", "")
    require(token, "The repository job token is required; no PAT or permission expansion.")
    verify_main(pins, token)
    work = frozen.private_directory(directory)
    docker_env = dict(os.environ, DOCKER_CONFIG=str(work / "docker"))

    def docker(command, **kwargs):
        if command[:1] == ["push"]:
            require(command[1].startswith(TARGET_IMAGE + ":") and
                    not command[1].startswith("ghcr.io/long2know/nflblitz:"),
                    "No push to the known public/original namespace is permitted.")
        return frozen.run(["docker"] + command, env=docker_env, **kwargs)

    logged_in = False
    try:
        if action == "cold-pull":
            require(re.fullmatch(r"sha256:[0-9a-f]{64}", expected_digest or ""), "Exact private published digest required.")
            private_target(token)
        docker(["login", "ghcr.io", "--username", "long2know", "--password-stdin"], input=(token + "\n").encode())
        logged_in = True
        immutable = TARGET_IMAGE + ":sha256-" + pins["imageId"][7:]
        if action == "recover":
            ensure_private_target(token, docker, work)
            verify_existing_images(check_no_other_image(token), pins, docker, work)
            source = frozen.IMAGE + "@" + pins["sourceManifestDigest"]
            require(frozen.remote_image(source, pins["imageId"], docker) == pins["sourceManifestDigest"],
                    "Read-only recovery source manifest/config changed.")
            docker(["pull", "--platform", "linux/amd64", source])
            docker(["tag", source, pins["sourceTag"]])
            verify_loaded(pins, docker, work)
            private_target(token)
            verify_existing_images(check_no_other_image(token), pins, docker, work)
            docker(["tag", pins["sourceTag"], immutable])
            docker(["push", immutable])
            digest = target_manifest(immutable.rsplit(":", 1)[1], token)
            private_target(token)
            latest = target_manifest("latest", token, absent_ok=True)
            require(latest is None or latest == digest, "Different private latest cannot be overwritten.")
            docker(["tag", pins["sourceTag"], TARGET_IMAGE + ":latest"])
            docker(["push", TARGET_IMAGE + ":latest"])
            require(target_manifest("latest", token) == digest, "Private latest/immutable manifests differ.")
            private_target(token)
        else:
            require(action == "cold-pull", "Unknown recovery action.")
            require(target_manifest("latest", token) == target_manifest(immutable.rsplit(":", 1)[1], token) ==
                    expected_digest, "Private cold-pull latest/immutable/config identity differs.")
            reference = TARGET_IMAGE + "@" + expected_digest
            previous = subprocess.run(["docker", "image", "inspect", reference], env=docker_env,
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            require(previous.returncode != 0, "Cold-pull runner already has the accepted image.")
            docker(["pull", "--platform", "linux/amd64", reference])
            docker(["tag", reference, pins["sourceTag"]])
            verify_loaded(pins, docker, work)
            private_target(token)
            digest = expected_digest
        receipt = {"schema": "explicit-private-target-exact7419-" + action + "/v1",
                   "target": TARGET_IMAGE, "latest": TARGET_IMAGE + ":latest", "immutable": immutable,
                   "manifest_digest": digest, "image_id": pins["imageId"], "package_visibility": "private",
                   "repository": REPOSITORY, "anonymousPullDenied": True, "allConfigAndLayerBytesVerified": True,
                   "actualCompiledHead": pins["actualCompiledHead"], "sourceRevision": pins["sourceRevision"],
                   "actualWorkflowMain": os.environ["GITHUB_SHA"], "buildInfoSha256": pins["buildInfoSha256"],
                   "sourceDigest": pins["sourceDigest"], "runtimeProofSha256": pins["runtimeProofSha256"],
                   "finalQualitySha256": pins["finalQualitySha256"], "noOldPublicNamespaceMutation": True,
                   "noAotImageOrLocalRuntimeCampaignRebuild": True, "frozenC554And17Unmodified": True}
        text = json.dumps(receipt, indent=2, sort_keys=True)
        print(text, flush=True)
        if os.environ.get("GITHUB_STEP_SUMMARY"):
            with Path(os.environ["GITHUB_STEP_SUMMARY"]).open("a") as summary:
                summary.write("## Explicit private target: exact7419 " + action + "\n\n```json\n" + text + "\n```\n")
        if action == "recover" and os.environ.get("GITHUB_OUTPUT"):
            with Path(os.environ["GITHUB_OUTPUT"]).open("a") as output:
                output.write("manifest_digest=" + digest + "\n")
        return receipt
    finally:
        try:
            if logged_in:
                docker(["logout", "ghcr.io"])
        finally:
            shutil.rmtree(work)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("recover", "cold-pull"))
    parser.add_argument("--work-dir", type=Path, required=True)
    parser.add_argument("--expected-digest")
    args = parser.parse_args()
    os.umask(0o077)
    execute(args.action, args.work_dir, args.expected_digest)


if __name__ == "__main__":
    main()
