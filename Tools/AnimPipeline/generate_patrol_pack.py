"""Authors and exports the 4-clip Beat Patrol custom skeletal animation pack
using exact FreeSO/MonoGame anatomical kinematics + 2-bone IK limb targeting,
baking every clip onto AdultRig in Blender 4.5 and exporting via blender_export_simanim.py.

Clips:
  1. patrol_sneak_loop   — crouched tactical burglar-catching sneak loop
  2. patrol_apprehend    — authoritative freeze/halt + handcuff apprehend
  3. patrol_write_ticket — citation pad writing + ticket tear-off
  4. patrol_inspect_clue — crouched flashlight hazard/clue inspection

Run:
  "C:\\Program Files\\Blender Foundation\\Blender 4.5\\blender.exe" --factory-startup --background ^
      Tools/AnimPipeline/adult_rig.blend --python Tools/AnimPipeline/generate_patrol_pack.py
"""
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from generate_cadet_pack import (
    OUT_DIR,
    SKEL_REF_PATH,
    PoseRig,
    bake_into_blender_and_export,
    lerp,
    smoothstep,
)


def make_patrol_sneak_loop(rig):
    """Unmistakable crouched stealth tiptoe sneak for creeping up behind a Burglar."""
    fps = 30.0
    n = 32  # ~1.07s deliberate stealth stride cycle
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        s = math.sin(p)
        c = math.cos(p)
        # Alternating high stealth tiptoe lift: left swings on s > 0, right swings on s < 0
        l_lift = max(0.0, s)
        r_lift = max(0.0, -s)

        # Deep crouched silhouette + stealthy creeping stride
        l_hip = -46.0 - 26.0 * s - 10.0 * l_lift
        r_hip = -46.0 + 26.0 * s - 10.0 * r_lift
        l_knee = 64.0 + 30.0 * l_lift + 12.0 * max(0.0, -c)
        r_knee = 64.0 + 30.0 * r_lift + 12.0 * max(0.0, c)
        l_ankle = -18.0 - 16.0 * l_lift + 12.0 * max(0.0, -s)
        r_ankle = -18.0 - 16.0 * r_lift + 12.0 * max(0.0, s)

        # Careful stealth hand balance in front of chest/waist using exact 2-bone IK
        arm_ik = {
            "L": (
                (-0.68 - 0.08 * c, 2.55 + 0.14 * r_lift, 0.72 + 0.18 * (-s)),
                (-1.35, 2.30, -0.55),
            ),
            "R": (
                (0.68 + 0.08 * c, 2.55 + 0.14 * l_lift, 0.72 + 0.18 * s),
                (1.35, 2.30, -0.55),
            ),
        }

        d = {
            "PELVIS": (18.0 + 3.0 * math.cos(2.0 * p), -9.0 * s, 5.5 * s),
            "SPINE": (16.0, 5.0 * s, -3.5 * s),
            "SPINE1": (14.0, 5.5 * s, -2.5 * s),
            "SPINE2": (8.0, 4.0 * s, 0.0),
            # Counter-pitch neck & head so eyes stay locked straight ahead on the Burglar!
            "NECK": (-18.0, -5.0 * s, 0.0),
            "HEAD": (-16.0, -5.0 * s + 6.0 * math.sin(p * 0.5), 0.0),
            "L_CLAVICLE": (4.0, 6.0, -6.0),
            "R_CLAVICLE": (4.0, -6.0, 6.0),
            "L_LEG": (l_hip, 6.0, 5.0),
            "L_LEG1": (l_knee, 0.0, -2.0),
            "L_FOOT": (l_ankle, 0.0, 0.0),
            "L_TOE0": (14.0 * max(0.0, -s), 0.0, 0.0),
            "R_LEG": (r_hip, -6.0, -5.0),
            "R_LEG1": (r_knee, 0.0, 2.0),
            "R_FOOT": (r_ankle, 0.0, 0.0),
            "R_TOE0": (14.0 * max(0.0, s), 0.0, 0.0),
        }
        bob_y = -0.08 * abs(s)
        sway_x = 0.09 * c
        frames.append(rig.build_pose(d, root_offset=(sway_x, bob_y, 0.0), arm_ik=arm_ik, ground_feet=True))

    return {
        "name": "patrol_sneak_loop",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 4.2,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Open"}],
        "events": [
            {"frame": 4, "name": "sneak_step_l"},
            {"frame": 20, "name": "sneak_step_r"},
        ],
        "frames": frames,
    }


