using UnityEngine;

namespace ThighPhysicsController;

/// <summary>Pure non-finite checks shared by both runtime solvers.</summary>
internal static class FleshSafetyGuard
{
    public static bool IsNan(Vector3 value)
    {
        return !FleshValue.IsFinite(value.x) ||
               !FleshValue.IsFinite(value.y) ||
               !FleshValue.IsFinite(value.z);
    }

    public static bool IsBadQuat(Quaternion value)
    {
        return !FleshValue.IsFinite(value.x) ||
               !FleshValue.IsFinite(value.y) ||
               !FleshValue.IsFinite(value.z) ||
               !FleshValue.IsFinite(value.w);
    }
}
