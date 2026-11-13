#!/usr/bin/env python3
"""Closed typed view-controls overlay, preserving the delivered verified-cache/Core/data generation."""
import argparse
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import selectors
import shutil
import subprocess
import tarfile
import time
from urllib.error import HTTPError, URLError

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
spec = importlib.util.spec_from_file_location("accepted_cache_publication", HERE / "verified_cache_image.py")
cache = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cache)
require, frozen = cache.require, cache.frozen
IMAGE = "ghcr.io/long2know/nflblitz"
PARENT_DIGEST = "sha256:4f6ec842f7a154dcc36cb6b1646a00961745becfdd7231c5e529a71d6c22b389"
PARENT_CONFIG = "sha256:439b2e1992a10b9e38fcae5bd642afe7afc6c2e50d7c538767df12e2d73a325a"
PARENT_REVISION = "685cf44ed4e9a2984058df156386f41e933087fa"
PARENT_BUILD = "6208299f3bffa7f2ba376d20b240ace9da03ca278386d97c2644fe3661171d1a"
PARENT_INVENTORY = "48012ef4f3a700f5645c97ef3d8ccc1213ab345f19ec4107c063303afd9c4f80"
UI_SOURCE_DIGEST = "a38e0c01ffcad96dce1fd8e511cc78edccac38aa8695243817e4ded2a4464e99"
STATIC_NAMES = ("main.js", "verified-assets.js", "index.html", "view-controls.js")
STATIC_PATHS = ["tools/source-player/wwwroot/" + name for name in STATIC_NAMES]
MODULES = {
    "main.js": "b5d86825f33f531fe11d3f02e1c02b573a742b460f98fd8f93bc60a75b6aed43",
    "verified-assets.js": "16ee24d472576efdb9f59d8c046675ee212729270bcafdc1e4d8429d073e7ffb",
    "index.html": "8b4b9797b2bc3c9421eaba0257bf261eee2894f96be58b6d494d600ca37cad2b",
    "view-controls.js": "dad9efaa95bd732c91de31cc6418ef3f47ddae6b48d48865c5bcd7bac0846250",
}
SOURCE_PINS = {
    ".github/scripts/verified_cache_image.py": "315978372ae35b32722cab907a0d411b8114562770909c8cd094bc02e9ee61c4",
    "tools/source-player/test_static_bundle_refresh.py": "a2d1a0cbe4d3055bc33d3ad0144dd58be53aabda722460e24651100a16b41a3c",
    "tools/source-player/deploy/test_deployment.py": "0c77e40eb95ad2705b1ce0c279f4106abd08f4ecf39dc0a5dd059ae837d16820",
    "tools/source-player/view-controls.browser.mjs": "9cea7d296faa3f4138fb222ceac76e8bb463fdb5630528765519787e5696bcfd",
    "tools/source-player/verified-cache.browser.mjs": "ad91774bb210968273fcabd41c12b464049d4b336b9f113339dc39879f04fd3e",
    "tools/source-player/verified-assets.test.mjs": "f8abbf5da5250f628279a536cc75bfca2f7b537307dd709b92c6556eaa8a1d7f",
    "tools/source-player/build.py": "61dd394da19180e1b05c6fde9ce97b95ed7c5d192f3823da7380e8895a110a68",
    "tools/source-player/deploy/package.py": "9750b455dfe4476cba9d4a39799b25f81d19e9281e40d48ee98c0953337817af",
    "tools/source-player/refresh_static_bundle.py": "09c619d9cfc68b06dcbf912614d2973336cce4b4b7025e33da72ba9cd0887da8",
    **{"tools/source-player/wwwroot/" + name: digest for name, digest in MODULES.items()},
}


def source_guard():
    require(re.fullmatch(r"[0-9a-f]{40}", os.environ.get("GITHUB_SHA", "")),
            "The actual UI main commit is required, never uncommitted candidate metadata.")
    require(subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip() ==
            os.environ["GITHUB_SHA"], "Actual UI checkout/workflow main mismatch.")
    require(subprocess.run(["git", "merge-base", "--is-ancestor", PARENT_REVISION,
                            os.environ["GITHUB_SHA"]], cwd=ROOT).returncode == 0,
            "Delivered685 cache parent must be an ancestor of the actual UI main.")
    require(subprocess.run(["git", "diff", "--quiet", "HEAD", "--"], cwd=ROOT).returncode == 0,
            "Only committed UI main bytes may be published.")
    allowed = {"tools/source-player/wwwroot/main.js", "tools/source-player/wwwroot/index.html",
               "tools/source-player/build.py", "tools/source-player/deploy/package.py",
               "tools/source-player/deploy/test_deployment.py"}
    for row in cache.base.pins()["requiredRemoteMainFiles"]:
        if row["path"] not in allowed:
            require(frozen.sha(ROOT / row["path"]) == row["sha256"],
                    "UI overlay changed compiled/input/records/runtime/private source: " + row["path"])
    for name, digest in SOURCE_PINS.items():
        require(frozen.sha(ROOT / name) == digest, "Released UI/cache source binding changed: " + name)


