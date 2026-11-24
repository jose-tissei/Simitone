"""Authors and exports the 7-clip Cadet Academy custom skeletal animation pack
using exact FreeSO/MonoGame anatomical kinematics + 2-bone IK limb targeting,
baking every clip onto AdultRig in Blender 4.5 and exporting via blender_export_simanim.py.

Run:
  "C:\\Program Files\\Blender Foundation\\Blender 4.5\\blender.exe" --factory-startup --background \
      Tools/AnimPipeline/adult_rig.blend --python Tools/AnimPipeline/generate_cadet_pack.py
"""
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from skel_math import (
    align_track_hemispheres,
    compute_fk,
    q_axis_deg,
    q_conj,
    q_from_euler_deg,
    q_from_two_vectors,
    q_mul,
    q_norm,
    q_rotate,
    v_add,
    v_cross,
    v_dot,
    v_len,
    v_norm,
    v_scale,
    v_sub,
    world_rots_to_local,
)

SKEL_REF_PATH = os.path.join(HERE, "adult_skel_ref.json")
OUT_DIR = os.path.normpath(os.path.join(HERE, "..", "Conversations", "Assets", "Animations"))
FOOT_BONES = ("L_TOE0", "R_TOE0", "L_TOE02", "R_TOE02", "L_FOOT", "R_FOOT")


