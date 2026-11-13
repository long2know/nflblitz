#!/usr/bin/env python3
"""Publish only the verified-loader overlay on exact7419; preserve actual Core/WASM/data and prior latest until ready."""
import argparse
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
from urllib.error import HTTPError, URLError
from urllib.request import Request, build_opener

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
DEPLOY = ROOT / "tools/source-player/deploy"
spec = importlib.util.spec_from_file_location("closed_base_identity", HERE / "public_existing_latest.py")
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)
frozen, require = base.frozen, base.require
spec = importlib.util.spec_from_file_location("static_refresh", ROOT / "tools/source-player/refresh_static_bundle.py")
refresh = importlib.util.module_from_spec(spec)
spec.loader.exec_module(refresh)
spec = importlib.util.spec_from_file_location("updated_package", DEPLOY / "package.py")
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)
IMAGE = "ghcr.io/long2know/nflblitz"


def source_guard():
    pins = base.pins()
    require(subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip() ==
            os.environ["GITHUB_SHA"], "Actual static source/workflow checkout mismatch.")
    require(subprocess.run(["git", "merge-base", "--is-ancestor", pins["sourceRevision"],
                            os.environ["GITHUB_SHA"]], cwd=ROOT).returncode == 0, "Accepted base is not merged into main.")
    require(subprocess.run(["git", "diff", "--quiet", "HEAD", "--"], cwd=ROOT).returncode == 0,
            "Release bytes must be the actual committed main tree, not working-tree replacements.")
    allowed = {"tools/source-player/wwwroot/main.js", "tools/source-player/build.py",
               "tools/source-player/deploy/package.py"}
    for row in pins["requiredRemoteMainFiles"]:
        if row["path"] not in allowed:
            require(frozen.sha(ROOT / row["path"]) == row["sha256"],
                    "Cache release must not change C#/native/input/records/project/runtime source: " + row["path"])
    return pins


def manifest(reference, expected_config=None, *, missing=False):
    require(reference == "latest" or re.fullmatch(r"sha256:[0-9a-f]{64}|cache-sha256-[0-9a-f]{64}", reference),
            "Unknown cache-release reference.")
    request = Request(base.REGISTRY + reference, headers={
        "Authorization": "Bearer " + base.registry_token(), "Accept": base.MEDIA_TYPE})
    try:
        with build_opener(frozen.NoRedirect()).open(request, timeout=30) as response:
            raw, digest = response.read(4 * 1024 * 1024), response.headers.get("Docker-Content-Digest")
            media_type = response.headers.get_content_type()
    except HTTPError as error:
        with error:
            status, body = error.code, json.loads(error.read(8192))
        if missing and status == 404:
            require(body.get("errors") and all(row.get("code") == "MANIFEST_UNKNOWN" for row in body["errors"]),
                    "Unknown cache tag absence.")
            return None
        raise ValueError("Cache manifest read failed HTTP" + str(status)) from None
    require(re.fullmatch(r"sha256:[0-9a-f]{64}", digest or "") and
            "sha256:" + hashlib.sha256(raw).hexdigest() == digest, "Actual cache manifest digest differs.")
    value = json.loads(raw)
    require(media_type == base.MEDIA_TYPE and value.get("schemaVersion") == 2 and
            "manifests" not in value and value.get("layers") and
            (not expected_config or value.get("config", {}).get("digest") == expected_config),
            "Actual cache image config/platform manifest differs.")
    return {"raw": raw, "digest": digest, "config": value["config"]["digest"], "layers": value["layers"]}


def latest_guard(candidate_digest=None):
    actual = manifest("latest", missing=True)
    require(actual is None or actual["digest"] in (base.DIGEST, candidate_digest),
            "Foreign latest cannot be replaced by this closed cache release.")
    return actual


def candidate_manifest(reference, image_id):
    value = manifest(reference, image_id)
    original = manifest(base.DIGEST, base.CONFIG)
    require(len(original["layers"]) == 12 and len(value["layers"]) == 14 and
            value["layers"][:12] == original["layers"],
            "Published cache image must preserve all12 original compressed layers and add only two static layers.")
    return value


def alias_headers(current):
    return {"If-Match": '"' + current["digest"] + '"'} if current else {"If-None-Match": "*"}


