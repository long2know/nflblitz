"""Bounded three-way execution and exact comparison, with no current-host golden."""
from contextlib import contextmanager
from dataclasses import dataclass, field
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import resource
import selectors
import shutil
import socket
import stat
import subprocess
import time
import uuid

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
LABELS = ("A", "B", "C")
NAMES = ("main40", "current", "near-frame-control")
OUTPUTS = ("default-host.jsonl", "default-rng.json", "default-menu.json")
OUTPUT_CAPS = (64 * 1024 * 1024, 1024 * 1024, 256 * 1024)
LOG_CAP = 128 * 1024
BODY_PATH = "Assets/Scripts/Gameplay/SourceAnimationState.cs"
HISTORICAL = "6816d64440ac83e4228247a8a571ab930850b5a5"
OLD_BODY = "189bc419161b118e471c83877bcee7a872db0eb65f4bc609b3ffb0f8b2ba519f"
CURRENT_BODY = "4f7fac4740f09e070709748beb8a1deb8f56a6902b55dc5f8e3512dcc8674380"
DRIVER_SHA = "fef927d3703a2e12dca051a3d99a77e810dfd592a46a6b1e43ffc03d8c19daed"
IMAGE_CAP = 16 * 1024 * 1024
PURE_CONTRACT = "ANIM.C:862-881/compiled-Fraction-scalars/v1"
PURE_CASES = (
    "nonstreaming", "selected-frame-zero", "positive-near-binary-fraction",
    "immediately-below-threshold", "strict-threshold", "ordinary-binary-fraction",
    "negative-near", "negative-clamp",
)
PRODUCTION_MAPS = (
    "631de24752a8b7657a71d77bf75241955291dd99c4009573f41e461f9ba35048",
    "0edf7ab158facf5c8f8cb2f89d0fa9c7819084e9c457070efb1d26c7e035d405",
)


