"""Offline BVH -> .simanim.json Global-Space Delta Quaternion Retargeter (ANIM-04).
Supports standard RH Y-up BVH mocap files (100STYLE CC-BY-4.0, ACCAD CC-BY-3.0, CMU).
Usage:
    python bvh_to_simanim.py <input.bvh> <output.simanim.json> [--name clip_name] [--loop] [--in-place] [--fps 30]
"""
import argparse
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from skel_math import (
    align_track_hemispheres,
    compute_fk,
    q_axis_deg,
    q_conj,
    q_mul,
    q_norm,
    q_rotate,
    v_add,
    v_sub,
    world_rots_to_local,
)

FOOT_BONES = ("L_TOE0", "R_TOE0", "L_TOE02", "R_TOE02", "L_FOOT", "R_FOOT")


class BvhJoint:
    def __init__(self, name, parent):
        self.name = name
        self.parent = parent
        self.offset = (0.0, 0.0, 0.0)
        self.channels = []
        self.channel_start = 0


def parse_bvh(path):
    with open(path, "r", encoding="utf-8", errors="ignore") as f:
        lines = [ln.strip() for ln in f if ln.strip()]

    joints = []
    stack = []
    ch_offset = 0
    i = 0
    in_end_site = False

    while i < len(lines):
        tok = lines[i].split()
        head = tok[0]
        if head in ("ROOT", "JOINT"):
            parent = stack[-1] if stack else None
            j = BvhJoint(tok[1], parent)
            joints.append(j)
            stack.append(j)
            in_end_site = False
        elif head == "End":
            in_end_site = True
            stack.append(None)
        elif head == "OFFSET" and not in_end_site and stack and stack[-1] is not None:
            stack[-1].offset = (float(tok[1]), float(tok[2]), float(tok[3]))
        elif head == "CHANNELS" and not in_end_site and stack and stack[-1] is not None:
            n = int(tok[1])
            stack[-1].channels = tok[2:2 + n]
            stack[-1].channel_start = ch_offset
            ch_offset += n
        elif head == "}":
            if stack:
                popped = stack.pop()
                if popped is None:
                    in_end_site = False
        elif head == "MOTION":
            i += 1
            break
        i += 1

    num_frames = 0
    frame_time = 1.0 / 30.0
    frames = []
    while i < len(lines):
        ln = lines[i]
        if ln.startswith("Frames:"):
            num_frames = int(ln.split(":")[1].strip())
        elif ln.startswith("Frame Time:"):
            frame_time = float(ln.split(":")[1].strip())
        else:
            vals = [float(x) for x in ln.split()]
            if len(vals) >= ch_offset:
                frames.append(vals)
        i += 1

    return joints, frames, frame_time


def bvh_frame_world_poses(joints, frame_vals):
    world_pos = {}
    world_rot = {}
    for j in joints:
        tx, ty, tz = j.offset
        q_local = (0.0, 0.0, 0.0, 1.0)
        for k, ch in enumerate(j.channels):
            val = frame_vals[j.channel_start + k]
            if ch == "Xposition":
                tx = val
            elif ch == "Yposition":
                ty = val
            elif ch == "Zposition":
                tz = val
            elif ch == "Xrotation":
                q_local = q_mul(q_local, q_axis_deg((1.0, 0.0, 0.0), val))
            elif ch == "Yrotation":
                q_local = q_mul(q_local, q_axis_deg((0.0, 1.0, 0.0), val))
            elif ch == "Zrotation":
                q_local = q_mul(q_local, q_axis_deg((0.0, 0.0, 1.0), val))
        q_local = q_norm(q_local)
        if j.parent is None:
            world_pos[j.name] = (tx, ty, tz)
            world_rot[j.name] = q_local
        else:
            pw = world_pos[j.parent.name]
            pr = world_rot[j.parent.name]
            world_pos[j.name] = v_add(pw, q_rotate(pr, (tx, ty, tz)))
            world_rot[j.name] = q_norm(q_mul(pr, q_local))
    return world_pos, world_rot