def alias_put(candidate, bearer, current, work, *, curl=False):
    headers = {"Authorization": "Bearer " + bearer, "Content-Type": base.MEDIA_TYPE, **alias_headers(current)}
    if not curl:
        request = Request(base.REGISTRY + "latest", data=candidate["raw"], method="PUT", headers=headers)
        with build_opener(frozen.NoRedirect()).open(request, timeout=60) as response:
            require(response.status in (200, 201, 202) and
                    response.headers.get("Docker-Content-Digest", candidate["digest"]) == candidate["digest"],
                    "Exact new cache manifest promotion failed.")
        return
    path = work / "verified-cache-manifest.json"
    path.write_bytes(candidate["raw"])
    config = ('url = "' + base.REGISTRY + 'latest"\nrequest = "PUT"\n' +
              "".join('header = "' + key + ': ' + value.replace('"', '\\"') + '"\n'
                      for key, value in headers.items()) +
              'data-binary = "@' + str(path.resolve()) + '"\n'
              'output = "' + str((work / "registry-put-response.json").resolve()) + '"\n'
              'write-out = "%{http_code}"\nsilent\nshow-error\nmax-time = "60"\n')
    status = frozen.run(["curl", "--config", "-"], input=config.encode()).decode().strip()
    require(status in ("200", "201", "202"), "Identical-byte cache alias fallback failed HTTP" + status)


def promote_candidate(candidate, token, work):
    base.public_package(token)
    require(candidate_manifest(candidate["digest"], candidate["config"]) == candidate,
            "Verified immutable cache manifest changed before promotion.")
    current = latest_guard(candidate["digest"])
    if current and current["digest"] == candidate["digest"]:
        return "already-exact-idempotent"
    bearer = base.registry_token(token)
    base.public_package(token)
    require(latest_guard(candidate["digest"]) == current, "Latest changed during cache candidate promotion.")
    try:
        alias_put(candidate, bearer, current, work)
        method = "exact-new-cache-manifest-put"
    except (HTTPError, URLError, TimeoutError) as error:
        if isinstance(error, HTTPError) and error.code in (401, 403, 409, 412):
            raise ValueError("Cache alias permission/race rejection HTTP" + str(error.code) +
                             "; no permission bypass.") from None
        after = latest_guard(candidate["digest"])
        if after and after["digest"] == candidate["digest"]:
            return "exact-cache-put-converged-after-response-failure"
        base.public_package(token)
        require(after == current and candidate_manifest(candidate["digest"], candidate["config"]) == candidate,
                "Latest/source changed before the identical-byte cache alias fallback.")
        require(latest_guard(candidate["digest"]) == current, "Latest raced before the safe cache alias fallback.")
        alias_put(candidate, bearer, current, work, curl=True)
        method = "identical-new-cache-manifest-curl-fallback"
    require(candidate_manifest("latest", candidate["config"]) == candidate,
            "Anonymous new latest bytes differ from the verified immutable cache manifest.")
    return method


def canonical_config(value):
    result = dict(value)
    # Legacy config.Image is a builder-parent hint omitted by OCI inspect; config SHA/rootfs identify the image.
    result.pop("Image", None)
    for key, default in {
        "AttachStderr": False, "AttachStdin": False, "AttachStdout": False,
        "Domainname": "", "Hostname": "", "OpenStdin": False,
        "StdinOnce": False, "Tty": False, "Volumes": None,
    }.items():
        if result.get(key, default) == default:
            result.pop(key, None)
    return result