def smoothstep(edge0, edge1, x):
    t = max(0.0, min(1.0, (x - edge0) / max(1e-6, edge1 - edge0)))
    return t * t * (3.0 - 2.0 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def solve_two_bone_ik(root_p, mid_p, end_p, target_p, pole_p):
    """Exact Law of Cosines 2-bone IK returning (upper_world_delta, lower_world_delta)."""
    u_vec = v_sub(mid_p, root_p)
    l_vec = v_sub(end_p, mid_p)
    len_a = v_len(u_vec)
    len_b = v_len(l_vec)
    to_t = v_sub(target_p, root_p)
    dist = v_len(to_t)
    min_r = abs(len_a - len_b) + 1e-3
    max_r = (len_a + len_b) - 1e-3
    clamped = max(min_r, min(max_r, dist))
    dir_v = v_norm(to_t) if dist > 1e-5 else v_norm(u_vec)

    to_pole = v_sub(pole_p, root_p)
    proj = v_sub(to_pole, v_scale(dir_v, v_dot(to_pole, dir_v)))
    if v_len(proj) < 1e-5:
        fallback = (0.0, 1.0, 0.0) if abs(dir_v[1]) < 0.9 else (0.0, 0.0, 1.0)
        proj = v_sub(fallback, v_scale(dir_v, v_dot(fallback, dir_v)))
    bend_dir = v_norm(proj)

    cos_a = (len_a * len_a + clamped * clamped - len_b * len_b) / (2.0 * len_a * clamped)
    cos_a = max(-1.0, min(1.0, cos_a))
    sin_a = math.sqrt(max(0.0, 1.0 - cos_a * cos_a))

    new_mid = v_add(v_add(root_p, v_scale(dir_v, len_a * cos_a)), v_scale(bend_dir, len_a * sin_a))
    new_end = v_add(root_p, v_scale(dir_v, clamped))

    upper_delta = q_from_two_vectors(u_vec, v_sub(new_mid, root_p))
    rot_l = q_rotate(upper_delta, l_vec)
    lower_delta = q_from_two_vectors(rot_l, v_sub(new_end, new_mid))
    return upper_delta, lower_delta


class PoseRig:
    """Hierarchical anatomical world-delta poser for TS1 adult.skel.
    Anatomical angle convention (MonoGame RH: +Y Up, +Z Forward, +X Sim Left):
      - pitch (+X): torso/head +deg leans forward; hanging thigh/arm -deg swings FORWARD, +deg BACKWARD;
                    knee +deg bends naturally BACKWARD; elbow -deg bends naturally FORWARD/UP.
      - yaw   (+Y): +deg turns Left (+Z->+X); -deg turns Right (+Z->-X).
      - roll  (+Z): Left arm +deg abducts OUT to Left (+X); Right arm -deg abducts OUT to Right (-X).
    """

    def __init__(self, bones):
        self.bones = bones
        self.by_name = {b["name"]: b for b in bones}
        self.rest_pos, self.rest_rot = compute_fk(bones)
        self.rest_root = tuple(bones[0]["localTranslation"])

    def build_pose(self, deltas_deg, root_offset=(0.0, 0.0, 0.0), arm_ik=None, ground_feet=True):
        """
        deltas_deg: dict of bone_name -> (pitch_x, yaw_y, roll_z) in degrees.
        arm_ik: optional dict of 'L'/'R' -> (hand_target_world_xyz, elbow_pole_world_xyz).
        Returns (root_trans, local_rots_dict, world_pos_dict).
        """
        cum_delta = {}
        world_rot = {}
        for b in self.bones:
            name = b["name"]
            parent = b["parent"]
            rx, ry, rz = deltas_deg.get(name, (0.0, 0.0, 0.0))
            # Apply roll, then pitch, then yaw in anatomical frame
            d_q = q_from_euler_deg(rx=rx, ry=ry, rz=rz, order="YXZ")
            if parent == "NULL":
                cum_delta[name] = d_q
            else:
                cum_delta[name] = q_norm(q_mul(cum_delta[parent], d_q))
            world_rot[name] = q_norm(q_mul(cum_delta[name], self.rest_rot[name]))

        local_rots = world_rots_to_local(self.bones, world_rot)
        root_t = v_add(self.rest_root, root_offset)

        # Optional analytical 2-bone arm IK (e.g., clasped 2-handed aim, temple salute, wall vault plant)
        if arm_ik:
            w_pos, w_rot = compute_fk(self.bones, local_trans_map={"ROOT": root_t}, local_rot_map=local_rots)
            for side, (target_p, pole_p) in arm_ik.items():
                b_up = f"{side}_ARM1"
                b_lo = f"{side}_ARM2"
                b_end = f"{side}_HAND"
                b_fin = f"{side}_FINGER0"
                up_d, lo_d = solve_two_bone_ik(w_pos[b_up], w_pos[b_lo], w_pos[b_end], target_p, pole_p)
                world_rot[b_up] = q_norm(q_mul(up_d, w_rot[b_up]))
                world_rot[b_lo] = q_norm(q_mul(lo_d, q_mul(up_d, w_rot[b_lo])))
                # Keep hand aligned cleanly with forearm
                hand_carry = q_mul(lo_d, up_d)
                world_rot[b_end] = q_norm(q_mul(hand_carry, w_rot[b_end]))
                world_rot[b_fin] = q_norm(q_mul(hand_carry, w_rot[b_fin]))
            local_rots = world_rots_to_local(self.bones, world_rot)

        w_pos, _ = compute_fk(self.bones, local_trans_map={"ROOT": root_t}, local_rot_map=local_rots)
        if ground_feet:
            min_y = min(w_pos[fb][1] for fb in FOOT_BONES if fb in w_pos)
            # Plant lowest foot cleanly on Y = 0.0
            root_t = (root_t[0], root_t[1] - min_y, root_t[2])
            w_pos, _ = compute_fk(self.bones, local_trans_map={"ROOT": root_t}, local_rot_map=local_rots)

        return root_t, local_rots, w_pos


# ---------------------------------------------------------------------------
# 7 High-Craft Cadet Academy Clips
# ---------------------------------------------------------------------------

def anim_cadet_idle_attention(rig):
    """Alert Police Academy ready stance: slight athletic foot stagger, calm breathing chest rise,
    relaxed slightly bent arms at sides, confident subtle head scan."""
    fps = 30.0
    n = 48  # 1.60s loop
    frames = []
    for i in range(n):
        ph = 2.0 * math.pi * i / n
        breath = math.sin(ph)
        scan = math.sin(ph * 0.5)
        shift = math.sin(ph) * 0.04

        d = {
            "PELVIS": (2.0, -4.0 + scan * 2.0, shift * 18.0),
            "SPINE": (2.0 - breath * 1.4, 2.0, -shift * 12.0),
            "SPINE1": (1.5 - breath * 1.6, 2.0, 0.0),
            "SPINE2": (-1.0 - breath * 1.5, 0.0, 0.0),
            "NECK": (-1.5 + breath * 1.2, scan * 5.0, 0.0),
            "HEAD": (-1.0 + breath * 0.8, scan * 7.0, 0.0),
            # Athletic alert arms slightly away from hips with natural 16 deg elbow bend
            "L_ARM": (0.0, 0.0, 3.0),
            "L_ARM1": (-5.0 + breath * 1.5, 0.0, 9.0 + breath * 1.2),
            "L_ARM2": (-18.0 - breath * 2.0, 0.0, -4.0),
            "R_ARM": (0.0, 0.0, -3.0),
            "R_ARM1": (-6.0 - breath * 1.5, 0.0, -9.0 - breath * 1.2),
            "R_ARM2": (-20.0 + breath * 2.0, 0.0, 4.0),
            # Confident shoulder-width stance with tiny knee softness
            "L_LEG": (-8.0, 4.0, 4.5),
            "L_LEG1": (10.0, 0.0, -3.5),
            "L_FOOT": (-2.0, 0.0, 0.0),
            "R_LEG": (5.0, -5.0, -4.5),
            "R_LEG1": (6.0, 0.0, 3.5),
            "R_FOOT": (-11.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(shift, -0.04 + breath * 0.015, 0.0), ground_feet=True))

    return {
        "name": "cadet_idle_attention",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Open"}],
        "events": [],
        "frames": frames,
    }


def anim_cadet_sprint_loop(rig):
    """Authentic Olympic/Police Academy 100m sprint cycle!
    - Natural 13 deg forward torso lean baked into SPINE/PELVIS (NO whole-mesh tilt hack!)
    - High knee drive (thigh up to -58 deg forward, knee flexing up to +92 deg on recovery)
    - Full rear leg extension push-off (+34 deg thigh, strong ankle plantarflexion)
    - Counter-rotating pelvis & ribcage
    - Athletic ~90 deg bent elbows pumping rhythmically front-to-back at chest/hip height (NEVER arms raised!)
    - Accurate rootSpeedUnitsPerSec = 10.9 (at SimScale=0.55 => exact 6.0 world units/sec, zero skating!)
    """
    fps = 30.0
    n = 18  # 0.60s full 2-step sprint cycle (snappy athletic cadence!)
    frames = []
    for i in range(n):
        ph = 2.0 * math.pi * i / n
        sL = math.sin(ph)          # +1 when L leg drives forward, -1 when L leg pushes back
        cL = math.cos(ph)
        sR = math.sin(ph + math.pi)
        cR = math.cos(ph + math.pi)

        # Vertical flight/contact bounce (2 peaks per cycle)
        bounce = -0.20 + 0.24 * abs(math.sin(ph + 0.35))

        # Left leg kinematics: thigh forward (-deg), knee bends back (+deg)
        l_thigh = -12.0 - 44.0 * sL
        # Biomechanical sprint knee curve:
        # - Rear push-off (sL = -1, ph = 270 deg): straight powerful drive (~14-18 deg)
        # - Early swing recovery (ph ~ 340..60 deg, thigh sweeping forward under hip): tight heel-to-glute fold (~88 deg)
        # - Front high-knee drive (sL = +1, ph = 90 deg): shin hangs ~vertical (~56 deg)
        # - Mid-stance support (ph ~ 180 deg): mild shock absorption (~22 deg)
        l_swing_tuck = max(0.0, math.cos(ph - 0.35))
        l_front_drive = max(0.0, math.sin(ph - 0.25))
        l_knee = 15.0 + 74.0 * (l_swing_tuck ** 1.35) + 38.0 * l_front_drive
        l_foot = -10.0 * sL + 22.0 * max(0.0, -sL - 0.25)  # toes push off behind, dorsiflex forward

        r_thigh = -12.0 - 44.0 * sR
        r_swing_tuck = max(0.0, math.cos(ph + math.pi - 0.35))
        r_front_drive = max(0.0, math.sin(ph + math.pi - 0.25))
        r_knee = 15.0 + 74.0 * (r_swing_tuck ** 1.35) + 38.0 * r_front_drive
        r_foot = -10.0 * sR + 22.0 * max(0.0, -sR - 0.25)

        # Counter-swinging athletic arms: opposite to legs!
        # When Left leg drives forward (sL = +1), Right arm drives forward (-deg) and Left arm drives back (+deg).
        # Keep pumping fists cleanly between hip height (3.1) and chest height (4.30) — well below shoulders (4.56)!
        l_sh_pitch = 4.0 + 40.0 * sL        # -36 deg forward .. +44 deg back
        l_elbow = -80.0 - 12.0 * max(0.0, -sL)  # crisp ~80-92 deg athletic bend!
        r_sh_pitch = 4.0 + 40.0 * sR
        r_elbow = -80.0 - 12.0 * max(0.0, -sR)

        # Natural torso forward lean + counter-twist (pelvis turns with legs, chest turns with arms)
        pelvis_yaw = -9.0 * sL
        chest_yaw = 16.0 * sL

        d = {
            "PELVIS": (8.0, pelvis_yaw, 2.5 * sL),
            "SPINE": (6.0, chest_yaw * 0.45, -2.0 * sL),
            "SPINE1": (4.0, chest_yaw * 0.45, -1.5 * sL),
            "SPINE2": (2.0, chest_yaw * 0.30, 0.0),
            # Counter-pitch neck & head so Cadet looks straight down the track!
            "NECK": (-9.0, -chest_yaw * 0.5, 0.0),
            "HEAD": (-9.0, -chest_yaw * 0.4, 0.0),

            "L_ARM": (0.0, 0.0, 4.0),
            "L_ARM1": (l_sh_pitch, 4.0, 12.0),
            "L_ARM2": (l_elbow, -6.0, -8.0),
            "L_HAND": (0.0, 0.0, 0.0),

            "R_ARM": (0.0, 0.0, -4.0),
            "R_ARM1": (r_sh_pitch, -4.0, -12.0),
            "R_ARM2": (r_elbow, 6.0, 8.0),
            "R_HAND": (0.0, 0.0, 0.0),

            # Counter-yaw hips against pelvis twist so knees & feet track straight down the lane
            "L_LEG": (l_thigh, -pelvis_yaw * 0.85, 2.0),
            "L_LEG1": (l_knee, 0.0, -1.5),
            "L_FOOT": (l_foot, 0.0, 0.0),

            "R_LEG": (r_thigh, -pelvis_yaw * 0.85, -2.0),
            "R_LEG1": (r_knee, 0.0, 1.5),
            "R_FOOT": (r_foot, 0.0, 0.0),
        }
        # Only ground feet near stance phases (preserve brief flight phase!)
        root_t, l_rots, w_pos = rig.build_pose(d, root_offset=(0.0, bounce, 0.0), ground_feet=False)
        min_y = min(w_pos[fb][1] for fb in FOOT_BONES if fb in w_pos)
        if min_y < 0.0:
            root_t = (root_t[0], root_t[1] - min_y, root_t[2])
            w_pos, _ = compute_fk(rig.bones, local_trans_map={"ROOT": root_t}, local_rot_map=l_rots)
        frames.append((root_t, l_rots, w_pos))

    return {
        "name": "cadet_sprint_loop",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 10.9,  # 10.9 * 0.55 SimScale == 6.0 world units/s (zero skating!)
        "hand_gestures": [{"frame": 0, "left": "Fist", "right": "Fist"}],
        "events": [
            {"frame": 4, "name": "footstep_l", "value": "1"},
            {"frame": 13, "name": "footstep_r", "value": "1"},
        ],
        "frames": frames,
    }


def anim_cadet_hurdle_hop(rig):
    """Authentic track hurdler clearance!
    Lead leg (Left) drives straight & high forward over the red-and-white hurdle bar,
    trail leg (Right) tucks bent sharply under/out to the side, Right arm reaches forward
    in classic hurdler balance, then crisp landing absorption."""
    fps = 30.0
    n = 18  # 0.60s one-shot
    frames = []
    for i in range(n):
        u = i / float(n - 1)
        peak = math.sin(u * math.pi)          # 0 -> 1 -> 0
        takeoff = smoothstep(0.0, 0.30, u) * (1.0 - smoothstep(0.55, 0.90, u))
        land = smoothstep(0.70, 0.95, u)

        # Lead leg (Left) extends high forward over hurdle bar, then snaps down for landing
        l_thigh = lerp(-20.0, -82.0, peak) + land * 10.0
        l_knee = lerp(45.0, 18.0, peak) + land * 32.0
        l_foot = lerp(15.0, -18.0, peak)

        # Trail leg (Right) pushes off then tucks sharply out & bent to clear the bar
        r_thigh = lerp(28.0, -34.0, peak)
        r_knee = lerp(24.0, 108.0, peak)
        r_roll = -28.0 * peak  # trail thigh abducts cleanly out to side like a real hurdler!

        # Opposite arm (Right) reaches forward toward lead toe for aerodynamic balance
        r_sh = lerp(18.0, -68.0, peak)
        r_el = lerp(-85.0, -35.0, peak)
        l_sh = lerp(-30.0, 38.0, peak)
        l_el = lerp(-90.0, -95.0, peak)

        d = {
            "PELVIS": (10.0 + 14.0 * peak, -8.0 * peak, 5.0 * peak),
            "SPINE": (8.0 + 12.0 * peak, 8.0 * peak, 0.0),
            "SPINE1": (6.0 + 10.0 * peak, 6.0 * peak, 0.0),
            "SPINE2": (4.0 + 6.0 * peak, 0.0, 0.0),
            "NECK": (-12.0 - 12.0 * peak, 0.0, 0.0),
            "HEAD": (-12.0 - 12.0 * peak, 0.0, 0.0),

            "L_ARM1": (l_sh, 0.0, 18.0 + 14.0 * peak),
            "L_ARM2": (l_el, 0.0, -8.0),
            "R_ARM1": (r_sh, 0.0, -10.0 + 8.0 * peak),
            "R_ARM2": (r_el, 0.0, 8.0),

            "L_LEG": (l_thigh, 0.0, 4.0),
            "L_LEG1": (l_knee, 0.0, -2.0),
            "L_FOOT": (l_foot, 0.0, 0.0),

            "R_LEG": (r_thigh, 0.0, r_roll),
            "R_LEG1": (r_knee, 0.0, 10.0 * peak),
            "R_FOOT": (18.0 * peak, 0.0, 0.0),
        }
        # Note: CadetAcademySceneRenderer already adds hop vertical Y arc, so keep ROOT Y near neutral
        root_t, l_rots, w_pos = rig.build_pose(d, root_offset=(0.0, -0.18 * land + 0.14 * peak, 0.0), ground_feet=False)
        frames.append((root_t, l_rots, w_pos))

    return {
        "name": "cadet_hurdle_hop",
        "fps": fps,
        "loop": False,
        "mask": "full",
        "root_speed": 10.9,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Open"}],
        "events": [{"frame": 16, "name": "land", "value": "1"}],
        "frames": frames,
    }


def anim_cadet_wall_vault(rig):
    """Authentic Academy Parkour / Speed Vault over the brick-and-timber wall!
    Phase 1 (0.00-0.25): Explosive spring takeoff, hands reach forward-down onto timber top rail.
    Phase 2 (0.25-0.62): Hands push strongly off the rail while torso leans horizontal-forward
                         and BOTH knees tuck high & cleanly to the left side to clear the wall!
    Phase 3 (0.62-0.82): Legs swing down & extend toward the blue landing mat.
    Phase 4 (0.82-1.00): Athletic two-foot landing crouch absorbing impact & rising to ready!
    """
    fps = 30.0
    n = 30  # 1.00s one-shot (mapped directly across Vault resolution!)
    frames = []
    for i in range(n):
        u = i / float(n - 1)
        # Smooth phase weights
        w_takeoff = math.sin(min(1.0, u / 0.25) * math.pi) if u < 0.25 else 0.0
        w_vault = smoothstep(0.15, 0.34, u) * (1.0 - smoothstep(0.58, 0.78, u))
        w_land = smoothstep(0.68, 0.86, u) * (1.0 - smoothstep(0.90, 1.0, u)) * 1.15
        w_plant = smoothstep(0.12, 0.28, u) * (1.0 - smoothstep(0.50, 0.68, u))

        # Torso pitches forward athletically during wall push-off, hips kick slightly to side like a speed-vault!
        pelvis_pitch = 10.0 + 26.0 * w_vault + 18.0 * w_land
        pelvis_roll = -24.0 * w_vault
        pelvis_yaw = 20.0 * w_vault
        spine_pitch = 10.0 + 24.0 * w_vault + 16.0 * w_land

        # Legs: explosive push -> tight tucked knees high up near chest -> extended landing reach -> knee absorption bend!
        l_thigh = -22.0 * w_takeoff - 116.0 * w_vault - 44.0 * w_land - 12.0
        l_knee = 35.0 * w_takeoff + 122.0 * w_vault + 78.0 * w_land + 14.0
        r_thigh = 18.0 * w_takeoff - 108.0 * w_vault - 42.0 * w_land - 8.0
        r_knee = 28.0 * w_takeoff + 118.0 * w_vault + 76.0 * w_land + 14.0

        # Arms: reach down onto top timber rail during vault plant, then flare for landing balance!
        arm_ik = None
        l_sh = -32.0 - 38.0 * w_takeoff + 15.0 * w_land
        l_el = -62.0 + 30.0 * w_plant - 22.0 * w_land
        r_sh = -32.0 - 38.0 * w_takeoff + 15.0 * w_land
        r_el = -62.0 + 30.0 * w_plant - 22.0 * w_land

        if w_plant > 0.08:
            # Plant hands firmly down & slightly in front of torso as if vaulting off timber rail
            arm_ik = {
                "L": ((0.52, lerp(3.4, 2.35, w_plant), lerp(0.85, 0.55, w_plant)), (0.95, 3.8, -0.3)),
                "R": ((-0.52, lerp(3.4, 2.35, w_plant), lerp(0.85, 0.55, w_plant)), (-0.95, 3.8, -0.3)),
            }

        d = {
            "PELVIS": (pelvis_pitch, pelvis_yaw, pelvis_roll),
            "SPINE": (spine_pitch * 0.55, -pelvis_yaw * 0.4, -pelvis_roll * 0.35),
            "SPINE1": (spine_pitch * 0.45, -pelvis_yaw * 0.3, -pelvis_roll * 0.25),
            "SPINE2": (6.0 * w_vault, 0.0, 0.0),
            "NECK": (-16.0 - 22.0 * w_vault - 12.0 * w_land, 0.0, 0.0),
            "HEAD": (-16.0 - 22.0 * w_vault - 12.0 * w_land, 0.0, 0.0),

            "L_ARM": (0.0, 0.0, 8.0),
            "L_ARM1": (l_sh, 0.0, 18.0 + 18.0 * w_land),
            "L_ARM2": (l_el, 0.0, -6.0),
            "R_ARM": (0.0, 0.0, -8.0),
            "R_ARM1": (r_sh, 0.0, -18.0 - 18.0 * w_land),
            "R_ARM2": (r_el, 0.0, 6.0),

            "L_LEG": (l_thigh, 0.0, 8.0 * w_vault),
            "L_LEG1": (l_knee, 0.0, 0.0),
            "L_FOOT": (22.0 * w_vault - 18.0 * w_land, 0.0, 0.0),

            "R_LEG": (r_thigh, 0.0, -8.0 * w_vault),
            "R_LEG1": (r_knee, 0.0, 0.0),
            "R_FOOT": (22.0 * w_vault - 18.0 * w_land, 0.0, 0.0),
        }
        # Only ground feet on final landing frames (airborne arc is driven by world trajectory)
        ground = u >= 0.82
        root_t, l_rots, w_pos = rig.build_pose(d, root_offset=(0.0, -0.42 * w_land, 0.0), arm_ik=arm_ik, ground_feet=ground)
        frames.append((root_t, l_rots, w_pos))

    return {
        "name": "cadet_wall_vault",
        "fps": fps,
        "loop": False,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [
            {"frame": 0, "left": "Open", "right": "Open"},
            {"frame": 24, "left": "Fist", "right": "Fist"},
        ],
        "events": [
            {"frame": 8, "name": "hand_plant", "value": "1"},
            {"frame": 24, "name": "land", "value": "1"},
        ],
        "frames": frames,
    }


def anim_cadet_target_aim_loop(rig):
    """Crisp Police Academy two-handed Isosceles / Weaver range stance!
    - Athletic balanced wide stance, slight knee bend, torso angled subtly forward
    - Both hands clasped cleanly together straight ahead at chest/eye sightline using exact 2-bone IK!
    - Gentle realistic breathing micro-sway so the Cadet feels alive while aiming.
    """
    fps = 30.0
    n = 36  # 1.20s loop
    frames = []
    for i in range(n):
        ph = 2.0 * math.pi * i / n
        breath = math.sin(ph)
        sway_x = math.sin(ph * 0.5) * 0.03

        # Aim point dead ahead at eye/shoulder line (Z = +1.32, Y = 4.42)
        hand_y = 4.40 + breath * 0.025
        hand_z = 1.32
        arm_ik = {
            "R": ((-0.05 + sway_x, hand_y, hand_z), (-0.78, 3.85, 0.55)),
            "L": ((+0.05 + sway_x, hand_y - 0.02, hand_z - 0.03), (+0.78, 3.85, 0.55)),
        }

        d = {
            "PELVIS": (5.0, -10.0, 0.0),
            "SPINE": (4.0, 6.0, 0.0),
            "SPINE1": (3.0 - breath * 0.8, 4.0, 0.0),
            "SPINE2": (2.0 - breath * 0.8, 0.0, 0.0),
            "NECK": (-7.0, 0.0, 0.0),
            "HEAD": (-5.0, 0.0, 0.0),

            "L_ARM": (0.0, -10.0, 4.0),
            "R_ARM": (0.0, 10.0, -4.0),

            # Stable athletic range stance: left foot slightly forward, right foot braced back
            "L_LEG": (-20.0, 8.0, 7.0),
            "L_LEG1": (24.0, 0.0, -5.0),
            "L_FOOT": (-4.0, 0.0, 0.0),

            "R_LEG": (10.0, -10.0, -7.0),
            "R_LEG1": (14.0, 0.0, 5.0),
            "R_FOOT": (-24.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, -0.14, 0.0), arm_ik=arm_ik, ground_feet=True))

    return {
        "name": "cadet_target_aim_loop",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Fist", "right": "Point"}],
        "events": [],
        "frames": frames,
    }


