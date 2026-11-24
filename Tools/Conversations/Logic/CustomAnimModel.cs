using System;
using System.Collections.Generic;

namespace Simitone.Conversations
{
    public enum AnimBoneMask
    {
        Full = 0,
        UpperBody = 1,
        LowerBody = 2
    }

    public struct AnimVec3
    {
        public float X;
        public float Y;
        public float Z;

        public static readonly AnimVec3 Zero = new AnimVec3(0f, 0f, 0f);
        public static readonly AnimVec3 UnitX = new AnimVec3(1f, 0f, 0f);
        public static readonly AnimVec3 UnitY = new AnimVec3(0f, 1f, 0f);
        public static readonly AnimVec3 UnitZ = new AnimVec3(0f, 0f, 1f);

        public AnimVec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float LengthSquared() => X * X + Y * Y + Z * Z;
        public float Length() => (float)Math.Sqrt(LengthSquared());

        public AnimVec3 Normalized()
        {
            float len = Length();
            if (len <= 1e-6f) return Zero;
            float inv = 1f / len;
            return new AnimVec3(X * inv, Y * inv, Z * inv);
        }

        public static float Dot(AnimVec3 a, AnimVec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static AnimVec3 Cross(AnimVec3 a, AnimVec3 b) => new AnimVec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

        public static AnimVec3 Lerp(AnimVec3 a, AnimVec3 b, float t) => new AnimVec3(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t);

        public static AnimVec3 CatmullRom(AnimVec3 p0, AnimVec3 p1, AnimVec3 p2, AnimVec3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return new AnimVec3(
                0.5f * ((2f * p1.X) + (-p0.X + p2.X) * t + (2f * p0.X - 5f * p1.X + 4f * p2.X - p3.X) * t2 + (-p0.X + 3f * p1.X - 3f * p2.X + p3.X) * t3),
                0.5f * ((2f * p1.Y) + (-p0.Y + p2.Y) * t + (2f * p0.Y - 5f * p1.Y + 4f * p2.Y - p3.Y) * t2 + (-p0.Y + 3f * p1.Y - 3f * p2.Y + p3.Y) * t3),
                0.5f * ((2f * p1.Z) + (-p0.Z + p2.Z) * t + (2f * p0.Z - 5f * p1.Z + 4f * p2.Z - p3.Z) * t2 + (-p0.Z + 3f * p1.Z - 3f * p2.Z + p3.Z) * t3));
        }

        public static AnimVec3 operator +(AnimVec3 a, AnimVec3 b) => new AnimVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static AnimVec3 operator -(AnimVec3 a, AnimVec3 b) => new AnimVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static AnimVec3 operator -(AnimVec3 a) => new AnimVec3(-a.X, -a.Y, -a.Z);
        public static AnimVec3 operator *(AnimVec3 a, float s) => new AnimVec3(a.X * s, a.Y * s, a.Z * s);
        public static AnimVec3 operator *(float s, AnimVec3 a) => new AnimVec3(a.X * s, a.Y * s, a.Z * s);
    }

    // Hamilton unit quaternion matching MonoGame/XNA right-handed rotation convention:
    // Rotate(v, q) == q * (v,0) * conj(q) == v * Matrix.CreateFromQuaternion(q).
    // Chaining local then parent: Q_world = Q_parent * Q_local.
    public struct AnimQuat
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public static readonly AnimQuat Identity = new AnimQuat(0f, 0f, 0f, 1f);

