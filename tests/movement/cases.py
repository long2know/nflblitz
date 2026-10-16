"""Discriminating source inputs only. All expected movement values come from executing C."""
import copy
import itertools
import re
import struct

FLOAT_FIELDS = ("X", "Y", "Z", "VelocityX", "VelocityY", "VelocityZ", "Friction", "OverrideSpeed")
INT_FIELDS = (
    "PlayerId", "Station", "Mode", "Job", "Flags", "LowerMode", "UpperMode", "Heading", "TargetHeading",
    "UpperHeading", "UpperTargetHeading", "TurnRate", "UpperTurnRate", "UpperTurnHack",
    "LowerVariableFrame", "LowerVariableCount", "UpperVariableFrame", "UpperVariableCount",
    "Stick", "PreviousStick", "StickTime", "Buttons", "OldButtons", "NewButtons", "NonTurboTime",
    "NonATime", "NonBTime", "BackpedalExpireTime", "AttackTime", "AttackMode", "CelebrateTime",
    "OverrideTime", "NoCatchTime", "DroneFace", "TeamSpinCount", "TeamHurdleCount",
)


def case(name, operation, state=None, context=None, raw=None):
    s = dict.fromkeys(INT_FIELDS, 0)
    s.update(dict.fromkeys(FLOAT_FIELDS, 0.0))
    s.update(PlayerId=3, Station=0, Mode=7, Flags=2, Stick=21, PreviousStick=24,
             TurnRate=64, UpperTurnRate=48, LowerSequence="RUN", UpperSequence="RUN")
    if state:
        s.update(state)
    c = dict(
        SourceTick=1000, Execute=1, OffenseSide=0, Phase=2, GameFlags=0, PlayType=0,
        OffensePlayFlags=0, DefensePlayFlags=0, Carrier=3, CarrierMode=7, BallType=0,
        IntendedReceiver=-1, BallTicksLeft=11, BallX=0.0, BallY=0.0, BallDistance=100.0,
        CameraHeading=0, LineOfScrimmage=50, LineZ=0.0, MotionSnapX=0.0, BackpedalTargetHeading=-1,
        TeamIndexedTurbo=100.0, DefenseFire=0, SpeedPowerup=0, FastTurboPowerup=0,
        RunOutOfBoundsPowerup=0, ViolencePowerup=0, CarrierLateHittable=0, CarrierDistance=100.0,
        BallTargetX=0.0, DroneScript="juke_c_anim", DroneScriptFullBody=1,
        Distances=[100.0] * 14, Bearings=[512] * 14, Handicap=5, Field=0, BreathControl=0,
        EnteredControlBlock=1, InputStick=24, InputButtons=0, InputBackpedal=0, InputTurbo=100.0,
        StationTurbo=100.0, StationActive=1, StationControlled=1, StationFire=0,
        StationInfinite=0, StationHeld=0, StationDefends=0, ChangeValue=0, StaticSpeedAttribute=0,
        DefenderModes=[3, 3, 3, 9, 9, 9, 9],
    )
    if context:
        c.update(context)
    if c["Carrier"] == s["PlayerId"]:
        c["CarrierMode"] = s["Mode"]
    if operation in ("input", "step") and s["Station"] == s["PlayerId"] // 7:
        c["TeamIndexedTurbo"] = c["InputTurbo"]
    return {"Name": name, "Operation": operation, "State": s, "Context": c, "Raw": list(raw if raw is not None else [0] * 256)}


def command(row):
    tokens = ["Op=" + row["Operation"]]
    # Player identity precedes every other field so all pointers resolve to the right source actor.
    for name, value in row["State"].items():
        tokens.append("s." + name + "=" + (str(value) if value != "" else "-"))
    for name, value in row["Context"].items():
        if name in ("SourceTick", "Distances", "Bearings", "DefenderModes"):
            continue
        tokens.append("c." + name + "=" + str(value))
    for i, value in enumerate(row["Context"]["Distances"]):
        tokens.append("n%dDistance=%s" % (i, value))
    for i, value in enumerate(row["Context"]["Bearings"]):
        tokens.append("n%dBearing=%s" % (i, value))
    if row["Operation"] == "defense":
        for i, value in enumerate(row["Context"]["DefenderModes"]):
            tokens.append("d%dMode=%s" % (i, value))
    if row["Operation"] == "leap":
        for name, value in row["Leap"].items():
            if name == "Matrix":
                tokens.extend("l.Matrix%d=%s" % (i, entry) for i, entry in enumerate(value))
            else:
                tokens.append("l." + name + "=" + str(value))
    tokens.append("Raw=" + ",".join(map(str, row["Raw"])))
    return " ".join(tokens)


