"""Deterministic inputs only. Expected values always come from the compiled original."""
from itertools import product
import struct


def binary32(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


def neighbors(value):
    value = binary32(value)
    bits = struct.unpack("<I", struct.pack("<f", value))[0]
    if bits in (0, 0x80000000):
        return [-binary32(2 ** -149), value, binary32(2 ** -149)]
    return [struct.unpack("<f", struct.pack("<I", bits + delta))[0] for delta in (-1, 0, 1)]


def valid_dispatch(play_type, cause, ball):
    return not (cause == 5 and (
        (play_type == 0 and ball == 3)
        or (play_type == 1 and ball not in (1, 3, 6))
    ))


def dispatch_input(play_type=0, cause=1, flags=0, side=0, ball=0, field_end=50,
                   end_x=123.25, fumble_x=0.0, previous=-123):
    return {
        "play_type": play_type, "cause": cause, "flags": flags, "offense_side": side,
        "ball_type": ball, "field_end": field_end, "play_end_x": binary32(end_x),
        "fumble_x": binary32(fumble_x), "previous_result": previous,
    }


def clock_input(packed=0x1f00, quarter=0, play_time=0, side=0, active=True,
                attract=False, adjustment=2, read_error=False, initialize=False):
    return {
        "packed_time": packed, "quarter_index": quarter, "play_time": play_time,
        "offense_side": side, "clock_active": active, "is_attract_mode": attract,
        "time_adjustment": adjustment, "adjustment_read_error": read_error,
        "time_of_possession": [13, 19], "original_initialization": initialize,
    }


def command(kind, data):
    if kind == "pat":
        return " ".join(map(str, ["pat", *[data[name] for name in (
            "side", "human", "quarter", "packed", "offense_score", "defense_score", "choice",
            "go_draw", "free_draw",
        )]]))
    if kind == "resolve":
        return " ".join(map(str, ["resolve", *[
            data[name] for name in (
                "play_type", "cause", "ball_type", "flags", "offense_side", "initial_side",
                "last_possession", "down", "los", "first_down", "play_end_x", "play_end_z",
                "fumble_x", "no_first_down", "previous_result", "quarter_index", "home_score",
                "away_score", "first_possession", "captain", "packed_time",
            )
        ]]))
    if kind in ("classify", "spot"):
        return " ".join(map(str, [
            kind, data["play_type"], data["cause"], data["flags"], data["offense_side"],
            data["ball_type"], data["field_end"], data["play_end_x"],
            data["fumble_x"], data["previous_result"],
        ]))
    if kind in ("clock", "clock_pair"):
        return " ".join(map(str, [
            "clock-pair" if kind == "clock_pair" else "clock-init" if data["original_initialization"] else "clock",
            data["packed_time"], data["quarter_index"], data["play_time"],
            data["offense_side"], int(data["clock_active"]),
            0 if data["is_attract_mode"] else 2, data["time_adjustment"],
            int(data["adjustment_read_error"]), *data["time_of_possession"], 2 if kind == "clock_pair" else 1,
        ]))
    if kind in ("field", "world"):
        return f'{kind} {data["offense_side"]} {data["value"]}'
    if kind == "discard":
        return f'discard {data["packed_time"]}'
    if kind == "onside":
        return " ".join(map(str, ["onside", data["quarter_index"], data["packed_time"],
                                 data["offense_side"], data["kicking_score"], data["receiving_score"],
                                 data["raw_rand_0"], data["raw_rand_1"]]))
    if kind == "point":
        return "point " + " ".join(map(str, data["a"] + data["b"] + data["point"]))
    if kind == "line":
        return "line " + " ".join(map(str, data["a"] + data["b"] + data["c"] + data["d"] + [data["distance"]]))
    raise ValueError("Unknown original oracle input kind: " + kind)

def caller_inputs():
    rows = []

    def add(identity, side, trigger="carrier-oob", **changes):
        data = dict(play_type=0, cause=0, ball_type=0, flags=0, offense_side=side,
                    initial_side=side, last_possession=side, down=1, los=40, first_down=70,
                    play_end_x=0.0, play_end_z=190.0, fumble_x=0.0, no_first_down=0,
                    previous_result=0, quarter_index=0, home_score=0, away_score=0,
                    first_possession=0, captain=0, packed_time=0x13b06,
                    trigger=trigger, actor_x=0.0, actor_z=190.0, held_ticks=19)
        data.update(changes)
        for name in ("play_end_x", "play_end_z", "fumble_x", "actor_x", "actor_z"):
            data[name] = binary32(data[name])
        rows.append(dict(id=identity, kind="resolve", input=data))

    for side in (0, 1):
        label = "Home" if side == 0 else "Away"
        prefix = "R1/" + label + "/"
        for down in (1, 2, 3, 4):
            add(prefix + "down" + str(down) + "-short", side, down=down)
            add(prefix + "incomplete-down" + str(down), side, "pass-ground",
                cause=4, play_end_z=0.0, actor_z=0.0, down=down)
        add(prefix + "fourth-down-converted", side, down=4, first_down=50)
        add(prefix + "goal-to-go-first-down", side, first_down=80, los=60,
            actor_x=234.0 if side == 0 else -234.0, play_end_x=234.0 if side == 0 else -234.0)
        add(prefix + "no-first-down-turnover", side, down=4, first_down=100, no_first_down=1)
        add(prefix + "upper-clamp-turnover", side, down=4, first_down=100,
            actor_x=468.0 if side == 0 else -468.0, play_end_x=468.0 if side == 0 else -468.0)
        for ordinal, x in enumerate((-8.0, -7.999, -0.001, 0.0, 0.001, 7.999, 8.0)):
            add(prefix + "round-boundary-" + str(ordinal), side, down=3, los=49, first_down=51,
                actor_x=x, play_end_x=x)
        add(prefix + "unestablished-catch-returns-initial", side, cause=5,
            initial_side=1 - side, last_possession=1 - side, held_ticks=1)
        add(prefix + "established-possession-repairs-flag", side, initial_side=1 - side)
        add(prefix + "recovered-own-EZ-touchback", side, cause=2,
            initial_side=1 - side, flags=1, actor_x=-405.0 if side == 0 else 405.0,
            play_end_x=-405.0 if side == 0 else 405.0)
        add(prefix + "left-and-returned-own-EZ-safety", side, cause=2,
            initial_side=1 - side, flags=3, actor_x=-405.0 if side == 0 else 405.0,
            play_end_x=-405.0 if side == 0 else 405.0)
        add(prefix + "forward-fumble-OOB-retains-native-fpend", side, "ball-oob",
            cause=5, ball_type=1, play_end_z=190.0,
            fumble_x=-78.0 if side == 0 else 78.0)
        pat = dict(play_type=4, down=-1, los=90, first_down=100)
        add("S1/" + label + "/conversion-offense-OOB", side, **pat)
        add("S1/" + label + "/conversion-offense-touchdown", side, "carrier-td",
            cause=3, play_end_z=0.0, actor_x=405.0 if side == 0 else -405.0, actor_z=0.0, **pat)
        add("S1/" + label + "/conversion-defensive-touchdown", side, "carrier-td",
            cause=3, initial_side=1 - side, flags=8, play_end_z=0.0,
            actor_x=405.0 if side == 0 else -405.0, actor_z=0.0, **pat)
        add("S1/" + label + "/conversion-recovered-own-EZ-fails", side,
            cause=2, initial_side=1 - side, flags=9,
            actor_x=-405.0 if side == 0 else 405.0, play_end_x=-405.0 if side == 0 else 405.0, **pat)
        add("S1/" + label + "/conversion-left-own-EZ-safety", side,
            cause=2, initial_side=1 - side, flags=11,
            actor_x=-405.0 if side == 0 else 405.0, play_end_x=-405.0 if side == 0 else 405.0, **pat)
        for ball, name in ((0, "pass"), (1, "fumble"), (6, "lateral")):
            add("S1/" + label + "/conversion-own-EZ-OOB-" + name, side, "ball-oob",
                cause=5, ball_type=ball, initial_side=1 - side, flags=9,
                actor_x=-405.0 if side == 0 else 405.0, play_end_x=-405.0 if side == 0 else 405.0, **pat)
    return rows

def pat_inputs():
    cases = (
        ("human-q2-lead15-no-miss", 1, 2, 0x100, 15, 0, 1, 0, 0),
        ("human-q3-lead14-no-miss", 1, 3, 0x100, 14, 0, 1, 0, 0),
        ("human-q3-lead15-draw0-miss", 1, 3, 0x100, 15, 0, 1, 0, 0),
        ("human-q3-lead15-draw1-miss", 1, 3, 0x100, 15, 0, 1, 0, 1),
        ("human-q3-lead15-draw2-good", 1, 3, 0x100, 15, 0, 1, 0, 2),
        ("human-q3-lead15-draw99-good", 1, 3, 0x100, 15, 0, 1, 0, 99),
        ("human-q3-expired-leading-skip", 1, 3, 0, 15, 0, 1, 0, 0),
        ("human-q3-expired-lead1-skip-even-two-choice", 1, 3, 0, 1, 0, 2, 0, 0),
        ("human-q2-expired-lead1-still-kicks", 1, 2, 0, 1, 0, 1, 0, 0),
        ("human-q3-expired-tied-winning-one", 1, 3, 0, 0, 0, 1, 0, 0),
        ("human-q3-expired-down1-tying-one", 1, 3, 0, 0, 1, 1, 0, 0),
        ("human-q3-expired-down2-still-loses", 1, 3, 0, 0, 2, 1, 0, 0),
        ("human-q3-expired-tied-two-play", 1, 3, 0, 0, 0, 2, 0, 0),
        ("human-OT3-expired-tied-winning-one", 1, 6, 0, 0, 0, 1, 0, 0),
        ("cpu-q1-lead9-no-miss", 0, 1, 0x100, 9, 0, 0, 0, 0),
        ("cpu-q2-lead8-go9-no-free-draw", 0, 2, 0x100, 8, 0, 0, 9, 0),
        ("cpu-q2-lead9-free0-miss", 0, 2, 0x100, 9, 0, 0, 0, 0),
        ("cpu-q2-lead9-free2-miss", 0, 2, 0x100, 9, 0, 0, 0, 2),
        ("cpu-q2-lead9-free3-good", 0, 2, 0x100, 9, 0, 0, 0, 3),
        ("cpu-q2-lead9-free99-good", 0, 2, 0x100, 9, 0, 0, 0, 99),
        ("cpu-q3-expired-leading-skip", 0, 3, 0, 9, 0, 0, 0, 0),
        ("cpu-q3-expired-tie-go0-still-draws", 0, 3, 0, 0, 0, 0, 0, 0),
        ("cpu-q3-expired-down2-go9-two", 0, 3, 0, 0, 2, 0, 9, 0),
        ("cpu-q3-expired-down9-no-go-draw", 0, 3, 0, 0, 9, 0, 0, 0),
        ("cpu-down7-go1-two", 0, 0, 0x10000, 0, 7, 0, 1, 0),
        ("cpu-down7-go2-free", 0, 0, 0x10000, 0, 7, 0, 2, 0),
        ("cpu-up4-go7-two", 0, 0, 0x10000, 4, 0, 0, 7, 0),
        ("cpu-up4-go8-free", 0, 0, 0x10000, 4, 0, 0, 8, 0),
        ("cpu-up8-go8-two", 0, 0, 0x10000, 8, 0, 0, 8, 0),
        ("cpu-up8-go9-free", 0, 0, 0x10000, 8, 0, 0, 9, 0),
    )
    return [dict(id="S1/PAT/" + ("Home" if side == 0 else "Away") + "/" + row[0],
                 kind="pat", input=dict(zip(
                     ("side", "human", "quarter", "packed", "offense_score", "defense_score",
                      "choice", "go_draw", "free_draw"), (side, *row[1:]))))
            for side in (0, 1) for row in cases]


def inputs(full_matrix=False):
    rows = []

    def add(kind, data, label=None):
        rows.append({"id": label or f"{kind}.{len(rows):06d}", "kind": kind, "input": data})

    add("clock", clock_input(packed=0, initialize=True), "T1.original-initialization")
    add("clock", clock_input(), "T1.default-second")
    add("clock", clock_input(quarter=1), "T1.late-half")
    add("clock", clock_input(packed=0x2800, play_time=17), "T1.long-play")
    add("clock", clock_input(adjustment=-1), "T1.bad-adjustment")
    packed_values = [0, 1, 24, 0x100, 0x101, 0x1e00, 0x1f00, 0x2000, 0x2800,
                     0x3b00, 0x10000, 0x13b18, 0x20000]
    if full_matrix:
        for packed, quarter, play_time, side, active, attract, adjustment, error in product(
            packed_values, [0, 1, 2, 3, 4, 6], [0, 16, 17, 18], [0, 1],
            [False, True], [False, True], [-1, 0, 1, 2, 3, 4, 5], [False, True],
        ):
            add("clock", clock_input(packed, quarter, play_time, side, active, attract, adjustment, error))
    else:
        for packed, quarter, play_time, side, adjustment in product(
            packed_values, [0, 1, 3, 4], [0, 17], [0, 1], [-1, 0, 1, 2, 3, 4, 5],
        ):
            add("clock", clock_input(packed, quarter, play_time, side, adjustment=adjustment))
        for active, attract, error in product([False, True], [False, True], [False, True]):
            add("clock", clock_input(active=active, attract=attract, read_error=error))
    for packed, quarter, play_time, side, active in product(
        [24, 0x100, 0x1f00, 0x2800, 0x10000, 0x13b18, 0x20000],
        [0, 1, 3, 4], [0, 17], [0, 1], [False, True],
    ):
        add("clock_pair", clock_input(packed, quarter, play_time, side, active))

    flag_values = range(16) if full_matrix else [0, 1, 2, 4, 8, 9, 11, 15]
    for play_type, cause, flags, side, ball, field_end in product(
        range(5), range(6), flag_values, [0, 1], range(7), [-1, 0, 1, 49, 99, 100, 101],
    ):
        if not valid_dispatch(play_type, cause, ball):
            continue
        fumbles = [-390.0, 0.0, 390.0] if cause == 5 and play_type in (0, 1) and ball in (1, 6) else [0.0]
        for fumble in fumbles:
            add("classify", dispatch_input(play_type, cause, flags, side, ball, field_end, fumble_x=fumble))
    # Unrelated source flags must not accidentally act like the four classifier bits.
    for play_type, cause, side in product([0, 1, 4], range(6), [0, 1]):
        ball = 3 if play_type == 1 and cause == 5 else 1
        add("classify", dispatch_input(play_type, cause, 0x7fff0, side, ball))

    positions = [
        -468.1, -468, -390.01, -389.99, -389, -382.21, -382.2, -382.19,
        -7.81, -7.8, -7.79, -1, -0.99, -0.00001, 0, 0.00001, 0.99, 1,
        7.79, 7.8, 7.81, 382.19, 382.2, 382.21, 389, 389.99, 390.01, 468, 468.1,
    ]
    for anchor in [-390, -382.2, -7.8, -1, 0, 1, 7.8, 382.2, 390]:
        positions.extend(neighbors(anchor))
    # Preserve negative zero as an explicit separate binary32 parser/rounding input.
    positions = sorted(set(binary32(value) for value in positions)) + [-0.0]
    for side, value in product([0, 1], positions):
        add("field", {"offense_side": side, "value": value})
    for side, yards in product([0, 1], [-10, 0, 1, 20, 49, 50, 51, 80, 99, 100, 110]):
        add("world", {"offense_side": side, "value": yards})
    for side, play_type, cause, flags, position in product(
        [0, 1], [0, 1, 4], range(6), [0, 4, 8, 9, 11], positions,
    ):
        balls = [0, 2, 4] if cause == 4 else (
            [1, 3, 6] if cause == 5 and play_type == 1
            else [0, 1, 2, 4, 5, 6] if cause == 5 else [0]
        )
        for ball in balls:
            add("spot", dispatch_input(play_type, cause, flags, side, ball, end_x=position, fumble_x=-123.4))

    for packed in [0, 1, 24, 0xff, 0x100, 0x1f22, 0x13b18, 0x20000, 0x00ffffff]:
        add("discard", {"packed_time": packed})
    for quarter, packed, side, deficit, first, second in product(
        range(7), [0, 0x3bff, 0x3c00, 0x10000], [0, 1], [-8, -1, 0, 1, 7, 8, 16],
        [0, 1, 32, 33, 34, 66, 99, 2147483647], [0, 1, 4, 5, 6, 10, 2147483647],
    ):
        add("onside", {
            "quarter_index": quarter, "packed_time": packed, "offense_side": side,
            "kicking_score": 20, "receiving_score": 20 + deficit,
            "raw_rand_0": first, "raw_rand_1": second,
        })
    for end, point in [
        ([10, 0, 0], [5, 3, 0]), ([10, 0, 0], [-5, 0, 0]),
        ([10, 0, 0], [15, 0, 0]), ([1, 2, 3], [4, 5, 6]),
        ([0, 0, 0], [1, 0, 0]),
    ]:
        add("point", {"a": [0, 0, 0], "b": end, "point": point})
    line_pairs = [
        ([0, 0, 0], [10, 0, 0], [5, -5, 0], [5, 5, 0]),
        ([0, 0, 0], [10, 0, 0], [0, 2, 0], [10, 2, 0]),
        ([0, 0, 0], [1, 0, 0], [10, 0, 0], [11, 0, 0]),
        ([0, 0, 0], [1, 0, 0], [2, 2, 0], [2, 3, 0]),
        ([0, 0, 0], [1, 1, 1], [1, 0, 1], [2, -1, 3]),
    ]
    for (a, b, c, d), distance in product(line_pairs, [0, 0.1, 1.9999, 2, 2.0001, 4]):
        add("line", {"a": a, "b": b, "c": c, "d": d, "distance": distance})
    return rows
