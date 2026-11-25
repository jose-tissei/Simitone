"""Authors and exports the 6-clip Vice Squad (Undercover Sting) custom skeletal animation pack
using exact FreeSO/MonoGame anatomical kinematics, baking every clip onto AdultRig in Blender 4.5
and exporting via blender_export_simanim.py.

Clips:
  1. sting_seated_idle      — cool plainclothes Vice officer seated at speakeasy lounge table
  2. sting_talk_smooth      — leaning forward with smooth charismatic persuasion gestures
  3. sting_wire_check       — discreet hand-to-ear hidden wire tap / cool cover sip gesture
  4. sting_flash_badge      — standing up boldly, thrusting right hand forward to flash Vice badge!
  5. sting_suspect_nervous  — seated suspect glancing around nervously & fidgeting while talking
  6. sting_suspect_handsup  — busted suspect standing with both hands raised high in surrender!

Run:
  "C:\\Program Files\\Blender Foundation\\Blender 4.5\\blender.exe" --factory-startup --background ^
      Tools/AnimPipeline/adult_rig.blend --python Tools/AnimPipeline/generate_sting_pack.py
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
from generate_desk_pack import SEATED_LEGS, validate_clip_arms


def make_sting_seated_idle(rig):
    """Cool, relaxed undercover Vice officer seated at speakeasy lounge table scanning room."""
    fps = 30.0
    n = 48
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        breathe = math.sin(p)
        glance = math.sin(p * 0.5)

        d = {
            "PELVIS": (-3.0, 2.0 * glance, 0.0),
            "SPINE": (-2.0 + 1.0 * breathe, 2.0 * glance, 0.0),
            "SPINE1": (2.0 + 1.0 * breathe, 1.5 * glance, 0.0),
            "SPINE2": (2.0, 1.0 * glance, 0.0),
            "NECK": (1.0, 4.5 * glance, 0.0),
            "HEAD": (2.0 - 1.5 * breathe, 7.0 * glance, 2.0 * math.cos(p)),

            # Relaxed forearms resting near lounge table edge
            "L_ARM": (0.0, 0.0, 5.0),
            "L_ARM1": (-28.0 + 2.0 * breathe, -8.0, 14.0),
            "L_ARM2": (-62.0 - 2.0 * breathe, -10.0, -6.0),
            "L_HAND": (10.0, 0.0, 0.0),

            "R_ARM": (0.0, 0.0, -5.0),
            "R_ARM1": (-30.0 + 2.0 * breathe, 8.0, -14.0),
            "R_ARM2": (-64.0 - 2.0 * breathe, 10.0, 6.0),
            "R_HAND": (8.0, 0.0, 0.0),
            **SEATED_LEGS,
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "sting_seated_idle",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Open"}],
        "events": [],
        "frames": frames,
    }


def make_sting_talk_smooth(rig):
    """Smooth undercover charm & persuasion gestures across the speakeasy table."""
    fps = 30.0
    n = 36
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        g1 = math.sin(p)
        g2 = math.sin(p * 2.0 + 0.4)
        lean = 0.5 + 0.5 * math.sin(p)

        d = {
            "PELVIS": (4.0 + 2.5 * lean, 2.0 * g1, 0.0),
            "SPINE": (6.0 + 3.0 * lean, 3.0 * g1, 0.0),
            "SPINE1": (5.0 + 2.0 * lean, 2.0 * g1, 0.0),
            "SPINE2": (3.0, 1.0 * g1, 0.0),
            "NECK": (-3.0 + 2.5 * g2, 4.0 * g1, 0.0),
            "HEAD": (-3.0 + 4.0 * g2, 6.0 * g1, 3.5 * g1),

            # Smooth open-palm gestures across the table
            "L_ARM": (0.0, 0.0, 5.0),
            "L_ARM1": (-38.0 - 10.0 * g2, -12.0 + 6.0 * g1, 18.0 + 4.0 * lean),
            "L_ARM2": (-68.0 - 12.0 * g1, -12.0, -6.0),
            "L_HAND": (-8.0 + 6.0 * g2, 0.0, 0.0),

            "R_ARM": (0.0, 0.0, -5.0),
            "R_ARM1": (-40.0 + 10.0 * g2, 12.0 + 6.0 * g1, -18.0 - 4.0 * lean),
            "R_ARM2": (-70.0 + 12.0 * g1, 12.0, 6.0),
            "R_HAND": (-8.0 - 6.0 * g2, 0.0, 0.0),
            **SEATED_LEGS,
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "sting_talk_smooth",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Open"}],
        "events": [],
        "frames": frames,
    }


def make_sting_wire_check(rig):
    """Discreetly touches hidden ear/collar wire with left hand while leaning back coolly."""
    fps = 30.0
    n = 36
    frames = []
    for i in range(n):
        t = i / (n - 1)
        if t < 0.25:
            hold = smoothstep(0.0, 0.25, t)
        elif t < 0.78:
            hold = 1.0
        else:
            hold = 1.0 - smoothstep(0.78, 1.0, t)

        nod = math.sin(t * math.pi * 4.0) * hold
        d = {
            "PELVIS": (-4.0, -2.0 * hold, 0.0),
            "SPINE": (-3.0, -2.0 * hold, 0.0),
            "SPINE1": (1.0, -1.5 * hold, 0.0),
            "SPINE2": (1.0, 0.0, 0.0),
            "NECK": (2.0 + 3.0 * nod, -5.0 * hold, 0.0),
            "HEAD": (3.0 + 5.0 * nod, -7.0 * hold, -4.0 * hold),

            # Left arm (+X) raises casually to ear/collar wire
            "L_ARM": (0.0, 0.0, 6.0),
            "L_ARM1": (-30.0 - 34.0 * hold, -10.0, 14.0 + 20.0 * hold),
            "L_ARM2": (-62.0 - 48.0 * hold, -22.0 * hold, -12.0 * hold),
            "L_HAND": (6.0, -6.0, 0.0),

            # Right arm (-X) rests calmly on lounge table
            "R_ARM": (0.0, 0.0, -4.0),
            "R_ARM1": (-32.0, 8.0, -12.0),
            "R_ARM2": (-64.0, 10.0, 6.0),
            "R_HAND": (10.0, 0.0, 0.0),
            **SEATED_LEGS,
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "sting_wire_check",
        "fps": fps,
        "loop": False,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Closed", "right": "Open"}],
        "events": [{"frame": 10, "name": "wire_tap"}],
        "frames": frames,
    }


def make_sting_flash_badge(rig):
    """Dramatic standing Vice Squad bust: thrusts right arm forward flashing police badge!"""
    fps = 30.0
    n = 36
    frames = []
    for i in range(n):
        t = i / (n - 1)
        thrust = smoothstep(0.0, 0.30, t)
        pulse = math.sin(t * math.pi * 4.0) * 0.5 * thrust

        d = {
            "PELVIS": (4.0 + 3.0 * thrust, -10.0 * thrust, 0.0),
            "SPINE": (5.0 + 3.0 * thrust, -6.0 * thrust, 0.0),
            "SPINE1": (4.0, -4.0 * thrust, 0.0),
            "SPINE2": (2.0, 0.0, 0.0),
            "NECK": (-2.0, 8.0 * thrust, 0.0),
            "HEAD": (-2.0 + 2.0 * pulse, 10.0 * thrust, 0.0),

            # Left arm braced authoritatively near hip/belt
            "L_ARM": (0.0, 0.0, 5.0),
            "L_ARM1": (-16.0 - 8.0 * thrust, -10.0, 16.0),
            "L_ARM2": (-54.0 - 12.0 * thrust, -8.0, -6.0),
            "L_HAND": (4.0, 0.0, 0.0),

            # Right arm thrusts forward at chest/eye height flashing the Vice Squad badge!
            "R_ARM": (0.0, 0.0, -8.0),
            "R_ARM1": (-24.0 - 58.0 * thrust - 3.0 * pulse, 12.0 * thrust, -14.0 - 6.0 * thrust),
            "R_ARM2": (-56.0 + 26.0 * thrust, 8.0, 6.0),
            "R_HAND": (-18.0 * thrust, 0.0, 0.0),

            # Firm standing action stance
            "L_LEG": (-10.0 * thrust - 4.0, 6.0, 3.0),
            "L_LEG1": (16.0 * thrust + 6.0, 0.0, -1.5),
            "L_FOOT": (-6.0 * thrust - 2.0, 0.0, 0.0),
            "R_LEG": (8.0 * thrust - 3.0, -6.0, -3.0),
            "R_LEG1": (10.0 * thrust + 5.0, 0.0, 1.5),
            "R_FOOT": (-4.0 * thrust - 2.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "sting_flash_badge",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Closed", "right": "Open"}],
        "events": [{"frame": 10, "name": "badge_flash"}],
        "frames": frames,
    }


def make_sting_suspect_nervous(rig):
    """Seated suspect glancing sideways nervously, fidgeting & leaning guardedly across table."""
    fps = 30.0
    n = 48
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        scan = math.sin(p)
        twitch = math.sin(p * 3.0)

        d = {
            "PELVIS": (3.0, 3.0 * scan, 0.0),
            "SPINE": (6.0 + 1.5 * twitch, 4.0 * scan, 0.0),
            "SPINE1": (5.0, 3.0 * scan, 0.0),
            "SPINE2": (3.0, 2.0 * scan, 0.0),
            "NECK": (-2.0, 10.0 * scan, 0.0),
            "HEAD": (-2.0 + 2.0 * twitch, 14.0 * scan, 3.0 * scan),

            # Guarded fidgety forearms near chest/table
            "L_ARM": (0.0, 0.0, 5.0),
            "L_ARM1": (-32.0 + 4.0 * twitch, -10.0, 14.0),
            "L_ARM2": (-72.0 - 6.0 * twitch, -12.0, -6.0),
            "L_HAND": (8.0 + 4.0 * twitch, 0.0, 0.0),

            "R_ARM": (0.0, 0.0, -5.0),
            "R_ARM1": (-34.0 - 4.0 * twitch, 10.0, -14.0),
            "R_ARM2": (-74.0 + 6.0 * twitch, 12.0, 6.0),
            "R_HAND": (8.0 - 4.0 * twitch, 0.0, 0.0),
            **SEATED_LEGS,
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "sting_suspect_nervous",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Open"}],
        "events": [],
        "frames": frames,
    }


def make_sting_suspect_handsup(rig):
    """Busted suspect standing with both hands raised high in clear surrender!"""
    fps = 30.0
    n = 36
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        shiver = math.sin(p * 2.0)

        d = {
            "PELVIS": (-2.0, 2.0 * math.sin(p), 0.0),
            "SPINE": (-3.0, 1.5 * math.sin(p), 0.0),
            "SPINE1": (-2.0, 0.0, 0.0),
            "SPINE2": (-1.0, 0.0, 0.0),
            "NECK": (3.0, 6.0 * math.sin(p), 0.0),
            "HEAD": (5.0 + 2.0 * shiver, 8.0 * math.sin(p), 2.5 * math.sin(p)),

            # Both arms raised clearly up-and-outward in classic hands-up surrender (valid non-crossing bends!)
            "L_ARM": (0.0, 0.0, 10.0),
            "L_ARM1": (-62.0 - 4.0 * shiver, -18.0, 42.0 + 3.0 * shiver),
            "L_ARM2": (-68.0 - 4.0 * shiver, -10.0, -14.0),
            "L_HAND": (-14.0, 0.0, 0.0),

            "R_ARM": (0.0, 0.0, -10.0),
            "R_ARM1": (-62.0 + 4.0 * shiver, 18.0, -42.0 - 3.0 * shiver),
            "R_ARM2": (-68.0 + 4.0 * shiver, 10.0, 14.0),
            "R_HAND": (-14.0, 0.0, 0.0),

            "L_LEG": (-4.0, 5.0, 3.0),
            "L_LEG1": (8.0, 0.0, -1.5),
            "L_FOOT": (-4.0, 0.0, 0.0),
            "R_LEG": (-4.0, -5.0, -3.0),
            "R_LEG1": (8.0, 0.0, 1.5),
            "R_FOOT": (-4.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "sting_suspect_handsup",
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
        make_sting_seated_idle(rig),
        make_sting_talk_smooth(rig),
        make_sting_wire_check(rig),
        make_sting_flash_badge(rig),
        make_sting_suspect_nervous(rig),
        make_sting_suspect_handsup(rig),
    ]

    os.makedirs(OUT_DIR, exist_ok=True)
    for spec in specs:
        validate_clip_arms(spec)
    print(f"Generating & exporting {len(specs)} validated Vice Squad Sting clips via Blender 4.5 -> {OUT_DIR}")
    for spec in specs:
        bake_into_blender_and_export(arm_obj, bones, spec)
    print("VICE SQUAD STING PACK EXPORT COMPLETE")


if __name__ == "__main__":
    main()