def require(value, code):
    if not value:
        raise AssertionError("DEFAULT DIAGNOSTIC: " + code)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def pe_module():
    spec = importlib.util.spec_from_file_location("default_diagnostic_pe", HERE / "default_pe.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":")).encode()


def child_succeeded(result, label, compiler):
    good = result["exit"] == 0 and (
        all(row["bytes"] == 0 for row in result["diagnostics"].values()) if compiler
        else result["diagnostics"]["stderr"]["bytes"] == 0)
    require(good, ("compiler" if compiler else "driver") + " exit/diagnostics " + label + " " +
            json.dumps(result, sort_keys=True))


def file_seal(path, maximum=None):
    path = Path(path)
    require(path.is_absolute() and path.resolve() == path, "canonical file path required")
    before = path.lstat()
    require(stat.S_ISREG(before.st_mode) and before.st_nlink == 1, "regular nonalias file required")
    require(maximum is None or 0 < before.st_size <= maximum, "file byte cap")
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for data in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(data)
    after = path.lstat()
    require((before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) ==
            (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns), "file changed while sealing")
    return dict(path=str(path), bytes=before.st_size, sha256=h.hexdigest())


def tool_seals(executable, compiler, references):
    paths = {Path(executable), Path(compiler), *map(Path, references)}
    for suffix in (".runtimeconfig.json", ".deps.json"):
        paths.add(Path(compiler).with_suffix(suffix))
    deps = json.loads(Path(compiler).with_suffix(".deps.json").read_bytes())
    for target in deps["targets"].values():
        for library in target.values():
            for kind in ("runtime", "resources"):
                for name in library.get(kind, {}):
                    relative = Path(name)
                    require(not relative.is_absolute() and ".." not in relative.parts, "compiler dependency path")
                    paths.add(Path(compiler).parent / relative)
    return {str(path): file_seal(path) for path in sorted(paths)}


def input_seals(args):
    result = {}
    roots = (Path(args.fixture).absolute().parent, Path(args.m1).absolute())
    for root in roots:
        require(root.resolve() == root and root.is_dir(), "explicit fixture/M1 root required")
        for current, directories, files in os.walk(root, followlinks=False):
            require(all(not (Path(current) / name).is_symlink()
                        and name != ".env" and not name.startswith(".env.") for name in directories),
                    "input directory symlink or secret-shaped path")
            for name in files:
                require(name != ".env" and not name.startswith(".env."), "secret-shaped input path refused")
                path = Path(current) / name
                result[str(path)] = file_seal(path)
                require(len(result) <= 2048, "input file-count cap")
    for name in ("fixture", "sound", "impact", "normal", "speech", "hud"):
        path = Path(getattr(args, name)).absolute()
        result[str(path)] = file_seal(path)
    return result


def method_span(data, signature):
    anchor = b"        " + signature + b"\n"
    require(data.count(anchor) == 1, "unique exact source method required")
    begin = data.index(anchor)
    end = data.index(b"\n        }\n", begin) + len(b"\n        }\n")
    return begin, end


def method(data, signature):
    start, end = method_span(data, signature)
    return data[start:end]


def brace_body(data):
    return data[data.index(b"{"):data.rindex(b"}") + 1]


def historical_fraction():
    def git(*args):
        return subprocess.run(["git", "--no-pager", *args], cwd=ROOT,
                              capture_output=True, check=True, timeout=30).stdout
    try:
        git("cat-file", "-e", HISTORICAL + "^{commit}")
    except subprocess.CalledProcessError:
        git("fetch", "--no-tags", "--depth=1", "origin", HISTORICAL)
    entry = git("ls-tree", HISTORICAL, "--", BODY_PATH).decode().strip().split()
    require(len(entry) == 4 and entry[0] == "100644" and entry[1] == "blob"
            and entry[3] == BODY_PATH, "historical regular blob required")
    data = git("show", HISTORICAL + ":" + BODY_PATH)
    require(hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest() == entry[2],
            "historical blob authentication")
    return data, entry[2]


def one_body_control(current, historical):
    old = method(historical, b"private float Fraction()")
    new = method(current, b"private float Fraction()")
    require(sha(brace_body(old)) == OLD_BODY and sha(brace_body(new)) == CURRENT_BODY, "Fraction body pins")
    begin, end = method_span(current, b"private float Fraction()")
    result = current[:begin] + old + current[end:]
    require(method(result, b"private void StreamFrame(ActorState actor)") ==
            method(current, b"private void StreamFrame(ActorState actor)"), "StreamFrame must remain current")
    require(result[:begin] == current[:begin] and result[begin + len(old):] == current[end:],
            "only one source body may change")
    return result


def guarded_driver(original):
    require(sha(original) == DRIVER_SHA, "exact actual DefaultChecks driver required")
    changes = (
        (b"            FullKernelInputs inputs = FullKernelInputs.Load(",
         b"            var budget = new DefaultDiagnosticBudget(args[7]);\n"
         b"            budget.VerifyCompiledNearFrameContract();\n"
         b"            FullKernelInputs inputs = FullKernelInputs.Load("),
        (b"            GameSimulation game = inputs.Create(configuration: configuration,",
         b"            GameSimulation game = budget.Construct(() => inputs.Create(configuration: configuration,"),
        (b"                compatibility: SourceGameplayCompatibility.Portable, camera: false);",
         b"                compatibility: SourceGameplayCompatibility.Portable, camera: false,\n"
         b"                randomFactory: budget.CreateRandom));\n"
         b"            budget.Constructed(game.SourceRandomService, game.State.Tick, game.SourceTick);"),
        (b"            var bridge = new FixedStepBridge(game);",
         b"            var bridge = new FixedStepBridge(game);\n"
         b"            bridge.BeforeHostStep += state => budget.BeforeHost(game.State.Tick, game.SourceTick);"),
        (b'            using (var writer = new StreamWriter(Path.Combine(args[7], "default-host.jsonl"), false, new UTF8Encoding(false)))',
         b'            using (var writer = budget.OpenText("default-host.jsonl"))'),
        (b"                    bridge.AdvanceSource(FixedStepBridge.StepSeconds, buffer.NextHostFrame);",
         b"                    budget.BeforeAdvance(game.State.Tick, game.SourceTick);\n"
         b"                    bridge.AdvanceSource(FixedStepBridge.StepSeconds, buffer.NextHostFrame);\n"
         b"                    budget.AfterAdvance(game.State.Tick, game.SourceTick);"),
        (b'            File.WriteAllText(Path.Combine(args[7], "default-rng.json"), JsonSerializer.Serialize(random.Trace));',
         b'            budget.WriteText("default-rng.json", JsonSerializer.Serialize(random.Trace));'),
        (b'            File.WriteAllText(Path.Combine(args[7], "default-menu.json"), JsonSerializer.Serialize(menuEvents));',
         b'            budget.WriteText("default-menu.json", JsonSerializer.Serialize(menuEvents));\n'
         b"            budget.Finish(random);"),
    )
    result = original
    for before, after in changes:
        require(result.count(before) == 1, "unique driver wrapper anchor")
        result = result.replace(before, after, 1)
    restored = result
    for before, after in reversed(changes):
        require(restored.count(after) == 1, "reversible driver wrapper")
        restored = restored.replace(after, before, 1)
    require(restored == original, "exact counting/I/O and pure pre-Game check driver delta")
    return result


def verify_random_source(source, client):
    game = source["Assets/Scripts/Gameplay/GameSimulation.cs"]
    exact = method(game, b"private int NextInt(int maximum)")
    compact = lambda data: b"".join(line.strip() for line in brace_body(data).splitlines())
    client_begin = client.index(b"    private int NextInt(int maximum)")
    client_end = client.index(b"\n    }\n", client_begin) + len(b"\n    }\n")
    require(compact(exact) == compact(client[client_begin:client_end]), "copied exact NextInt statements")
    for value in (b"randomState = unchecked((uint)seed) ^ 0x9E3779B9u;",
                  b'"OriginalRelease57Hz/preview"',
                  b'"Explicit legacy LCG preview provider; NOT the historical platform random()/rand() body"',
                  b'"GameSimulation.Restart(" + seed + ")"'):
        require(value in game and value in client, "same seed/provider/provenance")
    require(game.count(b"site => NextInt(16777216)") == 1, "one default source raw callback")
    for before, after in ((b'Call("construct");', b"return factory();"),
                          (b'Call("stream", seed);', b"random = new SourceInjectedRandom"),
                          (b'Call("raw");', b"int value = NextInt(16777216);"),
                          (b'owner.Call("write", id, count);', b"inner.Write(buffer, offset, count);")):
        require(client.count(before) == 1 and client.index(before) < client.index(after),
                "client pre-consumption guard")
    require(client.count(b"object.ReferenceEquals(random, actual)") == 2, "concrete injected identity guards")
    require(b"public const int TicksPerSecond = 60;" in source["Assets/Scripts/Contracts/GameplayContracts.cs"]
            and b"public const int DefaultTicksPerSecond = 57;" in source["Assets/Scripts/Contracts/SourceAnimationContracts.cs"],
            "source-derived wake prediction")
    require(source["Assets/Scripts/Runtime/SourceC2Composition.cs"].count(b"new GameSimulation(") == 1,
            "one physical M1 constructor path")
    return dict(nextIntSha256=sha(exact), clientSha256=sha(client), defaultSeed=1,
                provenance="OriginalRelease57Hz/preview; ExternalUnverified", concreteIdentityPreserved=True)


@dataclass
class Window:
    pure_reserved: int = 0
    pure_invocations: int = 0
    pure_cases: int = 0
    source_matches: int = 0
    legacy_matches: int = 0
    unrounded: int = 0
    reservation: int = 0
    constructors: int = 0
    constructed: bool = False
    advances: int = 0
    hosts: int = 0
    wakes: int = 0
    callbacks: int = 0
    values: int = 0
    requests: int = 0
    advance_open: bool = False
    host_open: bool = False
    finished: bool = False
    bytes: list = field(default_factory=lambda: [0, 0, 0])


class Graph:
    """ONE object across all three processes; refusal permanently closes it."""
    def __init__(self, labels=LABELS):
        require(tuple(labels) == LABELS, "exact three-variant graph before any constructor")
        self.windows = {label: Window() for label in LABELS}
        self.compilers, self.drivers = [], []
        self.active = None
        self.failed = False

    def check(self, condition, code):
        if not condition:
            self.failed = True
            require(False, code)

    def compile_claim(self, label):
        self.check(not self.failed and len(self.compilers) < 3
                   and label == LABELS[len(self.compilers)] and not self.drivers, "compile claim/order/retry")
        self.compilers.append(label)

    def driver_claim(self, label):
        self.check(not self.failed and self.compilers == list(LABELS) and self.active is None
                   and len(self.drivers) < 3 and label == LABELS[len(self.drivers)], "driver claim/order/retry")
        self.drivers.append(label)
        self.active = label

    def line(self, message):
        self.check(not self.failed and len(message) <= 128 and message.endswith("\n"), "counter wire bounds")
        parts = message[:-1].split(" ")
        self.check(len(parts) >= 2 and parts[0] == self.active and parts[0] in self.windows
                   and all(p.isascii() and p.isdecimal() for p in parts[2:]), "counter wire identity")
        label, operation = parts[:2]
        numbers = tuple(int(p) for p in parts[2:])
        w = self.windows[label]
        self.check(not w.finished, "finished window reused")
        if operation == "pure-start":
            self.check(numbers == (8,) and w.pure_reserved == 0 and w.reservation == 0,
                       "one finite pure scalar set before any invocation")
            w.pure_reserved = 8
        elif operation == "pure-case":
            self.check(w.pure_reserved == 8 and w.pure_cases == 0 and w.pure_invocations < 8
                       and numbers == (w.pure_invocations,), "pure invocation cap/order before actual method")
            w.pure_invocations += 1
        elif operation == "pure":
            expected = (8, 8, 6, 8) if label == "B" else (8, 6, 8, 8)
            self.check(numbers == expected and w.pure_reserved == w.pure_invocations == 8
                       and w.pure_cases == 0 and w.reservation == 0,
                       "actual compiled source/legacy Fraction proof before Game")
            w.pure_cases, w.source_matches, w.legacy_matches, w.unrounded = numbers
        elif operation == "construct":
            self.check(not numbers and w.reservation == 0 and w.pure_cases == 8,
                       "constructor cap and compiled pure proof before factory")
            w.reservation = 1
        elif operation == "stream":
            self.check(numbers == (1,) and w.reservation == 1 and w.constructors == 0
                       and not w.constructed, "constructor-observed stream/seed cap before initialization draws")
            w.constructors = 1
        elif operation == "constructed":
            self.check(numbers == (0, 0) and w.constructors == 1 and not w.constructed, "initial clocks/identity")
            w.constructed = True
        elif operation == "raw":
            self.check(not numbers and w.constructors == 1 and w.callbacks == w.values
                       and w.callbacks < 1024, "raw cap before callback")
            w.callbacks += 1
        elif operation == "value":
            self.check(not numbers and w.callbacks == w.values + 1, "raw value pairing")
            w.values += 1
        elif operation == "advance":
            self.check(w.constructed and not w.advance_open and w.advances < 1200
                       and numbers == (w.hosts, w.wakes), "Advance cap before call")
            w.advances += 1
            w.advance_open = True
        elif operation == "host":
            self.check(w.advance_open and not w.host_open and w.hosts == w.advances - 1
                       and numbers == (w.hosts, w.wakes), "physical host guard before Step")
            predicted = (w.hosts + 1) * 57 // 60
            self.check(w.hosts < 1200 and predicted <= 1140, "source-wake cap before Step")
            w.hosts, w.wakes, w.host_open = w.hosts + 1, predicted, True
        elif operation == "advanced":
            self.check(w.advance_open and w.host_open and numbers == (w.hosts, w.wakes), "actual post-step clocks")
            w.advance_open = w.host_open = False
        elif operation == "write":
            self.check(w.constructed and len(numbers) == 2 and numbers[0] in (0, 1, 2), "output identity")
            index, count = numbers
            self.check(w.bytes[index] + count <= OUTPUT_CAPS[index], "raw output cap before byte write")
            w.bytes[index] += count
        elif operation == "finish":
            self.check(len(numbers) == 3 and (w.advances, w.hosts, w.wakes) == (1200, 1200, 1140)
                       and not w.advance_open and w.callbacks == w.values
                       and numbers[1:] == (w.callbacks, w.values) and numbers[0] >= w.callbacks,
                       "full physical window and request/callback/value seals")
            w.requests, w.finished, self.active = numbers[0], True, None
        else:
            self.check(False, "unknown counter operation")
        return b"OK\n"


class Workspace:
    def __init__(self):
        # Not workspace/.scratch: no raw output is eligible for a workspace artifact upload.
        self.root = Path("/dev/shm") / ("selection-default-" + uuid.uuid4().hex[:16])
        self.root.mkdir(mode=0o700)
        self.initial = self.root.lstat()
        self.verify_dir(self.root)
        self.static_bytes = 0

    def verify_dir(self, path):
        info = path.lstat()
        require(stat.S_ISDIR(info.st_mode) and info.st_uid == os.geteuid()
                and stat.S_IMODE(info.st_mode) == 0o700, "genuinely private owned directory required")

    def write(self, relative, data):
        path = self.root / relative
        require(".." not in Path(relative).parts and not Path(relative).is_absolute(), "private output path")
        for parent in reversed(path.parents):
            if parent == self.root or self.root in parent.parents:
                if not parent.exists():
                    parent.mkdir(mode=0o700)
                self.verify_dir(parent)
        require(len(data) <= IMAGE_CAP and self.static_bytes + len(data) <= 32 * 1024 * 1024,
                "public source/metadata preparation byte cap")
        self.static_bytes += len(data)
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
        with os.fdopen(fd, "wb") as stream:
            stream.write(data)
        return path

    def close(self):
        # Only this exact fresh owned root. Never follow a link or touch an input root.
        require(self.root.lstat().st_ino == self.initial.st_ino, "private root cleanup identity")
        for current, directories, files in os.walk(self.root, followlinks=False):
            self.verify_dir(Path(current))
            require(all(not (Path(current) / name).is_symlink() for name in directories + files),
                    "private tree cleanup symlink")
        shutil.rmtree(self.root)


class Counter:
    def __init__(self, workspace, graph, label):
        self.graph, self.label = graph, label
        self.path = workspace.root / "authority.sock"
        self.socket = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.socket.bind(str(self.path))
        self.path.chmod(0o600)
        self.socket.listen(1)
        self.socket.setblocking(False)
        self.client = None
        self.buffer = bytearray()

    def receive(self, selection, kind):
        if kind == "listen":
            require(self.client is None, "second driver counter connection")
            self.client, _ = self.socket.accept()
            self.client.settimeout(2)
            selection.unregister(self.socket)
            self.socket.close()
            selection.register(self.client, selectors.EVENT_READ, "counter")
            return
        data = self.client.recv(129 - len(self.buffer))
        if not data:
            require(not self.buffer and self.graph.windows[self.label].finished, "unfinished driver disconnect")
            selection.unregister(self.client)
            self.client.close()
            return
        self.buffer.extend(data)
        require(len(self.buffer) <= 128, "counter line cap")
        while b"\n" in self.buffer:
            end = self.buffer.index(10) + 1
            message = bytes(self.buffer[:end]).decode("ascii")
            del self.buffer[:end]
            self.client.sendall(self.graph.line(message))

    def close(self):
        self.socket.close()
        if self.client is not None:
            self.client.close()
        require(stat.S_ISSOCK(self.path.lstat().st_mode), "owned counter socket")
        self.path.unlink()


def native(command, directory, environment, timeout, counter=None):
    """Normal hosted-job permissions, NOT the failed private-host filesystem sandbox."""
    process = subprocess.Popen(command, cwd=directory, env=environment, stdin=subprocess.DEVNULL,
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, close_fds=True,
                               umask=0o077)
    selection = selectors.DefaultSelector()
    logs, lengths, hashes = {}, {}, {}
    for name in ("stdout", "stderr"):
        stream = getattr(process, name)
        os.set_blocking(stream.fileno(), False)
        selection.register(stream, selectors.EVENT_READ, name)
        path = directory / ("child-" + name + ".log")
        logs[name] = os.fdopen(os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600), "wb")
        lengths[name], hashes[name] = 0, hashlib.sha256()
    if counter:
        selection.register(counter.socket, selectors.EVENT_READ, "listen")
    deadline = time.monotonic() + timeout
    try:
        while selection.get_map() or process.poll() is None:
            require(time.monotonic() < deadline, "child wall-clock cap")
            for event, _ in selection.select(0.1):
                if event.data in ("listen", "counter"):
                    counter.receive(selection, event.data)
                    continue
                name = event.data
                block = os.read(event.fd, min(65536, LOG_CAP - lengths[name] + 1))
                if not block:
                    selection.unregister(event.fileobj)
                    event.fileobj.close()
                else:
                    require(lengths[name] + len(block) <= LOG_CAP, "private diagnostic stream byte cap")
                    logs[name].write(block)
                    lengths[name] += len(block)
                    hashes[name].update(block)
            if counter and process.poll() is not None and counter.client is None:
                # CLR startup failure must not wait for a connection that can no
                # longer arrive. Drain the bounded logs, then reject the exit.
                if counter.socket.fileno() >= 0:
                    selection.unregister(counter.socket)
                    counter.socket.close()
    except BaseException:
        if process.poll() is None:
            process.kill()
        process.wait()
        raise
    finally:
        for key in list(selection.get_map().values()):
            selection.unregister(key.fileobj)
            key.fileobj.close()
        selection.close()
        for log in logs.values():
            log.close()
        if counter:
            counter.close()
    return dict(exit=process.wait(), diagnostics={
        name: dict(path=str(directory / ("child-" + name + ".log")), bytes=lengths[name],
                   sha256=hashes[name].hexdigest()) for name in ("stdout", "stderr")})


