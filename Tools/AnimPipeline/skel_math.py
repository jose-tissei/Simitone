"""Pure-Python right-handed quaternion and FK utilities matching FreeSO/MonoGame adult.skel."""
import math

def v_add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])

def v_sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])

def v_scale(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)

def v_dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]

def v_cross(a, b):
    return (
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    )

def v_len(a):
    return math.sqrt(v_dot(a, a))

def v_norm(a):
    l = v_len(a)
    if l < 1e-9:
        return (0.0, 0.0, 0.0)
    return (a[0] / l, a[1] / l, a[2] / l)

def q_norm(q):
    x, y, z, w = q
    l = math.sqrt(x * x + y * y + z * z + w * w)
    if l < 1e-10:
        return (0.0, 0.0, 0.0, 1.0)
    return (x / l, y / l, z / l, w / l)

def q_conj(q):
    return (-q[0], -q[1], -q[2], q[3])

def q_dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3]

def q_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    )

def q_axis_angle(axis, radians):
    ax = v_norm(axis)
    if v_len(ax) < 1e-9:
        return (0.0, 0.0, 0.0, 1.0)
    h = 0.5 * radians
    s = math.sin(h)
    c = math.cos(h)
    return q_norm((ax[0] * s, ax[1] * s, ax[2] * s, c))

def q_axis_deg(axis, deg):
    return q_axis_angle(axis, math.radians(deg))

def q_from_two_vectors(u, v):
    u = v_norm(u)
    v = v_norm(v)
    d = v_dot(u, v)
    if d > 0.999999:
        return (0.0, 0.0, 0.0, 1.0)
    if d < -0.999999:
        ortho = (1.0, 0.0, 0.0) if abs(u[0]) < 0.9 else (0.0, 1.0, 0.0)
        axis = v_norm(v_cross(u, ortho))
        return (axis[0], axis[1], axis[2], 0.0)
    c = v_cross(u, v)
    return q_norm((c[0], c[1], c[2], 1.0 + d))

def q_rotate(q, v):
    qx, qy, qz, qw = q
    vx, vy, vz = v
    tx = 2.0 * (qy * vz - qz * vy)
    ty = 2.0 * (qz * vx - qx * vz)
    tz = 2.0 * (qx * vy - qy * vx)
    return (
        vx + qw * tx + (qy * tz - qz * ty),
        vy + qw * ty + (qz * tx - qx * tz),
        vz + qw * tz + (qx * ty - qy * tx),
    )

def q_slerp(a, b, t):
    if t <= 0.0:
        return q_norm(a)
    if t >= 1.0:
        return q_norm(b)
    d = q_dot(a, b)
    if d < 0.0:
        b = (-b[0], -b[1], -b[2], -b[3])
        d = -d
    if d > 0.9995:
        return q_norm((
            a[0] + (b[0] - a[0]) * t,
            a[1] + (b[1] - a[1]) * t,
            a[2] + (b[2] - a[2]) * t,
            a[3] + (b[3] - a[3]) * t,
        ))
    d = min(1.0, d)
    th0 = math.acos(d)
    s0 = math.sin(th0 - th0 * t) / math.sin(th0)
    s1 = math.sin(th0 * t) / math.sin(th0)
    return q_norm((
        s0 * a[0] + s1 * b[0],
        s0 * a[1] + s1 * b[1],
        s0 * a[2] + s1 * b[2],
        s0 * a[3] + s1 * b[3],
    ))

def q_from_euler_deg(rx=0.0, ry=0.0, rz=0.0, order="YXZ"):
    """Intrinsic/extrinsic Euler helper returning Hamilton quaternion.
    Applies rotations right-to-left (last letter first), matching standard q_mul(R_first, ...)."""
    axes = {
        "X": (1.0, 0.0, 0.0),
        "Y": (0.0, 1.0, 0.0),
        "Z": (0.0, 0.0, 1.0),
    }
    vals = {"X": rx, "Y": ry, "Z": rz}
    q = (0.0, 0.0, 0.0, 1.0)
    for ch in order:
        ang = vals[ch]
        if abs(ang) > 1e-9:
            q = q_mul(q, q_axis_deg(axes[ch], ang))
    return q_norm(q)

def compute_fk(bones, local_trans_map=None, local_rot_map=None):
    """Exact FreeSO Skeleton.ComputeBonePositions forward kinematics.
    Returns (world_pos_by_name, world_rot_by_name)."""
    local_trans_map = local_trans_map or {}
    local_rot_map = local_rot_map or {}
    world_pos = {}
    world_rot = {}
    for b in bones:
        name = b["name"]
        parent = b["parent"]
        lt = tuple(local_trans_map.get(name, b["localTranslation"]))
        lr = q_norm(tuple(local_rot_map.get(name, b["localRotation"])))
        if parent == "NULL" or parent not in world_pos:
            world_pos[name] = lt
            world_rot[name] = lr
        else:
            pw = world_pos[parent]
            pr = world_rot[parent]
            world_pos[name] = v_add(pw, q_rotate(pr, lt))
            world_rot[name] = q_norm(q_mul(pr, lr))
    return world_pos, world_rot

def world_rots_to_local(bones, world_rot_map):
    """Converts desired world orientations per bone back into FreeSO local bone quaternions."""
    local_rot = {}
    for b in bones:
        name = b["name"]
        parent = b["parent"]
        wr = q_norm(world_rot_map[name])
        if parent == "NULL" or parent not in world_rot_map:
            local_rot[name] = wr
        else:
            pr = q_norm(world_rot_map[parent])
            local_rot[name] = q_norm(q_mul(q_conj(pr), wr))
    return local_rot

def align_track_hemispheres(quats):
    out = [q_norm(q) for q in quats]
    for i in range(1, len(out)):
        if q_dot(out[i - 1], out[i]) < 0.0:
            q = out[i]
            out[i] = (-q[0], -q[1], -q[2], -q[3])
    return out