def verify_image(docker, tag, image_id, build_sha, source_digest, site, inventory_path):
    actual = json.loads(docker(["image", "inspect", tag]))[0]
    require(actual["RootFS"]["Layers"][:12] == base.pins()["rootfsDiffIds"] and
            len(actual["RootFS"]["Layers"]) == 14, "Only the two static overlay layers may extend exact7419.")
    values = {"source_tag": tag, "source_revision": os.environ["GITHUB_SHA"],
              "image_id": image_id, "build_sha256": build_sha, "source_digest": source_digest,
              "data_manifest_sha256": "51e94caffcf5875e024206eb069dfd905ee13e134b09e7f5020ff68349461e5d"}
    frozen.image_config({"os": actual["Os"], "architecture": actual["Architecture"], "config": actual["Config"]}, values)
    labels = actual["Config"]["Labels"]
    require(labels.get("io.nflblitz.compiled-source-commit") == "6ad57cbd7969f3e0fe2473cebe6d076f6d0c7189" and
            labels.get("io.nflblitz.static-source-commit") == os.environ["GITHUB_SHA"] and
            labels.get("io.nflblitz.build-info-sha256") == build_sha and
            labels.get("io.nflblitz.source-digest") == source_digest,
            "Actual unchanged Core compilation or new static provenance differs.")
    raw = docker(["image", "save", tag])
    with tarfile.open(fileobj=io.BytesIO(raw), mode="r:*") as stream:
        records = json.load(stream.extractfile("manifest.json"))
        require(len(records) == 1, "Exactly one actual image must be verified.")
        config = stream.extractfile(records[0]["Config"]).read()
        require("sha256:" + hashlib.sha256(config).hexdigest() == image_id, "Actual original config ID changed.")
        cfg = json.loads(config)
        require(cfg.get("rootfs", {}).get("diff_ids") == actual["RootFS"]["Layers"] and
                canonical_config(cfg.get("config", {})) == canonical_config(actual["Config"]) and
                len(records[0]["Layers"]) == 14,
                "Actual complete exported config/all14 layer records differ.")
        for path, digest in zip(records[0]["Layers"], cfg["rootfs"]["diff_ids"]):
            with stream.extractfile(path) as layer:
                require("sha256:" + hashlib.file_digest(layer, "sha256").hexdigest() == digest,
                        "Actual static image layer changed.")
    build, rows = package.inspect_bundle(site, build_sha, source_digest)
    require(len(rows) == 1218 and build["execution"]["nativeRuntimeSha256"] == refresh.BASE_NATIVE_SHA and
            build["execution"]["compiledAotReuse"]["originalBuildInfoSha256"] == refresh.BASE_BUILD_SHA and
            build["execution"]["compiledAotReuse"]["staticSourceRevision"] == os.environ["GITHUB_SHA"] and
            build["verifiedAssetCache"]["schema"] == "persistent-verified-content-cache/v1",
            "Actual unchanged Core/WASM provenance is missing.")
    require(rows == json.loads(inventory_path.read_bytes())["files"], "Actual complete new image inventory differs.")


def actual_user_cache(docker, image, inventory, site, work):
    name = "cache-image-" + os.environ["GITHUB_RUN_ID"] + "-" + os.environ["GITHUB_RUN_ATTEMPT"]
    created = False
    try:
        docker(["create", "--name", name, "--platform", "linux/amd64", "--pull", "never",
                "--user", "1000:1000", "--env", "PUID=1000", "--env", "PGID=1000", "--env", "BASE_URL=/nflblitz",
                "--publish", "127.0.0.1:8920:8080/tcp", image])
        created = True
        docker(["start", name])
        until = time.monotonic() + 100
        while True:
            actual = json.loads(docker(["inspect", name]))[0]
            health = actual["State"].get("Health", {}).get("Status")
            if health == "healthy":
                break
            require(actual["State"]["Running"] and health != "unhealthy" and time.monotonic() < until,
                    "Actual cache image did not become configured-health healthy.")
            time.sleep(2)
        host = actual["HostConfig"]
        require(actual["Config"]["User"] == "1000:1000" and not host["ReadonlyRootfs"] and not host["Privileged"] and
                not host.get("Tmpfs") and not any("unconfined" in x for x in host.get("SecurityOpt") or []),
                "Actual cache image changed the normal writable/least-privilege user contract.")
        env = dict(os.environ, NFLBLITZ_TEST_URL="http://127.0.0.1:8920/nflblitz/",
                   NFLBLITZ_TEST_INVENTORY=str(inventory), NFLBLITZ_CACHE_MANIFEST=str(site / "data/manifest.json"))
        frozen.run(["python3", "-B", str(DEPLOY / "test_deployment.py"), "ImageHttpTests", "-q"], env=env)
        browser = work / "cache-browser.json"
        checked = subprocess.run(
            ["node", str(ROOT / "tools/source-player/verified-cache.browser.mjs"), env["NFLBLITZ_TEST_URL"],
             str(browser), str(work / "chrome"), "--image-smoke"], env=env,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=600)
        print(checked.stdout.decode(errors="replace")[-8000:], flush=True)
        require(checked.returncode == 0, "Actual normal-sandbox candidate/image cache browser failed.")
        receipt = json.loads(browser.read_bytes())
        require(receipt["status"] == "passed" and receipt["normalSandbox"] and receipt["cleanup"]["chromeExited"],
                "Actual normal-sandbox image cache proof did not pass.")
        receipt["actualBrowserReceiptSha256"] = frozen.sha(browser)
        return receipt
    finally:
        if created:
            docker(["rm", "--force", name])


def extract_new_site(docker, tag, work):
    container = docker(["create", "--pull=never", "--network", "none", "--read-only", "--entrypoint", "/bin/false", tag]).decode().strip()
    try:
        site, inventory = work / "image-site", work / "image-inventory.json"
        docker(["cp", container + ":/opt/nflblitz/www", str(site)])
        docker(["cp", container + ":/opt/nflblitz/bundle-inventory.json", str(inventory)])
        return site, inventory
    finally:
        docker(["rm", container])


