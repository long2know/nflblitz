#!/usr/bin/env python3
"""Closed promotion of existing public5cace to same-package latest; no game-blob upload or privacy change."""
import argparse
import base64
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, build_opener

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
DEPLOY = ROOT / "tools/source-player/deploy"
IMAGE = "ghcr.io/long2know/nflblitz"
API = "/users/long2know/packages/container/nflblitz"
REGISTRY = "https://ghcr.io/v2/long2know/nflblitz/manifests/"
DIGEST = "sha256:5cace3ee21a0a7f3ecf08e78661aa84b221a3c4c409b390cf6b50996b99b977c"
CONFIG = "sha256:7419b62b728945fa59e90590dfe4e7eeec9a0eed96a9b53daebc225b31fae6cb"
INVENTORY_SHA = "b2f69cb31e51e88841f70b33dc8a752fbb39985bdcc220aeffc9203307dd12b5"
MEDIA_TYPE = "application/vnd.docker.distribution.manifest.v2+json"
spec = importlib.util.spec_from_file_location("approved_exact_recovery", DEPLOY / "private_target_recovery.py")
identity = importlib.util.module_from_spec(spec)
spec.loader.exec_module(identity)
frozen, require = identity.frozen, identity.require


def pins():
    require(frozen.sha(DEPLOY / "private_target_recovery.py") ==
            "d031a16c6623c0985d2f25cac691cc1b1a358ebf79a036792f149291d9884e49",
            "Existing private recovery and its guards must remain untouched.")
    value = identity.load_pins()
    require(value["imageId"] == CONFIG and value["sourceManifestDigest"] == DIGEST,
            "Only the already-public exact verified manifest/config is authorized.")
    return value