def anim_cadet_target_fire_recoil(rig):
    """Crisp single-shot muzzle recoil & controlled sight re-acquisition!
    Matches cadet_target_aim_loop on frame 0 and final frame so transitions are buttery smooth."""
    fps = 30.0
    n = 15  # 0.50s crisp recoil & recovery
    frames = []
    for i in range(n):
        u = i / float(n - 1)
        # Fast sharp recoil impulse peaking at u ~ 0.18, smooth damped recovery by u = 1.0
        if u < 0.18:
            kick = smoothstep(0.0, 0.18, u)
        else:
            t = (u - 0.18) / 0.82
            kick = (1.0 - smoothstep(0.0, 1.0, t)) * (1.0 - 0.12 * math.sin(t * math.pi * 2.0))
        kick = max(0.0, kick)

        # Hands snap UP (+Y) and BACK (-Z) on recoil then settle cleanly onto bullseye
        hand_y = 4.40 + 0.42 * kick
        hand_z = 1.32 - 0.22 * kick
        arm_ik = {
            "R": ((-0.05, hand_y, hand_z), (-0.82, 3.80 + 0.12 * kick, 0.48)),
            "L": ((+0.05, hand_y - 0.02, hand_z - 0.03), (+0.82, 3.80 + 0.12 * kick, 0.48)),
        }

        d = {
            "PELVIS": (5.0 - 2.0 * kick, -10.0, 0.0),
            "SPINE": (4.0 - 4.5 * kick, 6.0, 0.0),
            "SPINE1": (3.0 - 5.5 * kick, 4.0, 0.0),
            "SPINE2": (2.0 - 4.0 * kick, 0.0, 0.0),
            "NECK": (-7.0 + 6.0 * kick, 0.0, 0.0),
            "HEAD": (-5.0 + 4.0 * kick, 0.0, 0.0),

            "L_ARM": (0.0, -10.0, 4.0),
            "R_ARM": (0.0, 10.0, -4.0),

            "L_LEG": (-20.0, 8.0, 7.0),
            "L_LEG1": (24.0, 0.0, -5.0),
            "L_FOOT": (-4.0, 0.0, 0.0),

            "R_LEG": (10.0, -10.0, -7.0),
            "R_LEG1": (14.0, 0.0, 5.0),
            "R_FOOT": (-24.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, -0.14, -0.05 * kick), arm_ik=arm_ik, ground_feet=True))

    return {
        "name": "cadet_target_fire_recoil",
        "fps": fps,
        "loop": False,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Fist", "right": "Point"}],
        "events": [{"frame": 1, "name": "muzzle_flash", "value": "1"}],
        "frames": frames,
    }


