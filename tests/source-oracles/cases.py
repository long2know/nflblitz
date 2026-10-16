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