def latest_guard(candidate=None):
    actual = cache.manifest("latest", missing=True)
    require(actual is not None and (
        actual["digest"] == PARENT_DIGEST and actual["config"] == PARENT_CONFIG or
        candidate is not None and actual["digest"] == candidate["digest"] and actual["config"] == candidate["config"]),
        "UI publication requires exact delivered4f6/config439 or the same verified candidate; missing/foreign latest STOP.")
    return actual


def validate_build(info, revision, static=None):
    require(re.fullmatch(r"[0-9a-f]{40}", revision or "") and
            info.get("schema") == "blitz-source-player-build/v1" and info.get("sourceRootDigest") == UI_SOURCE_DIGEST,
            "Exact frozen typed UI source digest and actual committed revision are required.")
    reuse = info.get("execution", {}).get("compiledAotReuse", {})
    require(reuse.get("schema") == "unchanged-core-wasm-loader-static-refresh/v1" and
            reuse.get("originalBuildInfoSha256") == cache.refresh.BASE_BUILD_SHA and
            reuse.get("originalCompiledSourceDigest") == cache.refresh.BASE_SOURCE_DIGEST and
            reuse.get("originalCompiledHead") == "6ad57cbd7969f3e0fe2473cebe6d076f6d0c7189" and
            reuse.get("staticSourceRevision") == revision and reuse.get("staticSourceUncommittedCandidate") is False and
            reuse.get("changedStaticSources") == STATIC_PATHS and
            reuse.get("projectChange") == "None; all C#/compile/trimmer/project bytes identical" and
            reuse.get("nativeRuntimeSha256") == cache.refresh.BASE_NATIVE_SHA and
            reuse.get("applicationObjects") == info["execution"].get("applicationObjects"),
            "UI typed opt-in must retain exact ordered four static sources and genuine original compiler provenance.")
    sources = {row["path"]: row["sha256"] for row in info.get("sources", [])}
    require(cache.refresh.sha(json.dumps(info.get("sources", []), sort_keys=True).encode()) == UI_SOURCE_DIGEST and
            all(sources.get("tools/source-player/wwwroot/" + name) == digest for name, digest in MODULES.items()),
            "The view module source declaration/index/cache module pins differ.")
    require(info.get("verifiedAssetCache") == {
        "schema": "persistent-verified-content-cache/v1", "key": "normalized-application-base-and-sha256",
        "verifyCacheHitsAndDownloads": True, "fallback": "explicit-warning-and-verified-network-only",
    } and info.get("verifiedRuntimeResources", {}).get("schema") == "source-verified-runtime-resources/v1" and
            len(info["verifiedRuntimeResources"]["files"]) == 48,
            "UI overlay must retain all48 verified runtime resources and both-hit/download cache integrity.")
    if static is not None:
        require(set(static) == set(STATIC_NAMES) | {"build-info.json"} and
                all(cache.refresh.sha(static[name]) == digest for name, digest in MODULES.items()) and
                static["build-info.json"] == cache.refresh.encoded(info),
                "Only the released exact five static files may be copied into the UI image.")


def refresh_ui(build_bytes, inventory_bytes, revision):
    info, static, inventory = cache.refresh.refresh(build_bytes, inventory_bytes, ROOT, revision, view_controls=True)
    validate_build(info, revision, static)
    require(len(inventory["files"]) == inventory["bundleFiles"] == 1219 and
            inventory["sourceBaseCommit"] == revision and inventory["compiledSourceDigest"] == UI_SOURCE_DIGEST and
            inventory["buildInfoSha256"] == cache.refresh.sha(static["build-info.json"]) and
            inventory["actualCompiledSourceDigest"] == cache.refresh.BASE_SOURCE_DIGEST and
            inventory["actualCompiledHead"] == "6ad57cbd7969f3e0fe2473cebe6d076f6d0c7189",
            "Typed UI inventory is not the exact1219/current-static/original-compiled product.")
    return info, static, inventory


