"""Authors and exports the 7-clip Detective (Crime Scene Deduction) custom skeletal animation pack
using exact FreeSO/MonoGame anatomical kinematics, baking every clip onto AdultRig in Blender 4.5
and exporting via blender_export_simanim.py.

Clips:
  1. detective_idle_think      — thoughtful standing detective chin-stroke & scanning the crime scene
  2. detective_inspect_ground  — kneeling/crouching forensic inspection of floor footprint evidence
  3. detective_take_notes      — holding notepad in left hand & scribbling forensic notes at desk/clue
  4. detective_question        — holding notepad & gesturing firmly while questioning a suspect
  5. detective_point_accuse    — dramatic outstretched finger-point accusation climax!
  6. detective_suspect_nervous — standing suspect shifting weight, glancing sideways & crossing arms
  7. detective_culprit_caught  — caught culprit flinching & raising both hands in surrender!

Run:
  "C:\\Program Files\\Blender Foundation\\Blender 4.5\\blender.exe" --factory-startup --background ^
      Tools/AnimPipeline/adult_rig.blend --python Tools/AnimPipeline/generate_detective_pack.py
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
from generate_desk_pack import validate_clip_arms

STANDING_LEGS = {
    "L_LEG": (-3.0, 5.0, 2.5),
    "L_LEG1": (5.0, 0.0, -1.0),
    "L_FOOT": (-2.0, 0.0, 0.0),
    "R_LEG": (2.0, -5.0, -2.5),
    "R_LEG1": (4.0, 0.0, 1.0),
    "R_FOOT": (-2.0, 0.0, 0.0),
}


def make_detective_idle_think(rig):
    """Thoughtful standing detective chin-stroke & observant room scan."""
    fps = 30.0
    n = 48
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        breathe = math.sin(p)
        scan = math.sin(p * 0.5)

        d = {
            "PELVIS": (2.0, 3.0 * scan, 0.0),
            "SPINE": (3.0 + 1.0 * breathe, 2.5 * scan, 0.0),
            "SPINE1": (2.0 + 1.0 * breathe, 2.0 * scan, 0.0),
            "SPINE2": (2.0, 1.5 * scan, 0.0),
            "NECK": (3.0, 6.0 * scan, 0.0),
            "HEAD": (4.0 - 1.5 * breathe, 9.0 * scan, 2.5 * math.cos(p)),

            # Left arm bent across midriff supporting right elbow
            "L_ARM": (0.0, 0.0, 4.0),
            "L_ARM1": (-26.0, -14.0, 14.0),
            "L_ARM2": (-72.0, -24.0, -8.0),
            "L_HAND": (6.0, 0.0, 0.0),

            # Right hand raised thoughtfully near chin
            "R_ARM": (0.0, 0.0, -5.0),
            "R_ARM1": (-48.0 - 3.0 * breathe, 12.0, -14.0),
            "R_ARM2": (-96.0 + 3.0 * breathe, 18.0, 8.0),
            "R_HAND": (-10.0, 0.0, 0.0),
            **STANDING_LEGS,
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "detective_idle_think",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Closed"}],
        "events": [],
        "frames": frames,
    }


def make_detective_inspect_ground(rig):
    """Kneeling/crouching forensic inspection of floor footprint evidence."""
    fps = 30.0
    n = 48
    frames = []
    for i in range(n):
        t = i / (n - 1)
        if t < 0.22:
            crouch = smoothstep(0.0, 0.22, t)
        elif t < 0.80:
            crouch = 1.0
        else:
            crouch = 1.0 - smoothstep(0.80, 1.0, t)

        scan = math.sin(t * math.pi * 4.0) * crouch
        d = {
            "PELVIS": (18.0 * crouch, 4.0 * scan, 0.0),
            "SPINE": (16.0 * crouch, 3.0 * scan, 0.0),
            "SPINE1": (12.0 * crouch, 2.0 * scan, 0.0),
            "SPINE2": (6.0 * crouch, 0.0, 0.0),
            "NECK": (10.0 * crouch, 5.0 * scan, 0.0),
            "HEAD": (14.0 * crouch, 8.0 * scan, 2.0 * scan),

            # Left arm braces on knee
            "L_ARM": (0.0, 0.0, 5.0),
            "L_ARM1": (-30.0 * crouch - 10.0, -8.0, 12.0),
            "L_ARM2": (-42.0 * crouch - 16.0, -8.0, -4.0),
            "L_HAND": (12.0 * crouch, 0.0, 0.0),

            # Right arm reaches low scanning the footprint with forensic magnifier
            "R_ARM": (0.0, 0.0, -6.0),
            "R_ARM1": (-52.0 * crouch - 12.0, 10.0 + 12.0 * scan, -14.0),
            "R_ARM2": (-32.0 * crouch - 14.0, 6.0, 4.0),
            "R_HAND": (-12.0 * crouch, 0.0, 0.0),

            # Deep detective crouch
            "L_LEG": (-68.0 * crouch - 4.0, 8.0 * crouch, 4.0),
            "L_LEG1": (96.0 * crouch + 6.0, 0.0, -2.0),
            "L_FOOT": (-26.0 * crouch - 2.0, 0.0, 0.0),

            "R_LEG": (-54.0 * crouch - 3.0, -8.0 * crouch, -4.0),
            "R_LEG1": (88.0 * crouch + 5.0, 0.0, 2.0),
            "R_FOOT": (-28.0 * crouch - 2.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "detective_inspect_ground",
        "fps": fps,
        "loop": False,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Closed"}],
        "events": [{"frame": 16, "name": "camera_flash"}],
        "frames": frames,
    }


def make_detective_take_notes(rig):
    """Holding case notebook in left hand and jotting forensic clues with right hand."""
    fps = 30.0
    n = 42
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        scribble = math.sin(p * 3.0)
        glance_up = max(0.0, math.sin(p))

        d = {
            "PELVIS": (3.0, 2.0 * math.sin(p), 0.0),
            "SPINE": (5.0, 2.0 * math.sin(p), 0.0),
            "SPINE1": (4.0, 0.0, 0.0),
            "SPINE2": (3.0, 0.0, 0.0),
            "NECK": (10.0 - 9.0 * glance_up, 2.0 * math.sin(p), 0.0),
            "HEAD": (14.0 - 12.0 * glance_up, 4.0 * math.sin(p), 2.0 * scribble),

            # Left hand holds open detective notepad at chest height
            "L_ARM": (0.0, 0.0, 5.0),
            "L_ARM1": (-36.0, -12.0, 14.0),
            "L_ARM2": (-76.0, -20.0, -8.0),
            "L_HAND": (12.0, 6.0, 0.0),

            # Right hand writes crisp notes across the pad
            "R_ARM": (0.0, 0.0, -5.0),
            "R_ARM1": (-38.0 + 3.0 * scribble, 12.0 + 6.0 * scribble, -14.0),
            "R_ARM2": (-78.0 - 5.0 * scribble, 22.0, 8.0),
            "R_HAND": (-8.0 + 6.0 * scribble, 0.0, 0.0),
            **STANDING_LEGS,
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "detective_take_notes",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Closed"}],
        "events": [{"frame": 8, "name": "scribble"}],
        "frames": frames,
    }


def make_detective_question(rig):
    """Authoritative detective questioning stance — notepad in left hand, probing gesture with right."""
    fps = 30.0
    n = 36
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        g1 = math.sin(p)
        g2 = math.sin(p * 2.0 + 0.5)

        d = {
            "PELVIS": (3.0, -3.0 + 3.0 * g1, 0.0),
            "SPINE": (5.0 + 2.0 * g1, -2.0 + 3.0 * g1, 0.0),
            "SPINE1": (4.0, 2.0 * g1, 0.0),
            "SPINE2": (2.0, 0.0, 0.0),
            "NECK": (-2.0 + 2.0 * g2, 3.0 * g1, 0.0),
            "HEAD": (-2.0 + 4.0 * g2, 5.0 * g1, 3.0 * g1),

            # Left arm holds case dossier steady
            "L_ARM": (0.0, 0.0, 5.0),
            "L_ARM1": (-32.0, -12.0, 14.0),
            "L_ARM2": (-74.0, -18.0, -8.0),
            "L_HAND": (10.0, 0.0, 0.0),

            # Right arm gestures incisively toward the suspect
            "R_ARM": (0.0, 0.0, -6.0),
            "R_ARM1": (-42.0 - 14.0 * g2, 10.0 + 6.0 * g1, -16.0),
            "R_ARM2": (-56.0 + 18.0 * g1, 10.0, 6.0),
            "R_HAND": (-10.0 + 8.0 * g2, 0.0, 0.0),
            **STANDING_LEGS,
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "detective_question",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Open"}],
        "events": [],
        "frames": frames,
    }


def make_detective_point_accuse(rig):
    """Dramatic Phoenix-style / noir detective outstretched accusation point!"""
    fps = 30.0
    n = 36
    frames = []
    for i in range(n):
        t = i / (n - 1)
        thrust = smoothstep(0.0, 0.28, t)
        pulse = math.sin(t * math.pi * 4.0) * 0.5 * thrust

        d = {
            "PELVIS": (5.0 + 4.0 * thrust, -12.0 * thrust, 0.0),
            "SPINE": (6.0 + 4.0 * thrust, -8.0 * thrust, 0.0),
            "SPINE1": (5.0, -4.0 * thrust, 0.0),
            "SPINE2": (2.0, 0.0, 0.0),
            "NECK": (-3.0, 10.0 * thrust, 0.0),
            "HEAD": (-3.0 + 2.0 * pulse, 12.0 * thrust, 0.0),

            # Left arm holds case file firmly at side/chest
            "L_ARM": (0.0, 0.0, 5.0),
            "L_ARM1": (-22.0 - 6.0 * thrust, -12.0, 16.0),
            "L_ARM2": (-64.0 - 10.0 * thrust, -12.0, -6.0),
            "L_HAND": (6.0, 0.0, 0.0),

            # Right arm snaps straight forward pointing dramatically at the culprit!
            "R_ARM": (0.0, 0.0, -8.0),
            "R_ARM1": (-24.0 - 64.0 * thrust - 2.5 * pulse, 12.0 * thrust, -14.0 - 4.0 * thrust),
            "R_ARM2": (-52.0 + 42.0 * thrust, 6.0, 4.0),
            "R_HAND": (-12.0 * thrust, 0.0, 0.0),

            # Wide dramatic lunge stance
            "L_LEG": (-14.0 * thrust - 4.0, 6.0, 3.0),
            "L_LEG1": (20.0 * thrust + 6.0, 0.0, -1.5),
            "L_FOOT": (-8.0 * thrust - 2.0, 0.0, 0.0),
            "R_LEG": (10.0 * thrust - 3.0, -6.0, -3.0),
            "R_LEG1": (12.0 * thrust + 5.0, 0.0, 1.5),
            "R_FOOT": (-4.0 * thrust - 2.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "detective_point_accuse",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Closed"}],
        "events": [{"frame": 10, "name": "accuse_point"}],
        "frames": frames,
    }


def make_detective_suspect_nervous(rig):
    """Standing parlor suspect shifting weight nervously, glancing sideways, arms guarded."""
    fps = 30.0
    n = 48
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        scan = math.sin(p)
        fidget = math.sin(p * 3.0)

        d = {
            "PELVIS": (2.0, 4.0 * scan, 2.0 * scan),
            "SPINE": (4.0 + 1.5 * fidget, 4.0 * scan, -1.5 * scan),
            "SPINE1": (4.0, 3.0 * scan, 0.0),
            "SPINE2": (2.0, 2.0 * scan, 0.0),
            "NECK": (-1.0, 9.0 * scan, 0.0),
            "HEAD": (-1.0 + 2.0 * fidget, 14.0 * scan, 3.5 * scan),

            # Defensive fidgety arms tucked in front of torso
            "L_ARM": (0.0, 0.0, 5.0),
            "L_ARM1": (-28.0 + 3.0 * fidget, -14.0, 14.0),
            "L_ARM2": (-76.0 - 4.0 * fidget, -20.0, -8.0),
            "L_HAND": (8.0 + 4.0 * fidget, 0.0, 0.0),

            "R_ARM": (0.0, 0.0, -5.0),
            "R_ARM1": (-30.0 - 3.0 * fidget, 14.0, -14.0),
            "R_ARM2": (-78.0 + 4.0 * fidget, 20.0, 8.0),
            "R_HAND": (8.0 - 4.0 * fidget, 0.0, 0.0),
            **STANDING_LEGS,
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "detective_suspect_nervous",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Open"}],
        "events": [],
        "frames": frames,
    }


def make_detective_culprit_caught(rig):
    """Culprit caught red-handed — recoils and throws both hands up in surrender!"""
    fps = 30.0
    n = 36
    frames = []
    for i in range(n):
        t = i / (n - 1)
        raise_k = smoothstep(0.0, 0.35, t)
        shiver = math.sin(t * math.pi * 6.0) * raise_k

        d = {
            "PELVIS": (-4.0 * raise_k, 3.0 * shiver, 0.0),
            "SPINE": (-5.0 * raise_k, 2.0 * shiver, 0.0),
            "SPINE1": (-3.0 * raise_k, 0.0, 0.0),
            "SPINE2": (-2.0 * raise_k, 0.0, 0.0),
            "NECK": (4.0 * raise_k, -5.0 * shiver, 0.0),
            "HEAD": (5.0 * raise_k, -8.0 * shiver, 4.0 * shiver),

            # Both arms shoot up high in classic non-crossing surrender pose
            "L_ARM": (0.0, 0.0, 6.0 + 4.0 * raise_k),
            "L_ARM1": (-28.0 - 34.0 * raise_k + 3.0 * shiver, -18.0, 18.0 + 24.0 * raise_k),
            "L_ARM2": (-56.0 - 12.0 * raise_k, -10.0, -14.0),
            "L_HAND": (-14.0 * raise_k, 0.0, 0.0),

            "R_ARM": (0.0, 0.0, -6.0 - 4.0 * raise_k),
            "R_ARM1": (-28.0 - 34.0 * raise_k - 3.0 * shiver, 18.0, -18.0 - 24.0 * raise_k),
            "R_ARM2": (-56.0 - 12.0 * raise_k, 10.0, 14.0),
            "R_HAND": (-14.0 * raise_k, 0.0, 0.0),

            # Slight recoil step back
            "L_LEG": (-8.0 * raise_k - 2.0, 5.0, 3.0),
            "L_LEG1": (14.0 * raise_k + 4.0, 0.0, -1.0),
            "L_FOOT": (-5.0 * raise_k - 2.0, 0.0, 0.0),
            "R_LEG": (6.0 * raise_k, -5.0, -3.0),
            "R_LEG1": (10.0 * raise_k + 4.0, 0.0, 1.0),
            "R_FOOT": (-4.0 * raise_k - 2.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

    return {
        "name": "detective_culprit_caught",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Open"}],
        "events": [{"frame": 10, "name": "surrender"}],
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
        make_detective_idle_think(rig),
        make_detective_inspect_ground(rig),
        make_detective_take_notes(rig),
        make_detective_question(rig),
        make_detective_point_accuse(rig),
        make_detective_suspect_nervous(rig),
        make_detective_culprit_caught(rig),
    ]

    os.makedirs(OUT_DIR, exist_ok=True)
    for spec in specs:
        validate_clip_arms(spec)
    print(f"Generating & exporting {len(specs)} validated Detective clips via Blender 4.5 -> {OUT_DIR}")
    for spec in specs:
        bake_into_blender_and_export(arm_obj, bones, spec)
    print("DETECTIVE CRIME SCENE PACK EXPORT COMPLETE")


if __name__ == "__main__":
    main()