def all_cases(data):
    rows = []
    add = rows.append
    for mode, side, human, turbo, stick, camera in itertools.product(
            (2, 3, 6, 7, 8, 9, 10), (0, 1), (0, 1), (0, 1), range(49), (0, 256)):
        player = side * 7 + 3
        add(case("run/m%d/side%d/human%d/turbo%d/stick%d/camera%d" % (mode, side, human, turbo, stick, camera),
                 "standard", dict(PlayerId=player, Station=side if human else -1, Mode=mode,
                                  Stick=stick, Flags=2 | (8 if turbo else 0)),
                 dict(Carrier=-1, CameraHeading=camera)))
    for side, turbo, stick, camera, x in itertools.product((0, 1), (0, 1), range(49), (0, 256, 512, 768), (0.0, -148.2)):
        player = side * 7 + 3
        add(case("qb/side%d/turbo%d/stick%d/camera%d/x%s" % (side, turbo, stick, camera, x), "quarterback",
                 dict(PlayerId=player, Station=side, Mode=5, Stick=stick, Flags=2 | (8 if turbo else 0), X=x),
                 dict(Carrier=player, OffenseSide=side, CameraHeading=camera)))
    for side, turbo, sector in itertools.product((0, 1), (0, 1), (128, 129, 332, 333, 459, 460, 564, 565, 691, 692, 895, 896)):
        player = side * 7 + 3
        add(case("qb/sector-boundary/%d/%d/%d" % (side, turbo, sector), "quarterback",
                 dict(PlayerId=player, Station=side, Mode=5, Flags=2 | (8 if turbo else 0)),
                 dict(OffenseSide=side, Carrier=player, CameraHeading=(sector - 512 * side) & 1023)))
    for human, flags, override, carrier, speedpup, fastpup, dragging in itertools.product(
            (0, 1), (0, 2), (0.5, 2.3), (-1, 3), (0, 1), (0, 1), (0, 1)):
        add(case("run/factors/%d/%d/%s/%d/%d/%d/%d" % (human, flags, override, carrier, speedpup, fastpup, dragging),
                 "standard", dict(PlayerId=10, Station=2 if human else -1, Mode=9, Flags=flags | 8 | 0x8000 | (0x100000 if dragging else 0),
                                  OverrideSpeed=override),
                 dict(Carrier=carrier, SpeedPowerup=speedpup, FastTurboPowerup=fastpup, BallX=100.0)))
    for human, carrier, target, angle, turbo in itertools.product((0, 1), (-1, 3), (-1, 0), (383, 384, 385, 512), (0, 1)):
        add(case("backpedal/%d/%d/%d/%d/%d" % (human, carrier, target, angle, turbo), "standard",
                 dict(Station=0 if human else -1, Flags=2 | 0x800 | (8 if turbo else 0)),
                 dict(Carrier=carrier, BackpedalTargetHeading=-1 if target == -1 else (256 - angle) & 1023)))
    for mode, human, fire in itertools.product((3, 6, 7, 8, 9, 10), (0, 1), (0, 1)):
        add(case("neutral/mode%d/human%d/fire%d" % (mode, human, fire), "standard",
                 dict(PlayerId=10, Mode=mode, Station=2 if human else -1, Stick=24, PreviousStick=21,
                      LowerMode=2, VelocityX=1.7, VelocityY=0.4, VelocityZ=-0.8, Flags=2 | 0x40000, DroneFace=900),
                 dict(Carrier=-1, Phase=1, DefenseFire=fire, MotionSnapX=-120.0)))
    for attr in (0, 1, 5, 10, 99):
        add(case("literal-at_spd-is-not-a-run-multiplier/%d" % attr, "standard", context=dict(StaticSpeedAttribute=attr)))
    for sequence, mode, upper in itertools.product(("RUN", "M_WAVQB", "M_WAVQBF"), (0, 2, 0x20), (0, 2, 4, 6)):
        for actor_mode in (5, 7, 9):
            add(case("eligibility/%s/%d/%d/%d" % (sequence, mode, upper, actor_mode), "allow",
                     dict(Mode=actor_mode, LowerMode=mode, UpperMode=upper, UpperSequence=sequence)))
    for flags in (0, 2, 4, 16):
        add(case("qb/special-play-gate/%d" % flags, "allow", dict(Mode=5), dict(OffensePlayFlags=flags)))
    for current, target, rate in itertools.product((0, 1, 511, 512, 513, 1023), (0, 1, 511, 512, 513, 1023), (0, 1, 48, 64, 512)):
        add(case("turn/%d/%d/%d" % (current, target, rate), "turn",
                 dict(Heading=current, TargetHeading=target, UpperHeading=target, UpperTargetHeading=current,
                      TurnRate=rate, UpperTurnRate=rate)))
    for station, resource, pass_receiver, held, released in itertools.product((-1, 0), (0.0, 0.0001, 100.0), (0, 1, 2), (0, 4), (-1, 0, 1, 8, 9, 500)):
        add(case("turbo/actor/%d/%s/receiver%d/held%d/released%d" % (station, resource, pass_receiver, held, released), "input",
                 dict(Station=station, Flags=2 | 8, NonTurboTime=released),
                 dict(Carrier=-1, BallType=6 if pass_receiver == 2 else 0, IntendedReceiver=3 if pass_receiver else -1,
                      InputButtons=held, InputTurbo=resource)))
    for released, buttons, old in itertools.product((-1, 0, 1, 8, 9, 500), (0, 1, 2, 3), (0, 1, 2, 3)):
        add(case("A-B-doublepress/%d/%d/%d" % (released, buttons, old), "input",
                 dict(NonATime=released, NonBTime=released, Buttons=old),
                 dict(InputButtons=buttons)))
    for override, expire, oldbp, newbp in itertools.product((0, 1, 2), (0, 1, 2, 20), (0, 1), (0, 1)):
        add(case("timers/%d/%d/%d/%d" % (override, expire, oldbp, newbp), "input",
                 dict(OverrideTime=override, BackpedalExpireTime=expire, Flags=2 | 0x100 | (0x800 if oldbp else 0),
                      AttackTime=1, AttackMode=8, CelebrateTime=1, NoCatchTime=1, StickTime=13),
                 dict(InputBackpedal=newbp, InputStick=24)))
    for resource, active, controlled, fire, infinite, held, defending in itertools.product(
            (0.0, 0.34, 0.35, 0.36, 99.5, 99.64, 99.65, 99.7, 99.85, 99.9, 100.0),
            (0, 1), (0, 1), (0, 1), (0, 1), (0, 1), (0, 1)):
        add(case("station/%s/%d%d%d%d%d%d" % (resource, active, controlled, fire, infinite, held, defending), "station",
                 context=dict(StationTurbo=resource, StationActive=active, StationControlled=controlled,
                              StationFire=fire, StationInfinite=infinite, StationHeld=held, StationDefends=defending)))
    for phase in (0, 1, 3, 4, 5, 6):
        add(case("station/non-live-phase%d" % phase, "station", context=dict(Phase=phase, StationHeld=1)))
    for field, speed, friction in itertools.product(range(7), (0.0, 0.00001, 0.00009999, 0.0001, 0.2, 1.5),
                                                   (0.0, 0.00001, 0.2, 1.5, 1.6, -0.1)):
        add(case("friction/%d/%s/%s" % (field, speed, friction), "physics",
                 dict(VelocityX=speed, VelocityZ=speed / 2.0, Friction=friction), dict(Field=field)))
    for axis, position, velocity, lower, spin, runob in itertools.product(
            ("X", "Z"), (-504.0, -503.0, -468.0, -463.1, -179.1, 0.0, 179.1, 463.1, 468.0, 503.0, 504.0),
            (-2.0, -0.51, -0.5, -0.49, 0.0, 0.49, 0.5, 0.51, 2.0), (0, 2, 0x800000), (0, 1), (0, 1)):
        add(case("bounds/%s/%s/%s/%d/%d/%d" % (axis, position, velocity, lower, spin, runob), "physics",
                 dict({axis: position, "Velocity" + axis: velocity}, LowerMode=lower,
                      LowerSequence="SPIN" if spin else "RUN", Friction=0.067),
                 dict(RunOutOfBoundsPowerup=runob)))
    for y, vy in itertools.product((0.0, 0.001, 0.03999999, 0.04, 0.04000001, 1.0, 10.0), (-1.0, -0.08, 0.0, 0.04, 0.75, 1.25)):
        add(case("air/landing/%s/%s" % (y, vy), "physics", dict(Y=y, VelocityY=vy, VelocityX=1.65)))
    for phase, dragging, new_buttons, non_turbo, spin_count in itertools.product(
            (1, 2, 3, 5), (0, 1), (0, 1, 2, 3), (-1, 0), (1, 2)):
        add(case("weapon/actions/%d/%d/%d/%d/%d" % (phase, dragging, new_buttons, non_turbo, spin_count), "weapon",
                 dict(Flags=2 | (0x100000 if dragging else 0), Buttons=4 | new_buttons, NewButtons=new_buttons,
                      NonTurboTime=non_turbo, TeamSpinCount=spin_count, VelocityX=1.65), dict(Phase=phase)))
    for raw in range(8):
        for turbo in (0, 1):
            add(case("hurdle/table-selection/%d/%d" % (turbo, raw), "weapon",
                     dict(Buttons=1 | (4 if turbo else 0), NewButtons=1), raw=[raw] * 256))
    for dragging, upper, pressed in itertools.product((0, 1), (0, 2), (0, 1)):
        add(case("head-plow-escape/%d/%d/%d" % (dragging, upper, pressed), "before",
                 dict(Flags=2 | (0x100000 if dragging else 0), UpperSequence="M_PLOWHD",
                      UpperMode=upper, LowerMode=2, Buttons=5, NewButtons=pressed)))
    for team_resource, held, non_turbo, buttons in itertools.product((0.0, 2.0, 2.000001, 100.0), (0, 4), (-1, 0), (1, 8, 9)):
        add(case("qb/actions/%s/%d/%d/%d" % (team_resource, held, non_turbo, buttons), "qb_action",
                 dict(Mode=5, Buttons=held | buttons, NewButtons=buttons, NonTurboTime=non_turbo, TeamSpinCount=2),
                 dict(TeamIndexedTurbo=team_resource)))
    for player, offside, released, flip in itertools.product((0, 3, 7, 10), (0, 1), (-1, 0, 1, 8, 9), (0, 1)):
        add(case("maybe-dive/identity%d/off%d/released%d/flip%d" % (player, offside, released, flip), "maybe_dive",
                 dict(PlayerId=player, Station=-1, NonATime=released, Flags=2 | flip), dict(OffenseSide=offside)))
    for count in (0, 1, 2, 3, 4, 99):
        add(case("spin/slower-boundary/%d" % count, "slower", dict(TeamSpinCount=count)))
    for role, job, human, held, kind, time, stick, phase in itertools.product(
            ("weapon", "db_jump"), (0, 3), (0, 1), (0, 4), (0, 1, 5, 6), (10, 11), (21, 24), (2,)):
        add(case("jump/%s/job%d/human%d/turbo%d/ball%d/t%d/stick%d/phase%d" %
                 (role, job, human, held, kind, time, stick, phase), role,
                 dict(PlayerId=10, Mode=9 if role == "db_jump" else 7, Job=job, Station=2 if human else -1,
                      Buttons=1 | held, NewButtons=1, Stick=stick, Heading=320),
                 dict(Carrier=-1, BallType=kind, BallTicksLeft=time, CameraHeading=64, BallDistance=62.4, BallY=20.0,
                      IntendedReceiver=10)))
    for distance, height in itertools.product((62.39999, 62.4, 62.40001), (19.99999, 20.0, 20.00001)):
        add(case("loose-dive/strict-range-height/%s/%s" % (distance, height), "weapon",
                 dict(PlayerId=10, Buttons=1, NewButtons=1), dict(Carrier=-1, BallType=1, BallDistance=distance, BallY=height)))
    for distance, violent, neutral, camera in itertools.product((38.99999, 39.0, 39.00001), (0, 1), (0, 1), (0, 128)):
        add(case("defense/dive-late-boundary/%s/%d/%d/%d" % (distance, violent, neutral, camera), "db_jump",
                 dict(PlayerId=10, Mode=9, Buttons=1, NewButtons=1, Stick=24 if neutral else 21, Heading=300),
                 dict(CarrierLateHittable=1, CarrierDistance=distance, ViolencePowerup=violent, CameraHeading=camera)))
    for full_body, human in itertools.product((0, 1), (0, 1)):
        add(case("juke/source-drone-script/full%d/human%d" % (full_body, human), "weapon",
                 dict(PlayerId=10, Station=2 if human else -1, NewButtons=8, Buttons=8, Heading=256),
                 dict(Carrier=-1, DroneScriptFullBody=full_body)))
    for speed, running, change, job in itertools.product((1.0, 1.00001, 1.1, 1.10001, 1.65), (0, 0x1000), (0, 1), (0, 3)):
        add(case("push-and-switch/%s/%d/%d/%d" % (speed, running, change, job), "weapon",
                 dict(PlayerId=10, Job=job, VelocityX=speed, Buttons=6, NewButtons=2, UpperMode=running),
                 dict(Carrier=-1, ChangeValue=change)))
    for h, red, column in itertools.product(range(12), (0, 1), range(6)):
        add(case("defense/handicap%d/red%d/column%d" % (h, red, column), "defense",
                 context=dict(Handicap=h, DefensePlayFlags=1024 if red else 0),
                 raw=[value for i in range(7) for value in (column, i * 17)]))
    for h, raw, bearing, distance in itertools.product((0, 5, 11), (50, 51, 100), (63, 64), (62.4, 62.40001)):
        distances = [1000.0] * 14
        bearings = [512] * 14
        distances[7] = distances[8] = distance
        bearings[7] = bearings[8] = bearing
        add(case("plow/order-and-inclusive-probability/%d/%d/%d/%s" % (h, raw, bearing, distance), "standard",
                 dict(Flags=2 | 8, Buttons=4, LowerSequence="R_TRB_WB"),
                 dict(Handicap=h, Distances=distances, Bearings=bearings), raw=[raw] * 256))
    for mask in (0, 1, (1 << len(data["Tables"]["breath_seq_tbl"])) - 1):
        add(case("unique-breath/retry-and-exhaustion/%d" % mask, "standard",
                 dict(Stick=24, PreviousStick=21), dict(Carrier=-1, Phase=3, BreathControl=mask)))
    for ball, onside, phase, target in itertools.product((0, 3, 6), (0, 0x10000), (1, 2, 5), (-1, 3)):
        add(case("human-control/ball%d/onside%d/phase%d/target%d" % (ball, onside, phase, target), "allow",
                 context=dict(Carrier=-1, BallType=ball, PlayType=1, GameFlags=onside, Phase=phase, IntendedReceiver=target)))
    for op in ("input", "turn", "standard", "quarterback", "general", "physics", "before", "weapon", "qb_action", "db_jump", "maybe_dive", "slower", "station", "defense", "step"):
        add(case("source-frame-gate/" + op, op,
                 dict(NonTurboTime=-1, NonATime=-1, Buttons=5, NewButtons=1, TeamSpinCount=2, TeamHurdleCount=4,
                      LowerSequence="SPIN", LowerMode=2, VelocityX=1.65, VelocityY=1.25),
                 dict(Execute=0, StationHeld=1)))
    rows.extend(extra_cases(data))
    rows.extend(leap_cases())
    return rows