class Number(str):
    pass


def first_field(left, right, path="$"):
    if type(left) is not type(right):
        return path
    if isinstance(left, dict):
        if list(left) != list(right):
            for key in (*left, *right):
                if key not in left or key not in right:
                    return path + "/" + key.replace("~", "~0").replace("/", "~1")
            return path + "/@property-order"
        for key in left:
            found = first_field(left[key], right[key], path + "/" + key.replace("~", "~0").replace("/", "~1"))
            if found:
                return found
    elif isinstance(left, list):
        for index, (a, b) in enumerate(zip(left, right)):
            found = first_field(a, b, path + "/" + str(index))
            if found:
                return found
        if len(left) != len(right):
            return path + "/" + str(min(len(left), len(right)))
    elif left != right:
        return path
    return None


def comparison(left, right, jsonl, explain=True):
    result = dict(byteIdentical=left == right, leftBytes=len(left), rightBytes=len(right),
                  leftSha256=sha(left), rightSha256=sha(right))
    if left == right:
        return result
    result["firstByteOffsetZeroBased"] = next(
        (i for i, (a, b) in enumerate(zip(left, right)) if a != b), min(len(left), len(right)))
    if not explain:
        return result
    a, b = left, right
    if jsonl:
        a, b = left.splitlines(keepends=True), right.splitlines(keepends=True)
        row = next((i for i, (x, y) in enumerate(zip(a, b)) if x != y), min(len(a), len(b)))
        result.update(firstRowZeroBased=row, firstHostOrdinalOneBased=row + 1)
        if row >= min(len(a), len(b)):
            result["firstField"] = "$/@end-of-file"
            return result
        a, b = a[row], b[row]
        result.update(leftRowSha256=sha(a), rightRowSha256=sha(b))
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, "duplicate result JSON field")
            result[key] = value
        return result
    parse = lambda data: json.loads(data, parse_int=Number, parse_float=Number, object_pairs_hook=pairs)
    result["firstField"] = first_field(parse(a), parse(b)) or "$/@json-encoding"
    return result


