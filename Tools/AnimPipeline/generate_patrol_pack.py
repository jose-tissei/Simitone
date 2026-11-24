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


def validate_clip_arms(spec):
    """Verify every frame has anatomically valid, non-crossing arms and bent elbows."""
    name = spec["name"]
    for fi, (_root_t, _lrots, w_pos) in enumerate(spec["frames"]):
        for side, sign in (("L", +1.0), ("R", -1.0)):
            sh = w_pos[f"{side}_ARM1"]
            el = w_pos[f"{side}_ARM2"]
            hd = w_pos[f"{side}_HAND"]
            # Elbow must stay on its own anatomical side relative to shoulder (never cross sternum!)
            if sign * (el[0] - sh[0]) < -0.18:
                raise AssertionError(
                    f"{name} f={fi} {side} elbow crossed inward: sh={sh[0]:+.2f} el={el[0]:+.2f}"
                )
            # Hand must not cross deeply onto opposite flank
            if sign * hd[0] < -0.22:
                raise AssertionError(
                    f"{name} f={fi} {side} hand crossed opposite side: hd={hd[0]:+.2f}"
                )
            ux, uy, uz = el[0] - sh[0], el[1] - sh[1], el[2] - sh[2]
            lx, ly, lz = hd[0] - el[0], hd[1] - el[1], hd[2] - el[2]
            ulen = max(1e-6, math.sqrt(ux * ux + uy * uy + uz * uz))
            llen = max(1e-6, math.sqrt(lx * lx + ly * ly + lz * lz))
            dot = max(-1.0, min(1.0, (ux * lx + uy * ly + uz * lz) / (ulen * llen)))
            bend = math.degrees(math.acos(dot))
            if bend < 22.0 or bend > 135.0:
                raise AssertionError(
                    f"{name} f={fi} {side} elbow bend out of range: {bend:.1f} deg"
                )