def extra_cases(data):
    rows = []
    add = rows.append
    for field, pair in itertools.product(range(7), (
            (0.00009999, 0.0001), (0.0001, 0.0001), (0.2, 0.19999999),
            (0.2, 0.2), (0.2, 0.20000002), (1.0, 1.0))):
        velocity, friction = pair
        add(case("friction/exact-axis-magnitude-and-equality/%d/%s/%s" % (field, velocity, friction),
                 "physics", dict(VelocityX=velocity, VelocityZ=0.0, Friction=friction), dict(Field=field)))
    for side, turbo, upper, rotating, neutral in itertools.product((0, 1), (0, 1), (0, 2), (0, 0x20), (0, 1)):
        add(case("qb/upper-throw-and-rotation-only/%d/%d/%d/%d/%d" % (side, turbo, upper, rotating, neutral),
                 "quarterback", dict(PlayerId=side * 7 + 3, Station=side, Mode=5, Flags=2 | (8 if turbo else 0),
                                     UpperMode=upper, LowerMode=rotating, Stick=24 if neutral else 21,
                                     VelocityX=0.5, VelocityY=0.4, VelocityZ=-0.5, PreviousStick=21,
                                     Heading=511, UpperHeading=1023),
                 dict(OffenseSide=side, Carrier=side * 7 + 3, CameraHeading=384, BreathControl=7)))
    for side, x in itertools.product((0, 1), (-140.40002, -140.4, -140.39998, 140.39998, 140.4, 140.40002)):
        add(case("qb/field-integer-slow-boundary/%d/%s" % (side, x), "quarterback",
                 dict(PlayerId=side * 7 + 3, Station=side, Mode=5, X=x, Flags=10),
                 dict(OffenseSide=side, Carrier=side * 7 + 3)))
    for side, turbo, carrier, onside, phase, mode in itertools.product(
            (0, 1), (0, 1), (-1, 3), (0, 0x10000), (1, 2, 5), (6, 7)):
        player = side * 7 + 4
        add(case("kickoff/movement/%d/%d/%d/%d/%d/%d" % (side, turbo, carrier, onside, phase, mode), "standard",
                 dict(PlayerId=player, Station=-1, Mode=mode, Flags=2 | (8 if turbo else 0)),
                 dict(OffenseSide=side, PlayType=1, Carrier=carrier, GameFlags=onside, Phase=phase,
                      BallType=3, FastTurboPowerup=1)))
    for side, human, z, stick, fake in itertools.product(
            (0, 1), (0, 1), (-60.00001, -60.0, -59.99999, 59.99999, 60.0, 60.00001), (3, 21, 45), (0, 16)):
        add(case("presnap-motion/width-and-fake-fg/%d/%d/%s/%d/%d" % (side, human, z, stick, fake), "standard",
                 dict(PlayerId=side * 7 + 4, Station=side if human else -1, Mode=6, Z=z, Stick=stick, PreviousStick=21),
                 dict(OffenseSide=side, Carrier=-1, OffensePlayFlags=fake, MotionSnapX=234.0 if side else -234.0)))
    for dragging, upper, turbo, raw in itertools.product((0, 1), (0, 0x400), (5.0, 5.00001), (49, 50)):
        add(case("carrier/stiff-arm-source-team-index/%d/%d/%s/%d" % (dragging, upper, turbo, raw), "standard",
                 dict(Flags=2 | 8 | (0x100000 if dragging else 0), UpperMode=upper, LowerSequence="R_TRB_WB", Buttons=0),
                 dict(TeamIndexedTurbo=turbo), raw=[raw] * 256))
    for upper, wave, camera, seq in itertools.product((0, 0x1000), (0, 1), (0, 256, 512, 768), ("RUN", "R_TRB")):
        add(case("noncarrier/running-push-and-wave/%d/%d/%d/%s" % (upper, wave, camera, seq), "standard",
                 dict(PlayerId=4, Station=-1, UpperMode=upper, Flags=2 | (0x2000 if wave else 0), LowerSequence=seq),
                 dict(Carrier=3, CameraHeading=camera)))
    for distance, x, upper in itertools.product((62.39999, 62.4, 62.40001), (-7.8, 0.0, 7.8), (0, 4)):
        add(case("defense/blitz-run-selection/%s/%s/%d" % (distance, x, upper), "standard",
                 dict(PlayerId=7, Station=-1, Mode=3, X=x, UpperMode=upper),
                 dict(Carrier=3, CarrierMode=5, CarrierDistance=distance, DefensePlayFlags=1)))
    for side, x, distance, raw in itertools.product(
            (0, 1), (233.99998, 234.0, 234.00002), (46.79999, 46.8, 46.80001), range(4)):
        distances = [1000.0] * 14
        distances[7 if side == 0 else 0] = distance
        player = side * 7 + 4
        add(case("carrier/taunt80-and-sprint6-boundaries/%d/%s/%s/%d" % (side, x, distance, raw), "standard",
                 dict(PlayerId=player, Station=side, X=x if side == 0 else -x),
                 dict(Carrier=player, OffenseSide=side, Distances=distances), raw=[raw] * 256))
    for side, x, ball in itertools.product((0, 1), (-7.8, -0.00001, 0.0, 0.00001, 7.8), (-7.8, 0.0, 7.8, 7.80001)):
        add(case("human-defense/chase-integer-field-boundary/%d/%s/%s" % (side, x, ball), "standard",
                 dict(PlayerId=(1 - side) * 7 + 4, Station=1 - side, Mode=9, X=x),
                 dict(OffenseSide=side, Carrier=side * 7 + 3, CarrierMode=7, BallX=ball)))
    add(case("carrier/neutral-dragging-retains-momentum", "standard",
             dict(Stick=24, Flags=2 | 0x100000, VelocityX=1.5, VelocityY=0.75, VelocityZ=-0.9)))
    for role, job, upper, entered in itertools.product(("weapon", "db_jump"), (0, 3), (0, 0x1000), (0, 1)):
        add(case("jump/carried-ball-facing-and-entry/%s/%d/%d/%d" % (role, job, upper, entered), role,
                 dict(PlayerId=10, Station=2, Mode=9 if role == "db_jump" else 7, Job=job,
                      Buttons=5, NewButtons=1, Heading=320, Stick=24, UpperMode=upper, LowerMode=2),
                 dict(CameraHeading=128, EnteredControlBlock=entered)))
    for side in (0, 1):
        add(case("defense/ordered-opposite-side/%d" % side, "defense",
                 dict(PlayerId=side * 7 + 3, Station=side), dict(OffenseSide=side, Handicap=11),
                 raw=[value for i in range(7) for value in (i, 10)]))
    for mode, human, held, non_turbo, lower, upper in itertools.product(
            (5, 7, 9), (0, 1), (0, 1, 4, 5), (0, 8, 9), (0, 2), (0, 2)):
        player = 3 if mode != 9 else 10
        add(case("source-pure-call-order/mode%d/human%d/input%d/repress%d/lower%d/upper%d" %
                 (mode, human, held, non_turbo, lower, upper), "step",
                 dict(PlayerId=player, Station=0 if human else -1, Mode=mode, NonTurboTime=non_turbo,
                      LowerMode=lower, UpperMode=upper, Heading=1023, TargetHeading=255,
                      UpperHeading=512, UpperTargetHeading=256, TeamSpinCount=2),
                 dict(Carrier=3, CarrierMode=5 if mode == 5 else 7, InputButtons=held, InputStick=21, CameraHeading=64)))
    return rows


