#!/usr/bin/env python3
"""Export only the saved positive new scenario's public commands; execute no game."""
import gzip
import json
from pathlib import Path
import sys

import export_portable_public as exporter
import open_space_scenario as scenario
import prepare_public_replay as public


def export():
    authority = scenario.AUTHORITY
    policy_raw = scenario.POLICY.read_bytes()
    policy = public.decode(policy_raw)
    scenario.identity(policy)
    public.require(public.sha(policy_raw) == public.make_open_space_recipe(b"", b"")["authoring_policy_sha256"],
                   "The exact prospective source-driven policy is required.")
    production, harness = scenario.inventories()
    public.require({n: public.sha(b) for n, b in {**production, **harness}.items()} == policy["frozenCodeSha256"] and
                   scenario.manifest({n: public.sha(b) for n, b in harness.items()}) ==
                   public.OPEN_SPACE_HARNESS_SHA256 == policy["harnessManifestSha256"],
                   "The authoring103-source/twelve-harness snapshot must remain byte-identical.")
    for name, pin in policy["toolSha256"].items():
        saved = authority / "frozen-tools" / name
        public.require(saved.is_file() and not saved.is_symlink() and public.sha(saved.read_bytes()) == pin,
                       "The exact pre-integration authoring tool bytes must be retained.")
    data = scenario.inputs(Path(policy["fixtureManifest"]), Path(policy["soundFunctions"]), Path(policy["profileInputs"]))
    public.require(data == policy["frozenInputSha256"] and scenario.manifest(data) == policy["inputManifestSha256"],
                   "Authoring private inputs changed.")
    for name, pin in policy["failedV6ArtifactsSha256"].items():
        public.require(public.sha((scenario.ROOT / name).read_bytes()) == pin, "Failed v6 evidence changed.")
    entrypoint = Path(policy["entrypoint"])
    public.require(public.sha(entrypoint.read_bytes()) == policy["entrypointSha256"] and
                   public.sha(entrypoint.with_suffix(".pdb").read_bytes()) == policy["pdbSha256"],
                   "The actual normal Main authoring PE/PDB must be retained.")
    public.exact_metadata(public.decode((authority / "only-public-window-started.json").read_bytes()),
        dict(policySha=public.sha(policy_raw), maximumInitialWindows=1, maxHosts=12000,
             recordingGeneration=scenario.GENERATION), "source-driven actual marker")
    invocation = public.decode((authority / "only-authoring-invocation.json").read_bytes())
    public.require(invocation["policySha256"] == public.sha(policy_raw) and
                   invocation["maximumAuthoringInvocations"] == 1 and invocation["maxHosts"] == 12000 and
                   invocation["recordingGeneration"] == scenario.GENERATION,
                   "Exactly one prospectively bound authoring invocation is required.")
    result = public.decode((authority / "authoring-result.json").read_bytes())
    receipt_raw = (authority / "capture/public-window-result.json").read_bytes()
    receipt = public.decode(receipt_raw)
    public.require(result["exit"] == 0 and result["status"] == "recorded-new-window-full-qualification-required" and
                   result["actualAuthoringInvocations"] == result["maximumInitialWindows"] == 1 and
                   result["sourceHarnessToolsInputsAndFailedV6Unchanged"] is True and
                   result["receipt"] == receipt and result["receiptSha256"] == public.sha(receipt_raw) and
                   receipt["policySha256"] == public.sha(policy_raw) and
                   receipt["schema"] == "source-open-space-public-policy-first-window/v1" and
                   receipt["recordingGeneration"] == scenario.GENERATION and receipt["faults"] == [] and
                   receipt["attemptedHosts"] == receipt["completedHosts"] == 12000 and
                   receipt["initialExecutions"] == receipt["maximumInitialWindows"] == 1 and
                   receipt["preSnapSwitchWitnesses"] > 0,
                   "Only the actual single complete positive source-driven window may export.")
    exporter.require_candidate(receipt,
        public.decode((authority / "capture/R_SKIP-real-poses.json").read_bytes()), True)
    capture = (authority / "capture/captured-public-commands.jsonl").read_bytes()
    public.require(public.sha(capture) == receipt["publicInputSha256"], "Saved capture changed after authoring.")
    projected = exporter.project_capture(capture)
    packed = public.compress(projected)
    public.require(gzip.decompress(packed) == projected, "Public input gzip roundtrip failed.")
    recipe = public.validate_open_space_recipe(public.make_open_space_recipe(projected, packed))
    recipe_raw = (json.dumps(recipe, indent=2) + "\n").encode("ascii")
    output = public.FIXTURE.parent
    public.write_new_or_verify(output / (scenario.CASE + ".jsonl.gz"), packed)
    public.write_new_or_verify(output / (scenario.CASE + ".recipe.json"), recipe_raw)
    public.write_new_or_verify(authority / (scenario.CASE + ".jsonl"), projected)
    print(json.dumps(dict(schema="source-driven-public-input-export/v1", status="exported-not-full-qualified",
        case=scenario.CASE, jsonl_sha256=public.sha(projected), jsonl_bytes=len(projected),
        gzip_sha256=public.sha(packed), gzip_bytes=len(packed), recipe_sha256=public.sha(recipe_raw),
        source_count=103, source_manifest_sha256=policy["sourceManifestSha256"], harness_count=12,
        harness_manifest_sha256=policy["harnessManifestSha256"], authoring_windows=1, gameplayExecutions=0,
        originalNumericalExpectedArraysExported=False)))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(export())
    except (OSError, ValueError, KeyError, TypeError) as error:
        print("Source-driven export rejected: " + str(error), file=sys.stderr)
        raise SystemExit(1)