def validate_cache_browser(receipt):
    require(receipt.get("schema") == "persistent-verified-cache-actual-browser/v1" and
            receipt.get("status") == "passed" and receipt.get("normalSandbox") is True and
            len(receipt.get("stages", [])) == 2, "Startup diagnostics are not actual UI image cache acceptance.")
    cold, warm = receipt["stages"]
    require(cold["assetBodyBytes"] > 250000000 and warm["assetRequests"] == warm["assetBodyBytes"] ==
            warm["binaryRequests"] == warm["cache"]["downloadedBytes"] == 0 and
            warm["buildInfoRequests"] == warm["manifestRequests"] == 1 and warm["allWireBytes"] < 3000000,
            "The UI image did not retain verified warm-cache downloads and fresh metadata.")


def verify_archive(docker, tag, image_id, actual):
    with tarfile.open(fileobj=io.BytesIO(docker(["image", "save", tag])), mode="r:*") as stream:
        records = json.load(stream.extractfile("manifest.json"))
        require(len(records) == 1, "Exactly one UI/cache image may be exported.")
        record = records[0]
        raw = stream.extractfile(record["Config"]).read()
        require("sha256:" + hashlib.sha256(raw).hexdigest() == image_id, "Actual UI/cache config bytes differ.")
        cfg = json.loads(raw)
        cache.verify_exported_metadata(cfg, actual, record["Layers"])
        for name, digest in zip(record["Layers"], cfg["rootfs"]["diff_ids"]):
            with stream.extractfile(name) as layer:
                require("sha256:" + hashlib.file_digest(layer, "sha256").hexdigest() == digest,
                        "Actual UI/cache exported layer bytes differ.")


def verify_prior(docker, work):
    cache.candidate_manifest(PARENT_DIGEST, PARENT_CONFIG)
    reference = IMAGE + "@" + PARENT_DIGEST
    tag = "nflblitz-original:verified-cache-" + PARENT_REVISION[:12]
    docker(["pull", "--platform", "linux/amd64", reference])
    docker(["tag", reference, tag])
    actual = json.loads(docker(["image", "inspect", tag]))[0]
    values = {"source_tag": tag, "source_revision": PARENT_REVISION}
    frozen.image_config({"os": actual["Os"], "architecture": actual["Architecture"], "config": actual["Config"]}, values)
    require(actual["RootFS"]["Layers"][:12] == cache.base.pins()["rootfsDiffIds"] and
            len(actual["RootFS"]["Layers"]) == 14 and
            actual["Config"]["Labels"].get("io.nflblitz.build-info-sha256") == PARENT_BUILD,
            "The fixed delivered UI transition parent changed.")
    verify_archive(docker, tag, PARENT_CONFIG, actual)
    container = docker(["create", "--pull=never", "--network", "none", "--read-only",
                        "--entrypoint", "/bin/false", tag]).decode().strip()
    require(re.fullmatch(r"[0-9a-f]{64}", container), "Unexpected owned parent-container identity.")
    try:
        for name, image_path, expected in (
            ("ui-parent-build.json", "/opt/nflblitz/www/build-info.json", PARENT_BUILD),
            ("ui-parent-inventory.json", "/opt/nflblitz/bundle-inventory.json", PARENT_INVENTORY),
        ):
            target = work / name
            docker(["cp", container + ":" + image_path, str(target)])
            require(frozen.sha(target) == expected, "Exact accepted685 parent metadata changed: " + name)
        info = json.loads((work / "ui-parent-build.json").read_bytes())
        inventory = json.loads((work / "ui-parent-inventory.json").read_bytes())
        require(len(inventory["files"]) == 1218 and
                info["execution"]["nativeRuntimeSha256"] == cache.refresh.BASE_NATIVE_SHA and
                info["execution"]["compiledAotReuse"]["staticSourceRevision"] == PARENT_REVISION,
                "Actual accepted cache parent no longer describes the qualified1218/685 runtime.")
        return info
    finally:
        docker(["rm", container])