def leap_case(name, state=None, context=None, leap=None):
    row = case(name, "leap", state, context)
    values = dict(JointX=40.0, JointY=8.0, JointZ=20.0, TargetVelocityX=0.0, TargetVelocityY=0.0,
                  TargetVelocityZ=0.0, OffsetX=0.0, OffsetY=-4.4, OffsetZ=0.0, Time=22,
                  MaximumYVelocity=0.7, MaximumPlanarVelocity=2.2, MinimumPlanarVelocity=1.65,
                  VictimArgument=-2, ThrowTarget=7, CameraX=0.0, CameraY=0.0, CameraZ=0.0,
                  Matrix=[1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0])
    if leap:
        values.update(leap)
    row["Leap"] = values
    return row


def leap_cases():
    rows = []
    add = rows.append
    for x, z, time, heading, vy in itertools.product(
            (0.0, 1.0, 36.3, 48.4, 100.0, -100.0), (0.0, 20.0, -20.0), (1, 22, 57), (0, 256, 511, 1023), (-0.4, 0.0, 0.7)):
        add(leap_case("leap/four-iteration-dive/%s/%s/%d/%d/%s" % (x, z, time, heading, vy),
                      dict(TargetHeading=heading, X=2.25, Z=-1.5),
                      leap=dict(JointX=x, JointZ=z, Time=time, TargetVelocityY=vy, OffsetX=2.25, OffsetZ=-1.2)))
    for height, vy, offset in itertools.product((0.0, 1.0, 20.0), (-0.4, 0.0, 0.65, 1.25), (-4.4, 0.0)):
        add(leap_case("leap/ground-time-truncation-and-NaN-roots/%s/%s/%s" % (height, vy, offset),
                      dict(Y=height, VelocityY=vy, TargetHeading=256),
                      leap=dict(Time=-1, OffsetY=offset)))
    for minimum, maximum, distance, target_y in itertools.product((0.0, 1.65), (1.65, 2.2), (0.0, 36.3, 48.4), (0.0, 100.0)):
        add(leap_case("leap/minimum-early-return-preserves-uncapped-Y/%s/%s/%s/%s" % (minimum, maximum, distance, target_y),
                      dict(TargetHeading=256), leap=dict(OffsetY=0.0, JointX=distance, JointZ=0.0,
                                                       JointY=target_y, MinimumPlanarVelocity=minimum, MaximumPlanarVelocity=maximum)))
    for side, requested, carrier, assigned in itertools.product((0, 1), (-2, -1, 0, 3, 7, 10, 13), (-1, 4), (-1, 7)):
        add(leap_case("leap/victim-resolution-and-pointer-alias/%d/%d/%d/%d" % (side, requested, carrier, assigned),
                      dict(VelocityY=0.65, TargetHeading=900), dict(OffenseSide=side, Carrier=carrier),
                      dict(VictimArgument=requested, ThrowTarget=assigned, JointY=40.0)))
    for matrix in (
            [0.0, 0.0, -1.0, 3.0, 0.0, 1.0, 0.0, 4.0, 1.0, 0.0, 0.0, 5.0],
            [0.707107, 0.0, -0.707107, 7.0, 0.0, 1.0, 0.0, 8.0, 0.707107, 0.0, 0.707107, 9.0]):
        add(leap_case("leap/camera-space-joint-transform/" + str(matrix[0]), dict(TargetHeading=1023),
                      leap=dict(Matrix=matrix, CameraX=13.0, CameraY=4.0, CameraZ=-3.0,
                                TargetVelocityX=0.7, TargetVelocityY=0.8, TargetVelocityZ=-0.3)))
    add(leap_case("leap/second-target-Y-prediction-is-not-a-typo", dict(Y=20.0, VelocityY=0.65),
                  leap=dict(TargetVelocityY=0.7, MinimumPlanarVelocity=0.0)))
    add(leap_case("source-frame-gate/leap", dict(VelocityX=1.65, VelocityY=0.8), dict(Execute=0)))
    return rows


