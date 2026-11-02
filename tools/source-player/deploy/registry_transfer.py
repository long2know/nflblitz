#!/usr/bin/env python3
"""Transfer an accepted Docker archive encrypted; publish it only inside Actions."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import secrets
import shutil
import subprocess
import tarfile
from urllib.error import HTTPError
from urllib.request import HTTPRedirectHandler, Request, build_opener


REPOSITORY = "long2know/nflblitz"
IMAGE = "ghcr.io/" + REPOSITORY
ASSET = "image.tar.gz.enc"
INPUTS = {
    "release_tag", "secret_name", "cipher_sha256", "archive_sha256", "image_id",
    "source_tag", "build_sha256", "source_digest", "data_manifest_sha256", "source_revision",
}
PROFILE = "original-teams-complete-original303-prematch"
FACTORY = "SourceC2Composition.CreateCompleteOriginalPrematchSimulation"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def validate_inputs(values):
    require(set(values) == INPUTS, "Exactly the ten publication inputs are required.")
    require(all(isinstance(value, str) for value in values.values()), "Inputs must be strings.")
    for field in ("cipher_sha256", "archive_sha256", "build_sha256", "source_digest",
                  "data_manifest_sha256"):
        require(re.fullmatch(r"[0-9a-f]{64}", values[field]), "Invalid " + field + ".")
    require(re.fullmatch(r"sha256:[0-9a-f]{64}", values["image_id"]), "Invalid image_id.")
    require(re.fullmatch(r"[0-9a-f]{40}", values["source_revision"]), "Invalid source_revision.")
    require(re.fullmatch(r"nflblitz-original:[A-Za-z0-9][A-Za-z0-9_.-]{0,100}", values["source_tag"]),
            "Invalid source_tag; only an explicit local original-image tag is accepted.")
    require(re.fullmatch(r"container-transfer-[0-9]{8}-[0-9a-f]{32}", values["release_tag"]),
            "Invalid release_tag.")
    datetime.strptime(values["release_tag"].split("-")[2], "%Y%m%d")
    expected_secret = "CONTAINER_TRANSFER_" + values["release_tag"].rsplit("-", 1)[1].upper()
    require(values["secret_name"] == expected_secret, "secret_name must belong to this transfer.")
    return values


def publication_context(env):
    require(env.get("GITHUB_ACTIONS") == "true" and
            env.get("GITHUB_EVENT_NAME") == "workflow_dispatch" and
            env.get("GITHUB_REPOSITORY") == REPOSITORY and
            env.get("GITHUB_REF") == "refs/heads/main",
            "Publishing requires a manual dispatch from long2know/nflblitz main inside Actions.")
    return validate_inputs({field: env.get("TRANSFER_" + field.upper()) for field in INPUTS})


def run(command, *, input=None, env=None):
    result = subprocess.run(command, input=input, env=env, stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, check=False, timeout=1800)
    require(result.returncode == 0,
            "Command failed: " + " ".join(command[:3]) + " (exit " + str(result.returncode) + ").")
    return result.stdout


def private_directory(path):
    path = Path(path)
    parent = path.parent.resolve(strict=True)
    require(parent.stat().st_uid == os.geteuid() and parent.stat().st_mode & 0o022 == 0,
            "The transfer parent must be owner-controlled and not group/world writable.")
    path = parent / path.name
    path.mkdir(mode=0o700)
    path.chmod(0o700)
    require(not path.is_symlink() and path.stat().st_uid == os.geteuid() and
            path.stat().st_mode & 0o777 == 0o700,
            "A new owner-only POSIX directory is required (use your private home, not a shared mount).")
    return path


def crypt(source, destination, key, *, decrypt=False):
    require(isinstance(key, str) and re.fullmatch(r"[A-Za-z0-9_-]{64}", key),
            "A generated 384-bit transfer passphrase is required.")
    require(not destination.exists() and not destination.is_symlink(), "Refusing an existing crypto output.")
    # The passphrase goes only through stdin, never argv, a file, or child environment.
    run(["openssl", "enc", "-aes-256-cbc", "-pbkdf2", "-iter", "600000", "-md", "sha256",
         "-d" if decrypt else "-e", "-salt", "-in", str(source), "-out", str(destination),
         "-pass", "stdin"], input=(key + "\n").encode())
    destination.chmod(0o600)


def image_config(config, values):
    require(config.get("os") == "linux" and config.get("architecture") == "amd64",
            "Only the accepted linux/amd64 image is supported.")
    runtime = config.get("config", {})
    require(runtime.get("User") == "101:101" and runtime.get("WorkingDir") == "/opt/nflblitz" and
            runtime.get("Entrypoint") == ["nginx"] and runtime.get("Cmd") == ["-g", "daemon off;"],
            "Image must retain the accepted non-root static-server runtime.")
    labels = runtime.get("Labels", {})
    require(labels.get("io.nflblitz.runtime") == "portable-csharp-browser-wasm" and
            labels.get("org.opencontainers.image.revision") == values["source_revision"] and
            labels.get("org.opencontainers.image.version") == values["source_tag"].split(":", 1)[1],
            "Image runtime/source labels differ from the accepted image.")


def inspect_archive(archive, values):
    validate_inputs(values)
    with tarfile.open(archive, "r:*") as stream:
        members = stream.getmembers()
        names = {member.name: member for member in members}
        require(len(names) == len(members), "Duplicate Docker archive entries.")
        for member in members:
            parts = member.name.split("/")
            require(not PurePosixPath(member.name).is_absolute() and
                    all(part not in ("", ".", "..") for part in parts) and
                    (member.isfile() or member.isdir()), "Unsafe Docker archive entry.")

        def read(name):
            member = names.get(name)
            require(member is not None and member.isfile() and member.size <= 4 * 1024 * 1024,
                    "Missing or oversized Docker archive metadata.")
            return stream.extractfile(member).read()

        manifest = json.loads(read("manifest.json"))
        require(isinstance(manifest, list) and len(manifest) == 1, "Exactly one saved image is required.")
        record = manifest[0]
        require(record.get("RepoTags") == [values["source_tag"]], "Saved source tag differs.")
        config_bytes = read(record["Config"])
        require("sha256:" + hashlib.sha256(config_bytes).hexdigest() == values["image_id"],
                "Saved image config identity differs from image_id.")
        config = json.loads(config_bytes)
        image_config(config, values)
        layers = record.get("Layers", [])
        require(layers and len(layers) == len(set(layers)) and
                len(layers) == len(config.get("rootfs", {}).get("diff_ids", [])) and
                all(name in names and names[name].isfile() for name in layers),
                "Docker archive layer inventory differs from the image config.")
        return config


def verify_transfer(ciphertext, archive, values, key):
    validate_inputs(values)
    require(sha(ciphertext) == values["cipher_sha256"], "Ciphertext SHA256 mismatch; refusing decryption.")
    try:
        crypt(ciphertext, archive, key, decrypt=True)
        require(sha(archive) == values["archive_sha256"], "Archive SHA256 mismatch; refusing Docker load.")
        return inspect_archive(archive, values)
    except Exception:
        archive.unlink(missing_ok=True)
        raise


def verify_payload(values, directory, docker):
    container = docker(["create", "--network", "none", "--read-only", "--entrypoint", "/bin/false",
                        values["image_id"]]).decode().strip()
    require(re.fullmatch(r"[0-9a-f]{64}", container), "Unexpected stopped-container identity.")
    try:
        for name in ("build-info.json", "data/manifest.json"):
            destination = directory / Path(name).name
            docker(["cp", container + ":/opt/nflblitz/www/" + name, str(destination)])
            require(destination.is_file() and not destination.is_symlink(), "Missing image receipt.")
        build_path, data_path = directory / "build-info.json", directory / "manifest.json"
        require(sha(build_path) == values["build_sha256"], "Embedded build-info SHA256 differs.")
        require(sha(data_path) == values["data_manifest_sha256"], "Embedded data-manifest SHA256 differs.")
        build, data = json.loads(build_path.read_bytes()), json.loads(data_path.read_bytes())
        require(build.get("schema") == "blitz-source-player-build/v1" and
                build.get("runtimeIdentifier") == "browser-wasm" and
                build.get("nativeUnityBuild") is False and
                build.get("profile") == data.get("profile") == PROFILE and
                build.get("factory") == data.get("factory") == FACTORY,
                "Embedded receipts do not describe the accepted portable original game.")
        digest = hashlib.sha256(json.dumps(build["sources"], sort_keys=True).encode()).hexdigest()
        require(build.get("sourceRootDigest") == digest == values["source_digest"] and
                build.get("dataManifestSha256") == values["data_manifest_sha256"] and
                len(data["files"]) == 963 and sum(entry["bytes"] for entry in data["files"]) == 244325118,
                "Embedded compiled-source/original-payload inventory differs.")
    finally:
        docker(["rm", container])


def receive_and_load(ciphertext, archive, values, key, docker):
    try:
        verify_transfer(ciphertext, archive, values, key)
        docker(["load", "--input", str(archive)])
        loaded = json.loads(docker(["image", "inspect", values["source_tag"]]))
        require(len(loaded) == 1 and loaded[0].get("Id") == values["image_id"],
                "Loaded image identity differs from the accepted image.")
        image_config({"os": loaded[0]["Os"], "architecture": loaded[0]["Architecture"],
                      "config": loaded[0]["Config"]}, values)
        verify_payload(values, archive.parent, docker)
    finally:
        archive.unlink(missing_ok=True)


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, new_url):
        return None


def api(path, token=None):
    require(path.startswith(("/users/long2know/packages/", "/repos/" + REPOSITORY + "/releases/tags/")),
            "Unexpected API resource.")
    headers = {"Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28"}
    if token:
        headers["Authorization"] = "Bearer " + token
    request = Request("https://api.github.com" + path, headers=headers)
    try:
        with build_opener(NoRedirect()).open(request, timeout=30) as response:
            require(response.status == 200, "Unexpected GitHub API status.")
            return json.loads(response.read(4 * 1024 * 1024))
    except HTTPError as error:
        if error.code == 404:
            return None
        raise ValueError("GitHub metadata check failed (HTTP " + str(error.code) + "); refusing publication.") from None


def private_package(token, *, allow_new=False):
    path = "/users/long2know/packages/container/nflblitz"
    package = api(path, token)
    if package is None:
        require(allow_new and api(path) is None, "Package visibility is unknown; refusing publication.")
        return
    require(package.get("visibility") == "private" and package.get("package_type") == "container" and
            package.get("name") == "nflblitz" and package.get("owner", {}).get("login") == "long2know" and
            package.get("repository", {}).get("full_name") == REPOSITORY,
            "Existing package must be private and linked to long2know/nflblitz; refusing publication.")


def remote_image(reference, expected_id, docker):
    descriptor = json.loads(docker(["buildx", "imagetools", "inspect", reference,
                                    "--format", "{{json .Manifest}}"]))
    digest = descriptor.get("digest")
    require(isinstance(digest, str) and re.fullmatch(r"sha256:[0-9a-f]{64}", digest),
            "Registry did not return an immutable manifest digest.")
    manifest = json.loads(docker(["buildx", "imagetools", "inspect", IMAGE + "@" + digest, "--raw"]))
    require(manifest.get("schemaVersion") == 2 and manifest.get("config", {}).get("digest") == expected_id
            and manifest.get("layers") and "manifests" not in manifest,
            "Remote manifest does not link to the exact accepted image config.")
    return digest


def publish(directory):
    values = publication_context(os.environ)
    key, token = os.environ.pop("REGISTRY_TRANSFER_KEY", ""), os.environ.pop("GH_TOKEN", "")
    require(key and token, "The transfer secret and job-scoped GITHUB_TOKEN are required.")
    work = private_directory(directory)
    docker_env = dict(os.environ, DOCKER_CONFIG=str(work / "docker"))
    logged_in = False

    def docker(command, **kwargs):
        return run(["docker"] + command, env=docker_env, **kwargs)

    try:
        private_package(token, allow_new=True)
        release = api("/repos/" + REPOSITORY + "/releases/tags/" + values["release_tag"], token)
        require(release is not None and release.get("tag_name") == values["release_tag"] and
                release.get("prerelease") is True and release.get("draft") is False and
                [asset.get("name") for asset in release.get("assets", [])] == [ASSET],
                "Transfer must be the named ciphertext-only prerelease.")
        run(["gh", "release", "download", values["release_tag"], "--repo", REPOSITORY,
             "--pattern", ASSET, "--dir", str(work)], env=dict(os.environ, GH_TOKEN=token))
        receive_and_load(work / ASSET, work / "image.tar.gz", values, key, docker)
        docker(["login", "ghcr.io", "--username", "long2know", "--password-stdin"],
               input=(token + "\n").encode())
        logged_in = True
        private_package(token, allow_new=True)
        immutable = IMAGE + ":sha256-" + values["image_id"].split(":")[1]
        docker(["tag", values["image_id"], immutable])
        docker(["push", immutable])
        digest = remote_image(immutable, values["image_id"], docker)
        private_package(token)
        docker(["tag", values["image_id"], IMAGE + ":latest"])
        docker(["push", IMAGE + ":latest"])
        require(remote_image(IMAGE + ":latest", values["image_id"], docker) == digest,
                "Latest and immutable registry manifest digests differ.")
        private_package(token)
        receipt = dict(values, immutable_tag=immutable, latest=IMAGE + ":latest",
                       manifest_digest=digest, package_visibility="private")
        summary = os.environ.get("GITHUB_STEP_SUMMARY")
        if summary:
            with Path(summary).open("a") as stream:
                stream.write("## Exact accepted image published (private)\n\n```json\n" +
                             json.dumps(receipt, indent=2, sort_keys=True) + "\n```\n")
        print(json.dumps(receipt, indent=2, sort_keys=True))
    finally:
        try:
            if logged_in:
                docker(["logout", "ghcr.io"])
        finally:
            shutil.rmtree(work)


def prepare(args):
    require(args.qa_approved, "Independent browser QA approval is required before preparing a transfer.")
    suffix = secrets.token_hex(16)
    values = {field: getattr(args, field) for field in INPUTS -
              {"release_tag", "secret_name", "cipher_sha256"}}
    values.update(release_tag="container-transfer-" + datetime.now(timezone.utc).strftime("%Y%m%d") + "-" + suffix,
                  secret_name="CONTAINER_TRANSFER_" + suffix.upper(), cipher_sha256="0" * 64)
    validate_inputs(values)
    require(args.archive.is_file() and not args.archive.is_symlink(), "A regular saved-image archive is required.")
    require(sha(args.archive) == values["archive_sha256"], "Accepted archive SHA256 differs.")
    inspect_archive(args.archive, values)
    work = private_directory(args.output_dir)
    key = secrets.token_urlsafe(48)
    secret_created = release_created = False
    try:
        crypt(args.archive, work / ASSET, key)
        values["cipher_sha256"] = sha(work / ASSET)
        existing = json.loads(run(["gh", "secret", "list", "--repo", REPOSITORY, "--app", "actions", "--json", "name"]))
        require(values["secret_name"] not in {entry["name"] for entry in existing}, "Refusing to overwrite an existing secret.")
        main_sha = run(["gh", "api", "repos/" + REPOSITORY + "/git/ref/heads/main", "--jq", ".object.sha"]).decode().strip()
        require(re.fullmatch(r"[0-9a-f]{40}", main_sha), "Cannot pin the transfer release to main.")
        run(["gh", "api", "repos/" + REPOSITORY + "/contents/.github/workflows/publish-container.yml?ref=" + main_sha,
             "--jq", ".sha"])
        receipt = {"schema": "nflblitz-registry-transfer/v1", "repository": REPOSITORY,
                   "workflow": "publish-container.yml", "ref": "main", "release_target": main_sha,
                   "inputs": values, "state": "not-published-transfer-preparation-in-progress"}
        (work / "receipt.json").write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n")
        run(["gh", "secret", "set", values["secret_name"], "--repo", REPOSITORY, "--app", "actions"],
            input=(key + "\n").encode())
        secret_created = True
        run(["gh", "release", "create", values["release_tag"], "--repo", REPOSITORY, "--target", main_sha,
             "--prerelease", "--latest=false", "--title", "Encrypted accepted-image transfer",
             "--notes", "Temporary ciphertext only. No plaintext assets or decryption key are attached."])
        release_created = True
        run(["gh", "release", "upload", values["release_tag"], str(work / ASSET), "--repo", REPOSITORY])
        receipt["state"] = "encrypted-transfer-ready-not-published"
        (work / "workflow-inputs.json").write_text(json.dumps(values, indent=2, sort_keys=True) + "\n")
        (work / "receipt.json").write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n")
        print(json.dumps(receipt, indent=2, sort_keys=True))
    except Exception:
        try:
            if release_created:
                run(["gh", "release", "delete", values["release_tag"], "--repo", REPOSITORY, "--yes", "--cleanup-tag"])
        finally:
            if secret_created:
                run(["gh", "secret", "delete", values["secret_name"], "--repo", REPOSITORY, "--app", "actions"])
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    local = commands.add_parser("prepare", help="After QA and merge only: encrypt, set a unique Actions secret, upload ciphertext.")
    local.add_argument("--archive", required=True, type=Path)
    local.add_argument("--output-dir", required=True, type=Path, help="New owner-only POSIX home directory; parent must exist.")
    local.add_argument("--qa-approved", action="store_true", help="Owner attests independent exact-image browser QA passed.")
    for field in sorted(INPUTS - {"release_tag", "secret_name", "cipher_sha256"}):
        local.add_argument("--" + field.replace("_", "-"), required=True)
    remote = commands.add_parser("publish", help="Workflow only: verify, load, publish privately, verify remote identities.")
    remote.add_argument("--work-dir", required=True, type=Path)
    args = parser.parse_args()
    previous_umask = os.umask(0o077)
    try:
        if args.command == "prepare":
            prepare(args)
        else:
            publish(args.work_dir)
    except (OSError, ValueError, KeyError, TypeError, tarfile.TarError, subprocess.SubprocessError) as error:
        parser.exit(1, str(error) + "\n")
    finally:
        os.umask(previous_umask)


if __name__ == "__main__":
    main()