def all_comparisons(blobs):
    require(set(blobs) == set(LABELS) and all(set(row) == set(OUTPUTS) for row in blobs.values()),
            "exact unfiltered three-variant output inventory")
    pairs = {a + b: {name: comparison(blobs[a][name], blobs[b][name], name.endswith(".jsonl"), False)
                    for name in OUTPUTS} for a, b in (("A", "B"), ("A", "C"), ("B", "C"))}
    # Every exact byte comparison is complete before parsing any state for a field path.
    for a, b in (("A", "B"), ("A", "C"), ("B", "C")):
        for name in OUTPUTS:
            pairs[a + b][name] = comparison(blobs[a][name], blobs[b][name], name.endswith(".jsonl"))
    reproduced = any(not row["byteIdentical"] for row in pairs["AB"].values())
    return dict(comparisons=pairs, baselineDifferenceReproduced=reproduced,
                controlMatchesBaselineAllThree=all(row["byteIdentical"] for row in pairs["AC"].values()),
                comparisonBytesUnfiltered=True, originalTargetNumericOrNativeQualification=False)


def source_correct_projection(blobs, report):
    """Historical projection plus independent current execution, never a B golden."""
    require(set(blobs) == set(LABELS), "all actual variants required")
    shape = {}
    for label in LABELS:
        require(set(blobs[label]) == set(OUTPUTS) and all(blobs[label].values()), "complete nonempty files")
        host_rows = len(blobs[label]["default-host.jsonl"].splitlines())
        random = json.loads(blobs[label]["default-rng.json"])
        menu = json.loads(blobs[label]["default-menu.json"])
        require(host_rows == 1200 and isinstance(random, list) and len(random) == 491
                and all(isinstance(row, dict) for row in random)
                and isinstance(menu, list) and len(menu) > 0, "complete host/RNG/menu shapes")
        shape[label] = dict(hostRows=host_rows, rngRecords=len(random), menuRecords=len(menu))
    for name in OUTPUTS:
        require(blobs["A"][name] == blobs["C"][name],
                "complete historical counterfactual projection differs: " + name)
    for name in OUTPUTS[1:]:
        require(blobs["A"][name] == blobs["B"][name],
                "complete actual current ordered RNG/menu differs: " + name)
    return dict(policy="source-correct-Fraction/legacy-counterfactual/v1",
                legacyCounterfactualProjection=report["comparisons"]["AC"],
                actualCurrentOrderedRandomAndMenu={name: report["comparisons"]["AB"][name] for name in OUTPUTS[1:]},
                actualHistoricalVsCurrentHost=report["comparisons"]["AB"]["default-host.jsonl"],
                outputShape=shape, currentHostGoldenUsed=False)