def identity_map(oracle):
    """Bind literal original call sites to their production instrumentation names."""
    source = oracle.sources["player.c"].decode("latin1").splitlines()
    mapping = {}
    for line_number, text in enumerate(source, 1):
        if "randrng" not in text or text.lstrip().startswith("//"):
            continue
        if 3804 <= line_number < 4269:
            if "if (randrng(100) < 50)" in text:
                identity = "player.c:standard_run:drag-stiff-arm" if line_number < 4030 else "player.c:standard_run:stiff-arm"
            elif "plow_ht" in text:
                identity = "player.c:standard_run:plow"
            elif "running_taunt_tbl" in text:
                identity = "player.c:standard_run:taunt"
            else:
                continue
        elif 2195 <= line_number < 2754:
            if "turbo_hurdle_tbl" in text and line_number < 2500:
                identity = "player.c:_mode_wpn:plow-hurdle"
            elif "hurdle_tbl" in text:
                identity = "player.c:_mode_wpn:hurdle"
            elif "intercept_anim_tbl" in text:
                identity = "player.c:mode:intercept" if line_number < 2650 else "player.c:_mode_wpn:loose-intercept"
            elif "push_anim_tbl" in text:
                identity = "player.c:_mode_wpn:push"
            else:
                continue
        elif 2847 <= line_number < 3045:
            if "intercept_anim_tbl" in text:
                identity = "player.c:mode:intercept"
            elif "db_late_hits_tbl" in text:
                identity = "player.c:_mode_db:late-hit"
            else:
                continue
        elif 5362 <= line_number < 5410:
            identity = "player.c:unique_random:fallback" if line_number > 5390 else "player.c:unique_random"
        else:
            continue
        mapping["player.c:" + str(line_number)] = identity
    misc = oracle.sources["pmisc.c"].decode("latin1").splitlines()
    for line_number, text in enumerate(misc, 1):
        if not 109 <= line_number < 146 or "randrng" not in text or text.lstrip().startswith("//"):
            continue
        mapping["pmisc.c:" + str(line_number)] = "pmisc.c:set_defense_speeds:column" if "randrng(6)" in text else "pmisc.c:set_defense_speeds:delay"
    return mapping


