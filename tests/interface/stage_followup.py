#!/usr/bin/env python3
"""Asset-free checkout caller guards; optional explicit release/snapshot Unity API compile."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import unittest
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
RUNTIME = "Assets/Scripts/Runtime/BlitzRuntime.cs"
HUD = "Assets/Scripts/Presentation/PlayHud.cs"
BRIDGE = "Assets/Scripts/Runtime/FixedStepBridge.cs"
SERVICES = "Assets/Scripts/Runtime/SourceInterfaceServices.cs"
SOUND_FACTORIES = (
    ("Assets/Scripts/Runtime/SourceContactComposition.cs", "CreateSimulation", "soundFunctions"),
    ("Assets/Scripts/Runtime/SourceC2Composition.cs", "CreateChecked", "legacySoundFunctions"),
)
LIVE_OVERRIDES = (RUNTIME, HUD)
REQUIRED_OWNED_UI = (RUNTIME, HUD, BRIDGE, "Assets/Scripts/Runtime/SourceSoundVmTransport.cs")
BASE_SHA = "419a8b2d53abbaaa59008f23040e940e0466d0612fb2cb7e6f648ad77526e986"
TEAM_SHA = "c433c1ab2af939c6ef78776436b2fb5ab4c1676f26d53c2a01d390b954e20405"
ARTIFACTS = ROOT / "tests/interface/.artifacts/stage-followup"
SOURCE_ROOT = ROOT
DEPENDENCY = "Assets/Scripts/Presentation/SourceAnimationLibraryAsset.cs"
OWNED_UI = (
    RUNTIME, HUD, BRIDGE,
    "Assets/Scripts/AssetImport/SourceInterfaceData.cs",
    "Assets/Scripts/AssetImport/SourceInterfaceMovementData.cs",
    "Assets/Scripts/Presentation/SourceHudAsset.cs",
    "Assets/Scripts/Presentation/SourceHudComposer.cs",
    "Assets/Scripts/Presentation/SourceHudModel.cs",
    "Assets/Scripts/Presentation/SourceHudRenderer.cs",
    "Assets/Scripts/Presentation/SourceHudViewport.cs",
    "Assets/Scripts/Runtime/SourceCabinetInput.cs",
    "Assets/Scripts/Runtime/SourceCabinetInputPlaySelection.cs",
    "Assets/Scripts/Runtime/SourceCabinetInputUnity.cs",
    "Assets/Scripts/Runtime/SourceHudWebViewport.cs",
    "Assets/Scripts/Runtime/SourceInterfacePresentation.cs",
    "Assets/Scripts/Runtime/SourceInterfaceServices.cs",
    "Assets/Scripts/Runtime/SourceSoundVmTransport.cs",
)
PROJECTS = ("InterfaceModel.csproj", "InterfaceChecks.csproj", "UnityAPICompile.csproj")


def require(value, message):
    if not value:
        raise AssertionError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def validate_publishable_projects(root):
    for name in PROJECTS:
        path = root / "tests/interface" / name
        require(path.is_file(), "Publishable interface project is missing: " + name)
        require(ET.fromstring(path.read_bytes()).tag == "Project", "Invalid publishable interface project: " + name)


def expected_owned_sources(root):
    require(root.is_dir() and (root / "Assets/Scripts").is_dir(), "Missing/wrong explicit source root: " + str(root))
    expected = {}
    for name in OWNED_UI:
        path = root / name
        require(path.is_file(), "Owned UI source is missing from checkout: " + name)
        expected[name] = digest(path.read_bytes())
    validate_publishable_projects(root)
    return expected


def validate_snapshot_options(core_root, team_root, base_root, team_manifest):
    values = (core_root, team_root, base_root, team_manifest)
    if not any(value is not None for value in values):
        return False
    require(all(value is not None for value in values),
            "Explicit snapshot mode requires ALL --core-root/--team-root/--base-root/--team-manifest; no fallback.")
    for value, marker in ((core_root, "Assets/Scripts/Gameplay/GameSimulation.cs"),
                          (team_root, "Assets/Scripts/Presentation/PlayCamera.cs"),
                          (base_root, "source-interface-freeze.json")):
        require(value.is_dir() and (value / marker).is_file(), "Missing/wrong explicit snapshot root: " + str(value))
    require(team_manifest.is_file(), "Explicit Team manifest is missing: " + str(team_manifest))
    return True


def dependency_bytes(path, expected_sha256):
    require(path is not None and expected_sha256 is not None,
            "Dependency overlay requires BOTH --dependency-overlay and --dependency-overlay-sha256.")
    require(path.name == "SourceAnimationLibraryAsset.cs" and path.is_file(),
            "Missing/wrong explicit dependency overlay: " + str(path))
    require(re.fullmatch(r"[0-9a-f]{64}", expected_sha256) is not None, "Invalid dependency overlay SHA256.")
    data = path.read_bytes()
    require(digest(data) == expected_sha256, "Explicit dependency overlay content pin mismatch.")
    return data


def check_initializer_only_overlay(original, overlay):
    expected = original.decode("utf-8")
    for field in ("TextAsset inventory", "string inventorySha256", "string[] sourcePaths", "TextAsset[] decodedClips"):
        old, new = "private " + field + ";", "private " + field + " = null;"
        if old in expected:
            require(expected.count(old) == 1, "Ambiguous original serialized field: " + field)
            expected = expected.replace(old, new, 1)
        else:
            require(new in expected, "Dependency overlay base lacks expected serialized field: " + field)
    require(compact(uncomment(expected)) == compact(uncomment(overlay.decode("utf-8"))),
            "Dependency overlay must ONLY explicitly initialize the four serialized fields to null.")


def uncomment(text):
    return re.sub(r'"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*.*?\*/',
                  lambda m: m[0] if m[0].startswith(('"', "'")) else " ", text, flags=re.S)


def method(source, name):
    clean = uncomment(source)
    match = re.search(r"\b(?:public|private|internal)\s+(?:static\s+)?[\w<>?,.]+\s+"
                      + re.escape(name) + r"\s*\([^)]*\)\s*\{", clean)
    require(match is not None, "Actual Runtime method missing: " + name)
    start = clean.index("{", match.start())
    depth, quoted, escaped = 0, None, False
    for index in range(start, len(clean)):
        char = clean[index]
        if quoted:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == quoted:
                quoted = None
        elif char in "\"'":
            quoted = char
        elif char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return clean[start + 1:index]
    raise AssertionError("Unbalanced actual Runtime method: " + name)


def compact(value):
    return re.sub(r"\s+", "", value)


def validate_runtime(source):
    code = compact(uncomment(source))
    awake = compact(method(source, "Awake"))
    configure = ("playCamera.ConfigureSourceStage(State,()=>simulation.SourceCameraStageOrder,"
                 "simulation.DrawSourceCameraRandom,state=>simulation.SourceCameraRawGameMode,"
                 "simulation.SourceActorWorldPosition,simulation.ApplySourceCameraFeedback);")
    register = "RegisterSourceCameraTickProducer(playCamera.SampleAtSourceTick);"
    attach = "simulation.SourceCameraHeadingProvider=SampleSourceCameraHeading;"
    require(awake.count(configure) == 1, "M1 stage must bind actual raw Core mode/ordinal/shared RNG/canonical roots and camera feedback via six-argument Configure")
    require(awake.count(register) == 1, "actual Team SampleAtSourceTick must be registered exactly once")
    require(awake.count(attach) == 1 and awake.index(configure) < awake.index(register) < awake.index(attach),
            "configure/register before exposing Core's executing-source callback")
    source_block = awake[awake.index("playCamera=newPlayCamera(transform,prefab);"):awake.index("hud=gameObject.AddComponent<PlayHud>();")]
    require("if(IsSourceProfile){" in source_block, "source camera registration must not activate in explicit legacy")
    require("SourcePresentationCueRaised+=ConsumeSourcePresentationCue;" in source_block
            and awake.count("SourcePresentationCueRaised+=ConsumeSourcePresentationCue;") == 1,
            "Runtime owns one resolved-versus-generic source cue route")
    require(awake.count("AfterHostStep+=playCamera.ObserveSourceHostStep;") == 1,
            "Team owns the single host lifecycle subscription")
    require(awake.count("bridge.SourcePresentationCueRaised+=OnSourcePresentationCue;") == 1
            and awake.count("bridge.AfterHostStep+=OnAfterHostStep;") == 1,
            "actual per-Step bridge transport remains connected once")
    cue = compact(method(source, "OnSourcePresentationCue"))
    require("SourcePresentationCueRaised" in cue and cue.count("handler(cue);") == 1
            and ".Execute(" not in cue and ".ConsumeSourceCue(" not in cue and "catch(" not in cue,
            "cue dispatch must not also execute or swallow the event-subscribed consumer")
    require(".ResetSourceStage(" not in code and ".ObserveSourceCameraSuspension(" not in code
            and ".InitializeSourceStage(" not in code and ".AdvanceSourceStage(" not in code,
            "Runtime must not duplicate Team camera lifecycle/advance ownership")
    sample = compact(method(source, "SampleSourceCameraHeading"))
    registration = compact(method(source, "RegisterSourceCameraTickProducer"))
    require("sourceCameraProducer=producer;" in registration
            and "intheading=sourceCameraProducer(preActorState,executingSourceTick);" in sample
            and "lastSourceCameraTick=executingSourceTick;" in sample and "returnheading;" in sample,
            "registered producer must actually execute and return the same tick's heading")
    after = compact(method(source, "OnAfterHostStep"))
    require("simulation.SourceCameraHeadingSampleTick!=source.SourceTick" in after
            and "simulation.SourceCameraHeading!=lastSourceCameraHeading" in after
            and after.count("handler(state);") == 1,
            "source-stage heading must be consumed by actual Core before host observation")
    restart = compact(method(source, "RestartMatch"))
    require(restart.count("bridge.Restart();") == 1 and restart.count("playCamera.ObserveSourceHostStep(State);") == 1
            and restart.index("bridge.Restart();") < restart.index("playCamera.ObserveSourceHostStep(State);")
            < restart.index("ForwardInitialSourcePresentationCues();") < restart.index("playCamera.Render(State,true);"),
            "restart must let Team observe one rewind before initial cues/render")
    initial = compact(method(source, "ForwardInitialSourcePresentationCues"))
    require("SourcePresentationCueBatchbatch=simulation.PresentationCues;" in initial
            and "if(batch==null)thrownewSourceInterfaceException(" in initial
            and "foreach(SourcePresentationCuecueinbatch.Cues)OnSourcePresentationCue(cue);" in initial
            and awake.count("ForwardInitialSourcePresentationCues();") == 1
            and code.count("ForwardInitialSourcePresentationCues();") == 2,
            "constructor/restart initial cue batches must use the same once-only transport")
    destroy = compact(method(source, "OnDestroy"))
    require(destroy.count("SourcePresentationCueRaised-=ConsumeSourcePresentationCue;") == 1
            and destroy.count("AfterHostStep-=playCamera.ObserveSourceHostStep;") == 1,
            "source stage subscriptions must be removed once on destruction")
    require("SourceHeading1024" not in code and "AdvanceSourceCameraAtStage" not in code
            and "(int)state.Phase" not in code and "worldToCameraMatrix" not in code and "WorldJoint" not in code,
            "M1 caller must not invent raw GM, renderer matrices/joints or last-render heading")
    legacy = awake[awake.index("if(prefab==null){"):awake.index("simulation=newBlitz.Gameplay.GameSimulation(1);")]
    require("if(kickMeters!=SourceKickMeterMode.Disabled)thrownewSourceInterfaceException(" in legacy
            and "if(profile.UsesC2Data)thrownewSourceInterfaceException(" in legacy
            and "if(!ExplicitGraybox(Application.absoluteURL))thrownewSourceInterfaceException(" in legacy
            and awake.count("simulation=newBlitz.Gameplay.GameSimulation(1);") == 1
            and legacy.index("if(profile.UsesC2Data)") < legacy.index("if(!ExplicitGraybox("),
            "only explicit non-C2 graybox keeps the legacy constructor; C2 and implicit source fallbacks are forbidden")
    require("stringserviceKey=SourceInterfaceServices.ResourceKeyFor(profile);" in awake
            and "sourceServices=Resources.Load<SourceInterfaceServices>(serviceKey);" in awake
            and "if(sourceServices==null)thrownewSourceInterfaceException(" in awake
            and "simulation=sourceServices.CreateSimulation(prefab,compatibility,fireEffects:fire,kickMeters:kickMeters);" in awake,
            "missing selected-profile resources must fail visibly and preserve explicit compatibility, not select legacy")
    require("bridge.AdvanceSource(Time.unscaledDeltaTime,sourceInput.Buffer.NextHostFrame);" in compact(method(source, "Update")),
            "the actual source raw input/Step caller must remain connected")
    raster = "playCamera.ConfigureSourceMovementRaster(bootstrap.HorizontalResolution,bootstrap.VerticalResolution);"
    require(awake.count(raster) == 1 and awake.index(raster) < awake.index(configure),
            "explicit source DIP raster must be configured before source camera initialization")
    require("SourceInterfaceMovementBootstrapbootstrap=sourceServices.MovementBootstrap;" in source_block,
            "source camera raster must come from the required pinned bootstrap, not defaults")
    frame = "SourceCinematicTransformframe=playCamera.RequireSourceCameraAtTick(executingSourceTick);"
    inject = ("simulation.SetSourceMovementCameraFrame(newBlitz.Gameplay.SourceMovementCameraFrame("
              "frame.Position,frame.Transform,bootstrap.HorizontalResolution,bootstrap.VerticalResolution),executingSourceTick);")
    require(sample.count(frame) == 1 and sample.count(inject) == 1,
            "M1 must inject the actual tick-guarded position/full transform and original DIP, not zero or render data")
    require(sample.index("sourceCameraProducer(preActorState,executingSourceTick)") < sample.index(frame)
            < sample.index(inject) < sample.index("lastSourceCameraTick=executingSourceTick;") < sample.index("returnheading;"),
            "actual source sample/frame injection must occur in-order inside the existing pre-player provider")
    require(code.count("simulation.SetSourceMovementCameraFrame(") == 1 and ".SetSourceMovementCameraFrame(" not in after,
            "M1 frame must not be repeated or injected by post-host/render observers")
    require("SourceTeamResourceProfile.SelectRuntimeProfile(Application.absoluteURL,sourceResourceProfile)" in awake
            and "SourcePlayerProvider.ResolveActorMode(profile)" in awake,
            "activation must preserve the selected C1/C2 team/resource profile, not historical B1/157 resources")
    require("!ReferenceEquals(animations,SourcePlayerProvider.ResolveRequiredAnimationLibrary(prefab))" in awake,
            "Core/geometry/Team/renderer must share the actual canonical157 animation instance")
    require(awake.count("bridge.AfterSimulationStep+=OnAfterSimulationStep;") == 1
            and awake.count("bridge.AfterSourcePresentation+=CompleteSourceSoundBatch;") == 1
            and awake.count("bridge.Restarted+=ResetSourceSoundTransport;") == 1,
            "sound drain/end-batch/restart lifecycle must be connected once before wakes")
    drain = compact(method(source, "OnAfterSimulationStep"))
    require(drain.count("simulation.TakeSourceSoundVmDispatches()") == 1 and "soundTransport.Drain(batch);" in drain
            and drain.index("soundTransport.Drain(batch);") < drain.index("SourceSoundVmDispatchRaised"),
            "every per-Step sound drain must retain the batch before observation")
    route = compact(method(source, "ConsumeSourcePresentationCue"))
    require("callback.Kind!=SourcePresentationCueKind.PlayerDownCallback" in route
            and "soundTransport.TakeForCue(callback)" in route
            and "dispatch.Status==SourceSoundVmStatus.Resolved" in route
            and "playCamera.ConsumeResolvedSourceSound(dispatch,soundTransport.DeliverResolved);"
            "soundTransport.FlushNativePrefix();return;" in route
            and route.count("playCamera.ConsumeSourceCue(cue);") == 1,
            "paired resolved sounds must bypass generic callbacks once; Unsupported/PlayerDown are not replayed as sound")
    require("SourcePresentationCueObserved" in cue and cue.index("observed(cue);") < cue.index("handler(cue);"),
            "raw cues must remain observable before a real-output/unsupported failure")
    require("OnAfterSimulationStep();" in initial and initial.index("OnAfterSimulationStep();")
            < initial.index("foreach(SourcePresentationCuecueinbatch.Cues)") and "CompleteSourceSoundBatch();" in initial,
            "initial/restart cue batches need the same drain-before-cue protocol")
    return 28


def validate_services(source):
    code = compact(uncomment(source))
    require("ResourceKey=SourceInterfacePackagePlan.ContactServicesResourceKey;" in code
            and "C2ResourceKey=SourceInterfacePackagePlan.C2ServicesResourceKey;" in code
            and "C2M1ResourceKey=SourceInterfacePackagePlan.C2M1ServicesResourceKey;" in code
            and "HistoricalSoundVmResourceKey=SourceInterfacePackagePlan.SoundVmServicesResourceKey;" in code
            and 'ExpandedAnimationSha256="f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e"' in code,
            "current C1/C2 resource identities and historical157 manifest must remain distinct")
    create = compact(method(source, "CreateSimulation"))
    require("SourceContactComposition.CreateSimulation(animations,pivots,playbook,Configuration(),"
            "teams.Rosters,PresentationSettings,MovementData,MovementSettings,SoundVmData,"
            "kickMeterMetrics:kickMetrics)" in create
            and "SourceC2Composition.CreateSimulation(animations,pivots,playbook,Configuration(),"
            "teams.Rosters,PresentationSettings,MovementData,MovementSettings,SoundVmData,bootstrap,"
            "kickMeterMetrics:kickMetrics)" in create
            and "SourceC2Composition.CreateM1Simulation(animations,pivots,playbook,Configuration(),"
            "teams.Rosters,PresentationSettings,MovementData,MovementSettings,SoundVmData,bootstrap,"
            "compatibility:compatibility,fireEffects:fireEffects,kickMeterMetrics:kickMetrics)" in create,
            "all actual C1/C2 factories must receive mandatory typed movement/settings and sound inputs")
    require("newSourceMovementDefenseRow(row.Line,row.Back,row.Base,row.Range,row.Plow)" in code
            and "newSourceMovementRuntimeData(movement,actual.Friction,rows)" in code
            and "for(inti=0;i<rows.Length;i++)" in code
            and "movementData=SourceInterfaceMovementComposition.RuntimeData(Movement,MovementRuntime)" in code,
            "M1 composition must map every actual Line/Back/friction/handicap row, not tuned or swapped substitutes")
    require("newSourceMovementSettings(actual.FieldIndex,actual.InfiniteTurboTeamMask,actual.SpeedPowerup,"
            "actual.FastTurboTeamMask,actual.RunOutOfBoundsTeamMask,actual.ViolencePowerup)" in code
            and "movementSettings=SourceInterfaceMovementComposition.Settings(MovementBootstrap)" in code,
            "settings must preserve all original team masks and global speed without Home/bool conversion")
    require("SourceInterfaceMovementBootstrap.Read(originalMovementBootstrap.bytes,movementBootstrapSha256)" in code
            and "SourceInterfaceMovementBootstrap.Read(movementBootstrapText.bytes,trustedMovementBootstrapSha256)" in code
            and "if(originalMovementBootstrap==null)thrownewSourceInterfaceException(" in code,
            "serialized settings/source DIP need explicit pinned bytes and missing-bootstrap failure")
    require("SourceInterfaceMovementData.Read(movementAsset.bytes,SourceInterfaceMovementData.ContentSha256)" in code
            and "Resources.Load<TextAsset>(SourceInterfaceMovementData.ResourceKey)" in code
            and "if(movementAsset==null)thrownewSourceInterfaceException(" in code,
            "mandatory movement data must use the strict original89c790 resource, never Responses or defaults")
    require("SourcePlayerProvider.ResolveRequiredTeamPresentation(prefab)" in code
            and "prefab.JointCount!=SourceTiming.JointCount" in create
            and "prefab.GetUnityReferencePivot(i)" in create,
            "source factories require per-prefab rosters and all fifteen actual reference pivots")
    require("TextAssetmovementBootstrapText,stringtrustedMovementBootstrapSha256," in code
            and "TextAssetsoundVmFunctions,stringtrustedSoundVmSHA)" in code,
            "sound profile must retain M1 prefix plus explicit SAFN bytes/pin")
    require("Resources.Load<SourceAnimationLibraryAsset>(profile.AnimationResourceKey)" in code
            and "!ReferenceEquals(originalAnimations,canonical)" in code,
            "matching hashes on another profile or copied asset cannot bypass canonical Resource identity")
    require("SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(C2Data)" in create
            and create.index("SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(C2Data)") <
            create.index("SourceC2Composition.CreateM1Simulation(")
            and "newGameSimulation(" not in create and "BindSourceSoundVmData(" not in create,
            "services delegate immutable binding to the actual factories, with C2 bootstrap before construction")
    require('SoundVmFunctionsResourceKey="SourceData/sound-functions-safn1-4a557304"' in code
            and "Resources.Load<TextAsset>(SoundVmFunctionsResourceKey)" in code
            and "!ReferenceEquals(originalSoundVmFunctions,canonical)" in code
            and "originalSoundVmFunctions.bytes.Length!=1983" in code
            and "SourceAudioFunctionData.Read(originalSoundVmFunctions.bytes,GameSimulation.SourceSoundVmDataSha256)" in code,
            "SAFN must use the canonical original1983-byte TextAsset and Core trusted4a557 pin, not a resource alias")
    return 11


def validate_sound_factory(source, method_name, sound_parameter):
    create = compact(method(source, method_name))
    construction = "newGameSimulation(animations,geometry,playbook,configuration,randomFactory,seed,"
    require(construction + "rosters,presentation,movementData,movementSettings," in create,
            "actual sound factory must pass complete typed kernel inputs to the real constructor")
    pin = sound_parameter + ".PayloadSha256!=SourceInterfacePackagePlan.SoundVmFunctionsSha256"
    require(pin in create and create.index(pin) < create.index(construction),
            "actual sound factory must validate the trusted SAFN before construction")
    geometry = "newSourceCatchGeometry(animations,referencePivots)"
    require("referencePivots.Count!=SourceTiming.JointCount" in create and geometry in create
            and create.index(geometry) < create.index(construction),
            "actual sound factory must use all fifteen real pivots for its same-library geometry")
    binding = "game.BindSourceSoundVmData(" + sound_parameter + ");"
    require(create.count("BindSourceSoundVmData(") == 1 and binding in create and "returngame;" in create
            and create.index(construction) < create.index(binding) < create.index("returngame;"),
            "actual sound factory must bind immutable SAFN exactly once before returning to any bridge wake")
    return 4


def validate_sound_factories(source_root):
    return sum(validate_sound_factory((source_root / name).read_text(), method_name, sound_parameter)
               for name, method_name, sound_parameter in SOUND_FACTORIES)


def validate_hud_scope(source):
    event = compact(method(source, "OnGameEvent"))
    require("internalvoidOnSourceGameEvent" not in event and "internalvoidOnSourceHostStep" not in event,
            "source HUD event/host methods must be class members, not nested in OnGameEvent")
    require("sourceHud.OnSourceEvent(value);" in compact(method(source, "OnSourceGameEvent"))
            and "sourceHud.OnHostStep(state);" in compact(method(source, "OnSourceHostStep")),
            "actual source HUD event/host bodies must remain intact")


def assert_compile_inventory(compiled, expected_owned):
    require(all(name in expected_owned for name in REQUIRED_OWNED_UI),
            "Owned UI manifest must explicitly include BlitzRuntime, PlayHud and FixedStepBridge.")
    for name, expected in expected_owned.items():
        require(name in compiled, "Actual compiled owned UI source omitted: " + name)
        require(compiled[name] == expected, "Actual compiled owned UI hash mismatch: " + name)


class CallerGuards(unittest.TestCase):
    def test_actual_caller(self):
        self.assertEqual(validate_runtime((SOURCE_ROOT / RUNTIME).read_text()), 28)

    def test_actual_movement_services(self):
        self.assertEqual(validate_services((SOURCE_ROOT / SERVICES).read_text()), 11)

    def test_actual_factories_bind_immutable_sound_before_returning(self):
        self.assertEqual(validate_sound_factories(SOURCE_ROOT), 8)

    def test_isolated_actual_caller_mutants(self):
        source = (SOURCE_ROOT / RUNTIME).read_text()
        mutants = [
            ("unregistered", "RegisterSourceCameraTickProducer(playCamera.SampleAtSourceTick);", ""),
            ("render-getter", "RegisterSourceCameraTickProducer(playCamera.SampleAtSourceTick);",
             "RegisterSourceCameraTickProducer((state, tick) => playCamera.SourceHeading1024);"),
            ("fixed-order", "() => simulation.SourceCameraStageOrder", "() => 0"),
            ("fake-random", "simulation.DrawSourceCameraRandom, state => simulation.SourceCameraRawGameMode",
             "(bound, site) => 0, state => simulation.SourceCameraRawGameMode"),
            ("yard-root", "simulation.SourceActorWorldPosition", "id => default(SourceVector3)"),
            ("double-cue", "Action<SourcePresentationCue> handler = SourcePresentationCueRaised;",
             "playCamera.SourcePlayback.Execute(new SourcePresentationCueBatch(new[] { cue }));\n"
             "Action<SourcePresentationCue> handler = SourcePresentationCueRaised;"),
            ("double-host", "AfterHostStep += playCamera.ObserveSourceHostStep;",
             "AfterHostStep += playCamera.ObserveSourceHostStep;\nAfterHostStep += playCamera.ObserveSourceHostStep;"),
            ("double-reset", "playCamera.ObserveSourceHostStep(State);",
             "playCamera.ObserveSourceHostStep(State);\nplayCamera.ResetSourceStage(State);"),
            ("missing-rewind", "playCamera.ObserveSourceHostStep(State);", ""),
            ("unused-delegate", "int heading = sourceCameraProducer(preActorState, executingSourceTick);", "int heading = 0;"),
            ("no-consumed-check", "simulation.SourceCameraHeadingSampleTick != source.SourceTick", "false"),
            ("silent-legacy", 'if (!ExplicitGraybox(Application.absoluteURL))', "if (false)"),
            ("C2-graybox-fallback",
             "if (profile.UsesC2Data)\n                        throw new SourceInterfaceException(\"Explicit C2 entry cannot silently select legacy graybox/old bank initialization.\");",
             "if (false)\n                        throw new SourceInterfaceException(\"Explicit C2 entry cannot silently select legacy graybox/old bank initialization.\");"),
            ("resource-tolerance", "if (sourceServices == null)", "if (false)"),
            ("missing-unsubscribe", "SourcePresentationCueRaised -= ConsumeSourcePresentationCue;", ""),
            ("guessed-raw-mode", "state => simulation.SourceCameraRawGameMode", "state => (int)state.Phase"),
            ("missing-raster", "playCamera.ConfigureSourceMovementRaster(bootstrap.HorizontalResolution, bootstrap.VerticalResolution);", ""),
            ("stale-frame", "playCamera.RequireSourceCameraAtTick(executingSourceTick)",
             "playCamera.RequireSourceCameraAtTick(executingSourceTick - 1)"),
            ("zero-matrix", "frame.Position, frame.Transform, bootstrap.HorizontalResolution, bootstrap.VerticalResolution",
             "frame.Position, new float[12], bootstrap.HorizontalResolution, bootstrap.VerticalResolution"),
            ("browser-raster", "frame.Position, frame.Transform, bootstrap.HorizontalResolution, bootstrap.VerticalResolution",
             "frame.Position, frame.Transform, Screen.width, Screen.height"),
            ("wrong-frame-tick", "bootstrap.VerticalResolution), executingSourceTick);",
             "bootstrap.VerticalResolution), preActorState.Tick);"),
            ("old-team-profile", "SourcePlayerProvider.ResolveActorMode(profile)", "SourcePlayerProvider.ResolveActorMode(true)"),
            ("fixed-profile-resource", "SourceInterfaceServices.ResourceKeyFor(profile)", "SourceInterfaceServices.ResourceKey"),
            ("missing-explicit-profile", "SourceTeamResourceProfile.SelectRuntimeProfile(Application.absoluteURL, sourceResourceProfile)",
             "SourceTeamResourceProfile.Contact272"),
            ("compatibility-disconnected",
             "sourceServices.CreateSimulation(prefab, compatibility, fireEffects: fire, kickMeters: kickMeters)",
             "sourceServices.CreateSimulation(prefab)"),
            ("native-prefix-flush-removed",
             "soundTransport.FlushNativePrefix();\n                    return;", "return;"),
            ("different-library-instance", "!ReferenceEquals(animations, SourcePlayerProvider.ResolveRequiredAnimationLibrary(prefab))",
             "false"),
            ("missing-movement-frame",
             "simulation.SetSourceMovementCameraFrame(new Blitz.Gameplay.SourceMovementCameraFrame(\n"
             "                frame.Position, frame.Transform, bootstrap.HorizontalResolution, bootstrap.VerticalResolution), executingSourceTick);",
             ""),
            ("duplicate-movement-frame",
             "simulation.SetSourceMovementCameraFrame(new Blitz.Gameplay.SourceMovementCameraFrame(\n"
             "                frame.Position, frame.Transform, bootstrap.HorizontalResolution, bootstrap.VerticalResolution), executingSourceTick);",
             "simulation.SetSourceMovementCameraFrame(new Blitz.Gameplay.SourceMovementCameraFrame(\n"
             "                frame.Position, frame.Transform, bootstrap.HorizontalResolution, bootstrap.VerticalResolution), executingSourceTick);\n"
             "simulation.SetSourceMovementCameraFrame(new Blitz.Gameplay.SourceMovementCameraFrame(\n"
             "                frame.Position, frame.Transform, bootstrap.HorizontalResolution, bootstrap.VerticalResolution), executingSourceTick);"),
            ("missing-sound-drain", "bridge.AfterSimulationStep += OnAfterSimulationStep;", ""),
            ("resolved-generic-replay", "playCamera.ConsumeResolvedSourceSound(dispatch, soundTransport.DeliverResolved);",
             "playCamera.ConsumeSourceCue(dispatch.OriginalCue);"),
            ("sound-restart-leak", "bridge.Restarted += ResetSourceSoundTransport;", ""),
        ]
        for name, old, new in mutants:
            with self.subTest(name=name):
                self.assertEqual(source.count(old), 1, name)
                with self.assertRaises(AssertionError, msg=name):
                    validate_runtime(source.replace(old, new, 1))

    def test_isolated_actual_service_mutants(self):
        source = (SOURCE_ROOT / SERVICES).read_text()
        c1 = ("SourceContactComposition.CreateSimulation(animations, pivots, playbook, Configuration(),\n"
              "                teams.Rosters, PresentationSettings, MovementData, MovementSettings, SoundVmData,\n"
              "                kickMeterMetrics: kickMetrics)")
        c2 = ("SourceC2Composition.CreateSimulation(animations, pivots, playbook, Configuration(),\n"
              "                    teams.Rosters, PresentationSettings, MovementData, MovementSettings, SoundVmData, bootstrap,\n"
              "                    kickMeterMetrics: kickMetrics)")
        m1 = ("SourceC2Composition.CreateM1Simulation(animations, pivots, playbook, Configuration(),\n"
              "                        teams.Rosters, PresentationSettings, MovementData, MovementSettings, SoundVmData, bootstrap,\n"
              "                        compatibility: compatibility, fireEffects: fireEffects, kickMeterMetrics: kickMetrics)")
        mutants = (
            ("omitted-data", c1, c1.replace("MovementData,", "null,")),
            ("omitted-settings", c1, c1.replace("MovementSettings,", "null,")),
            ("omitted-c2-data", c2, c2.replace("MovementData,", "null,")),
            ("omitted-c2-settings", c2, c2.replace("MovementSettings,", "null,")),
            ("omitted-m1-data", m1, m1.replace("MovementData,", "null,")),
            ("omitted-m1-settings", m1, m1.replace("MovementSettings,", "null,")),
            ("swapped-defense", "row.Line, row.Back, row.Base, row.Range, row.Plow",
             "row.Back, row.Line, row.Base, row.Range, row.Plow"),
            ("fake-friction", "new SourceMovementRuntimeData(movement, actual.Friction, rows)",
             "new SourceMovementRuntimeData(movement, new float[7], rows)"),
            ("mask-as-bool", "actual.SpeedPowerup, actual.FastTurboTeamMask, actual.RunOutOfBoundsTeamMask",
             "actual.SpeedPowerup, actual.FastTurboTeamMask == 0 ? 0 : 1, actual.RunOutOfBoundsTeamMask"),
            ("speed-from-side-mask", "actual.SpeedPowerup, actual.FastTurboTeamMask",
             "actual.InfiniteTurboTeamMask != 0, actual.FastTurboTeamMask"),
            ("missing-bootstrap-tolerance", "if (originalMovementBootstrap == null)", "if (false)"),
            ("legacy-team-resource", "SourcePlayerProvider.ResolveRequiredTeamPresentation(prefab)",
             "SourcePlayerProvider.ResolveRequiredTeamPresentation()"),
            ("hash-only-animation-alias", "!ReferenceEquals(originalAnimations, canonical)", "false"),
            ("missing-SAFN-input", c1, c1.replace("SoundVmData,", "null,")),
            ("SAFN-resource-alias", "!ReferenceEquals(originalSoundVmFunctions, canonical)", "false"),
        )
        for name, old, new in mutants:
            with self.subTest(name=name):
                self.assertEqual(source.count(old), 1, name)
                with self.assertRaises(AssertionError, msg=name):
                    validate_services(source.replace(old, new, 1))

    def test_isolated_actual_factory_mutants(self):
        for name, method_name, sound_parameter in SOUND_FACTORIES:
            source = (SOURCE_ROOT / name).read_text()
            binding = "game.BindSourceSoundVmData(" + sound_parameter + ");"
            for label, old, new, assertion in (
                ("missing-binding", binding, "", "bind immutable SAFN"),
                ("wrong-binding", binding, "game.BindSourceSoundVmData(null);", "bind immutable SAFN"),
                ("omitted-movement",
                 "new GameSimulation(animations, geometry, playbook, configuration, randomFactory, seed,\n"
                 "                rosters, presentation, movementData, movementSettings,",
                 "new GameSimulation(animations, geometry, playbook, configuration, randomFactory, seed,\n"
                 "                rosters, presentation, null, movementSettings,", "complete typed kernel inputs"),
                ("omitted-geometry", "new SourceCatchGeometry(animations, referencePivots)", "null", "fifteen real pivots"),
            ):
                with self.subTest(factory=name, mutant=label):
                    self.assertEqual(source.count(old), 1, label)
                    with self.assertRaisesRegex(AssertionError, assertion):
                        validate_sound_factory(source.replace(old, new, 1), method_name, sound_parameter)

    def test_actual_hud_scope(self):
        source = (SOURCE_ROOT / HUD).read_text()
        validate_hud_scope(source)
        old = "        }\n\n        internal void OnSourceGameEvent"
        self.assertEqual(source.count(old), 1)
        with self.assertRaisesRegex(AssertionError, "class members"):
            validate_hud_scope(source.replace(old, "\n        internal void OnSourceGameEvent", 1))

    def test_existing_owned_ui_cannot_be_omitted_or_replaced(self):
        expected = {name: digest((SOURCE_ROOT / name).read_bytes()) for name in REQUIRED_OWNED_UI}
        assert_compile_inventory(dict(expected), expected)
        for name in REQUIRED_OWNED_UI:
            with self.subTest(omitted=name):
                missing = dict(expected)
                del missing[name]
                with self.assertRaisesRegex(AssertionError, "source omitted"):
                    assert_compile_inventory(missing, expected)
            with self.subTest(changed=name):
                wrong = dict(expected)
                wrong[name] = "0" * 64
                with self.assertRaisesRegex(AssertionError, "hash mismatch"):
                    assert_compile_inventory(wrong, expected)

    def test_clean_checkout_owns_complete_UI_and_projects(self):
        expected = expected_owned_sources(SOURCE_ROOT)
        self.assertEqual(len(expected), 17)
        assert_compile_inventory(dict(expected), expected)

    def test_explicit_snapshot_roots_fail_closed(self):
        self.assertFalse(validate_snapshot_options(None, None, None, None))
        with self.assertRaisesRegex(AssertionError, "requires ALL"):
            validate_snapshot_options(SOURCE_ROOT, None, None, None)
        absent = ARTIFACTS / "nonexistent-explicit-snapshot"
        with self.assertRaisesRegex(AssertionError, "explicit snapshot root"):
            validate_snapshot_options(absent, absent, absent, absent)

    def test_dependency_overlay_failures(self):
        absent = ARTIFACTS / "nonexistent-overlay" / "SourceAnimationLibraryAsset.cs"
        with self.assertRaisesRegex(AssertionError, "BOTH"):
            dependency_bytes(None, None)
        with self.assertRaisesRegex(AssertionError, "Missing/wrong"):
            dependency_bytes(absent, "0" * 64)
        with self.assertRaisesRegex(AssertionError, "Missing/wrong"):
            dependency_bytes(SOURCE_ROOT / RUNTIME, digest((SOURCE_ROOT / RUNTIME).read_bytes()))
        actual = SOURCE_ROOT / DEPENDENCY
        with self.assertRaisesRegex(AssertionError, "pin mismatch"):
            dependency_bytes(actual, "0" * 64)

    def test_publishable_projects_cannot_be_missing_or_invalid(self):
        workspace = ARTIFACTS / ("project-guard-" + uuid.uuid4().hex)
        target = workspace / "tests/interface"
        target.mkdir(parents=True)
        try:
            for missing in PROJECTS:
                for name in PROJECTS:
                    path = target / name
                    if name != missing:
                        path.write_bytes((SOURCE_ROOT / "tests/interface" / name).read_bytes())
                    elif path.exists():
                        path.unlink()
                with self.assertRaisesRegex(AssertionError, "project is missing: " + re.escape(missing)):
                    validate_publishable_projects(workspace)
            for name in PROJECTS:
                (target / name).write_bytes((SOURCE_ROOT / "tests/interface" / name).read_bytes())
            (target / PROJECTS[0]).write_text("<NotAProject />")
            with self.assertRaisesRegex(AssertionError, "Invalid publishable"):
                validate_publishable_projects(workspace)
        finally:
            shutil.rmtree(workspace)

    def test_dependency_overlay_cannot_change_guards_or_API(self):
        source = (SOURCE_ROOT / DEPENDENCY).read_bytes()
        initialized = source.decode()
        for field in ("TextAsset inventory", "string inventorySha256", "string[] sourcePaths", "TextAsset[] decodedClips"):
            initialized = initialized.replace("private " + field + ";", "private " + field + " = null;")
        check_initializer_only_overlay(source, initialized.encode())
        altered = initialized.replace("public ISourceAnimationLibrary Load()", "public ISourceAnimationLibrary LoadChanged()")
        self.assertNotEqual(altered, initialized)
        with self.assertRaisesRegex(AssertionError, "ONLY"):
            check_initializer_only_overlay(source, altered.encode())


def protected_sources(source_root, core_root, team_root, base_root, team_manifest):
    base_manifest = base_root / "source-interface-freeze.json"
    require(digest(base_manifest.read_bytes()) == BASE_SHA, "Protected base33 manifest changed.")
    require(digest(team_manifest.read_bytes()) == TEAM_SHA, "Final Team manifest changed.")
    base = json.loads(base_manifest.read_bytes())
    team = json.loads(team_manifest.read_bytes())
    guarded = {base_manifest: digest(base_manifest.read_bytes()), team_manifest: digest(team_manifest.read_bytes())}
    sources = {}
    origins = {}
    for directory in ("Contracts", "Gameplay", "AssetImport", "Presentation", "Runtime"):
        for path in sorted((core_root / "Assets/Scripts" / directory).glob("*.cs")):
            relative = path.relative_to(core_root).as_posix()
            data = path.read_bytes()
            guarded[path] = digest(data)
            sources[relative] = data
            origins[relative] = "protected-B1"
    require(len([p for p in sources if p.startswith("Assets/Scripts/Gameplay/")]) == 24
            and not any("SourceMovementIntegration" in p for p in sources), "Use protected B1, not Primary M1.")
    for relative, expected in team["production_sources"].items():
        path = team_root / relative
        data = path.read_bytes()
        require(digest(data) == expected, "Final protected Team source changed: " + relative)
        guarded[path] = expected
        if "/Editor/" not in relative:
            sources[relative] = data
            origins[relative] = "final-protected-Team"
    for relative, expected in base["owned_files_sha256"].items():
        path = base_root / relative
        data = path.read_bytes()
        require(digest(data) == expected, "Protected base33 file changed: " + relative)
        guarded[path] = expected
        if relative.startswith("Assets/") and relative.endswith(".cs"):
            sources[relative] = data
            origins[relative] = "protected-base33"
    expected_owned = expected_owned_sources(source_root)
    for relative, expected in base["owned_files_sha256"].items():
        if not (relative.startswith("Assets/") and relative.endswith(".cs")):
            continue
        path = source_root / relative
        data = path.read_bytes()
        actual = digest(data)
        if relative not in LIVE_OVERRIDES:
            require(actual == expected, "Unchanged owned UI source differs from protected base: " + relative)
        sources[relative] = data
        origins[relative] = "live-authorized-owned-override" if relative in LIVE_OVERRIDES else "live-owned-identical-to-protected"
        guarded[path] = actual
        require(expected_owned[relative] == actual, "Owned source changed during capture: " + relative)
    require(len(expected_owned) == 16, "Complete protected base UI has sixteen actual C# sources.")
    assert_compile_inventory({name: digest(data) for name, data in sources.items()}, expected_owned)
    return sources, origins, guarded, expected_owned


def release_sources(source_root):
    expected_owned = expected_owned_sources(source_root)
    sources, origins, guarded = {}, {}, {}
    for path in sorted((source_root / "Assets/Scripts").rglob("*.cs")):
        if "Editor" in path.relative_to(source_root / "Assets/Scripts").parts:
            continue
        relative = path.relative_to(source_root).as_posix()
        data = path.read_bytes()
        sources[relative], origins[relative], guarded[path] = data, "explicit-current-release-checkout", digest(data)
    assert_compile_inventory({name: digest(data) for name, data in sources.items()}, expected_owned)
    return sources, origins, guarded, expected_owned


def select_sources(args):
    snapshot = validate_snapshot_options(args.core_root, args.team_root, args.base_root, args.team_manifest)
    inputs = protected_sources(args.source_root, args.core_root, args.team_root, args.base_root, args.team_manifest) if snapshot else release_sources(args.source_root)
    sources, origins, guarded, expected_owned = inputs
    overlays = []
    if args.dependency_overlay is not None or args.dependency_overlay_sha256 is not None:
        data = dependency_bytes(args.dependency_overlay, args.dependency_overlay_sha256)
        require(DEPENDENCY in sources, "Dependency overlay target is missing from selected source root.")
        check_initializer_only_overlay(sources[DEPENDENCY], data)
        sources[DEPENDENCY], origins[DEPENDENCY] = data, "explicit-parent-serialized-field-overlay"
        guarded[args.dependency_overlay] = digest(data)
        overlays.append({"path": str(args.dependency_overlay), "target": DEPENDENCY, "sha256": digest(data),
                         "four_null_initializers_only": True})
    return inputs, ("explicit-protected-snapshots" if snapshot else "current-release-checkout"), overlays


def compile_actual(editor_data, args):
    (sources, origins, guarded, expected_owned), mode, overlays = select_sources(args)
    standard = editor_data / "NetStandard/ref/2.1.0/netstandard.dll"
    engine = editor_data / "Managed/UnityEngine"
    require(standard.is_file() and (engine / "UnityEngine.CoreModule.dll").is_file(),
            "Actual Unity NetStandard2.1/engine assemblies required; no stubs or SDK substitution.")
    sdks = subprocess.run(["dotnet", "--list-sdks"], capture_output=True, text=True, check=True)
    found = re.findall(r"^(\d+\.\d+\.\d+)[^\n]*\[([^\]]+)\]$", sdks.stdout, re.M)
    require(found, "Installed Roslyn SDK compiler is required.")
    version, prefix = max(found, key=lambda item: tuple(map(int, item[0].split("."))))
    compiler = Path(prefix) / version / "Roslyn/bincore/csc.dll"
    require(compiler.is_file(), "Actual installed C# compiler is missing.")
    work = ARTIFACTS / ("compile-" + uuid.uuid4().hex)
    work.mkdir(parents=True)
    environment = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                       DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    try:
        copied = []
        for relative, data in sources.items():
            target = work / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            copied.append(target)
        references = [standard] + sorted(engine.glob("*.dll"))
        results = []
        evidence = {"status": "running", "language": "C#7.3",
                    "profile": "actual Unity .NET Standard2.1 + engine/target APIs",
                    "editor_data": str(editor_data), "scripts": len(sources),
                    "source_sha256": {path: digest(data) for path, data in sources.items()},
                    "source_origin": origins, "complete_owned_UI_file_sha256": expected_owned,
                    "source_mode": mode, "dependency_overlays": overlays,
                    "required_owned_UI_explicitly_compiled": list(REQUIRED_OWNED_UI),
                    "Primary_hot_Core_fallback": False, "fake_Unity_types": False,
                    "Editor_native_browser_executed": False, "results": results}
        for define in ("UNITY_EDITOR", "UNITY_WEBGL"):
            profile_references = list(references)
            if define == "UNITY_WEBGL":
                web_module = editor_data / "PlaybackEngines/WebGLSupport/Managed/UnityEngine.WebGLModule.dll"
                require(web_module.is_file(), "Actual Unity WebGL target API module is required; no fake WebGLInput.")
                profile_references = [path for path in profile_references if path.name != web_module.name] + [web_module]
            output = work / ("BlitzStage." + define + ".dll")
            arguments = ["-nologo", "-nostdlib+", "-target:library", "-langversion:7.3",
                         "-warn:4", "-warnaserror+", "-define:" + define, "-out:" + str(output)]
            arguments += ["-r:" + str(path) for path in profile_references]
            arguments += [str(path) for path in copied]
            compiled_owned = {path.relative_to(work).as_posix(): digest(path.read_bytes())
                              for path in copied if path.relative_to(work).as_posix() in expected_owned}
            assert_compile_inventory(compiled_owned, expected_owned)
            response = work / (define + ".rsp")
            response.write_text("\n".join(json.dumps(value) for value in arguments) + "\n")
            command = ["dotnet", str(compiler), "@" + str(response)]
            process = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=180)
            (ARTIFACTS / (define + "-compile.log")).write_text(process.stdout + process.stderr)
            profile = {"defines": define, "scripts": len(sources),
                       "status": "passed" if process.returncode == 0 else "failed",
                       "diagnostics": process.stdout + process.stderr,
                       "compiled_owned_file_sha256": compiled_owned,
                       "reference_sha256": {str(path): digest(path.read_bytes()) for path in profile_references},
                       "command": command}
            if process.returncode == 0:
                profile["assembly_sha256"] = digest(output.read_bytes())
                preserved = ARTIFACTS / output.name
                shutil.copyfile(output, preserved)
                profile["assembly_path"] = str(preserved)
            results.append(profile)
            evidence["status"] = "running" if process.returncode == 0 else "failed"
            (ARTIFACTS / "api-inventory.json").write_text(json.dumps(evidence, indent=2) + "\n")
            require(process.returncode == 0, "Actual protected B1/Team Runtime compile failed: " + define)
        for path, expected in guarded.items():
            require(digest(path.read_bytes()) == expected, "A protected input changed during validation: " + str(path))
        evidence.update(status="passed", protected_inputs_unchanged=True)
        (ARTIFACTS / "api-inventory.json").write_text(json.dumps(evidence, indent=2) + "\n")
        return evidence
    finally:
        shutil.rmtree(work)


def verify_clean_checkout(args):
    (sources, _, _, _), mode, overlays = select_sources(args)
    work = ARTIFACTS / ("clean-checkout-" + uuid.uuid4().hex)
    work.mkdir(parents=True)
    try:
        for relative, data in sources.items():
            target = work / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        tests = work / "tests/interface"
        tests.mkdir(parents=True, exist_ok=True)
        for name in PROJECTS + ("stage_followup.py",):
            shutil.copyfile(ROOT / "tests/interface" / name, tests / name)
        require(not (work / ".squad").exists(), "Clean reproduction must not contain authoring runtime state.")
        command = [sys.executable, "-B", str(tests / "stage_followup.py")]
        environment = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                           PYTHONDONTWRITEBYTECODE="1")
        process = subprocess.run(command, cwd=work, env=environment, capture_output=True, text=True, timeout=120)
        (ARTIFACTS / "clean-checkout-default.log").write_text(process.stdout + process.stderr)
        require(process.returncode == 0, "Default permanent command failed from clean release-source layout.")
        report = json.loads((tests / ".artifacts/stage-followup/result-guards.json").read_bytes())
        require(report["status"] == "passed" and not (work / ".squad").exists(),
                "Clean default may not depend on protected snapshots or original assets.")
        evidence = {"status": "passed", "source_mode": mode, "source_scripts": len(sources),
                    "dependency_overlays": overlays, "default_command": "python3 -B tests/interface/stage_followup.py",
                    "child_guard_tests": report["guard_tests"], "authoring_runtime_state_present": False,
                    "original_art_required": False, "Unity_required_for_default": False, "git_commands_used": False}
        if args.unity_editor_data:
            api_command = command + ["--unity-editor-data", str(args.unity_editor_data.resolve(strict=True))]
            api = subprocess.run(api_command, cwd=work, env=environment, capture_output=True, text=True, timeout=180)
            (ARTIFACTS / "clean-checkout-API.log").write_text(api.stdout + api.stderr)
            require(api.returncode == 0, "Optional API command failed from clean release source without snapshot options.")
            result = json.loads((tests / ".artifacts/stage-followup/result-api.json").read_bytes())
            inventory = result["actual_Unity_API"]
            require(inventory["status"] == "passed" and inventory["source_mode"] == "current-release-checkout" and
                    not inventory["dependency_overlays"], "Clean optional API must use only the release checkout, no hidden roots/overlays.")
            (ARTIFACTS / "clean-release-api-inventory.json").write_text(json.dumps(inventory, indent=2) + "\n")
            evidence["optional_current_release_API"] = {
                "status": "passed", "profiles": [item["defines"] for item in inventory["results"]],
                "scripts": inventory["scripts"], "owned_UI": len(inventory["complete_owned_UI_file_sha256"]),
                "snapshot_arguments_or_hidden_overlays": False}
        return evidence
    finally:
        shutil.rmtree(work)


def main(argv=None):
    global SOURCE_ROOT
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity-editor-data", type=Path, help="Actual Editor/Data directory; API compilation only.")
    parser.add_argument("--source-root", type=Path, default=ROOT, help="Current clean release source root; defaults to this checkout.")
    parser.add_argument("--core-root", type=Path, help="EXPLICIT authoring snapshot Core root; requires all snapshot options.")
    parser.add_argument("--team-root", type=Path, help="EXPLICIT frozen Team root; no implicit cache/root fallback.")
    parser.add_argument("--base-root", type=Path, help="EXPLICIT protected base33 root.")
    parser.add_argument("--team-manifest", type=Path, help="EXPLICIT final Team hash manifest.")
    parser.add_argument("--dependency-overlay", type=Path, help="EXPLICIT parent SourceAnimationLibraryAsset.cs initializer-only overlay.")
    parser.add_argument("--dependency-overlay-sha256", help="Trusted SHA256 of the explicit overlay; required with overlay path.")
    parser.add_argument("--verify-clean-checkout", action="store_true",
                        help="Run the copied permanent default in a clean selected release layout without .squad/assets/Unity.")
    args = parser.parse_args(argv)
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    report = {"schema": "source-interface-stage-followup-checks/v2", "status": "failed",
              "source_profile": "explicit-contact272-or-contact281-c2-services",
              "whole_game_parity_claimed": False, "native_editor_browser_executed": False}
    try:
        args.source_root = args.source_root.resolve(strict=True)
        SOURCE_ROOT = args.source_root
        expected_owned_sources(SOURCE_ROOT)
        snapshot = validate_snapshot_options(args.core_root, args.team_root, args.base_root, args.team_manifest)
        if args.dependency_overlay is not None or args.dependency_overlay_sha256 is not None:
            dependency_bytes(args.dependency_overlay, args.dependency_overlay_sha256)
        result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(CallerGuards))
        require(result.wasSuccessful(), "Actual Runtime caller/isolated mutation guards failed.")
        report.update(guard_contracts=47, rejected_actual_source_guard_mutants=32,
                      rejected_actual_service_guard_mutants=15, rejected_actual_factory_guard_mutants=8,
                      hud_scope_mutants_rejected=1, owned_inventory_mutants_rejected=8,
                      root_overlay_project_negative_guards=11, guard_tests=result.testsRun,
                      source_mode="explicit-protected-snapshots" if snapshot else "current-release-checkout",
                      implicit_protected_state_required=False,
                      runtime_sha256=digest((SOURCE_ROOT / RUNTIME).read_bytes()),
                      playhud_sha256=digest((SOURCE_ROOT / HUD).read_bytes()),
                      sound_factory_sha256={name: digest((SOURCE_ROOT / name).read_bytes())
                                            for name, _, _ in SOUND_FACTORIES})
        if args.verify_clean_checkout:
            report["clean_checkout_default"] = verify_clean_checkout(args)
        if args.unity_editor_data:
            report["actual_Unity_API"] = compile_actual(args.unity_editor_data.resolve(strict=True), args)
        report["status"] = "passed"
        print(json.dumps({"status": report["status"], "guard_contracts": report["guard_contracts"],
                          "rejected_guard_mutants": report["rejected_actual_source_guard_mutants"] +
                              report["rejected_actual_service_guard_mutants"] +
                              report["rejected_actual_factory_guard_mutants"],
                          "hud_scope_mutants": report["hud_scope_mutants_rejected"],
                          "owned_inventory_mutants": report["owned_inventory_mutants_rejected"],
                          "actual_Unity_API_compiled": args.unity_editor_data is not None, "native_claimed": False}))
        return 0
    except (AssertionError, OSError, ValueError, ET.ParseError, subprocess.SubprocessError) as error:
        report["error"] = str(error)
        inventory = ARTIFACTS / "api-inventory.json"
        if args.unity_editor_data and inventory.is_file():
            report["actual_Unity_API"] = json.loads(inventory.read_bytes())
        print("Source stage caller gate failed: " + str(error), file=sys.stderr)
        return 1
    finally:
        encoded = json.dumps(report, indent=2) + "\n"
        (ARTIFACTS / "result.json").write_text(encoded)
        (ARTIFACTS / ("result-api.json" if args.unity_editor_data else "result-guards.json")).write_text(encoded)


if __name__ == "__main__":
    raise SystemExit(main())
