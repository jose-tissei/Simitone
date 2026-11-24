"""Extracts adult.skel (29 bones) from TS1 Animation.far into adult_skel_ref.json
with exact FreeSO right-handed rest transforms AND a matching visual T-pose reference
for BVH/mocap retargeting."""
import json
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from skel_math import compute_fk, q_axis_deg, q_conj, q_from_two_vectors, q_mul, q_norm, q_rotate, v_sub

DEFAULT_FAR_CANDIDATES = [
    r"C:\Program Files (x86)\Maxis\The Sims\GameData\Animation\Animation.far",
    r"C:\Program Files\Maxis\The Sims\GameData\Animation\Animation.far",
]

def read_far_entry(far_path, target_name):
    with open(far_path, "rb") as f:
        magic = f.read(8)
        if magic != b"FAR!byAZ":
            raise ValueError(f"Not a FAR archive: {far_path}")
        version, manifest_offset = struct.unpack("<II", f.read(8))
        f.seek(manifest_offset)
        num_files, = struct.unpack("<I", f.read(4))
        for _ in range(num_files):
            if version == 1:
                length1, length2, offset, name_len = struct.unpack("<IIII", f.read(16))
            else:
                length1, length2, offset, name_len = struct.unpack("<IIIH", f.read(14))
            name = f.read(name_len).decode("latin1")
            if name.lower() == target_name.lower():
                cur = f.tell()
                f.seek(offset)
                data = f.read(length1)
                f.seek(cur)
                return data
    raise FileNotFoundError(f"{target_name} not found in {far_path}")

def read_pascal_str(buf, off):
    n = buf[off]
    off += 1
    s = buf[off:off + n].decode("latin1")
    return s, off + n

def parse_bcf_skeleton(data):
    """Exact mirror of FreeSO FSO.Vitaboy.Skeleton.ReadBCF (Skeleton.cs:147-160)."""
    off = 0
    num_skels, = struct.unpack_from("<I", data, off); off += 4
    if num_skels < 1:
        raise ValueError("No skeletons in BCF")
    skel_name, off = read_pascal_str(data, off)
    num_bones, = struct.unpack_from("<I", data, off); off += 4

    bones = []
    for idx in range(num_bones):
        bone_name, off = read_pascal_str(data, off)
        parent_name, off = read_pascal_str(data, off)
        prop_count, = struct.unpack_from("<I", data, off); off += 4
        for _ in range(prop_count):
            pair_count, = struct.unpack_from("<I", data, off); off += 4
            for _ in range(pair_count):
                _, off = read_pascal_str(data, off)
                _, off = read_pascal_str(data, off)

        tx, ty, tz = struct.unpack_from("<fff", data, off); off += 12
        qx, qy, qz, qw = struct.unpack_from("<ffff", data, off); off += 16
        can_trans, can_rot, can_blend = struct.unpack_from("<III", data, off); off += 12
        wiggle_val, wiggle_pow = struct.unpack_from("<ff", data, off); off += 8

        # Exact FreeSO left-handed -> right-handed X-mirror conversion:
        # T = (-tx, ty, tz)
        # Q = (qx, -qy, -qz, -qw) -> normalized
        t_rh = (-tx, ty, tz)
        q_rh = q_norm((qx, -qy, -qz, -qw))

        bones.append({
            "index": idx,
            "name": bone_name,
            "parent": parent_name,
            "localTranslation": [round(v, 6) for v in t_rh],
            "localRotation": [round(v, 6) for v in q_rh],
            "canTranslate": bool(can_trans),
            "canRotate": bool(can_rot),
        })
    return skel_name, bones

def compute_tpose_reference(bones, rest_pos, rest_rot):
    """Computes visual T-pose world rotations for adult.skel (arms raised horizontally
    along +X for Left arm and -X for Right arm) for global-delta BVH retargeting."""
    tpose_local = {b["name"]: tuple(b["localRotation"]) for b in bones}
    by_name = {b["name"]: b for b in bones}

    # Rotate L_ARM1 so L_ARM1 -> L_ARM2 points strictly along +X (Left),
    # and R_ARM1 so R_ARM1 -> R_ARM2 points strictly along -X (Right).
    for arm1, arm2, target_dir in [
        ("L_ARM1", "L_ARM2", (1.0, 0.0, 0.0)),
        ("R_ARM1", "R_ARM2", (-1.0, 0.0, 0.0)),
    ]:
        cur_dir = v_sub(rest_pos[arm2], rest_pos[arm1])
        delta_w = q_from_two_vectors(cur_dir, target_dir)
        new_w = q_norm(q_mul(delta_w, rest_rot[arm1]))
        parent = by_name[arm1]["parent"]
        tpose_local[arm1] = q_norm(q_mul(q_conj(rest_rot[parent]), new_w))

    # Recompute FK with upper arms horizontal, then straighten forearms (L_ARM2, R_ARM2) strictly along +/-X
    tp_pos, tp_rot = compute_fk(bones, local_rot_map=tpose_local)
    for arm2, hand, target_dir in [
        ("L_ARM2", "L_HAND", (1.0, 0.0, 0.0)),
        ("R_ARM2", "R_HAND", (-1.0, 0.0, 0.0)),
    ]:
        cur_dir = v_sub(tp_pos[hand], tp_pos[arm2])
        delta_w = q_from_two_vectors(cur_dir, target_dir)
        new_w = q_norm(q_mul(delta_w, tp_rot[arm2]))
        parent = by_name[arm2]["parent"]
        tpose_local[arm2] = q_norm(q_mul(q_conj(tp_rot[parent]), new_w))

    tp_pos, tp_rot = compute_fk(bones, local_rot_map=tpose_local)
    return tp_pos, tp_rot, tpose_local

def main():
    far_path = sys.argv[1] if len(sys.argv) > 1 else None
    if not far_path:
        for cand in DEFAULT_FAR_CANDIDATES:
            if os.path.exists(cand):
                far_path = cand
                break
    if not far_path or not os.path.exists(far_path):
        raise SystemExit("Could not locate TS1 Animation.far. Pass path as argv[1].")

    raw = read_far_entry(far_path, "adult-skeleton.cmx.bcf")
    skel_name, bones = parse_bcf_skeleton(raw)
    rest_pos, rest_rot = compute_fk(bones)
    tp_pos, tp_rot, tp_local = compute_tpose_reference(bones, rest_pos, rest_rot)

    for b in bones:
        n = b["name"]
        b["restWorldPos"] = [round(v, 6) for v in rest_pos[n]]
        b["restWorldRot"] = [round(v, 6) for v in rest_rot[n]]
        b["tposeWorldPos"] = [round(v, 6) for v in tp_pos[n]]
        b["tposeWorldRot"] = [round(v, 6) for v in tp_rot[n]]
        b["tposeLocalRot"] = [round(v, 6) for v in tp_local[n]]

    out_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "adult_skel_ref.json")
    payload = {
        "skeleton": skel_name,
        "boneCount": len(bones),
        "hipHeight": round(rest_pos["ROOT"][1], 6),
        "sourceFar": far_path,
        "bones": bones,
    }
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2)
    print(f"Wrote {out_path} ({len(bones)} bones, hipHeight={payload['hipHeight']})")
    print(f"  Rest  L_HAND: {rest_pos['L_HAND']} | R_HAND: {rest_pos['R_HAND']}")
    print(f"  TPose L_HAND: {[round(v,3) for v in tp_pos['L_HAND']]} | R_HAND: {[round(v,3) for v in tp_pos['R_HAND']]}")

if __name__ == "__main__":
    main()
