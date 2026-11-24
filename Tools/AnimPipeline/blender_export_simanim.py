"""Headless Blender 4.5 exporter: samples any Blender Action on AdultRig and writes
exact FreeSO/MonoGame local bone transforms into .simanim.json (ANIM-05).

Run standalone:
  "C:\\Program Files\\Blender Foundation\\Blender 4.5\\blender.exe" --background adult_rig.blend \
      --python Tools/AnimPipeline/blender_export_simanim.py -- --action MyAction --out out.simanim.json
"""
import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from skel_math import align_track_hemispheres, compute_fk, q_conj, q_mul, q_norm

SKEL_REF_PATH = os.path.join(HERE, "adult_skel_ref.json")
BASIS_MG_TO_BL = Matrix.Rotation(math.radians(90.0), 4, "X")
BASIS_BL_TO_MG = BASIS_MG_TO_BL.inverted()
FOOT_BONES = ("L_TOE0", "R_TOE0", "L_TOE02", "R_TOE02", "L_FOOT", "R_FOOT")


def bl_mat4_to_mg_pos_quat(m_bl):
    """Exact inverse of mg_quat_to_bl_mat4: converts Blender world bone matrix
    back to MonoGame world position (x,y,z) and Hamilton quaternion (x,y,z,w)."""
    m_mg = BASIS_BL_TO_MG @ m_bl @ BASIS_MG_TO_BL
    pos = m_mg.translation
    q_bl = m_mg.to_quaternion().normalized()
    # Blender Quaternion is (w, x, y, z); FreeSO/MonoGame is (x, y, z, w)
    return (float(pos.x), float(pos.y), float(pos.z)), q_norm((float(q_bl.x), float(q_bl.y), float(q_bl.z), float(q_bl.w)))


def export_action_to_simanim(
    arm_obj,
    action_name,
    out_path,
    clip_name=None,
    fps=30.0,
    loop=True,
    mask="full",
    root_speed=0.0,
    source="blender4.5",
    hand_gestures=None,
    events=None,
    ground_feet=False,
    frame_start=None,
    frame_end=None,
):
    with open(SKEL_REF_PATH, "r", encoding="utf-8") as f:
        skel_ref = json.load(f)
    bones = skel_ref["bones"]

    scene = bpy.context.scene
    scene.render.fps = int(round(fps))

    if action_name:
        act = bpy.data.actions.get(action_name)
        if act is None:
            raise ValueError(f"Action not found: {action_name}")
        if not arm_obj.animation_data:
            arm_obj.animation_data_create()
        arm_obj.animation_data.action = act
        if frame_start is None:
            frame_start = int(round(act.frame_range[0]))
        if frame_end is None:
            frame_end = int(round(act.frame_range[1]))
    else:
        act = arm_obj.animation_data.action if arm_obj.animation_data else None
        if frame_start is None:
            frame_start = int(scene.frame_start)
        if frame_end is None:
            frame_end = int(scene.frame_end)

    # For seamless looping, if the last frame duplicates frame_start, caller passes frame_end = N-1
    frame_count = max(1, (frame_end - frame_start) + 1)

    root_trans = []
    bone_rots = {b["name"]: [] for b in bones}
    arm_world = arm_obj.matrix_world

    for f_idx in range(frame_start, frame_end + 1):
        scene.frame_set(f_idx)
        bpy.context.view_layer.update()

        world_pos_mg = {}
        world_rot_mg = {}
        for b in bones:
            name = b["name"]
            pb = arm_obj.pose.bones[name]
            m_bl = arm_world @ pb.matrix
            pos_mg, rot_mg = bl_mat4_to_mg_pos_quat(m_bl)
            world_pos_mg[name] = pos_mg
            world_rot_mg[name] = rot_mg

        # Convert MonoGame world rotations back to exact FreeSO local bone rotations
        local_rots = {}
        for b in bones:
            name = b["name"]
            parent = b["parent"]
            if parent == "NULL":
                local_rots[name] = world_rot_mg[name]
            else:
                local_rots[name] = q_norm(q_mul(q_conj(world_rot_mg[parent]), world_rot_mg[name]))

        rt = world_pos_mg["ROOT"]
        if ground_feet:
            fk_pos, _ = compute_fk(bones, local_trans_map={"ROOT": rt}, local_rot_map=local_rots)
            min_y = min(fk_pos[fb][1] for fb in FOOT_BONES if fb in fk_pos)
            if min_y < 0.0:
                rt = (rt[0], rt[1] - min_y, rt[2])

        root_trans.append(rt)
        for b in bones:
            bone_rots[b["name"]].append(local_rots[b["name"]])

    # Collect Blender pose markers as events if present
    merged_events = list(events or [])
    if act and getattr(act, "pose_markers", None):
        for pm in act.pose_markers:
            merged_events.append({
                "frame": max(0, int(pm.frame) - frame_start),
                "name": pm.name,
                "value": "1",
            })

    tracks = []
    for b in bones:
        n = b["name"]
        aligned = align_track_hemispheres(bone_rots[n])
        tr = {
            "bone": n,
            "rotations": [[round(c, 5) for c in q] for q in aligned],
        }
        if n == "ROOT":
            tr["translations"] = [[round(c, 5) for c in t] for t in root_trans]
        tracks.append(tr)

    clip = {
        "version": 1,
        "name": clip_name or action_name or "clip",
        "skeleton": "adult",
        "fps": round(float(fps), 3),
        "frameCount": frame_count,
        "duration": round(frame_count / float(fps), 5),
        "loop": bool(loop),
        "mask": mask,
        "rootSpeedUnitsPerSec": round(float(root_speed), 4),
        "source": source,
        "events": merged_events,
        "handGestures": list(hand_gestures or []),
        "tracks": tracks,
    }

    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(clip, f, indent=2)
    return clip


def main():
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    else:
        argv = []
    ap = argparse.ArgumentParser()
    ap.add_argument("--action", default=None)
    ap.add_argument("--out", required=True)
    ap.add_argument("--name", default=None)
    ap.add_argument("--fps", type=float, default=30.0)
    ap.add_argument("--no-loop", dest="loop", action="store_false", default=True)
    ap.add_argument("--mask", default="full")
    ap.add_argument("--root-speed", type=float, default=0.0)
    args = ap.parse_args(argv)

    arm_obj = bpy.data.objects.get("AdultRig")
    if arm_obj is None:
        raise SystemExit("AdultRig object not found in .blend file")

    clip = export_action_to_simanim(
        arm_obj,
        action_name=args.action,
        out_path=args.out,
        clip_name=args.name,
        fps=args.fps,
        loop=args.loop,
        mask=args.mask,
        root_speed=args.root_speed,
    )
    print(f"Exported {clip['name']} -> {args.out} ({clip['frameCount']} frames)")


if __name__ == "__main__":
    main()