def publish(work, docker, token):
    base.public_package(token)
    latest_guard()
    pins = base.pins()
    source = IMAGE + "@" + base.DIGEST
    docker(["pull", "--platform", "linux/amd64", source])
    docker(["tag", source, pins["sourceTag"]])
    old_work = work / "base-proof"
    old_work.mkdir()
    base.verify_full_payload(pins, docker, old_work)
    info, static, inventory = refresh.refresh((old_work / "actual-payload/build-info.json").read_bytes(),
                                              (old_work / "bundle-inventory.json").read_bytes(),
                                              ROOT, os.environ["GITHUB_SHA"])
    context = work / "static-context"
    context.mkdir()
    for name, raw in static.items():
        (context / name).write_bytes(raw)
        (context / name).chmod(0o444)
    (context / "bundle-inventory.json").write_bytes(refresh.encoded(inventory))
    (context / "bundle-inventory.json").chmod(0o444)
    version = "verified-cache-" + os.environ["GITHUB_SHA"][:12]
    tag = "nflblitz-original:" + version
    build_sha, source_digest = refresh.sha(static["build-info.json"]), info["sourceRootDigest"]
    (context / "Dockerfile").write_text(
        "FROM " + source + "\n"
        'LABEL org.opencontainers.image.source="https://github.com/long2know/nflblitz" '
        'org.opencontainers.image.revision="' + os.environ["GITHUB_SHA"] + '" '
        'org.opencontainers.image.version="' + version + '" '
        'io.nflblitz.build-info-sha256="' + build_sha + '" '
        'io.nflblitz.source-digest="' + source_digest + '" '
        'io.nflblitz.static-source-commit="' + os.environ["GITHUB_SHA"] + '"\n'
        "COPY --chown=101:101 main.js verified-assets.js build-info.json /opt/nflblitz/www/\n"
        "COPY --chown=101:101 bundle-inventory.json /opt/nflblitz/bundle-inventory.json\n")
    docker(["build", "--network", "none", "--pull=false", "--tag", tag, str(context)])
    actual = json.loads(docker(["image", "inspect", tag]))[0]
    image_id = actual["Id"]
    # Containerd may expose an OCI descriptor; the exported config below remains authoritative.
    exported = docker(["image", "save", tag])
    with tarfile.open(fileobj=io.BytesIO(exported), mode="r:*") as stream:
        saved = json.load(stream.extractfile("manifest.json"))[0]
        image_id = "sha256:" + hashlib.sha256(stream.extractfile(saved["Config"]).read()).hexdigest()
    site, inv = extract_new_site(docker, tag, work)
    verify_image(docker, tag, image_id, build_sha, source_digest, site, inv)
    candidate_browser = actual_user_cache(docker, tag, inv, site, work)
    base.public_package(token)
    latest_guard()
    immutable = IMAGE + ":cache-sha256-" + image_id[7:]
    existing = manifest(immutable.rsplit(":", 1)[1], image_id, missing=True)
    if existing:
        candidate_manifest(existing["digest"], image_id)
    docker(["login", "ghcr.io", "--username", "long2know", "--password-stdin"], input=(token + "\n").encode())
    docker(["tag", tag, immutable])
    docker(["push", immutable])
    candidate = candidate_manifest(immutable.rsplit(":", 1)[1], image_id)
    method = promote_candidate(candidate, token, work)
    final = candidate_manifest("latest", image_id)
    require(final["digest"] == candidate["digest"], "New cache latest/immutable digests differ.")
    base.public_package(token)
    receipt = {"schema": "verified-static-cache-image-published/v1", "target": IMAGE + ":latest",
               "imageId": image_id, "manifestDigest": candidate["digest"], "buildInfoSha256": build_sha,
               "sourceDigest": source_digest, "sourceRevision": os.environ["GITHUB_SHA"],
               "actualCompiledHead": pins["actualCompiledHead"], "nativeWasmUnchanged": refresh.BASE_NATIVE_SHA,
               "dataManifestUnchanged": pins["dataManifestSha256"], "all1218PayloadsVerified": True,
               "candidateActualNormalUid1000CacheBrowserPassed": True,
               "actualBrowserReceiptSha256": candidate_browser["actualBrowserReceiptSha256"],
               "cacheStages": candidate_browser["stages"], "noAotOrCoreRebuild": True, "promotionMethod": method}
    return receipt