def make_patrol_sneak_loop(rig):
    """Unmistakable crouched stealth tiptoe sneak for creeping up behind a Burglar.
    Anatomical FK convention (MonoGame RH: +Y Up, +Z Forward, +X Sim Left, -X Sim Right):
    - Arms held wide & poised forward-bent at waist/ribcage height for stealth balance
    - Counter-swinging smoothly in opposite phase to legs, elbows clearly outside torso.
    """
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

        # Classic wide-poised stealth balance arms (opposite phase to legs, elbows out on own side!)
        # When Right leg lifts (s < 0), Left arm glides slightly forward (-deg pitch); vice versa for Right arm.
        l_sh_pitch = -28.0 + 18.0 * s
        r_sh_pitch = -28.0 - 18.0 * s
        l_elbow = -84.0 - 14.0 * r_lift
        r_elbow = -84.0 - 14.0 * l_lift

        d = {
            "PELVIS": (18.0 + 3.0 * math.cos(2.0 * p), -9.0 * s, 5.5 * s),
            "SPINE": (16.0, 5.0 * s, -3.5 * s),
            "SPINE1": (14.0, 5.5 * s, -2.5 * s),
            "SPINE2": (8.0, 4.0 * s, 0.0),
            # Counter-pitch neck & head so eyes stay locked straight ahead on the Burglar!
            "NECK": (-18.0, -5.0 * s, 0.0),
            "HEAD": (-16.0, -5.0 * s + 6.0 * math.sin(p * 0.5), 0.0),

            # Left arm (+X): cleanly outside torso (+roll_z), poised forward at shoulder width
            "L_ARM": (0.0, 0.0, 4.0),
            "L_ARM1": (l_sh_pitch, 4.0, 14.0 + 4.0 * c),
            "L_ARM2": (l_elbow, -14.0, -8.0),
            "L_HAND": (10.0 + 6.0 * r_lift, 0.0, 0.0),

            # Right arm (-X): exact anatomical mirror
            "R_ARM": (0.0, 0.0, -4.0),
            "R_ARM1": (r_sh_pitch, -4.0, -14.0 + 4.0 * c),
            "R_ARM2": (r_elbow, 14.0, 8.0),
            "R_HAND": (10.0 + 6.0 * l_lift, 0.0, 0.0),

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
        frames.append(rig.build_pose(d, root_offset=(sway_x, bob_y, 0.0), ground_feet=True))

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
    """Spring from stealth crouch into decisive FREEZE! halt stance -> right-belt cuff reach -> cuff click."""
    fps = 30.0
    n = 28
    frames = []
    for i in range(n):
        u = i / (n - 1)
        rise = smoothstep(0.0, 0.32, u)
        belt = smoothstep(0.38, 0.56, u) * (1.0 - smoothstep(0.62, 0.78, u))
        snap = smoothstep(0.68, 0.88, u)

        # Left arm (+X): rises from sneak crouch into authoritative forward HALT! then closes for cuff click
        l_sh_p = lerp(-28.0, -68.0, rise) + 14.0 * snap
        l_sh_y = lerp(4.0, -6.0, rise) - 18.0 * snap
        l_sh_r = lerp(14.0, 10.0, rise) - 4.0 * snap
        l_el_p = lerp(-84.0, -46.0, rise) - 16.0 * snap

        # Right arm (-X): rises to HALT! -> drops to RIGHT duty belt (-X) to unclip cuffs -> snaps forward!
        r_sh_p = lerp(-28.0, -68.0, rise) + 86.0 * belt + 12.0 * snap
        r_sh_y = lerp(-4.0, 6.0, rise) - 10.0 * belt + 20.0 * snap
        r_sh_r = lerp(-14.0, -10.0, rise) - 10.0 * belt + 4.0 * snap
        r_el_p = lerp(-84.0, -46.0, rise) - 42.0 * belt - 14.0 * snap

        d = {
            "PELVIS": (lerp(18.0, 4.0, rise), lerp(-6.0, -10.0, rise), 0.0),
            "SPINE": (lerp(15.0, 5.0, rise), lerp(4.0, 8.0, rise), 0.0),
            "SPINE1": (lerp(12.0, 3.0, rise), 4.0 * snap, 0.0),
            "SPINE2": (lerp(6.0, 2.0, rise), 0.0, 0.0),
            "NECK": (lerp(-14.0, -2.0, rise), 0.0, 0.0),
            "HEAD": (lerp(-12.0, -3.0, rise), 0.0, 0.0),

            "L_ARM": (0.0, 0.0, 4.0),
            "L_ARM1": (l_sh_p, l_sh_y, l_sh_r),
            "L_ARM2": (l_el_p, -12.0 - 10.0 * snap, -6.0),
            "L_HAND": (lerp(10.0, -12.0, rise), 0.0, 0.0),

            "R_ARM": (0.0, 0.0, -4.0),
            "R_ARM1": (r_sh_p, r_sh_y, r_sh_r),
            "R_ARM2": (r_el_p, 12.0 + 10.0 * snap, 6.0),
            "R_HAND": (lerp(10.0, -12.0, rise) + 14.0 * belt, 0.0, 0.0),

            "L_LEG": (lerp(-42.0, -20.0, rise), 8.0, 4.0),
            "L_LEG1": (lerp(60.0, 26.0, rise), 0.0, -2.0),
            "L_FOOT": (lerp(-18.0, -6.0, rise), 0.0, 0.0),
            "R_LEG": (lerp(-32.0, 12.0, rise), -8.0, -4.0),
            "R_LEG1": (lerp(52.0, 14.0, rise), 0.0, 2.0),
            "R_FOOT": (lerp(-18.0, -20.0, rise), 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.25 * rise), ground_feet=True))

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
    """Officer holds citation pad in Left hand (+X), writes briskly with Right hand (-X), tears off slip."""
    fps = 30.0
    n = 30
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        u = i / n
        scrib_y = math.sin(p * 3.0) if u < 0.72 else 0.0
        scrib_p = math.cos(p * 6.0) if u < 0.72 else 0.0
        tear = smoothstep(0.72, 0.88, u) * (1.0 - smoothstep(0.90, 1.0, u))

        d = {
            "PELVIS": (2.0, -4.0, 0.0),
            "SPINE": (4.0, 3.0, 0.0),
            "SPINE1": (4.0, 2.0, 0.0),
            "SPINE2": (2.0, 0.0, 0.0),
            "NECK": (10.0 - 8.0 * tear, -4.0, 0.0),
            "HEAD": (12.0 - 12.0 * tear, -3.0, 0.0),

            # Left arm (+X): holds citation pad steadily in front of chest, elbow cleanly out on +X
            "L_ARM": (0.0, 0.0, 4.0),
            "L_ARM1": (-28.0, -14.0, 16.0),
            "L_ARM2": (-86.0, -24.0, -10.0),
            "L_HAND": (6.0, 0.0, 0.0),

            # Right arm (-X): scribbles briskly on pad near midline, then pulls up-and-right on tear!
            "R_ARM": (0.0, 0.0, -4.0),
            "R_ARM1": (-32.0 + 4.0 * scrib_p - 18.0 * tear, 22.0 + 8.0 * scrib_y - 30.0 * tear, -16.0 - 10.0 * tear),
            "R_ARM2": (-92.0 + 6.0 * scrib_p + 24.0 * tear, 32.0 - 22.0 * tear, 10.0),
            "R_HAND": (4.0, 0.0, 0.0),

            "L_LEG": (-4.0, 5.0, 3.0),
            "L_LEG1": (6.0, 0.0, -2.0),
            "L_FOOT": (-2.0, 0.0, 0.0),
            "R_LEG": (-2.0, -5.0, -3.0),
            "R_LEG1": (4.0, 0.0, 2.0),
            "R_FOOT": (-2.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

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
    """Crouched tactical inspection with Left arm braced near Left knee (+X) and Right-hand flashlight sweeping."""
    fps = 30.0
    n = 32
    frames = []
    for i in range(n):
        p = (i / n) * 2.0 * math.pi
        sweep = math.sin(p)
        nod = math.cos(p)

        d = {
            "PELVIS": (26.0, 4.0 * sweep, 0.0),
            "SPINE": (18.0, 4.0 * sweep, 0.0),
            "SPINE1": (14.0, 3.0 * sweep, 0.0),
            "SPINE2": (8.0, 0.0, 0.0),
            "NECK": (-10.0, 8.0 * sweep, 0.0),
            "HEAD": (-8.0, 12.0 * sweep, 0.0),

            # Left arm (+X): braced cleanly near Left knee for crouched balance
            "L_ARM": (0.0, 0.0, 4.0),
            "L_ARM1": (-18.0, 2.0, 12.0),
            "L_ARM2": (-64.0, -10.0, -6.0),
            "L_HAND": (12.0, 0.0, 0.0),

            # Right arm (-X): flashlight held forward-down, sweeping cleanly across the hazard
            "R_ARM": (0.0, 0.0, -4.0),
            "R_ARM1": (-48.0 + 6.0 * nod, -2.0 + 14.0 * sweep, -14.0),
            "R_ARM2": (-56.0 - 8.0 * nod, 8.0, 6.0),
            "R_HAND": (8.0, 0.0, 0.0),

            "L_LEG": (-68.0, 10.0, 6.0),
            "L_LEG1": (96.0, 0.0, -2.0),
            "L_FOOT": (-28.0, 0.0, 0.0),
            "R_LEG": (-54.0, -10.0, -6.0),
            "R_LEG1": (84.0, 0.0, 2.0),
            "R_FOOT": (-30.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), ground_feet=True))

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
    for spec in specs:
        validate_clip_arms(spec)
    print(f"Generating & exporting {len(specs)} validated Beat Patrol clips via Blender 4.5 -> {OUT_DIR}")
    for spec in specs:
        bake_into_blender_and_export(arm_obj, bones, spec)
    print("PATROL PACK EXPORT COMPLETE")


if __name__ == "__main__":
    main()