def anim_cadet_victory_salute(rig):
    """Sharp Police Academy Graduation Salute!
    Cadet snaps to proud attention, right hand rises crisply to right temple/eyebrow
    (exact 2-bone IK to (-0.28, 5.16, +0.26) with elbow out to right-front!), holds sharp
    with confident breathing and subtle proud nod, left arm straight & crisp at left trouser seam."""
    fps = 30.0
    n = 48  # 1.60s loop (crisp hold with subtle living pride & breathing!)
    frames = []
    for i in range(n):
        ph = 2.0 * math.pi * i / n
        breath = math.sin(ph)
        nod = math.sin(ph) * 2.5

        # Right hand crisp at right brow/temple; elbow held cleanly out to the right at shoulder height!
        arm_ik = {
            "R": ((-0.26, 5.14 + breath * 0.015, 0.28), (-1.45, 4.42, 0.35)),
        }

        d = {
            "PELVIS": (-1.0, 0.0, 0.0),
            "SPINE": (-2.0 - breath * 1.0, 0.0, 0.0),
            "SPINE1": (-2.5 - breath * 1.2, 0.0, 0.0),
            "SPINE2": (-2.0 - breath * 1.0, 0.0, 0.0),
            "NECK": (2.0 + nod * 0.5, 4.0, 0.0),
            "HEAD": (2.0 + nod * 0.7, 4.0, 0.0),

            # Left arm crisp & straight at left side (parade attention)
            "L_ARM": (0.0, 0.0, 2.0),
            "L_ARM1": (-2.0, 0.0, 6.0),
            "L_ARM2": (-10.0, 0.0, -3.0),

            # Right shoulder lifts slightly for crisp salute silhouette
            "R_ARM": (0.0, 0.0, -6.0),

            # Crisp heels-near-together attention stance
            "L_LEG": (-2.0, 4.0, 2.0),
            "L_LEG1": (3.0, 0.0, -2.0),
            "L_FOOT": (-1.0, 0.0, 0.0),
            "R_LEG": (-2.0, -4.0, -2.0),
            "R_LEG1": (3.0, 0.0, 2.0),
            "R_FOOT": (-1.0, 0.0, 0.0),
        }
        frames.append(rig.build_pose(d, root_offset=(0.0, 0.0, 0.0), arm_ik=arm_ik, ground_feet=True))

    return {
        "name": "cadet_victory_salute",
        "fps": fps,
        "loop": True,
        "mask": "full",
        "root_speed": 0.0,
        "hand_gestures": [{"frame": 0, "left": "Open", "right": "Open"}],
        "events": [],
        "frames": frames,
    }