def verify_image(docker, tag, image_id, build_sha, site, inventory_path):
    actual = json.loads(docker(["image", "inspect", tag]))[0]
    require(actual["RootFS"]["Layers"][:12] == cache.base.pins()["rootfsDiffIds"] and
            len(actual["RootFS"]["Layers"]) == 14, "Only two UI static layers may extend the immutable original12.")
    frozen.image_config({"os": actual["Os"], "architecture": actual["Architecture"], "config": actual["Config"]},
                        {"source_tag": tag, "source_revision": os.environ["GITHUB_SHA"]})
    labels = actual["Config"]["Labels"]
    require(labels.get("io.nflblitz.compiled-source-commit") == "6ad57cbd7969f3e0fe2473cebe6d076f6d0c7189" and
            labels.get("io.nflblitz.static-source-commit") == os.environ["GITHUB_SHA"] and
            labels.get("io.nflblitz.build-info-sha256") == build_sha and
            labels.get("io.nflblitz.source-digest") == UI_SOURCE_DIGEST, "Actual UI static/compiler labels differ.")
    verify_archive(docker, tag, image_id, actual)
    info, rows = cache.package.inspect_bundle(site, build_sha, UI_SOURCE_DIGEST)
    validate_build(info, os.environ["GITHUB_SHA"])
    require(len(rows) == 1219 and rows == json.loads(inventory_path.read_bytes())["files"],
            "Actual complete UI1219 inventory differs.")


