using System.Collections.Generic;
using UnityEngine;

namespace ThighPhysicsController;

/// <summary>Mutable runtime state owned by the spring solver for one flesh bone.</summary>
internal sealed class FleshBone
{
    public Transform Bone;
    public Transform Parent;
    public Transform AimChild;
    public Vector3 RestDirLocal;
    public Vector3 PristineLocal;
    public Quaternion PristineRot;
    public Vector3 PristineScale;
    public Vector3 BaseLocal;
    public Vector3 Offset;
    public Vector3 PrevOffset;
    public Vector3 PrevBaseWorld;
    public Vector3 PrevBaseWorld2;
    public Vector3 AccelSmoothed;
    public Vector3 Position;
    public Vector3 PrevPosition;
    public float PreviousDt = 1f / 60f;
    public Vector3 LastSag;
    public int BoneIndex;
    public Vector3 LastApplied;
    public Vector3 LastWorldApply;
    public Vector3 LastAppliedLocal;
    public Vector3 RotSmoothed;
    public Quaternion LastSetRot;
    public Vector3 RotTarget;
    public bool PhysicsActive;
    public Quaternion PrevParentRot;
    public Vector3 AngularVelBody;
    public float LastOverwriteLogTime;
    public float DriftWatchTime;
    public float LastReanchorTime;
}

/// <summary>Mutable Verlet state owned by the chain solver for one flesh bone.</summary>
internal sealed class ChainParticle
{
    public Transform Bone;
    public Transform ParentBone;
    public Vector3 RestDirLocal;
    public float RestLength;
    public int BoneIndex;
    public Vector3 Position;
    public Vector3 PrevPosition;
    public float PreviousDt = 1f / 60f;
    public Vector3 LastAppliedLocal;
    public Vector3 PrevAnimatedLocal;
    public Vector3 BaseLocal;
    public Quaternion BaseRotLocal;
    public Quaternion LastAppliedRotLocal;
    public Vector3 RotTarget;
    public Vector3 RotSmoothed;
}

/// <summary>One anchored left, right, or unpaired chain.</summary>
internal sealed class SideChain
{
    public Transform Anchor;
    public Vector3 PrevAnchorPos;
    public Quaternion PrevAnchorRot;
    /// <summary>Most recent raw anchor deltas normalized to one 60 FPS step.</summary>
    public Vector3 AnchorMoveAt60;
    public Vector3 AnchorAngularAt60;
    /// <summary>Second-most-recent normalized samples for the high-FPS median guard.</summary>
    public Vector3 PreviousAnchorMoveAt60;
    public Vector3 PreviousAnchorAngularAt60;
    public int AnchorInputSampleCount;
    public readonly List<ChainParticle> Particles = new List<ChainParticle>();
}