def bake_into_blender_and_export(arm_obj, bones, spec):
    """Pushes the generated frames onto AdultRig in Blender 4.5 as a real Blender Action,
    then exports via blender_export_simanim.py so Blender <-> Simitone parity is 100% guaranteed."""
    import bpy
    from mathutils import Matrix, Quaternion, Vector
    from blender_export_simanim import BASIS_BL_TO_MG, BASIS_MG_TO_BL, export_action_to_simanim

    act_name = spec["name"]
    old = bpy.data.actions.get(act_name)
    if old:
        bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name=act_name)
    if not arm_obj.animation_data:
        arm_obj.animation_data_create()
    arm_obj.animation_data.action = act

    scene = bpy.context.scene
    scene.render.fps = int(round(spec["fps"]))

    # Precompute edit-bone rest matrices in Blender space
    rest_bl = {b["name"]: arm_obj.data.bones[b["name"]].matrix_local.copy() for b in bones}
    parent_map = {b["name"]: b["parent"] for b in bones}

    for fi, (root_t, local_rots, _w_pos) in enumerate(spec["frames"]):
        frame_num = fi + 1
        scene.frame_set(frame_num)

        # Compute exact MonoGame world pos/rot for every bone on this frame
        w_pos, w_rot = compute_fk(bones, local_trans_map={"ROOT": root_t}, local_rot_map=local_rots)

        # Convert each bone to Blender armature-space matrix and assign pose.bone.matrix_basis
        world_bl = {}
        for b in bones:
            name = b["name"]
            q = w_rot[name]
            q_mg = Quaternion((q[3], q[0], q[1], q[2]))
            m_mg = q_mg.to_matrix().to_4x4()
            m_mg.translation = Vector(w_pos[name])
            world_bl[name] = BASIS_MG_TO_BL @ m_mg @ BASIS_BL_TO_MG

        for b in bones:
            name = b["name"]
            pb = arm_obj.pose.bones[name]
            pb.rotation_mode = "QUATERNION"
            parent = parent_map[name]
            if parent == "NULL":
                basis = rest_bl[name].inverted() @ world_bl[name]
            else:
                parent_rest_inv = rest_bl[parent].inverted()
                rel_rest = parent_rest_inv @ rest_bl[name]
                rel_pose = world_bl[parent].inverted() @ world_bl[name]
                basis = rel_rest.inverted() @ rel_pose

            loc, rot, _scl = basis.decompose()
            if name == "ROOT":
                pb.location = loc
                pb.keyframe_insert(data_path="location", frame=frame_num)
            else:
                pb.location = (0.0, 0.0, 0.0)
            pb.rotation_quaternion = rot
            pb.keyframe_insert(data_path="rotation_quaternion", frame=frame_num)

    # Export straight from Blender's evaluated pose matrices via blender_export_simanim.py!
    out_path = os.path.join(OUT_DIR, f"{spec['name']}.simanim.json")
    clip = export_action_to_simanim(
        arm_obj,
        action_name=act_name,
        out_path=out_path,
        clip_name=spec["name"],
        fps=spec["fps"],
        loop=spec["loop"],
        mask=spec["mask"],
        root_speed=spec["root_speed"],
        source=f"blender4.5:adult_rig.blend/{act_name}",
        hand_gestures=spec["hand_gestures"],
        events=spec["events"],
        ground_feet=False,
        frame_start=1,
        frame_end=len(spec["frames"]),
    )
    print(f"  [OK] {clip['name']:26s} | {clip['frameCount']:2d}f @ {clip['fps']:4.1f}fps | loop={str(clip['loop']):5s} | speed={clip['rootSpeedUnitsPerSec']:5.2f} -> {os.path.basename(out_path)}")


def main():
    import bpy

    with open(SKEL_REF_PATH, "r", encoding="utf-8") as f:
        ref = json.load(f)
    bones = ref["bones"]
    rig = PoseRig(bones)

    arm_obj = bpy.data.objects.get("AdultRig")
    if arm_obj is None:
        raise SystemExit("AdultRig not found; run with adult_rig.blend")

    builders = [
        anim_cadet_idle_attention,
        anim_cadet_sprint_loop,
        anim_cadet_hurdle_hop,
        anim_cadet_wall_vault,
        anim_cadet_target_aim_loop,
        anim_cadet_target_fire_recoil,
        anim_cadet_victory_salute,
    ]

    os.makedirs(OUT_DIR, exist_ok=True)
    print("Generating Cadet Academy Custom Skeletal Animation Pack via Blender 4.5...")
    for fn in builders:
        spec = fn(rig)
        bake_into_blender_and_export(arm_obj, bones, spec)

    # Save all authored Actions back into adult_rig.blend for future interactive Blender GUI tweaking!
    bpy.ops.wm.save_mainfile()
    print("Updated adult_rig.blend with all 6+1 Cadet Actions.")


if __name__ == "__main__":
    main()