def ui_browser(url, work, *, startup_only=False):
    require(os.getuid() != 0, "UI image acceptance requires normal non-root Chrome sandboxing.")
    profile, evidence = work / "ui-profile", work / "ui-evidence"
    temporary = os.path.relpath(profile / "tmp", ROOT)
    require(not temporary.startswith("../") and not os.path.isabs(temporary) and
            len((temporary + "/com.google.Chrome.XXXXXX/SingletonSocket").encode()) < 108,
            "Owned project-relative UI Chrome path exceeds the socket/cwd boundary.")
    profile.mkdir(mode=0o700)
    child, endpoint, selector = None, None, selectors.DefaultSelector()
    try:
        for name in ("data", "cache", "tmp", "config"):
            (profile / name).mkdir(mode=0o700)
        args = ["/opt/google/chrome/chrome", "--headless=new", "--enable-automation",
                "--remote-debugging-address=127.0.0.1", "--remote-debugging-port=0",
                "--user-data-dir=" + str(profile / "data"), "--disk-cache-dir=" + str(profile / "cache"),
                "--no-first-run", "--no-default-browser-check", "--disable-background-networking",
                "--disable-component-update", "--disable-sync", "--metrics-recording-only", "--no-pings",
                "--window-size=1280,900", "about:blank"]
        env = dict(os.environ, HOME=str(profile), TMPDIR=temporary, XDG_RUNTIME_DIR=str(profile / "tmp"),
                   XDG_CACHE_HOME=str(profile / "cache"), XDG_CONFIG_HOME=str(profile / "config"),
                   XDG_DATA_HOME=str(profile / "config"))
        child = subprocess.Popen(args, cwd=ROOT, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
        selector.register(child.stderr, selectors.EVENT_READ)
        tail, deadline = "", time.monotonic() + 30
        while endpoint is None:
            require(child.poll() is None and time.monotonic() < deadline, "Normal UI Chrome startup failed: " + tail)
            for key, _ in selector.select(.2):
                tail = (tail + os.read(key.fileobj.fileno(), 4096).decode(errors="replace"))[-6000:]
                match = re.search(r"DevTools listening on (ws://127\.0\.0\.1:\d+/devtools/browser/[^\s]+)", tail)
                if match:
                    endpoint = match.group(1)
        if startup_only:
            return {"schema": "ui-publisher-chrome-startup-diagnostic/v1", "startupOnly": True,
                    "normalSandbox": True, "temporaryDirectory": temporary, "cacheAcceptance": False}
        caller = ROOT / "tools/source-player/view-controls.browser.mjs"
        require(caller.is_file(), "The frozen UI browser caller is missing.")
        checked = subprocess.run(["node", str(caller), "--url", url, "--cdp-port",
                                  endpoint.split(":")[2].split("/")[0], "--evidence", str(evidence)],
                                 cwd=ROOT, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=600)
        print(checked.stdout.decode(errors="replace")[-8000:], flush=True)
        require(checked.returncode == 0, "Actual UI image browser acceptance failed.")
        raw = (evidence / "evidence.json").read_bytes()
        result = json.loads(raw)
        screenshots = sorted(evidence.glob("*.png"))
        require(result.get("schema") == "source-player-game-view-browser/v1" and result.get("accepted") is True and
                len(result["layouts"]) >= 8 and len(result["screenshots"]) == len(screenshots) == 5 and
                {row["station"] for row in result["input"]} == {0, 1, 2, 3},
                "Actual UI layouts/real screenshots/all4 raw input proof is incomplete.")
        return {"schema": "ui-publisher-actual-image-browser/v1", "accepted": True, "normalSandbox": True,
                "receiptSha256": hashlib.sha256(raw).hexdigest(), "layouts": len(result["layouts"]),
                "screenshots": [{"path": p.name, "sha256": frozen.sha(p)} for p in screenshots],
                "allFourCabinetsObserved": True}
    finally:
        selector.close()
        if child is not None and child.poll() is None and endpoint:
            stop = ("import {Cdp} from " + json.dumps((ROOT / "tests/browser/lib.mjs").as_uri()) +
                    "; const b=await Cdp.connect(process.argv[1]);await b.send('Browser.close');b.close();")
            try:
                done = subprocess.run(["node", "--input-type=module", "-e", stop, endpoint], cwd=ROOT,
                                      stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=10)
            except subprocess.TimeoutExpired:
                done = None
            if done is None or done.returncode:
                print("UI Chrome protocol close failed; terminating only its owned PID.", flush=True)
        if child is not None:
            try: child.wait(5)
            except subprocess.TimeoutExpired:
                child.terminate()
                try: child.wait(5)
                except subprocess.TimeoutExpired: child.kill(); child.wait()
            if child.stderr:
                child.stderr.close()
        shutil.rmtree(profile)


def actual_ui_runtime(docker, image, work):
    name = "view-image-" + os.environ["GITHUB_RUN_ID"] + "-" + os.environ["GITHUB_RUN_ATTEMPT"]
    require(re.fullmatch(r"view-image-[0-9]+-[0-9]+", name), "Actual run-owned UI container name is required.")
    created = False
    try:
        docker(["create", "--name", name, "--platform", "linux/amd64", "--pull", "never",
                "--user", "1000:1000", "--env", "PUID=1000", "--env", "PGID=1000", "--env", "BASE_URL=/nflblitz",
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
                    "Actual UI image did not become configured-health healthy.")
            time.sleep(2)
        host = actual["HostConfig"]
        require(actual["Config"]["User"] == "1000:1000" and not host["ReadonlyRootfs"] and not host["Privileged"] and
                not host.get("Tmpfs") and not any("unconfined" in value for value in host.get("SecurityOpt") or []),
                "Actual UI image changed the normal writable/UID1000/sandbox runtime contract.")
        return ui_browser("http://127.0.0.1:8920/nflblitz/", work)
    finally:
        if created:
            docker(["rm", "--force", name])


def promote(candidate, token, work):
    cache.base.public_package(token)
    require(cache.candidate_manifest(candidate["digest"], candidate["config"]) == candidate,
            "Verified immutable UI manifest changed.")
    current = latest_guard(candidate)
    if current["digest"] == candidate["digest"]:
        return "already-exact-ui-idempotent"
    bearer = cache.base.registry_token(token)
    require(latest_guard(candidate) == current, "UI prior latest raced immediately before promotion.")
    try:
        cache.alias_put(candidate, bearer, current, work)
        method = "exact-ui-manifest-put"
    except (HTTPError, URLError, TimeoutError) as error:
        if isinstance(error, HTTPError) and error.code in (401, 403, 409, 412):
            raise ValueError("UI alias permission/race rejection; no bypass.") from None
        after = latest_guard(candidate)
        if after == candidate:
            return "exact-ui-put-converged-after-response-failure"
        cache.base.public_package(token)
        require(after == current and cache.candidate_manifest(candidate["digest"], candidate["config"]) == candidate and
                latest_guard(candidate) == current, "UI source/prior changed before identical-byte fallback.")
        cache.alias_put(candidate, bearer, current, work, curl=True)
        method = "identical-ui-manifest-curl-fallback"
    require(cache.candidate_manifest("latest", candidate["config"]) == candidate, "Anonymous latest differs from UI image.")
    return method


def publish(work, docker, token):
    cache.base.public_package(token)
    latest_guard()
    parent = verify_prior(docker, work)
    pins = cache.base.pins()
    source = IMAGE + "@" + cache.base.DIGEST
    docker(["pull", "--platform", "linux/amd64", source])
    docker(["tag", source, pins["sourceTag"]])
    old_work = work / "original-proof"
    old_work.mkdir()
    cache.base.verify_full_payload(pins, docker, old_work)
    info, static, inventory = refresh_ui((old_work / "actual-payload/build-info.json").read_bytes(),
        (old_work / "bundle-inventory.json").read_bytes(), os.environ["GITHUB_SHA"])
    require(info["execution"]["applicationObjects"] == parent["execution"]["applicationObjects"],
            "Typed UI original1217 refresh/compiler lineage differs.")
    context = work / "ui-context"
    context.mkdir()
    for name, raw in static.items():
        (context / name).write_bytes(raw)
        (context / name).chmod(0o444)
    (context / "bundle-inventory.json").write_bytes(cache.refresh.encoded(inventory))
    (context / "bundle-inventory.json").chmod(0o444)
    version = "verified-view-" + os.environ["GITHUB_SHA"][:12]
    tag = "nflblitz-original:" + version
    build_sha = cache.refresh.sha(static["build-info.json"])
    (context / "Dockerfile").write_text("FROM " + source + "\n"
        'LABEL org.opencontainers.image.source="https://github.com/long2know/nflblitz" '
        'org.opencontainers.image.revision="' + os.environ["GITHUB_SHA"] + '" '
        'org.opencontainers.image.version="' + version + '" '
        'io.nflblitz.build-info-sha256="' + build_sha + '" '
        'io.nflblitz.source-digest="' + UI_SOURCE_DIGEST + '" '
        'io.nflblitz.static-source-commit="' + os.environ["GITHUB_SHA"] + '"\n'
        "COPY --chown=101:101 main.js verified-assets.js index.html view-controls.js build-info.json /opt/nflblitz/www/\n"
        "COPY --chown=101:101 bundle-inventory.json /opt/nflblitz/bundle-inventory.json\n")
    docker(["build", "--network", "none", "--pull=false", "--tag", tag, str(context)])
    with tarfile.open(fileobj=io.BytesIO(docker(["image", "save", tag])), mode="r:*") as stream:
        saved = json.load(stream.extractfile("manifest.json"))[0]
        image_id = "sha256:" + hashlib.sha256(stream.extractfile(saved["Config"]).read()).hexdigest()
    site, inv = cache.extract_new_site(docker, tag, work)
    verify_image(docker, tag, image_id, build_sha, site, inv)
    cold_warm = cache.actual_user_cache(docker, tag, inv, site, work)
    validate_cache_browser(cold_warm)
    ui = actual_ui_runtime(docker, tag, work)
    require(ui["accepted"], "Actual UI candidate was not accepted.")
    cache.base.public_package(token)
    latest_guard()
    immutable = IMAGE + ":cache-sha256-" + image_id[7:]
    existing = cache.manifest(immutable.rsplit(":", 1)[1], image_id, missing=True)
    if existing:
        cache.candidate_manifest(existing["digest"], image_id)
    docker(["login", "ghcr.io", "--username", "long2know", "--password-stdin"], input=(token + "\n").encode())
    docker(["tag", tag, immutable])
    docker(["push", immutable])
    candidate = cache.candidate_manifest(immutable.rsplit(":", 1)[1], image_id)
    method = promote(candidate, token, work)
    cache.base.public_package(token)
    return {"schema": "verified-view-controls-image-published/v1", "target": IMAGE + ":latest",
            "imageId": image_id, "manifestDigest": candidate["digest"], "buildInfoSha256": build_sha,
            "sourceDigest": UI_SOURCE_DIGEST, "sourceRevision": os.environ["GITHUB_SHA"],
            "priorManifest": PARENT_DIGEST, "priorConfig": PARENT_CONFIG,
            "actualCompiledHead": "6ad57cbd7969f3e0fe2473cebe6d076f6d0c7189",
            "nativeWasmUnchanged": cache.refresh.BASE_NATIVE_SHA,
            "all1219PayloadsVerified": True, "normalUid1000HealthPrefixAndFullHttpPassed": True,
            "cacheStages": cold_warm["stages"], "actualCacheBrowserReceiptSha256": cold_warm["actualBrowserReceiptSha256"],
            "uiBrowser": ui, "noAotOrCoreDataRebuild": True, "promotionMethod": method}


def anonymous(work, docker):
    image_id, digest, build_sha = [os.environ.get(name, "") for name in
        ("VIEW_EXPECTED_IMAGE_ID", "VIEW_EXPECTED_MANIFEST", "VIEW_EXPECTED_BUILD")]
    require(all(re.fullmatch(r"sha256:[0-9a-f]{64}", value) for value in (image_id, digest)) and
            re.fullmatch(r"[0-9a-f]{64}", build_sha), "Actual new UI image outputs are required.")
    require(cache.candidate_manifest("latest", image_id)["digest"] == digest, "Anonymous UI latest identity differs.")
    require(not docker(["image", "ls", "--quiet", IMAGE + ":latest"]).strip(), "Fresh anonymous UI cold pull required.")
    verify_prior(docker, work)
    docker(["pull", "--platform", "linux/amd64", IMAGE + ":latest"])
    tag = "nflblitz-original:verified-view-" + os.environ["GITHUB_SHA"][:12]
    docker(["tag", IMAGE + ":latest", tag])
    site, inventory = cache.extract_new_site(docker, tag, work)
    verify_image(docker, tag, image_id, build_sha, site, inventory)
    cold_warm = cache.actual_user_cache(docker, IMAGE + ":latest", inventory, site, work)
    validate_cache_browser(cold_warm)
    ui = actual_ui_runtime(docker, IMAGE + ":latest", work)
    require(ui["accepted"] and cache.candidate_manifest("latest", image_id)["digest"] == digest,
            "Anonymous UI runtime/latest changed during acceptance.")
    config = work / "docker/config.json"
    credentials = json.loads(config.read_bytes()) if config.exists() else {}
    require(not credentials.get("auths") and not credentials.get("credsStore") and not credentials.get("credHelpers"),
            "Credentials appeared in the anonymous UI job.")
    return {"schema": "verified-view-controls-image-anonymous-consumer/v1", "target": IMAGE + ":latest",
            "imageId": image_id, "manifestDigest": digest, "buildInfoSha256": build_sha, "sourceDigest": UI_SOURCE_DIGEST,
            "all1219PayloadsVerified": True, "normalUid1000HealthPrefixAndFullHttpPassed": True,
            "cacheStages": cold_warm["stages"], "actualCacheBrowserReceiptSha256": cold_warm["actualBrowserReceiptSha256"],
            "uiBrowser": ui, "freshAnonymousPull": True, "noRegistryCredentials": True,
            "allOriginalCoreNativeDataHashesUnchanged": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("publish", "anonymous"))
    parser.add_argument("--work-dir", required=True, type=Path)
    args = parser.parse_args()
    cache.base.identity.context(os.environ)
    source_guard()
    token = os.environ.pop("GH_TOKEN", "")
    require(bool(token) if args.action == "publish" else not token,
            "Only the UI main publisher uses its repository job token; consumer must be anonymous.")
    require(not os.environ.get("DOCKER_AUTH_CONFIG") and not os.environ.get("GITHUB_TOKEN"),
            "No inherited registry credentials may participate.")
    os.umask(0o077)
    work = frozen.private_directory(args.work_dir)
    env = dict(os.environ, DOCKER_CONFIG=str(work / "docker"))

    def docker(command, **kwargs):
        require(args.action == "publish" or command[0] not in ("login", "push", "build", "manifest"),
                "Anonymous UI consumer cannot authenticate or publish.")
        if command[0] == "push":
            require(args.action == "publish" and
                    re.fullmatch(re.escape(IMAGE) + r":cache-sha256-[0-9a-f]{64}", command[1]),
                    "Only the closed verified UI immutable may receive image blobs.")
        return frozen.run(["docker"] + command, env=env, **kwargs)

    try:
        receipt = publish(work, docker, token) if args.action == "publish" else anonymous(work, docker)
        text = json.dumps(receipt, indent=2, sort_keys=True)
        print(text, flush=True)
        with Path(os.environ["GITHUB_STEP_SUMMARY"]).open("a") as summary:
            summary.write("## Actual typed UI image " + args.action + "\n\n```json\n" + text + "\n```\n")
        if args.action == "publish":
            with Path(os.environ["GITHUB_OUTPUT"]).open("a") as output:
                for key in ("imageId", "manifestDigest", "buildInfoSha256"):
                    output.write(key + "=" + receipt[key] + "\n")
    finally:
        if args.action == "publish" and token:
            try: docker(["logout", "ghcr.io"])
            except ValueError: pass
        shutil.rmtree(work)


if __name__ == "__main__":
    main()