def retarget_bvh_to_simanim(
    bvh_path,
    skel_ref_path,
    bone_map_path,
    clip_name=None,
    loop=True,
    in_place=True,
    target_fps=30.0,
    source_tag="",
):
    with open(skel_ref_path, "r", encoding="utf-8") as f:
        skel_ref = json.load(f)
    with open(bone_map_path, "r", encoding="utf-8") as f:
        bone_map = json.load(f)["bvhToAdult"]

    bones = skel_ref["bones"]
    rest_world_rot = {b["name"]: tuple(b["restWorldRot"]) for b in bones}
    tpose_world_rot = {b["name"]: tuple(b["tposeWorldRot"]) for b in bones}
    adult_hip_h = float(skel_ref["hipHeight"])

    joints, raw_frames, frame_time = parse_bvh(bvh_path)
    if not raw_frames:
        raise ValueError(f"BVH has no frames: {bvh_path}")

    # Resample to target_fps
    src_fps = 1.0 / max(1e-6, frame_time)
    step = max(1, int(round(src_fps / target_fps)))
    frames = raw_frames[::step]
    actual_fps = src_fps / step

    # Map adult bone -> bvh joint name
    adult_to_bvh = {}
    bvh_joint_names = {j.name for j in joints}
    for bvh_name, adult_name in bone_map.items():
        if bvh_name in bvh_joint_names and adult_name not in adult_to_bvh:
            adult_to_bvh[adult_name] = bvh_name

    # Source hip height at frame 0 (or offset Y)
    root_joint = joints[0]
    p0_src, _ = bvh_frame_world_poses(joints, frames[0])
    src_hip_y = p0_src[root_joint.name][1]
    min_src_y = min(p[1] for p in p0_src.values())
    src_leg_h = max(0.05, src_hip_y - min_src_y)
    hip_scale = adult_hip_h / src_leg_h

    # Extract forward speed along +Z across the clip
    p_last_src, _ = bvh_frame_world_poses(joints, frames[-1])
    total_time = max(1e-4, len(frames) / actual_fps)
    dz_src = (p_last_src[root_joint.name][2] - p0_src[root_joint.name][2]) * hip_scale
    root_speed = max(0.0, dz_src / total_time)

    # Global-space delta retargeting per frame:
    # A_T[i] = (A_S[i] * inv(R_S[i])) * R_T_ref[i]   (standard BVH rest R_S[i] == Identity)
    root_translations = []
    bone_rot_tracks = {b["name"]: [] for b in bones}

    for fi, fvals in enumerate(frames):
        s_pos, s_rot = bvh_frame_world_poses(joints, fvals)

        # Compute desired target world rotations top-down
        target_world_rot = {}
        for b in bones:
            name = b["name"]
            parent = b["parent"]
            if name == "ROOT":
                # Keep ROOT yaw from hips if present; otherwise rest ROOT orientation
                if "PELVIS" in adult_to_bvh:
                    src_q = s_rot[adult_to_bvh["PELVIS"]]
                    # Preserve ROOT's baked +90Y rest frame
                    target_world_rot[name] = rest_world_rot["ROOT"]
                else:
                    target_world_rot[name] = rest_world_rot["ROOT"]
            elif name in adult_to_bvh:
                src_q = s_rot[adult_to_bvh[name]]
                # Standard BVH rest pose is visual T-pose (R_S == Identity)
                target_world_rot[name] = q_norm(q_mul(src_q, tpose_world_rot[name]))
            else:
                # Unmapped bone (e.g., toes/fingers/clavicle): inherit parent delta rigidly
                if parent != "NULL" and parent in target_world_rot:
                    parent_delta = q_mul(target_world_rot[parent], q_conj(rest_world_rot[parent]))
                    target_world_rot[name] = q_norm(q_mul(parent_delta, rest_world_rot[name]))
                else:
                    target_world_rot[name] = rest_world_rot[name]

        local_rots = world_rots_to_local(bones, target_world_rot)

        # Root translation scaled from source hips
        hip_p = s_pos[root_joint.name]
        dx = (hip_p[0] - p0_src[root_joint.name][0]) * hip_scale
        dy = (hip_p[1] - src_hip_y) * hip_scale
        dz = 0.0 if in_place else (hip_p[2] - p0_src[root_joint.name][2]) * hip_scale
        root_t = (
            bones[0]["localTranslation"][0] + dx,
            bones[0]["localTranslation"][1] + dy,
            bones[0]["localTranslation"][2] + dz,
        )

        # Ground feet to Y >= 0 via FK check
        w_pos, _ = compute_fk(bones, local_trans_map={"ROOT": root_t}, local_rot_map=local_rots)
        min_foot_y = min(w_pos[fb][1] for fb in FOOT_BONES if fb in w_pos)
        if min_foot_y < 0.0:
            root_t = (root_t[0], root_t[1] - min_foot_y, root_t[2])

        root_translations.append(root_t)
        for b in bones:
            bone_rot_tracks[b["name"]].append(local_rots[b["name"]])

    tracks = []
    for b in bones:
        n = b["name"]
        rots = align_track_hemispheres(bone_rot_tracks[n])
        tr = {
            "bone": n,
            "rotations": [[round(c, 5) for c in q] for q in rots],
        }
        if n == "ROOT":
            tr["translations"] = [[round(c, 5) for c in t] for t in root_translations]
        tracks.append(tr)

    clip = {
        "version": 1,
        "name": clip_name or os.path.splitext(os.path.basename(bvh_path))[0],
        "skeleton": "adult",
        "fps": round(actual_fps, 3),
        "frameCount": len(frames),
        "duration": round(len(frames) / actual_fps, 5),
        "loop": bool(loop),
        "mask": "full",
        "rootSpeedUnitsPerSec": round(root_speed, 4),
        "source": source_tag or f"retargeted:{os.path.basename(bvh_path)}",
        "events": [],
        "handGestures": [],
        "tracks": tracks,
    }
    return clip


def main():
    ap = argparse.ArgumentParser(description="Retarget BVH mocap to Simitone .simanim.json")
    ap.add_argument("bvh", help="Input .bvh path")
    ap.add_argument("output", help="Output .simanim.json path")
    ap.add_argument("--name", default=None)
    ap.add_argument("--loop", action="store_true", default=True)
    ap.add_argument("--no-loop", dest="loop", action="store_false")
    ap.add_argument("--in-place", action="store_true", default=True)
    ap.add_argument("--fps", type=float, default=30.0)
    ap.add_argument("--source", default="")
    args = ap.parse_args()

    here = os.path.dirname(os.path.abspath(__file__))
    clip = retarget_bvh_to_simanim(
        args.bvh,
        os.path.join(here, "adult_skel_ref.json"),
        os.path.join(here, "bone_maps", "standard_bvh.json"),
        clip_name=args.name,
        loop=args.loop,
        in_place=args.in_place,
        target_fps=args.fps,
        source_tag=args.source,
    )
    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    with open(args.output, "w", encoding="utf-8") as f:
        json.dump(clip, f, indent=2)
    print(f"Retargeted {args.bvh} -> {args.output} ({clip['frameCount']} frames @ {clip['fps']}fps, speed={clip['rootSpeedUnitsPerSec']})")


if __name__ == "__main__":
    main()