        public AnimQuat(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public float LengthSquared() => X * X + Y * Y + Z * Z + W * W;
        public float Length() => (float)Math.Sqrt(LengthSquared());

        public bool IsValid()
        {
            if (float.IsNaN(X) || float.IsNaN(Y) || float.IsNaN(Z) || float.IsNaN(W)) return false;
            if (float.IsInfinity(X) || float.IsInfinity(Y) || float.IsInfinity(Z) || float.IsInfinity(W)) return false;
            return LengthSquared() > 1e-10f;
        }

        public AnimQuat Normalized()
        {
            if (!IsValid()) return Identity;
            float inv = 1f / Length();
            return new AnimQuat(X * inv, Y * inv, Z * inv, W * inv);
        }

        public AnimQuat Conjugate() => new AnimQuat(-X, -Y, -Z, W);

        public static float Dot(AnimQuat a, AnimQuat b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

        public static AnimQuat Multiply(AnimQuat a, AnimQuat b)
        {
            return new AnimQuat(
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
        }

        public static AnimQuat operator *(AnimQuat a, AnimQuat b) => Multiply(a, b);
        public static AnimQuat operator -(AnimQuat q) => new AnimQuat(-q.X, -q.Y, -q.Z, -q.W);

        public static AnimQuat FromAxisAngle(AnimVec3 axis, float radians)
        {
            AnimVec3 n = axis.Normalized();
            if (n.LengthSquared() < 1e-6f) return Identity;
            float half = radians * 0.5f;
            float s = (float)Math.Sin(half);
            float c = (float)Math.Cos(half);
            return new AnimQuat(n.X * s, n.Y * s, n.Z * s, c).Normalized();
        }

        public static AnimQuat FromTwoVectors(AnimVec3 from, AnimVec3 to)
        {
            AnimVec3 f = from.Normalized();
            AnimVec3 t = to.Normalized();
            if (f.LengthSquared() < 1e-6f || t.LengthSquared() < 1e-6f) return Identity;
            float dot = AnimVec3.Dot(f, t);
            if (dot > 0.99999f) return Identity;
            if (dot < -0.99999f)
            {
                AnimVec3 ortho = Math.Abs(f.X) < 0.9f ? AnimVec3.UnitX : AnimVec3.UnitY;
                AnimVec3 axis = AnimVec3.Cross(f, ortho).Normalized();
                return new AnimQuat(axis.X, axis.Y, axis.Z, 0f);
            }
            AnimVec3 c = AnimVec3.Cross(f, t);
            return new AnimQuat(c.X, c.Y, c.Z, 1f + dot).Normalized();
        }

        public AnimVec3 Rotate(AnimVec3 v)
        {
            // q * (v, 0) * conj(q) optimized
            float tx = 2f * (Y * v.Z - Z * v.Y);
            float ty = 2f * (Z * v.X - X * v.Z);
            float tz = 2f * (X * v.Y - Y * v.X);
            return new AnimVec3(
                v.X + W * tx + (Y * tz - Z * ty),
                v.Y + W * ty + (Z * tx - X * tz),
                v.Z + W * tz + (X * ty - Y * tx));
        }

        // Shortest-arc Spherical Linear Interpolation
        public static AnimQuat Slerp(AnimQuat a, AnimQuat b, float t)
        {
            if (t <= 0f) return a.Normalized();
            if (t >= 1f) return b.Normalized();

            float dot = Dot(a, b);
            if (dot < 0f)
            {
                b = -b;
                dot = -dot;
            }
            if (dot > 0.9995f)
            {
                return new AnimQuat(
                    a.X + (b.X - a.X) * t,
                    a.Y + (b.Y - a.Y) * t,
                    a.Z + (b.Z - a.Z) * t,
                    a.W + (b.W - a.W) * t).Normalized();
            }

            if (dot > 1f) dot = 1f;
            float theta0 = (float)Math.Acos(dot);
            float sinTheta0 = (float)Math.Sin(theta0);
            float theta = theta0 * t;
            float s0 = (float)Math.Sin(theta0 - theta) / sinTheta0;
            float s1 = (float)Math.Sin(theta) / sinTheta0;
            return new AnimQuat(
                s0 * a.X + s1 * b.X,
                s0 * a.Y + s1 * b.Y,
                s0 * a.Z + s1 * b.Z,
                s0 * a.W + s1 * b.W).Normalized();
        }

        static AnimQuat Log(AnimQuat q)
        {
            float w = Math.Max(-1f, Math.Min(1f, q.W));
            float theta = (float)Math.Acos(w);
            float sinTheta = (float)Math.Sin(theta);
            if (sinTheta < 1e-5f) return new AnimQuat(q.X, q.Y, q.Z, 0f);
            float k = theta / sinTheta;
            return new AnimQuat(q.X * k, q.Y * k, q.Z * k, 0f);
        }

        static AnimQuat Exp(AnimQuat q)
        {
            float theta = (float)Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z);
            float cosT = (float)Math.Cos(theta);
            if (theta < 1e-5f) return new AnimQuat(q.X, q.Y, q.Z, cosT).Normalized();
            float k = (float)Math.Sin(theta) / theta;
            return new AnimQuat(q.X * k, q.Y * k, q.Z * k, cosT).Normalized();
        }

        static AnimQuat SquadControlPoint(AnimQuat qPrev, AnimQuat qCurr, AnimQuat qNext)
        {
            if (Dot(qCurr, qPrev) < 0f) qPrev = -qPrev;
            if (Dot(qCurr, qNext) < 0f) qNext = -qNext;
            AnimQuat invCurr = qCurr.Conjugate();
            AnimQuat l1 = Log((invCurr * qNext).Normalized());
            AnimQuat l2 = Log((invCurr * qPrev).Normalized());
            AnimQuat avg = new AnimQuat(
                -0.25f * (l1.X + l2.X),
                -0.25f * (l1.Y + l2.Y),
                -0.25f * (l1.Z + l2.Z),
                0f);
            return (qCurr * Exp(avg)).Normalized();
        }

        // Cubic spherical quadrangle interpolation (C1 smooth velocity across keyframes)
        public static AnimQuat Squad(AnimQuat q0, AnimQuat q1, AnimQuat q2, AnimQuat q3, float t)
        {
            if (Dot(q1, q0) < 0f) q0 = -q0;
            if (Dot(q1, q2) < 0f) q2 = -q2;
            if (Dot(q2, q3) < 0f) q3 = -q3;

            AnimQuat s1 = SquadControlPoint(q0, q1, q2);
            AnimQuat s2 = SquadControlPoint(q1, q2, q3);
            AnimQuat slerp12 = Slerp(q1, q2, t);
            AnimQuat slerpS = Slerp(s1, s2, t);
            return Slerp(slerp12, slerpS, 2f * t * (1f - t));
        }
    }

    public sealed class CustomAnimEvent
    {
        public int Frame;
        public string Name;
        public string Value;
    }

    public sealed class CustomHandCue
    {
        public int Frame;
        public string Left;
        public string Right;
    }

    public sealed class CustomBoneTrack
    {
        public string Bone;
        public AnimVec3[] Translations; // optional (null if rotation-only)
        public AnimQuat[] Rotations;    // optional (null if translation-only)
        public float[] Times;           // optional sparse timestamps (null = uniform FPS)
    }

    public sealed class CustomAnimClip
    {
        public int Version = 1;
        public string Name = "";
        public string Skeleton = "adult";
        public float Fps = 30f;
        public int FrameCount;
        public float Duration;
        public bool Loop = true;
        public AnimBoneMask Mask = AnimBoneMask.Full;
        public float RootSpeedUnitsPerSec;
        public string Source = "";
        public List<CustomAnimEvent> Events = new List<CustomAnimEvent>();
        public List<CustomHandCue> HandGestures = new List<CustomHandCue>();
        public List<CustomBoneTrack> Tracks = new List<CustomBoneTrack>();

        Dictionary<string, CustomBoneTrack> trackMap;

        public CustomBoneTrack GetTrack(string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return null;
            if (trackMap == null)
            {
                trackMap = new Dictionary<string, CustomBoneTrack>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < Tracks.Count; i++)
                {
                    var tr = Tracks[i];
                    if (tr != null && !string.IsNullOrEmpty(tr.Bone))
                        trackMap[tr.Bone] = tr;
                }
            }
            CustomBoneTrack found;
            return trackMap.TryGetValue(boneName, out found) ? found : null;
        }

        public void RebuildIndex()
        {
            trackMap = null;
        }
    }

    public struct CustomBonePose
    {
        public bool HasTranslation;
        public AnimVec3 Translation;
        public bool HasRotation;
        public AnimQuat Rotation;
    }

    public static class AdultSkeletonMask
    {
        static readonly HashSet<string> UpperBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SPINE", "SPINE1", "SPINE2", "NECK", "HEAD",
            "R_ARM", "R_ARM1", "R_ARM2", "R_HAND", "R_FINGER0",
            "L_ARM", "L_ARM1", "L_ARM2", "L_HAND", "L_FINGER0"
        };

        static readonly HashSet<string> LowerBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ROOT", "PELVIS",
            "R_LEG", "R_LEG1", "R_LEG2", "R_FOOT", "R_TOE0",
            "L_LEG", "L_LEG1", "L_LEG2", "L_FOOT", "L_TOE0"
        };

        public static bool Includes(AnimBoneMask mask, string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return false;
            switch (mask)
            {
                case AnimBoneMask.UpperBody: return UpperBones.Contains(boneName);
                case AnimBoneMask.LowerBody: return LowerBones.Contains(boneName);
                default: return true;
            }
        }
    }
}