def make_patrol_apprehend(rig):
    """Spring from stealth crouch into decisive FREEZE! halt stance + handcuff click."""
    fps = 30.0
    n = 28
    frames = []
    for i in range(n):
        u = i / (n - 1)
        rise = smoothstep(0.0, 0.32, u)
        belt = smoothstep(0.38, 0.56, u) * (1.0 - smoothstep(0.62, 0.78, u))
        snap = smoothstep(0.68, 0.86, u)

        l_target = (
            lerp(-0.55, -0.36, rise),
            lerp(2.65, 3.85, rise) - 0.25 * snap,
            lerp(0.65, 1.38, rise) - 0.15 * snap,
        )
        r_halt = (0.38, 3.85, 1.40)
        r_belt = (0.68, 2.75, 0.28)
        r_snap = (0.20, 3.55, 1.32)
        if u < 0.38:
            r_target = (
                lerp(0.55, r_halt[0], rise),
                lerp(2.65, r_halt[1], rise),
                lerp(0.65, r_halt[2], rise),
            )
        elif u < 0.65:
            r_target = (
                lerp(r_halt[0], r_belt[0], belt),
                lerp(r_halt[1], r_belt[1], belt),
                lerp(r_halt[2], r_belt[2], belt),
            )
        else:
            r_target = (
                lerp(r_belt[0], r_snap[0], snap),
                lerp(r_belt[1], r_snap[1], snap),
                lerp(r_belt[2], r_snap[2], snap),
            )

        arm_ik = {
            "L": (l_target, (-1.35, 3.15, -0.35)),
            "R": (r_target, (1.35, 3.15, -0.35)),
        }

        d = {
            "PELVIS": (lerp(18.0, 4.0, rise), lerp(-6.0, -10.0, rise), 0.0),
            "SPINE": (lerp(15.0, 5.0, rise), lerp(4.0, 8.0, rise), 0.0),
            "SPINE1": (lerp(12.0, 3.0, rise), 4.0 * snap, 0.0),
            "SPINE2": (lerp(6.0, 2.0, rise), 0.0, 0.0),
            "NECK": (lerp(-14.0, -2.0, rise), 0.0, 0.0),
            "HEAD": (lerp(-12.0, -3.0, rise), 0.0, 0.0),
            "L_LEG": (lerp(-42.0, -20.0, rise), 8.0, 4.0),
            "L_LEG1": (lerp(60.0, 26.0, rise), 0.0, -2.0),
            "L_FOOT": (lerp(-18.0, -6.0, rise), 0.0, 0.0),
            "R_LEG": (lerp(-32.0, 12.0, rise), -8.0, -4.0),
            "R_LEG1": (lerp(52.0, 14.0, rise), 0.0, 2.0),
            "R_FOOT": (lerp(-18.0, -20.0, rise), 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.25 * rise), arm_ik=arm_ik, ground_feet=True))

    return {
        "name": "patrol_apprehend",
        "fps": fps,
        "loop": False,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [
            {"frame": 0, "left": "Open", "right": "Open"},
            {"frame": 14, "left": "Open", "right": "Closed"},
        ],
        "events": [
            {"frame": 9, "name": "halt_call"},
            {"frame": 23, "name": "handcuff_click"},
        ],
        "frames": frames,
    }