@contextmanager
def execute_three(args, variants, loaders, compiler, references, common_driver, client, provenance):
    """A/B are the original windows; C is the only incremental default window."""
    # No preexec_fn: this caller may be the existing post-check worker thread.
    # Core suppression is process-wide and does not alter simulation/JIT policy.
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    workspace = Workspace()
    graph = Graph()
    images, programs, rows, commands = {}, {}, {}, []
    pe = pe_module()
    try:
        require(len(references) == 167 and set(variants) == set(loaders) == set(LABELS), "exact compile graph/refset")
        executable = Path(shutil.which("dotnet") or "").resolve()
        tools = tool_seals(executable, Path(compiler).absolute(), references)
        inputs = input_seals(args)
        helpers = {name: sha((HERE / name).read_bytes())
                   for name in ("compatibility.py", "default_diagnostic.py", "default_pe.py", "DefaultBudget.cs")}
        env = dict(PATH=os.environ.get("PATH", ""), HOME=str(workspace.root / "home"),
                   DOTNET_CLI_HOME=str(workspace.root / "home"), TMPDIR=str(workspace.root / "tmp"),
                   TMP=str(workspace.root / "tmp"), TEMP=str(workspace.root / "tmp"),
                   DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_EnableDiagnostics="0",
                   DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
                   MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_USE_MSBUILD_SERVER="0")
        for directory in ("home", "tmp"):
            (workspace.root / directory).mkdir(mode=0o700)
        def unchanged():
            require(input_seals(args) == inputs, "input seals changed")
            require(all(file_seal(path) == seal for path, seal in tools.items()), "tool/ref seals changed")
            require(all(sha((HERE / name).read_bytes()) == digest for name, digest in helpers.items()),
                    "helper seals changed")
        for label in LABELS:
            graph.compile_claim(label)
            source = dict(variants[label])
            require(len(source) == 94, "exact94 production closure")
            harness = {"DefaultChecks.cs": common_driver, "FullKernelInputs.cs": loaders[label],
                       "DefaultBudget.cs": client}
            files = {**source, **harness}
            paths = [workspace.write(label + "/" + name, data) for name, data in files.items()]
            source_map = {str(path): sha(path.read_bytes()) for path in paths}
            assembly = workspace.root / label / "Default.Checks.dll"
            options = ["-nologo", "-nostdlib+", "-target:exe", "-langversion:7.3", "-warnaserror+",
                       "-deterministic+", "-optimize+", "-debug:portable", "-out:" + str(assembly),
                       "-pdb:" + str(assembly.with_suffix(".pdb")), "-main:DefaultCabinetChecks"]
            options += ["-r:" + str(path) for path in references] + [str(path) for path in paths]
            response = workspace.write(label + "/compile.rsp",
                                       ("\n".join(json.dumps(option) for option in options) + "\n").encode())
            runtime = dict(runtimeOptions=dict(tfm="net10.0",
                           framework=dict(name="Microsoft.NETCore.App", version="10.0.0")))
            runtime_path = workspace.write(label + "/Default.Checks.runtimeconfig.json", json.dumps(runtime).encode())
            unchanged()
            command = [str(executable), str(compiler), "-noconfig", "@" + str(response)]
            commands.append(command)
            started = time.monotonic()
            result = native(command, assembly.parent, env, 120)
            child_succeeded(result, label, True)
            seal = file_seal(assembly, IMAGE_CAP)
            pdb_seal = file_seal(assembly.with_suffix(".pdb"), IMAGE_CAP)
            require(all(stat.S_IMODE(path.lstat().st_mode) == 0o600
                        for path in (assembly, assembly.with_suffix(".pdb"))), "private PE/PDB file mode")
            image = pe.verify(assembly.read_bytes(), assembly.with_suffix(".pdb").read_bytes(), source_map)
            require(image["peSha256"] == seal["sha256"] and image["pdbSha256"] == pdb_seal["sha256"], "image race")
            images[label] = (seal, pdb_seal, image, source_map, file_seal(runtime_path))
            programs[label] = assembly
            rows[label] = dict(name=NAMES[LABELS.index(label)], compile=0, diagnostics="", exit=None,
                               compileSeconds=round(time.monotonic() - started, 3),
                               buildRoot=str(assembly.parent), runtimeRoot=str(assembly.parent),
                               harnessSha256={name: sha(data) for name, data in harness.items()},
                               sources={name: sha(data) for name, data in source.items()})
        # The barrier is explicit and global: no Game before every actual PE/PDB/source seal.
        require(set(images) == set(LABELS) and graph.compilers == list(LABELS), "all3 compiled image barrier")
        def image_barrier():
            unchanged()
            for label, (seal, pdb_seal, image, sources, runtime_seal) in images.items():
                require(file_seal(seal["path"]) == seal and file_seal(pdb_seal["path"]) == pdb_seal
                        and file_seal(runtime_seal["path"]) == runtime_seal, "sealed PE/PDB/runtimeconfig changed")
                require(all(file_seal(path)["sha256"] == digest for path, digest in sources.items()),
                        "compiled source changed")
                require(pe.verify(Path(seal["path"]).read_bytes(), Path(pdb_seal["path"]).read_bytes(), sources) == image,
                        "actual Main/source/PDB binding changed")
        for label in LABELS:
            image_barrier()
            graph.driver_claim(label)
            directory = programs[label].parent
            for name in OUTPUTS:
                workspace.write(label + "/" + name, b"")
            counter = Counter(workspace, graph, label)
            runtime_env = dict(env, BLITZ_DEFAULT_VARIANT=label, BLITZ_DEFAULT_AUTHORITY=str(counter.path),
                               BLITZ_DEFAULT_OUTPUT=str(directory))
            command = [str(executable), str(programs[label])] + [
                str(Path(getattr(args, name)).absolute()) for name in
                ("fixture", "sound", "m1", "impact", "normal", "speech", "hud")] + [str(directory)]
            commands.append(command)
            # Distinct paths prevent compiler log overwrite and preserve both diagnostics.
            runtime_dir = directory / "runtime"
            runtime_dir.mkdir(mode=0o700)
            started = time.monotonic()
            result = native(command, runtime_dir, runtime_env, 180, counter)
            child_succeeded(result, label, False)
            summary = json.loads(Path(result["diagnostics"]["stdout"]["path"]).read_text())
            window = graph.windows[label]
            require(summary.get("status") == "passed" and summary.get("hosts") == 1200
                    and summary.get("SourceTick") == 1140 and summary.get("defaultSeed") == 1
                    and summary.get("rawVectorFiltered") is False
                    and summary.get("originalTargetRandomQualified") is False
                    and summary.get("rawCalls") == window.requests and window.finished,
                    "actual driver summary/counters")
            require(summary["rawCalls"] == 491 and summary["selections"] == 4
                    and window.requests == window.callbacks == window.values == 491,
                    "unchanged default491/four-manual-selection anchor")
            for index, name in enumerate(OUTPUTS):
                path = directory / name
                require(path.stat().st_size == window.bytes[index] <= OUTPUT_CAPS[index], "unfiltered output byte seal")
                require(stat.S_IMODE(path.lstat().st_mode) == 0o600, "private data file mode")
            rows[label].update(exit=0, summary=summary, executeSeconds=round(time.monotonic() - started, 3))
        image_barrier()
        blobs = {label: {name: (programs[label].parent / name).read_bytes() for name in OUTPUTS} for label in LABELS}
        report = all_comparisons(blobs)
        report.update(schema="default-three-way-source-contract-comparisons/v2", status="comparisons-not-acceptance",
                      sourceControl=provenance, windows={label: vars(window) for label, window in graph.windows.items()},
                      images={label: row[2] for label, row in images.items()},
                      compiledPureContract=dict(id=PURE_CONTRACT, cases=list(PURE_CASES),
                                                casesPerVariant=8, totalInvocations=24,
                                                currentVariant="B", noGameStepsWakesRandomOrExtraCompiler=True),
                      inputsSha256=sha(canonical(inputs)), inputFiles=len(inputs),
                      toolsAndReferencesSha256=sha(canonical(tools)), helperSha256=helpers,
                      originalWindowsReusedNotRepeated=True,
                      incremental=dict(compilations=1, constructors=1, hosts=1200, wakes=1140, rawMaximum=1024),
                      prospectiveTotal=dict(compilations=3, constructors=3, hosts=3600, wakes=3420, rawMaximum=3072),
                      hostBoundary="ordinary pinned hosted-job permissions; no new Landlock/RLIMIT_FSIZE/W^X policy",
                      compilerByteBoundary="trusted pinned compiler; PE/PDB16MiB limit checked after exit before any Game, not a pre-write kernel quota",
                      resultBoundary="0700 owned shared-memory root;0600 data; before-write RPC byte caps; no workspace raw artifact",
                      cleanupPolicy="finally remove only the exact fresh owned private root, including on acceptance failure",
                      inputArgv=[dict(name=name, **inputs[str(Path(getattr(args, name)).absolute())])
                                 if name != "m1" else dict(
                                     name=name, path=str(Path(args.m1).absolute()),
                                     sha256=sha(canonical({path: seal for path, seal in inputs.items()
                                                          if Path(args.m1).absolute() in Path(path).parents})))
                                 for name in ("fixture", "sound", "m1", "impact", "normal", "speech", "hud")],
                      ownLoaderSha256={label: sha(data) for label, data in loaders.items()},
                      commonGuardedDriverSha256=sha(common_driver), commonClientSha256=sha(client),
                      exactCommands=commands, baseEnvironment=env,
                      completeInputSeals=inputs, completeToolAndReferenceSeals=tools,
                      completeCompiledSourceSeals={label: row[3] for label, row in images.items()},
                      referenceFiles=[str(path) for path in references],
                      compilerPath=str(compiler), dotnetPath=str(executable))
        yield dict(rows=rows, commands=commands, report=report, blobs=blobs)
    except BaseException:
        graph.failed = True
        raise
    finally:
        workspace.close()
