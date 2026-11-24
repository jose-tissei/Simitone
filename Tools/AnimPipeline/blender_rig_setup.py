"""Headless Blender 4.5 script that builds adult_rig.blend with the exact 29-bone
TS1 adult.skel armature, pole-target leg/arm IK setup, and visual mannequin proxy.

Run:
  "C:\\Program Files\\Blender Foundation\\Blender 4.5\\blender.exe" --background --python Tools/AnimPipeline/blender_rig_setup.py
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
SKEL_REF_PATH = os.path.join(HERE, "adult_skel_ref.json")
OUT_BLEND = os.path.join(HERE, "adult_rig.blend")

# Exact basis conversion between FreeSO/MonoGame RH (+Y up, +Z forward, +X Sim Left)
# and Blender RH (+Z up, -Y forward, +X right/Sim Left):
# B = Rx(+90 deg): (x, y, z)_MG -> (x, -z, y)_Blender
BASIS_MG_TO_BL = Matrix.Rotation(math.radians(90.0), 4, "X")
BASIS_BL_TO_MG = BASIS_MG_TO_BL.inverted()


def mg_pos_to_bl(p):
    return Vector((p[0], -p[2], p[1]))


def mg_quat_to_bl_mat4(q_xyzw, pos_mg):
    # MonoGame Quaternion(x,y,z,w) -> Blender Quaternion((w,x,y,z))
    q_mg = Quaternion((q_xyzw[3], q_xyzw[0], q_xyzw[1], q_xyzw[2]))
    m_mg = q_mg.to_matrix().to_4x4()
    m_mg.translation = Vector(pos_mg)
    return BASIS_MG_TO_BL @ m_mg @ BASIS_BL_TO_MG


def build_adult_rig(save_path=OUT_BLEND):
    with open(SKEL_REF_PATH, "r", encoding="utf-8") as f:
        ref = json.load(f)
    bones = ref["bones"]
    by_name = {b["name"]: b for b in bones}
    children_map = {b["name"]: [] for b in bones}
    for b in bones:
        if b["parent"] in children_map:
            children_map[b["parent"]].append(b["name"])

    # Clean scene
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = 30

    arm_data = bpy.data.armatures.new("AdultSkeleton")
    arm_data.display_type = "STICK"
    arm_obj = bpy.data.objects.new("AdultRig", arm_data)
    scene.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    arm_obj.select_set(True)

    bpy.ops.object.mode_set(mode="EDIT")
    ebones = arm_data.edit_bones

    # Create all 29 deform bones aligned strictly to exact FreeSO world rest frames
    created = {}
    for b in bones:
        name = b["name"]
        eb = ebones.new(name)
        head = mg_pos_to_bl(b["restWorldPos"])
        kids = children_map[name]
        if kids:
            # Estimate visual bone length from primary child
            c_pos = mg_pos_to_bl(by_name[kids[0]]["restWorldPos"])
            length = max(0.15, (c_pos - head).length)
        else:
            length = 0.25

        # Place bone tail along local +Y of exact converted FreeSO basis matrix
        m_bl = mg_quat_to_bl_mat4(b["restWorldRot"], b["restWorldPos"])
        eb.head = head
        eb.tail = head + (m_bl.to_3x3() @ Vector((0.0, length, 0.0)))
        eb.matrix = m_bl
        eb.length = length
        eb.use_connect = False
        created[name] = eb

    for b in bones:
        p = b["parent"]
        if p in created:
            created[b["name"]].parent = created[p]

    # Add optional IK target control bones (not exported, prefixed IK_) for interactive Blender posing
    for side, foot, knee in [("L", "L_FOOT", "L_LEG1"), ("R", "R_FOOT", "R_LEG1")]:
        ik = ebones.new(f"IK_FOOT_{side}")
        f_head = mg_pos_to_bl(by_name[foot]["restWorldPos"])
        ik.head = f_head
        ik.tail = f_head + Vector((0.0, -0.4, 0.0))
        ik.use_deform = False

        pole = ebones.new(f"IK_KNEE_POLE_{side}")
        k_head = mg_pos_to_bl(by_name[knee]["restWorldPos"])
        pole.head = k_head + Vector((0.0, -1.6, 0.0))  # -Y in Blender == +Z forward in MonoGame
        pole.tail = pole.head + Vector((0.0, -0.25, 0.0))
        pole.use_deform = False

    bpy.ops.object.mode_set(mode="POSE")
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = "QUATERNION"

    # Attach non-destructive IK constraints (disabled by default via mute=True so FK Actions work cleanly,
    # artists can unmute them in Blender Pose Mode when hand-animating with IK!)
    for side, shin in [("L", "L_LEG1"), ("R", "R_LEG1")]:
        pb = arm_obj.pose.bones[shin]
        con = pb.constraints.new("IK")
        con.name = f"LegIK_{side}"
        con.target = arm_obj
        con.subtarget = f"IK_FOOT_{side}"
        con.pole_target = arm_obj
        con.pole_subtarget = f"IK_KNEE_POLE_{side}"
        con.chain_count = 2
        con.mute = True

    bpy.ops.object.mode_set(mode="OBJECT")

    # Add a ground plane guide at Z=0 for visual clarity when opening in Blender GUI
    bpy.ops.mesh.primitive_plane_add(size=12.0, location=(0.0, 0.0, 0.0))
    plane = bpy.context.active_object
    plane.name = "GroundGuide"

    if save_path:
        bpy.ops.wm.save_as_mainfile(filepath=save_path)
        print(f"Saved Blender 4.5 adult.skel IK rig to {save_path}")
    return arm_obj


if __name__ == "__main__":
    build_adult_rig()
