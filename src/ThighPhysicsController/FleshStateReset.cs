using UnityEngine;

namespace ThighPhysicsController;

/// <summary>
/// Canonical runtime-state reset. Every recovery path must clear the same velocity,
/// rotation, drift and last-write fields so stale state cannot survive only because
/// a reset was triggered by a different condition.
/// </summary>
internal static class FleshStateReset
{
    public static void Spring(FleshBone state, Vector3 baseLocal)
    {
        Transform bone = state.Bone;
        state.LastSetRot = bone.localRotation;
        state.BaseLocal = baseLocal;
        state.Offset = Vector3.zero;
        state.PrevOffset = Vector3.zero;
        state.AccelSmoothed = Vector3.zero;
        state.RotSmoothed = Vector3.zero;
        state.RotTarget = Vector3.zero;
        state.AngularVelBody = Vector3.zero;
        state.LastSag = Vector3.zero;
        state.PrevBaseWorld = bone.position;
        state.PrevBaseWorld2 = bone.position;
        state.Position = bone.position;
        state.PrevPosition = bone.position;
        state.PreviousDt = 1f / 60f;
        state.LastApplied = Vector3.zero;
        state.LastWorldApply = Vector3.zero;
        state.LastAppliedLocal = Vector3.zero;
        state.DriftWatchTime = 0f;
        state.PhysicsActive = false;
        state.PrevParentRot = bone.parent == null
            ? Quaternion.identity
            : bone.parent.rotation;
    }

    public static void Chain(ChainParticle state)
    {
        state.Position = state.Bone.position;
        state.PrevPosition = state.Bone.position;
        state.PreviousDt = 1f / 60f;
        state.LastAppliedLocal = Vector3.zero;
        state.LastAppliedRotLocal = Quaternion.identity;
        state.RotSmoothed = Vector3.zero;
        state.RotTarget = Vector3.zero;
    }

    public static Vector3 ReanchorChain(ChainParticle state)
    {
        Transform bone = state.Bone;
        Transform parent = bone.parent;
        state.ParentBone = parent;
        state.BaseLocal = bone.localPosition;
        state.BaseRotLocal = bone.localRotation;
        state.PrevAnimatedLocal = bone.localPosition;
        Vector3 worldBase = parent == null
            ? bone.position
            : parent.TransformPoint(state.BaseLocal);
        state.Position = worldBase;
        state.PrevPosition = worldBase;
        state.PreviousDt = 1f / 60f;
        state.LastAppliedLocal = Vector3.zero;
        state.LastAppliedRotLocal = Quaternion.identity;
        state.RotSmoothed = Vector3.zero;
        state.RotTarget = Vector3.zero;
        return worldBase;
    }
}