def make_patrol_write_ticket(rig):
    """Officer holds citation pad in left hand, writes briskly with right hand, tears off citation."""
    fps = 30.0
    n = 30
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        u = i / n
        scribble_x = 0.14 * math.sin(p * 3.0) if u < 0.72 else 0.0
        scribble_y = 0.07 * math.cos(p * 6.0) if u < 0.72 else 0.0
        tear = smoothstep(0.72, 0.88, u) * (1.0 - smoothstep(0.90, 1.0, u))

        arm_ik = {
            "L": ((-0.28, 3.32, 0.78), (-1.20, 2.85, -0.35)),
            "R": (
                (-0.06 + scribble_x + 0.30 * tear, 3.38 + scribble_y + 0.28 * tear, 0.82 + 0.14 * tear),
                (1.25, 2.95, -0.30),
            ),
        }
        d = {
            "PELVIS": (2.0, -4.0, 0.0),
            "SPINE": (4.0, 3.0, 0.0),
            "SPINE1": (4.0, 2.0, 0.0),
            "SPINE2": (2.0, 0.0, 0.0),
            "NECK": (10.0 - 8.0 * tear, -4.0, 0.0),
            "HEAD": (12.0 - 12.0 * tear, -3.0, 0.0),
            "L_LEG": (-4.0, 5.0, 3.0),
            "L_LEG1": (6.0, 0.0, -2.0),
            "L_FOOT": (-2.0, 0.0, 0.0),
            "R_LEG": (-2.0, -5.0, -3.0),
            "R_LEG1": (4.0, 0.0, 2.0),
            "R_FOOT": (-2.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), arm_ik=arm_ik, ground_feet=True))

    return {
        "name": "patrol_write_ticket",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Point"}],
        "events": [{"frame": 23, "name": "ticket_rip"}],
        "frames": frames,
    }


def make_patrol_inspect_clue(rig):
    """Crouched tactical inspection with right-hand flashlight sweeping across a ground hazard."""
    fps = 30.0
    n = 32
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        sweep = math.sin(p)

        arm_ik = {
            "L": ((-0.62, 1.95, 0.55), (-1.30, 2.35, -0.35)),
            "R": ((0.22 + 0.44 * sweep, 1.68 + 0.08 * math.cos(p), 1.12), (1.28, 2.40, -0.25)),
        }
        d = {
            "PELVIS": (26.0, 4.0 * sweep, 0.0),
            "SPINE": (18.0, 5.0 * sweep, 0.0),
            "SPINE1": (14.0, 4.0 * sweep, 0.0),
            "SPINE2": (8.0, 0.0, 0.0),
            "NECK": (-10.0, 8.0 * sweep, 0.0),
            "HEAD": (-8.0, 12.0 * sweep, 0.0),
            "L_LEG": (-68.0, 10.0, 6.0),
            "L_LEG1": (96.0, 0.0, -2.0),
            "L_FOOT": (-28.0, 0.0, 0.0),
            "R_LEG": (-54.0, -10.0, -6.0),
            "R_LEG1": (84.0, 0.0, 2.0),
            "R_FOOT": (-30.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), arm_ik=arm_ik, ground_feet=True))

    return {
        "name": "patrol_inspect_clue",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Closed"}],
        "events": [{"frame": 2, "name": "flashlight_click"}],
        "frames": frames,
    }


def main():
    import bpy

    with open(SKEL_REF_PATH, "r", encoding="utf-8") as f:
        ref = json.load(f)
    bones = ref["bones"]
    rig = PoseRig(bones)

    arm_obj = bpy.data.objects.get("AdultRig")
    if arm_obj is None:
        raise RuntimeError("AdultRig armature not found in adult_rig.blend")

    os.makedirs(OUT_DIR, exist_ok=True)
    specs = [
        make_patrol_sneak_loop(rig),
        make_patrol_apprehend(rig),
        make_patrol_write_ticket(rig),
        make_patrol_inspect_clue(rig),
    ]
    print(f"Generating & exporting {len(specs)} Beat Patrol clips via Blender 4.5 -> {OUT_DIR}")
    for spec in specs:
        bake_into_blender_and_export(arm_obj, bones, spec)
    print("PATROL PACK EXPORT COMPLETE")


if __name__ == "__main__":
    main()
