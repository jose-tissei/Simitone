"""Authors and exports the 6-clip Desk Sergeant (911 Dispatch Triage) custom skeletal animation pack
using exact FreeSO/MonoGame anatomical kinematics, baking every clip onto AdultRig in Blender 4.5
and exporting via blender_export_simanim.py.

Clips:
  1. desk_idle_watch      — attentive Desk Sergeant behind precinct counter scanning switchboard & lobby
  2. desk_answer_phone    — right hand holding retro 911 handset to ear, left hand taking notes on desk
  3. desk_radio_dispatch  — leaning forward keying radio console / shoulder mic to dispatch units
  4. desk_wave_dismiss    — head-shake + brisk dismissive wave for prank calls / false alarms
  5. desk_walkin_walk     — brisk walk cycle for 3D neighborhood townies approaching/leaving front counter
  6. desk_walkin_plead    — urgent two-handed pleading & explaining across the precinct desk

Run:
  "C:\\Program Files\\Blender Foundation\\Blender 4.5\\blender.exe" --factory-startup --background ^
      Tools/AnimPipeline/adult_rig.blend --python Tools/AnimPipeline/generate_desk_pack.py
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
    smoothstep,
)


def validate_clip_arms(spec):
    """Verify every frame has anatomically valid, non-crossing arms and clean elbow bends."""
    name = spec["name"]
    for fi, (_root_t, _lrots, w_pos) in enumerate(spec["frames"]):
        for side, sign in (("L", +1.0), ("R", -1.0)):
            sh = w_pos[f"{side}_ARM1"]
            el = w_pos[f"{side}_ARM2"]
            hd = w_pos[f"{side}_HAND"]
            if sign * (el[0] - sh[0]) < -0.22:
                raise AssertionError(
                    f"{name} f={fi} {side} elbow crossed inward: sh={sh[0]:+.2f} el={el[0]:+.2f}"
                )
            if sign * hd[0] < -0.28:
                raise AssertionError(
                    f"{name} f={fi} {side} hand crossed opposite side: hd={hd[0]:+.2f}"
                )
            ux, uy, uz = el[0] - sh[0], el[1] - sh[1], el[2] - sh[2]
            lx, ly, lz = hd[0] - el[0], hd[1] - el[1], hd[2] - el[2]
            ulen = max(1e-6, math.sqrt(ux * ux + uy * uy + uz * uz))
            llen = max(1e-6, math.sqrt(lx * lx + ly * ly + lz * lz))
            dot = max(-1.0, min(1.0, (ux * lx + uy * ly + uz * lz) / (ulen * llen)))
            bend = math.degrees(math.acos(dot))
            if bend < 15.0 or bend > 142.0:
                raise AssertionError(
                    f"{name} f={fi} {side} elbow bend out of range: {bend:.1f} deg"
                )


def make_desk_idle_watch(rig):
    """Desk Sergeant standing alert behind the front counter, hands resting near switchboard & ledger."""
    fps = 30.0
    n = 48
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        breathe = math.sin(p)
        scan = math.sin(p * 0.5) * 1.0 if False else math.sin(p)
        subtle = math.cos(p * 2.0)

        d = {
            "PELVIS": (1.5, 2.0 * scan, 0.0),
            "SPINE": (2.5 + 1.0 * breathe, 2.5 * scan, 0.0),
            "SPINE1": (2.0 + 1.0 * breathe, 2.0 * scan, 0.0),
            "SPINE2": (1.5, 1.5 * scan, 0.0),
            "NECK": (3.0, 6.0 * scan, 0.0),
            "HEAD": (4.0 - 1.5 * breathe, 9.0 * scan, 1.5 * subtle),

            # Both hands resting forward at desk height (bent elbows cleanly outside shoulders)
            "L_ARM": (0.0, 0.0, 4.0),
            "L_ARM1": (-24.0 + 2.0 * breathe, -10.0, 14.0),
            "L_ARM2": (-68.0 - 3.0 * breathe, -12.0, -8.0),
            "L_HAND": (10.0, 0.0, 0.0),

            "R_ARM": (0.0, 0.0, -4.0),
            "R_ARM1": (-24.0 + 2.0 * breathe, 10.0, -14.0),
            "R_ARM2": (-68.0 - 3.0 * breathe, 12.0, 8.0),
            "R_HAND": (10.0, 0.0, 0.0),

            "L_LEG": (-3.0, 4.0, 2.5),
            "L_LEG1": (5.0, 0.0, -1.5),
            "L_FOOT": (-2.0, 0.0, 0.0),
            "R_LEG": (-3.0, -4.0, -2.5),
            "R_LEG1": (5.0, 0.0, 1.5),
            "R_FOOT": (-2.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "desk_idle_watch",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Open"}],
        "events": [],
        "frames": frames,
    }


def make_desk_answer_phone(rig):
    """Desk Sergeant holds 911 telephone handset to right ear while jotting notes on desk pad with left hand."""
    fps = 30.0
    n = 36
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        nod = math.sin(p * 2.0)
        scrib_y = math.sin(p * 3.0)
        scrib_p = math.cos(p * 3.0)

        d = {
            "PELVIS": (2.0, -3.0, 0.0),
            "SPINE": (4.0, 2.0, -2.0),
            "SPINE1": (4.0, 2.0, -2.5),
            "SPINE2": (2.5, 1.0, -2.0),
            "NECK": (5.0 + 3.0 * nod, -4.0, -5.0),
            "HEAD": (6.0 + 5.0 * nod, -5.0, -7.0),

            # Left arm (+X): taking notes briskly on the dispatch ledger on the counter
            "L_ARM": (0.0, 0.0, 4.0),
            "L_ARM1": (-30.0 + 3.0 * scrib_p, -14.0 + 6.0 * scrib_y, 16.0),
            "L_ARM2": (-82.0 + 5.0 * scrib_p, -18.0, -8.0),
            "L_HAND": (8.0, 0.0, 0.0),

            # Right arm (-X): elbow raised outward-forward, forearm flexed sharply bringing hand to right ear!
            "R_ARM": (0.0, 0.0, -6.0),
            "R_ARM1": (-62.0 + 2.0 * nod, 10.0, -34.0),
            "R_ARM2": (-118.0 + 2.0 * nod, 26.0, 14.0),
            "R_HAND": (6.0, 8.0, 0.0),

            "L_LEG": (-3.0, 4.0, 2.5),
            "L_LEG1": (5.0, 0.0, -1.5),
            "L_FOOT": (-2.0, 0.0, 0.0),
            "R_LEG": (-3.0, -4.0, -2.5),
            "R_LEG1": (5.0, 0.0, 1.5),
            "R_FOOT": (-2.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "desk_answer_phone",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Point", "right": "Closed"}],
        "events": [],
        "frames": frames,
    }


def make_desk_radio_dispatch(rig):
    """Authoritative dispatch call: lean forward, press desk/shoulder mic with right hand, signal with left."""
    fps = 30.0
    n = 30
    frames = []
    for i in range(n):
        u = i / (n - 1)
        reach = smoothstep(0.05, 0.30, u) * (1.0 - smoothstep(0.75, 0.98, u))
        pulse = math.sin(u * math.pi * 4.0) * reach

        d = {
            "PELVIS": (3.0 + 3.0 * reach, 0.0, 0.0),
            "SPINE": (4.0 + 5.0 * reach, -4.0 * reach, 0.0),
            "SPINE1": (4.0 + 4.0 * reach, -3.0 * reach, 0.0),
            "SPINE2": (2.0 + 2.0 * reach, 0.0, 0.0),
            "NECK": (-2.0 + 4.0 * pulse * 0.4, 4.0 * reach, 0.0),
            "HEAD": (-3.0 + 5.0 * pulse * 0.4, 6.0 * reach, 0.0),

            # Left arm (+X): crisp forward-outward authoritative dispatch gesture
            "L_ARM": (0.0, 0.0, 5.0),
            "L_ARM1": (-28.0 - 28.0 * reach + 4.0 * pulse, -10.0 - 8.0 * reach, 16.0 + 10.0 * reach),
            "L_ARM2": (-72.0 + 26.0 * reach, -12.0, -8.0),
            "L_HAND": (-8.0 * reach, 0.0, 0.0),

            # Right arm (-X): presses shoulder/chest radio transceiver mic firmly
            "R_ARM": (0.0, 0.0, -5.0),
            "R_ARM1": (-30.0 - 20.0 * reach, 14.0 + 8.0 * reach, -18.0 - 8.0 * reach),
            "R_ARM2": (-74.0 - 34.0 * reach, 22.0 + 10.0 * reach, 10.0),
            "R_HAND": (6.0, 0.0, 0.0),

            "L_LEG": (-4.0, 5.0, 3.0),
            "L_LEG1": (6.0, 0.0, -2.0),
            "L_FOOT": (-2.0, 0.0, 0.0),
            "R_LEG": (-4.0, -5.0, -3.0),
            "R_LEG1": (6.0, 0.0, 2.0),
            "R_FOOT": (-2.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "desk_radio_dispatch",
        "fps": fps,
        "loop": False,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [
            {"frame": 0, "left": "Open", "right": "Closed"},
            {"frame": 8, "left": "Point", "right": "Closed"},
        ],
        "events": [{"frame": 8, "name": "radio_key"}],
        "frames": frames,
    }


def make_desk_wave_dismiss(rig):
    """Crisp head-shake and dismissive right-hand sweep for prank calls & false alarms."""
    fps = 30.0
    n = 30
    frames = []
    for i in range(n):
        u = i / (n - 1)
        lift = smoothstep(0.08, 0.28, u) * (1.0 - smoothstep(0.76, 0.98, u))
        wave = math.sin(u * math.pi * 4.0) * lift
        shake = math.sin(u * math.pi * 4.0) * lift

        d = {
            "PELVIS": (1.0, 0.0, 0.0),
            "SPINE": (2.0, -3.0 * lift, 0.0),
            "SPINE1": (2.0, -2.0 * lift, 0.0),
            "SPINE2": (1.0, 0.0, 0.0),
            "NECK": (2.0, 12.0 * shake, 0.0),
            "HEAD": (2.0, 18.0 * shake, -4.0 * shake),

            # Left arm (+X): resting on hip/desk edge
            "L_ARM": (0.0, 0.0, 4.0),
            "L_ARM1": (-20.0, -8.0, 16.0),
            "L_ARM2": (-68.0, -10.0, -8.0),
            "L_HAND": (10.0, 0.0, 0.0),

            # Right arm (-X): raises and waves briskly side-to-side ("move along / hang up!")
            "R_ARM": (0.0, 0.0, -6.0),
            "R_ARM1": (-24.0 - 38.0 * lift, 8.0 + 22.0 * wave, -16.0 - 20.0 * lift),
            "R_ARM2": (-70.0 - 18.0 * lift, 12.0 + 18.0 * wave, 8.0),
            "R_HAND": (-12.0 * lift, 10.0 * wave, 0.0),

            "L_LEG": (-3.0, 4.0, 2.5),
            "L_LEG1": (5.0, 0.0, -1.5),
            "L_FOOT": (-2.0, 0.0, 0.0),
            "R_LEG": (-3.0, -4.0, -2.5),
            "R_LEG1": (5.0, 0.0, 1.5),
            "R_FOOT": (-2.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "desk_wave_dismiss",
        "fps": fps,
        "loop": False,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Open"}],
        "events": [{"frame": 10, "name": "dismiss_wave"}],
        "frames": frames,
    }


def make_desk_walkin_walk(rig):
    """Natural brisk walk loop for 3D neighborhood townies approaching/leaving the precinct front counter."""
    fps = 30.0
    n = 28
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        s = math.sin(p)
        c = math.cos(p)
        bob = abs(c)

        l_hip = -22.0 * s - 4.0
        r_hip = +22.0 * s - 4.0
        l_knee = max(4.0, 38.0 * max(0.0, math.sin(p - 0.7)) + 6.0)
        r_knee = max(4.0, 38.0 * max(0.0, math.sin(p + math.pi - 0.7)) + 6.0)

        d = {
            "PELVIS": (4.0, -6.0 * s, 2.5 * s),
            "SPINE": (4.0, 5.0 * s, -1.5 * s),
            "SPINE1": (3.0, 4.0 * s, -1.0 * s),
            "SPINE2": (2.0, 2.0 * s, 0.0),
            "NECK": (-2.0, -2.0 * s, 0.0),
            "HEAD": (-2.0, -2.0 * s, 0.0),

            # Natural counter-swinging arms
            "L_ARM": (0.0, 0.0, 3.0),
            "L_ARM1": (-12.0 + 18.0 * s, -4.0, 10.0),
            "L_ARM2": (-34.0 - 12.0 * max(0.0, -s), -6.0, -4.0),
            "L_HAND": (6.0, 0.0, 0.0),

            "R_ARM": (0.0, 0.0, -3.0),
            "R_ARM1": (-12.0 - 18.0 * s, 4.0, -10.0),
            "R_ARM2": (-34.0 - 12.0 * max(0.0, s), 6.0, 4.0),
            "R_HAND": (6.0, 0.0, 0.0),

            "L_LEG": (l_hip, 3.0, 2.0),
            "L_LEG1": (l_knee, 0.0, -1.0),
            "L_FOOT": (-l_hip * 0.35 - l_knee * 0.25, 0.0, 0.0),
            "R_LEG": (r_hip, -3.0, -2.0),
            "R_LEG1": (r_knee, 0.0, 1.0),
            "R_FOOT": (-r_hip * 0.35 - r_knee * 0.25, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.06 * bob, 0.0), ground_feet=True))

    return {
        "name": "desk_walkin_walk",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 2.1,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Open"}],
        "events": [
            {"frame": 7, "name": "step_l"},
            {"frame": 21, "name": "step_r"},
        ],
        "frames": frames,
    }


def make_desk_walkin_plead(rig):
    """Urgent expressive two-handed pleading & gesturing for walk-in citizens at the precinct desk."""
    fps = 30.0
    n = 36
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        g1 = math.sin(p)
        g2 = math.sin(p * 2.0 + 0.5)
        lean = 0.5 + 0.5 * math.sin(p)

        d = {
            "PELVIS": (4.0 + 3.0 * lean, 4.0 * g1, 0.0),
            "SPINE": (6.0 + 4.0 * lean, 3.0 * g1, 0.0),
            "SPINE1": (5.0 + 3.0 * lean, 2.0 * g1, 0.0),
            "SPINE2": (3.0, 0.0, 0.0),
            "NECK": (-4.0 + 4.0 * g2, 6.0 * g1, 0.0),
            "HEAD": (-4.0 + 6.0 * g2, 8.0 * g1, 4.0 * g1),

            # Both arms raised forward-outward gesturing urgently across the counter!
            "L_ARM": (0.0, 0.0, 6.0),
            "L_ARM1": (-42.0 - 12.0 * g2, -14.0 + 10.0 * g1, 22.0 + 6.0 * lean),
            "L_ARM2": (-68.0 - 14.0 * g1, -14.0, -8.0),
            "L_HAND": (-10.0, 0.0, 0.0),

            "R_ARM": (0.0, 0.0, -6.0),
            "R_ARM1": (-42.0 + 12.0 * g2, 14.0 + 10.0 * g1, -22.0 - 6.0 * lean),
            "R_ARM2": (-68.0 + 14.0 * g1, 14.0, 8.0),
            "R_HAND": (-10.0, 0.0, 0.0),

            "L_LEG": (-5.0, 5.0, 3.0),
            "L_LEG1": (8.0, 0.0, -1.5),
            "L_FOOT": (-3.0, 0.0, 0.0),
            "R_LEG": (-4.0, -5.0, -3.0),
            "R_LEG1": (7.0, 0.0, 1.5),
            "R_FOOT": (-3.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "desk_walkin_plead",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Open"}],
        "events": [],
        "frames": frames,
    }


def main():
    import bpy

    with open(SKEL_REF_PATH, "r", encoding="utf-8") as f:
        skel_ref = json.load(f)
    bones = skel_ref["bones"]
    rig = PoseRig(bones)

    arm_obj = bpy.data.objects.get("AdultRig")
    if arm_obj is None:
        raise RuntimeError("AdultRig armature not found in adult_rig.blend")

    specs = [
        make_desk_idle_watch(rig),
        make_desk_answer_phone(rig),
        make_desk_radio_dispatch(rig),
        make_desk_wave_dismiss(rig),
        make_desk_walkin_walk(rig),
        make_desk_walkin_plead(rig),
    ]

    os.makedirs(OUT_DIR, exist_ok=True)
    for spec in specs:
        validate_clip_arms(spec)
    print(f"Generating & exporting {len(specs)} validated Desk Sergeant clips via Blender 4.5 -> {OUT_DIR}")
    for spec in specs:
        bake_into_blender_and_export(arm_obj, bones, spec)
    print("DESK SERGEANT PACK EXPORT COMPLETE")


if __name__ == "__main__":
    main()