def local_main_source(value):
    root = ROOT
    require(subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip() ==
            os.environ["GITHUB_SHA"], "Actual checkout/workflow main mismatch.")
    result = subprocess.run(["git", "-C", str(root), "merge-base", "--is-ancestor",
                             value["sourceRevision"], os.environ["GITHUB_SHA"]],
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    require(result.returncode == 0, "Accepted image source is not an ancestor of actual main.")
    for row in value["requiredRemoteMainFiles"]:
        require(frozen.sha(root / row["path"]) == row["sha256"],
                "Actual main147/161/17 input differs: " + row["path"])


def public_package(token):
    package = frozen.api(API, token)
    require(isinstance(package, dict) and package.get("visibility") == "public" and
            package.get("name") == "nflblitz" and package.get("package_type") == "container" and
            (package.get("owner") or {}).get("login") == "long2know" and
            (package.get("owner") or {}).get("type") == "User" and
            (package.get("repository") or {}).get("full_name") == "long2know/nflblitz",
            "Closed public promotion requires actual PUBLIC exact owner/name/type/repository; never change visibility.")
    print(json.dumps({"stage": "closed-public-package-check", "httpStatus": 200,
                      "owner": "long2know/User", "name": "nflblitz", "package_type": "container",
                      "visibility": "public", "repository": "long2know/nflblitz",
                      "endpoint": "https://api.github.com" + API}, sort_keys=True), flush=True)


def registry_token(job_token=None):
    scope = "repository:long2know/nflblitz:" + ("pull,push" if job_token else "pull")
    headers = {"Accept": "application/json"}
    if job_token:
        headers["Authorization"] = "Basic " + base64.b64encode(("long2know:" + job_token).encode()).decode()
    request = Request("https://ghcr.io/token?" + urlencode({"service": "ghcr.io", "scope": scope}), headers=headers)
    with build_opener(frozen.NoRedirect()).open(request, timeout=30) as response:
        require(response.status == 200, "Registry token request failed.")
        value = json.loads(response.read(65536))
    require(isinstance(value.get("token"), str) and
            re.fullmatch(r"[A-Za-z0-9._~+/\-]+={0,2}", value["token"]),
            "Invalid closed-registry token.")
    return value["token"]


def anonymous_manifest(reference, *, missing_ok=False):
    require(reference in ("latest", DIGEST), "No arbitrary public image/tag/target is authorized.")
    request = Request(REGISTRY + reference, headers={
        "Authorization": "Bearer " + registry_token(), "Accept": MEDIA_TYPE})
    try:
        with build_opener(frozen.NoRedirect()).open(request, timeout=30) as response:
            raw, actual = response.read(4 * 1024 * 1024), response.headers.get("Docker-Content-Digest")
            media = response.headers.get_content_type()
    except HTTPError as error:
        with error:
            status, body = error.code, json.loads(error.read(8192))
        if missing_ok and reference == "latest" and status == 404:
            require(body.get("errors") and all(row.get("code") == "MANIFEST_UNKNOWN" for row in body["errors"]),
                    "Unknown latest404 response; refuse overwrite.")
            return None
        raise ValueError("Anonymous manifest GET failed HTTP" + str(status)) from None
    require(actual == DIGEST and "sha256:" + hashlib.sha256(raw).hexdigest() == DIGEST and media == MEDIA_TYPE,
            "Foreign or changed public manifest/latest; STOP without overwriting.")
    manifest = json.loads(raw)
    require(manifest.get("schemaVersion") == 2 and manifest.get("config", {}).get("digest") == CONFIG and
            len(manifest.get("layers", [])) == 12 and "manifests" not in manifest,
            "Existing public source config/all12layers shape differs.")
    return raw


def verify_full_payload(value, docker, work):
    identity.verify_loaded(value, docker, work)
    container = docker(["create", "--pull=never", "--network", "none", "--read-only",
                        "--entrypoint", "/bin/false", value["sourceTag"]]).decode().strip()
    require(re.fullmatch(r"[0-9a-f]{64}", container), "Unexpected owned stopped-container identity.")
    try:
        inventory = work / "bundle-inventory.json"
        docker(["cp", container + ":/opt/nflblitz/bundle-inventory.json", str(inventory)])
        require(frozen.sha(inventory) == INVENTORY_SHA, "Exact7419 bundle inventory changed.")
        rows = json.loads(inventory.read_bytes())["files"]
        require(len(rows) == 1217, "Exactly all1217 qualified payload files are required.")
        site = work / "actual-payload"
        docker(["cp", container + ":/opt/nflblitz/www", str(site)])
        actual = {}
        for path in site.rglob("*"):
            require(not path.is_symlink() and (path.is_dir() or path.is_file()), "Unsafe recovered app entry.")
            if path.is_file():
                actual[path.relative_to(site).as_posix()] = {
                    "path": path.relative_to(site).as_posix(), "bytes": path.stat().st_size,
                    "sha256": frozen.sha(path)}
        require(actual == {row["path"]: row for row in rows}, "Actual all1217 payload bytes differ from accepted7419.")
        return inventory
    finally:
        docker(["rm", container])


def urllib_alias_put(raw, bearer):
    request = Request(REGISTRY + "latest", data=raw, method="PUT", headers={
        "Authorization": "Bearer " + bearer, "Content-Type": MEDIA_TYPE, "If-None-Match": "*"})
    with build_opener(frozen.NoRedirect()).open(request, timeout=60) as response:
        require(response.status in (200, 201, 202) and
                response.headers.get("Docker-Content-Digest", DIGEST) == DIGEST,
                "Registry did not accept the exact manifest-only alias.")


def curl_alias_put(raw, bearer, work):
    path = work / "existing-manifest.json"
    path.write_bytes(raw)
    # Authorization travels only on stdin, never argv, a file or a child environment.
    config = ('url = "' + REGISTRY + 'latest"\nrequest = "PUT"\n'
              'header = "Authorization: Bearer ' + bearer + '"\n'
              'header = "Content-Type: ' + MEDIA_TYPE + '"\n'
              'header = "If-None-Match: *"\n'
              'data-binary = "@' + str(path.resolve()) + '"\n'
              'output = "' + str((work / "registry-put-response.json").resolve()) + '"\n'
              'write-out = "%{http_code}"\nsilent\nshow-error\nmax-time = "60"\n')
    status = frozen.run(["curl", "--config", "-"], input=config.encode()).decode().strip()
    require(status in ("200", "201", "202"), "Exact manifest-only curl fallback failed HTTP" + status)


def promote_alias(raw, token, work):
    public_package(token)
    require(anonymous_manifest(DIGEST) == raw, "Fixed public source bytes changed before alias.")
    current = anonymous_manifest("latest", missing_ok=True)
    if current is not None:
        require(current == raw, "Foreign latest cannot be overwritten.")
        return "already-exact-idempotent"
    bearer = registry_token(token)
    public_package(token)
    race_latest = anonymous_manifest("latest", missing_ok=True)
    if race_latest is not None:
        require(race_latest == raw, "Foreign latest appeared before alias; STOP.")
        return "already-exact-after-race-recheck"
    try:
        urllib_alias_put(raw, bearer)
        method = "exact-registry-manifest-put"
    except (HTTPError, URLError, TimeoutError) as error:
        current = anonymous_manifest("latest", missing_ok=True)
        if current == raw:
            return "exact-put-converged-after-response-failure"
        if isinstance(error, HTTPError) and error.code in (401, 403, 409, 412):
            raise ValueError("Alias permission/race rejection HTTP" + str(error.code) + "; no permission bypass.") from None
        public_package(token)
        require(anonymous_manifest(DIGEST) == raw and anonymous_manifest("latest", missing_ok=True) is None,
                "Latest/source changed before the safe identical-byte fallback.")
        curl_alias_put(raw, bearer, work)
        method = "same-manifest-byte-curl-fallback"
    require(anonymous_manifest("latest") == raw, "Anonymous latest does not equal exact source manifest bytes.")
    public_package(token)
    return method


def run_standard_user(docker, work, inventory):
    name = "exact-public-coldpull-" + os.environ["GITHUB_RUN_ID"] + "-" + os.environ["GITHUB_RUN_ATTEMPT"]
    require(re.fullmatch(r"exact-public-coldpull-[0-9]+-[0-9]+", name), "Actual run-owned container name required.")
    data = work / "unused-app-data"
    data.mkdir(mode=0o700)
    marker = data / "DO-NOT-MODIFY.txt"
    marker.write_text("Unused owned mount; records remain browser-origin local.\n")
    original = frozen.sha(marker)
    probe = socket.socket()
    try:
        probe.bind(("127.0.0.1", 8920))
    finally:
        probe.close()
    created = False
    try:
        docker(["create", "--name", name, "--platform", "linux/amd64", "--pull", "never",
                "--user", "1000:1000", "--restart", "unless-stopped",
                "--env", "PUID=1000", "--env", "PGID=1000", "--env", "BASE_URL=/nflblitz",
                "--publish", "127.0.0.1:8920:8080/tcp",
                "--mount", "type=bind,src=" + str(data.resolve()) + ",dst=/app/data", IMAGE + ":latest"])
        created = True
        docker(["start", name])
        deadline = time.monotonic() + 100
        while True:
            actual = json.loads(docker(["inspect", name]))[0]
            health = actual["State"].get("Health", {}).get("Status")
            if health == "healthy":
                break
            require(actual["State"]["Running"] and health != "unhealthy" and time.monotonic() < deadline,
                    "Anonymous exact image did not become configured-health healthy.")
            time.sleep(2)
        host = actual["HostConfig"]
        require(actual["Config"]["User"] == "1000:1000" and not host["ReadonlyRootfs"] and not host["Privileged"] and
                not host.get("Tmpfs") and not any("unconfined" in x for x in host.get("SecurityOpt") or []),
                "Normal writable UID1000 runtime contract was replaced or relaxed.")
        kernel = docker(["exec", name, "/bin/sh", "-c",
                         "id -u; id -g; awk '/^CapEff:/ {print $2} /^Seccomp:/ {print $2}' /proc/1/status; "
                         "stat -c '%u:%a' /tmp/nflblitz-$(id -u)"]).decode().splitlines()
        require(kernel == ["1000", "1000", "0000000000000000", "2", "1000:700"],
                "Actual UID/GID/capabilities/default-seccomp/private runtime permissions differ.")
        frozen.run(["python3", "-B", str(DEPLOY / "test_deployment.py"), "ImageHttpTests", "-q"],
                   env=dict(os.environ, NFLBLITZ_TEST_URL="http://127.0.0.1:8920/nflblitz/",
                            NFLBLITZ_TEST_INVENTORY=str(inventory)))
        require(frozen.sha(marker) == original and [p.name for p in data.iterdir()] == ["DO-NOT-MODIFY.txt"],
                "The anonymous app modified the unused optional data mount.")
    finally:
        if created:
            docker(["rm", "--force", name])


def execute(action, directory):
    identity.context(os.environ)
    value = pins()
    local_main_source(value)
    token = os.environ.pop("GH_TOKEN", "")
    require(action in ("promote", "anonymous-check"), "Unknown closed operation.")
    require(bool(token) if action == "promote" else not token,
            "Promotion uses only the main job token; independent anonymous check must have no token.")
    require(not os.environ.get("DOCKER_AUTH_CONFIG"), "No inherited registry credentials may participate.")
    work = frozen.private_directory(directory)
    docker_env = dict(os.environ, DOCKER_CONFIG=str(work / "anonymous-docker"))

    def docker(command, **kwargs):
        require(command[0] not in ("login", "push", "build", "manifest"),
                "Closed operation permits no Docker login/build/blob upload/arbitrary manifest writer.")
        return frozen.run(["docker"] + command, env=docker_env, **kwargs)

    try:
        if action == "promote":
            public_package(token)
        raw = anonymous_manifest(DIGEST)
        if action == "anonymous-check":
            require(anonymous_manifest("latest") == raw, "Anonymous latest must be the exact original manifest.")
            previous = subprocess.run(["docker", "image", "inspect", IMAGE + ":latest"], env=docker_env,
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            require(previous.returncode != 0, "Independent runner already has latest; cold pull not established.")
            reference = IMAGE + ":latest"
        else:
            anonymous_manifest("latest", missing_ok=True)
            reference = IMAGE + "@" + DIGEST
        docker(["pull", "--platform", "linux/amd64", reference])
        docker(["tag", reference, value["sourceTag"]])
        inventory = verify_full_payload(value, docker, work)
        method = promote_alias(raw, token, work) if action == "promote" else "fresh-anonymous-latest-pull"
        if action == "anonymous-check":
            run_standard_user(docker, work, inventory)
            require(anonymous_manifest("latest") == raw, "Anonymous latest changed during runtime verification.")
            config = work / "anonymous-docker/config.json"
            credentials = json.loads(config.read_bytes()) if config.exists() else {}
            require(not credentials.get("auths") and not credentials.get("credsStore") and
                    not credentials.get("credHelpers"), "Registry credentials appeared in anonymous job.")
        receipt = {"schema": "closed-public-existing7419-" + action + "/v1", "target": IMAGE + ":latest",
                   "manifest_digest": DIGEST, "image_id": CONFIG, "manifestBytesUnchanged": True,
                   "all12ConfigLayerPinsAndAll1217PayloadFilesVerified": True, "method": method,
                   "packageVisibility": "public", "anonymousAccessible": True,
                   "actualCompiledHead": value["actualCompiledHead"], "sourceRevision": value["sourceRevision"],
                   "actualWorkflowMain": os.environ["GITHUB_SHA"], "buildInfoSha256": value["buildInfoSha256"],
                   "sourceDigest": value["sourceDigest"], "runtimeProofSha256": value["runtimeProofSha256"],
                   "finalQualitySha256": value["finalQualitySha256"],
                   "noNewGameBlobUploadVisibilityMutationOrPrivateLaneChange": True,
                   "normalUID1000PrefixHealthFullHttpPassed": action == "anonymous-check",
                   "dockerPullCredentials": "none", "independentAnonymousJob": action == "anonymous-check",
                   "promotionAuthentication": "main-job-token-manifest-only" if action == "promote" else "none"}
        text = json.dumps(receipt, indent=2, sort_keys=True)
        print(text, flush=True)
        if os.environ.get("GITHUB_STEP_SUMMARY"):
            with Path(os.environ["GITHUB_STEP_SUMMARY"]).open("a") as summary:
                summary.write("## Exact existing public image " + action + "\n\n```json\n" + text + "\n```\n")
        return receipt
    finally:
        shutil.rmtree(work)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("promote", "anonymous-check"))
    parser.add_argument("--work-dir", type=Path, required=True)
    args = parser.parse_args()
    os.umask(0o077)
    execute(args.action, args.work_dir)


if __name__ == "__main__":
    main()