def anonymous(work, docker):
    image_id = os.environ.get("CACHE_EXPECTED_IMAGE_ID", "")
    digest = os.environ.get("CACHE_EXPECTED_MANIFEST", "")
    build_sha = os.environ.get("CACHE_EXPECTED_BUILD", "")
    source_digest = os.environ.get("CACHE_EXPECTED_SOURCE", "")
    require(re.fullmatch(r"sha256:[0-9a-f]{64}", image_id) and re.fullmatch(r"sha256:[0-9a-f]{64}", digest) and
            re.fullmatch(r"[0-9a-f]{64}", build_sha) and re.fullmatch(r"[0-9a-f]{64}", source_digest),
            "Actual new cache-image outputs are required.")
    require(candidate_manifest("latest", image_id)["digest"] == digest, "Anonymous latest differs from new cache image.")
    require(not docker(["image", "ls", "--quiet", IMAGE + ":latest"]).strip(),
            "Independent anonymous runner already has latest; a cold pull is required.")
    tag = "nflblitz-original:verified-cache-" + os.environ["GITHUB_SHA"][:12]
    docker(["pull", "--platform", "linux/amd64", IMAGE + ":latest"])
    docker(["tag", IMAGE + ":latest", tag])
    site, inventory = extract_new_site(docker, tag, work)
    verify_image(docker, tag, image_id, build_sha, source_digest, site, inventory)
    browser = actual_user_cache(docker, IMAGE + ":latest", inventory, site, work)
    require(candidate_manifest("latest", image_id)["digest"] == digest, "Anonymous latest changed during cache proof.")
    config = work / "docker/config.json"
    credentials = json.loads(config.read_bytes()) if config.exists() else {}
    require(not credentials.get("auths") and not credentials.get("credsStore") and not credentials.get("credHelpers"),
            "Registry credentials appeared in the fresh anonymous cache consumer.")
    return {"schema": "verified-static-cache-image-anonymous-consumer/v1", "target": IMAGE + ":latest",
            "imageId": image_id, "manifestDigest": digest, "buildInfoSha256": build_sha,
            "sourceDigest": source_digest, "cacheStages": browser["stages"], "normalSandbox": True,
            "actualBrowserReceiptSha256": browser["actualBrowserReceiptSha256"],
            "normalUid1000ConfiguredHealthPrefixAnd1218HttpPassed": True, "anonymousPull": True,
            "noRegistryCredentials": True, "allCoreNativeDataHashesUnchanged": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("publish", "anonymous"))
    parser.add_argument("--work-dir", type=Path, required=True)
    args = parser.parse_args()
    base.identity.context(os.environ)
    source_guard()
    token = os.environ.pop("GH_TOKEN", "")
    require(bool(token) if args.action == "publish" else not token,
            "Only publication uses the repository job token; consumer must be anonymous.")
    require(not os.environ.get("DOCKER_AUTH_CONFIG") and not os.environ.get("GITHUB_TOKEN"),
            "No inherited registry credentials may participate.")
    os.umask(0o077)
    work = frozen.private_directory(args.work_dir)
    env = dict(os.environ, DOCKER_CONFIG=str(work / "docker"))

    def docker(command, **kwargs):
        require(args.action == "publish" or command[0] not in ("login", "push", "build", "manifest"),
                "The independent cache consumer cannot authenticate or publish.")
        if command[0] == "push":
            require(args.action == "publish" and
                    re.fullmatch(re.escape(IMAGE) + r":cache-sha256-[0-9a-f]{64}", command[1]),
                    "Only the verified cache-image immutable may receive image blobs.")
        return frozen.run(["docker"] + command, env=env, **kwargs)

    try:
        receipt = publish(work, docker, token) if args.action == "publish" else anonymous(work, docker)
        text = json.dumps(receipt, indent=2, sort_keys=True)
        print(text, flush=True)
        with Path(os.environ["GITHUB_STEP_SUMMARY"]).open("a") as summary:
            summary.write("## Actual verified static-cache image " + args.action + "\n\n```json\n" + text + "\n```\n")
        if args.action == "publish":
            with Path(os.environ["GITHUB_OUTPUT"]).open("a") as output:
                for key in ("imageId", "manifestDigest", "buildInfoSha256", "sourceDigest"):
                    output.write(key + "=" + receipt[key] + "\n")
    finally:
        if args.action == "publish" and token:
            try: docker(["logout", "ghcr.io"])
            except ValueError: pass
        shutil.rmtree(work)


if __name__ == "__main__":
    main()
