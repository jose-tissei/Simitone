using System;

namespace Simitone.Conversations
{
    // Analytical 2-Bone Limb IK solver (Law of Cosines + pole vector) for precise
    // foot grounding, wall-hand planting, and target pointing in pure C# (ANIM-09).
    public static class TwoBoneIkSolver
    {
        public struct SolveResult
        {
            public AnimVec3 MidPosition;
            public AnimVec3 EndPosition;
            public AnimQuat UpperWorldDelta;
            public AnimQuat LowerWorldDelta;
            public bool ReachedTarget;
        }

        // Solves a 2-bone chain (root -> mid -> end) to reach target while bending the
        // middle joint toward poleHint. Returns world-space delta quaternions to pre-multiply
        // onto the current upper/lower world orientations.
        public static SolveResult Solve(
            AnimVec3 rootPos,
            AnimVec3 midPos,
            AnimVec3 endPos,
            AnimVec3 targetPos,
            AnimVec3 poleHint)
        {
            AnimVec3 uVec = midPos - rootPos;
            AnimVec3 lVec = endPos - midPos;
            float lenA = uVec.Length();
            float lenB = lVec.Length();

            if (lenA < 1e-5f || lenB < 1e-5f)
            {
                return new SolveResult
                {
                    MidPosition = midPos,
                    EndPosition = endPos,
                    UpperWorldDelta = AnimQuat.Identity,
                    LowerWorldDelta = AnimQuat.Identity,
                    ReachedTarget = false
                };
            }

            AnimVec3 toTarget = targetPos - rootPos;
            float dist = toTarget.Length();
            float minReach = Math.Abs(lenA - lenB) + 1e-4f;
            float maxReach = (lenA + lenB) - 1e-4f;
            bool reachable = dist >= minReach && dist <= maxReach;
            float clampedDist = Math.Max(minReach, Math.Min(maxReach, dist));

            AnimVec3 dir = dist > 1e-5f ? (toTarget * (1f / dist)) : uVec.Normalized();

            // Build orthonormal bend plane using poleHint
            AnimVec3 toPole = poleHint - rootPos;
            AnimVec3 projPole = toPole - dir * AnimVec3.Dot(toPole, dir);
            if (projPole.LengthSquared() < 1e-6f)
            {
                AnimVec3 fallback = Math.Abs(dir.Y) < 0.9f ? AnimVec3.UnitY : AnimVec3.UnitZ;
                projPole = fallback - dir * AnimVec3.Dot(fallback, dir);
            }
            AnimVec3 bendDir = projPole.Normalized();

            // Law of Cosines for angle at root between dir and upper limb
            float cosAlpha = (lenA * lenA + clampedDist * clampedDist - lenB * lenB) / (2f * lenA * clampedDist);
            cosAlpha = Math.Max(-1f, Math.Min(1f, cosAlpha));
            float sinAlpha = (float)Math.Sqrt(Math.Max(0f, 1f - cosAlpha * cosAlpha));

            AnimVec3 newMid = rootPos + dir * (lenA * cosAlpha) + bendDir * (lenA * sinAlpha);
            AnimVec3 newEnd = rootPos + dir * clampedDist;

            AnimVec3 newUVec = newMid - rootPos;
            AnimQuat upperDelta = AnimQuat.FromTwoVectors(uVec, newUVec);

            AnimVec3 rotatedLVec = upperDelta.Rotate(lVec);
            AnimVec3 newLVec = newEnd - newMid;
            AnimQuat lowerDelta = AnimQuat.FromTwoVectors(rotatedLVec, newLVec);

            return new SolveResult
            {
                MidPosition = newMid,
                EndPosition = newEnd,
                UpperWorldDelta = upperDelta,
                LowerWorldDelta = lowerDelta,
                ReachedTarget = reachable
            };
        }
    }
}