def attach(rows, results, sites):
    for row, expected in zip(rows, results, strict=True):
        for draw in expected["Draws"]:
            key = draw["File"] + ":" + str(draw["Line"])
            if key not in sites:
                raise RuntimeError("Unmapped actual original RNG site: " + key)
            draw["Identity"] = sites[key]
        row["Expected"] = expected


def from_result(state):
    result = copy.deepcopy(state)
    for name in FLOAT_FIELDS:
        result[name] = struct.unpack("<f", struct.pack("<I", result[name]))[0]
    return result


def lifecycle(oracle, sites):
    """Use original script launch operands plus actual C previous-state results, never a Python motion formula."""
    rows = []
    source = oracle.sources["plyrseq.c"].decode("latin1")
    for script in ("hurdle_anim", "turbo_hurdle_anim", "spin_hurdle_anim", "turbo_spin_hurdle_anim", "r_diveoh_anim", "qb_slide_anim"):
        start = re.search(r"(?m)^" + script + r":", source).end()
        end = re.search(r"(?m)^\s*\.globl", source[start:])
        body = source[start:start + end.start()] if end else source[start:]
        launch = re.search(r"ANI_SETYVEL\s*\r?\n\s*\.float\s*([.\d]+)", body)
        if not launch:
            raise RuntimeError("Missing actual source launch operand: " + script)
        state = case("seed", "physics")["State"]
        sequence = re.search(r"ANI_(?:NEWSEQ|INTTOSEQ[^\r\n]*)\s*\r?\n\s*\.word\s+SEQ_(\w+)", body)
        if not sequence:
            raise RuntimeError("Missing actual source sequence: " + script)
        state.update(LowerSequence=sequence[1], LowerMode=2,
                     VelocityX=1.65, VelocityZ=-0.8, VelocityY=float(launch[1]))
        for frame in range(45):
            row = case("lifecycle/%s/source-tick%d" % (script, frame), "physics", state, dict(SourceTick=2000 + frame))
            result = oracle.execute([command(row)])[0]
            attach([row], [result], sites)
            rows.append(row)
            state = from_result(result["State"])
    return rows
