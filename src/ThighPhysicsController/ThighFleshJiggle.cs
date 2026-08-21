using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using static ThighPhysicsController.FleshSafetyGuard;

namespace ThighPhysicsController;

/// <summary>
/// Drives the five thigh flesh bones per side.
/// Two modes:
/// - Spring mode (default): acceleration + angular velocity driven offsets in character local space.
/// - Chain mode ("Game DynamicBone chain physics"): a port of the game's DynamicBone_Ver02
///   chain-particle algorithm (root particle follows the anchor bone, child particles use
///   spring + length constraints, anchor movement drives the chain).
/// </summary>
[DefaultExecutionOrder(30000)]
public sealed partial class ThighFleshJiggle : MonoBehaviour
{
    public FleshPartId PartId { get; private set; }

    public ChaControl ChaControlRef;
    public ThighParams ParamsRef;

    private readonly List<FleshBone> _bones = new List<FleshBone>();
    private readonly List<SideChain> _chains = new List<SideChain>();

    private bool _chainsBuilt;
    private float _time;
    private float _lastLogTime;
    private float _chainTime;
    private float _chainLogTime;
    private float _chainReanchorLogTime;
    private bool _constraintSafeRotationsLastFrame;
    private bool _metricsEnabledLastFrame;
    private bool _collectMetricsThisFrame;
    private bool _runtimeStatusLogged;
    private float _retryTimer;
    private bool _chainPoseSettleActive;
    private int _chainPoseSettleMinFrames;
    private int _chainPoseSettleTimeoutFrames;
    private int _chainPoseStableFrames;
    private int _chainPoseSettleElapsedFrames;
    private bool _shapeRebasePending;
    private bool _lastGamePhysics;
    private bool _timelineSpringFallbackLastFrame;
    private FleshPartId _partId = FleshPartId.Thigh;
    private int _distalIndex = 3;

    // Timeline and Studio can write flesh-bone rotations in small increments.  A
    // five-degree ownership threshold lets those writes slip under Soma's radar and
    // makes Chain RC aim from a stale scene-load base.  Soma's own previous write is
    // deterministic in local space, so a sub-degree tolerance is enough to separate
    // float noise from a real external pose write.
    private const float ExternalRotationThreshold = 0.35f;
    public void Initialize(ChaControl control, ThighParams param, FleshPartId partId)
    {
        PartId = partId;
        _partId = partId;
        FleshPartDef def = FleshPartDef.Get(partId);
        _distalIndex = 0;
        for (int c = 0; c < def.Chains.Length; c++)
        {
            for (int b = 0; b < def.Chains[c].BoneIndexes.Length; b++)
            {
                if (def.Chains[c].BoneIndexes[b] > _distalIndex)
                {
                    _distalIndex = def.Chains[c].BoneIndexes[b];
                }
            }
        }
        ChaControlRef = control;
        ParamsRef = param;
        _runtimeStatusLogged = false;
        _lastGamePhysics = param != null && param.GamePhysics;
        _bones.Clear();
        for (int c = 0; c < def.Chains.Length; c++)
        {
            AddChainBones(def.Chains[c], c);
        }
        BuildChains();
        ThighPhysicsControllerPlugin.LogRuntime("SOMA_RUNTIME_INIT part=" + def.DisplayName +
            " bones=" + _bones.Count +
            " enabled=" + (param != null && param.Enabled) +
            " solver=" + (param != null && param.GamePhysics ? "Chain" : "Spring") +
            " collectMetrics=" + ThighPhysicsControllerPlugin.DebugCollectMetrics.Value);
    }

    private void AddChainBones(FleshChainDef chainDef, int chainIndex)
    {
        if (chainDef.Paired)
        {
            AddChainSide(chainDef, "L", chainIndex);
            AddChainSide(chainDef, "R", chainIndex);
        }
        else
        {
            AddChainSide(chainDef, "", chainIndex);
        }
    }

    private void AddChainSide(FleshChainDef chainDef, string side, int chainIndex)
    {
        List<FleshBone> sideBones = new List<FleshBone>();
        for (int i = 0; i < chainDef.BoneNameTemplates.Length; i++)
        {
            string boneName = chainDef.BoneNameTemplates[i].Replace("{side}", side);
            sideBones.Add(AddBone(boneName, chainDef.BoneIndexes[i]));
        }
        for (int i = 0; i + 1 < sideBones.Count; i++)
        {
            FleshBone current = sideBones[i];
            FleshBone next = sideBones[i + 1];
            if (current != null && next != null && next.Bone != null)
            {
                current.AimChild = next.Bone;
                current.RestDirLocal = current.Bone.InverseTransformDirection(
                    next.Bone.position - current.Bone.position);
            }
        }
    }

    private void BuildChains()
    {
        _chains.Clear();
        FleshPartDef def = FleshPartDef.Get(_partId);
        for (int c = 0; c < def.Chains.Length; c++)
        {
            FleshChainDef chainDef = def.Chains[c];
            if (chainDef.Paired)
            {
                BuildChain(chainDef, "L", c);
                BuildChain(chainDef, "R", c);
            }
            else
            {
                BuildChain(chainDef, "", c);
            }
        }
        _chainsBuilt = true;
    }

    private void BuildChain(FleshChainDef chainDef, string side, int chainIndex)
    {
        Transform anchor = FindBone(chainDef.AnchorTemplate.Replace("{side}", side));
        if (anchor == null)
        {
            return;
        }
        SideChain chain = new SideChain();
        chain.Anchor = anchor;
        chain.PrevAnchorPos = anchor.position;
        chain.PrevAnchorRot = anchor.rotation;
        Transform parentBone = anchor;
        for (int i = 0; i < chainDef.BoneNameTemplates.Length; i++)
        {
            Transform bone = FindBone(chainDef.BoneNameTemplates[i].Replace("{side}", side));
            if (bone == null)
            {
                parentBone = null;
                continue;
            }
            ChainParticle particle = new ChainParticle();
            particle.Bone = bone;
            particle.ParentBone = parentBone;
            particle.BoneIndex = chainDef.BoneIndexes[i];
            if (parentBone != null)
            {
                particle.RestDirLocal = parentBone.InverseTransformDirection(bone.position - parentBone.position);
                particle.RestLength = (bone.position - parentBone.position).magnitude;
            }
            particle.Position = bone.position;
            particle.PrevPosition = bone.position;
            particle.LastAppliedLocal = Vector3.zero;
            particle.PrevAnimatedLocal = bone.localPosition;
            particle.BaseLocal = bone.localPosition;
            particle.SafeBaseLocal = bone.localPosition;
            particle.BaseRotLocal = bone.localRotation;
            particle.LastAppliedRotLocal = Quaternion.identity;
            particle.RotTarget = Vector3.zero;
            particle.RotSmoothed = Vector3.zero;
            chain.Particles.Add(particle);
            parentBone = bone;
        }
        if (chain.Particles.Count >= 1)
        {
            _chains.Add(chain);
        }
    }

    public void ResetState()
    {
        ResetMetricWindow();
        ResetPerformanceWindow();
        _metricWarmupRemaining = 2f;
        BuildChains();
        for (int i = 0; i < _bones.Count; i++)
        {
            FleshBone bone = _bones[i];
            // Note: PristineLocal/PristineRot are intentionally NOT re-captured here.
            // They are the card-default pose recorded when the bone was first added, so
            // Clear shape keeps working after switching chain/spring modes.
            FleshStateReset.Spring(bone, bone.Bone.localPosition);
        }
    }

    public void ClearDeformation()
    {
        for (int i = 0; i < _bones.Count; i++)
        {
            FleshBone bone = _bones[i];
            if (bone.Bone == null)
            {
                continue;
            }
            bone.Bone.localPosition = bone.PristineLocal;
            bone.Bone.localRotation = bone.PristineRot;
            bone.AnimatedRotLocal = bone.PristineRot;
            bone.LastSetRotLocal = bone.PristineRot;
            FleshStateReset.Spring(bone, bone.PristineLocal);
        }
        // Rebuild chains AFTER restoring the pristine pose, otherwise the chain
        // particles keep the pre-restore (deformed) positions and pull bones back
        // to the deformation, making repeated Clear shape presses worse.
        BuildChains();
    }

    public void RestorePoseAndResetState()
    {
        ClearDeformation();
        ResetMetricWindow();
        ResetPerformanceWindow();
        _metricWarmupRemaining = 2f;
    }

    /// <summary>
    /// Prepare for a Studio/Timeline pose transition without restoring the card's
    /// pristine pose. Chain mode first removes only the deformation owned by Soma,
    /// then yields for a few LateUpdate frames so Timeline can establish the loaded
    /// pose before the chain's rest geometry is captured again.
    /// </summary>
    public void PrepareForExternalPoseChange(int settleFrames)
    {
        if (ParamsRef == null || !ParamsRef.GamePhysics)
        {
            // Spring mode already has its own drift/re-anchor path. Preserve its
            // established transition behaviour; this fix is deliberately Chain-only.
            RestorePoseAndResetState();
            return;
        }

        RemoveOwnedChainDeformation();
        _shapeRebasePending = false;
        _chainPoseSettleActive = true;
        _chainPoseSettleMinFrames = Math.Max(2, settleFrames);
        _chainPoseSettleTimeoutFrames = Math.Max(12, settleFrames + 8);
        _chainPoseStableFrames = 0;
        _chainPoseSettleElapsedFrames = 0;
        CapturePoseSample();
        ResetMetricWindow();
        ResetPerformanceWindow();
        _metricWarmupRemaining = 2f;
    }

    /// <summary>
    /// PushUp and maker body sliders queue ShapeBody work that ChaControl applies
    /// later in the frame. Remove Soma's current output, yield until that body pose
    /// is stable, then make it the new pristine/rest pose. Spring must participate
    /// too, otherwise arm and belly interpret a breast-shape refresh as motion.
    /// </summary>
    public void PrepareForExternalShapeChange(int settleFrames)
    {
        if (ParamsRef == null)
            return;

        if (ParamsRef.GamePhysics)
            RemoveOwnedChainDeformation();
        else
            RemoveOwnedSpringDeformation();

        _shapeRebasePending = true;
        _chainPoseSettleActive = true;
        _chainPoseSettleMinFrames = Math.Max(2, settleFrames);
        _chainPoseSettleTimeoutFrames = Math.Max(12, settleFrames + 8);
        _chainPoseStableFrames = 0;
        _chainPoseSettleElapsedFrames = 0;
        CapturePoseSample();
        ResetMetricWindow();
        ResetPerformanceWindow();
        _metricWarmupRemaining = 2f;
    }

    private void BeginChainModeCapture(int settleFrames)
    {
        // A mutable ThighParams instance can switch modes without ApplyFlesh seeing a
        // newly enabled component. Do not reuse particles from the last time Chain was
        // active; sample the current Spring/Timeline pose and rebuild from it.
        _chainPoseSettleActive = true;
        _shapeRebasePending = false;
        _chainPoseSettleMinFrames = Math.Max(2, settleFrames);
        _chainPoseSettleTimeoutFrames = Math.Max(12, settleFrames + 8);
        _chainPoseStableFrames = 0;
        _chainPoseSettleElapsedFrames = 0;
        CapturePoseSample();
        ResetMetricWindow();
        ResetPerformanceWindow();
        _metricWarmupRemaining = 2f;
    }

    private void RemoveOwnedChainDeformation()
    {
        if (!_chainsBuilt || _chains.Count == 0)
        {
            BuildChains();
        }
        for (int i = 0; i < _chains.Count; i++)
        {
            SideChain chain = _chains[i];
            for (int j = 0; j < chain.Particles.Count; j++)
            {
                ChainParticle particle = chain.Particles[j];
                if (particle == null || particle.Bone == null)
                {
                    continue;
                }

                Vector3 expectedLocal = particle.BaseLocal + particle.LastAppliedLocal;
                if ((particle.Bone.localPosition - expectedLocal).sqrMagnitude < 0.000001f)
                {
                    particle.Bone.localPosition = particle.BaseLocal;
                }
                else
                {
                    // Another owner already wrote a pose. Keep it instead of forcing
                    // the older chain base over Timeline's current value.
                    particle.BaseLocal = particle.Bone.localPosition;
                }

                Quaternion expectedRot = particle.BaseRotLocal * particle.LastAppliedRotLocal;
                if (Quaternion.Angle(particle.Bone.localRotation, expectedRot) < 0.5f)
                {
                    particle.Bone.localRotation = particle.BaseRotLocal;
                }
                else
                {
                    particle.BaseRotLocal = particle.Bone.localRotation;
                }
                FleshStateReset.ReanchorChain(particle);
            }
            if (chain.Anchor != null)
            {
                chain.PrevAnchorPos = chain.Anchor.position;
                chain.PrevAnchorRot = chain.Anchor.rotation;
            }
            ResetChainInputHistory(chain);
        }
        for (int i = 0; i < _bones.Count; i++)
        {
            FleshBone bone = _bones[i];
            if (bone != null && bone.Bone != null)
            {
                FleshStateReset.Spring(bone, bone.Bone.localPosition);
            }
        }
    }

    private void RemoveOwnedSpringDeformation()
    {
        for (int i = 0; i < _bones.Count; i++)
        {
            FleshBone flesh = _bones[i];
            if (flesh == null || flesh.Bone == null)
                continue;

            Vector3 expectedLocal = flesh.BaseLocal + flesh.LastAppliedLocal;
            if ((flesh.Bone.localPosition - expectedLocal).sqrMagnitude < 0.000001f)
                flesh.Bone.localPosition = flesh.BaseLocal;
            else
                flesh.BaseLocal = flesh.Bone.localPosition;

            if (Quaternion.Angle(flesh.Bone.localRotation, flesh.LastSetRotLocal) < 0.5f)
                flesh.Bone.localRotation = flesh.AnimatedRotLocal;
            else
                flesh.AnimatedRotLocal = flesh.Bone.localRotation;

            flesh.LastSetRotLocal = flesh.Bone.localRotation;
            FleshStateReset.Spring(flesh, flesh.Bone.localPosition);
        }
    }

    private void CapturePoseSample()
    {
        for (int i = 0; i < _bones.Count; i++)
        {
            FleshBone bone = _bones[i];
            if (bone == null || bone.Bone == null)
            {
                continue;
            }
            bone.PoseSampleLocal = bone.Bone.localPosition;
            bone.PoseSampleRot = bone.Bone.localRotation;
        }
    }

    private bool UpdateExternalPoseSettle()
    {
        if (!_chainPoseSettleActive)
        {
            return false;
        }
        if (ParamsRef == null)
        {
            _chainPoseSettleActive = false;
            _shapeRebasePending = false;
            return false;
        }

        bool stable = true;
        for (int i = 0; i < _bones.Count; i++)
        {
            FleshBone bone = _bones[i];
            if (bone == null || bone.Bone == null)
            {
                continue;
            }
            if ((bone.Bone.localPosition - bone.PoseSampleLocal).sqrMagnitude > 0.00000025f ||
                Quaternion.Angle(bone.Bone.localRotation, bone.PoseSampleRot) > 0.35f)
            {
                stable = false;
            }
            bone.PoseSampleLocal = bone.Bone.localPosition;
            bone.PoseSampleRot = bone.Bone.localRotation;
        }

        _chainPoseStableFrames = stable ? _chainPoseStableFrames + 1 : 0;
        _chainPoseSettleElapsedFrames++;
        _chainPoseSettleMinFrames--;
        _chainPoseSettleTimeoutFrames--;
        if ((_chainPoseSettleMinFrames <= 0 && _chainPoseStableFrames >= 2) ||
            _chainPoseSettleTimeoutFrames <= 0)
        {
            if (_shapeRebasePending)
            {
                // A body-shape edit is a legitimate new rest pose, not drift.
                for (int i = 0; i < _bones.Count; i++)
                {
                    FleshBone bone = _bones[i];
                    if (bone == null || bone.Bone == null)
                        continue;
                    bone.PristineLocal = bone.Bone.localPosition;
                    bone.PristineRot = bone.Bone.localRotation;
                    bone.PristineScale = bone.Bone.localScale;
                    bone.AnimatedRotLocal = bone.PristineRot;
                    bone.LastSetRotLocal = bone.PristineRot;
                }
            }
            // Build after Timeline's LateUpdate-visible pose has settled. The solver
            // intentionally starts next frame so this capture cannot create a spike.
            BuildChains();
            for (int i = 0; i < _bones.Count; i++)
            {
                FleshBone bone = _bones[i];
                if (bone != null && bone.Bone != null)
                {
                    FleshStateReset.Spring(bone, bone.Bone.localPosition);
                }
            }
            _chainPoseSettleActive = false;
            bool shapeRebase = _shapeRebasePending;
            _shapeRebasePending = false;
            UnityEngine.Debug.Log("SOMA_POSE_REBASE part=" + _partId +
                " frames=" + _chainPoseSettleElapsedFrames +
                " reason=" + (shapeRebase ? "body_shape_" : "") +
                (_chainPoseStableFrames >= 2 ? "stable" : "timeout"));
        }
        return true;
    }

    private void ResetFleshBoneState(FleshBone flesh)
    {
        if (flesh == null || flesh.Bone == null)
        {
            return;
        }
        _metricSafetyResets++;
        bool badBone = IsNan(flesh.Bone.localPosition) || IsBadQuat(flesh.Bone.localRotation);
        if (badBone)
        {
            flesh.Bone.localPosition = IsNan(flesh.PristineLocal) ? Vector3.zero : flesh.PristineLocal;
            flesh.Bone.localRotation = IsBadQuat(flesh.PristineRot) ? Quaternion.identity : flesh.PristineRot;
        }
        if (IsNan(flesh.Bone.localScale) || flesh.Bone.localScale.sqrMagnitude < 1e-8f)
        {
            Vector3 pristineScale = IsNan(flesh.PristineScale) ? Vector3.one : flesh.PristineScale;
            flesh.Bone.localScale = pristineScale.sqrMagnitude < 1e-8f ? Vector3.one : pristineScale;
        }
        FleshStateReset.Spring(flesh, flesh.Bone.localPosition);
    }

    private void ResetChainParticle(ChainParticle particle)
    {
        if (particle == null || particle.Bone == null)
        {
            return;
        }
        _metricSafetyResets++;
        if (IsNan(particle.BaseLocal))
        {
            particle.BaseLocal = Vector3.zero;
        }
        if (IsBadQuat(particle.BaseRotLocal))
        {
            particle.BaseRotLocal = Quaternion.identity;
        }
        if (IsNan(particle.Bone.localPosition) || IsBadQuat(particle.Bone.localRotation))
        {
            particle.Bone.localPosition = particle.BaseLocal;
            particle.Bone.localRotation = particle.BaseRotLocal;
        }
        if (IsNan(particle.Bone.localScale) || particle.Bone.localScale.sqrMagnitude < 1e-8f)
        {
            particle.Bone.localScale = Vector3.one;
        }
        FleshStateReset.Chain(particle);
    }

    private FleshBone AddBone(string name, int boneIndex)
    {
        Transform bone = FindBone(name);
        if (bone == null)
        {
            return null;
        }
        FleshBone flesh = new FleshBone();
        flesh.Bone = bone;
        flesh.Parent = bone.parent;
        flesh.PristineLocal = bone.localPosition;
        flesh.PristineRot = bone.localRotation;
        flesh.AnimatedRotLocal = bone.localRotation;
        flesh.LastSetRotLocal = bone.localRotation;
        flesh.PristineScale = bone.localScale;
        flesh.LastSetRot = bone.localRotation;
        flesh.BaseLocal = bone.localPosition;
        flesh.PrevBaseWorld = bone.position;
        flesh.PrevBaseWorld2 = bone.position;
        flesh.Position = bone.position;
        flesh.PrevPosition = bone.position;
        flesh.BoneIndex = boneIndex;
        flesh.PrevParentRot = bone.parent == null ? Quaternion.identity : bone.parent.rotation;
        _bones.Add(flesh);
        return flesh;
    }

    private Transform FindBone(string name)
    {
        if (ChaControlRef == null)
        {
            return null;
        }
        SkinnedMeshRenderer[] renderers = ChaControlRef.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Transform fallbackActive = null;
        Transform fallbackAny = null;
        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }
            bool active = renderer.gameObject.activeInHierarchy;
            Transform root = renderer.transform;
            while (root.parent != null)
            {
                root = root.parent;
            }
            bool bodyMesh = root.name == "p_cf_body_00";
            Transform[] bones = renderer.bones;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null || bones[i].name != name)
                {
                    continue;
                }
                if (active && bodyMesh)
                {
                    return bones[i];
                }
                if (active && fallbackActive == null)
                {
                    fallbackActive = bones[i];
                }
                if (fallbackAny == null)
                {
                    fallbackAny = bones[i];
                }
            }
        }
        if (fallbackActive != null)
        {
            return fallbackActive;
        }
        if (fallbackAny != null)
        {
            return fallbackAny;
        }
        Transform[] transforms = ChaControlRef.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == name && transforms[i].gameObject.activeInHierarchy)
            {
                return transforms[i];
            }
        }
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == name)
            {
                return transforms[i];
            }
        }
        return null;
    }

    private void CheckBones()
    {
        if (_bones.Count == 0)
        {
            if (ChaControlRef != null && ParamsRef != null)
            {
                _retryTimer += Time.deltaTime;
                if (_retryTimer >= 0.5f)
                {
                    _retryTimer = 0f;
                    Initialize(ChaControlRef, ParamsRef, _partId);
                }
            }
            return;
        }
        bool missing = false;
        for (int i = 0; i < _bones.Count; i++)
        {
            if (_bones[i].Bone == null || !_bones[i].Bone.gameObject.activeInHierarchy)
            {
                missing = true;
                break;
            }
        }
        if (missing && ChaControlRef != null && ParamsRef != null)
        {
            _retryTimer += Time.deltaTime;
            if (_retryTimer >= 0.5f)
            {
                _retryTimer = 0f;
                Initialize(ChaControlRef, ParamsRef, _partId);
            }
        }
    }

    private void LateUpdate()
    {
        CheckBones();
        if (!_runtimeStatusLogged && ParamsRef != null)
        {
            _runtimeStatusLogged = true;
            ThighPhysicsControllerPlugin.LogRuntime("SOMA_RUNTIME_STATUS part=" + PartId +
                " bones=" + _bones.Count +
                " enabled=" + ParamsRef.Enabled +
                " solver=" + (ParamsRef.GamePhysics ? "Chain" : "Spring") +
                " collectMetrics=" + ThighPhysicsControllerPlugin.DebugCollectMetrics.Value);
        }
        if (_bones.Count == 0 || ParamsRef == null)
        {
            return;
        }
        if (!ParamsRef.Enabled)
        {
            // Restore the pose when the part is turned off; otherwise the last
            // deformation stays frozen on the bones and Clear shape cannot fix it.
            ClearDeformation();
            return;
        }
        bool collectMetrics = ThighPhysicsControllerPlugin.DebugCollectMetrics.Value;
        _collectMetricsThisFrame = collectMetrics;
        if (!collectMetrics && _metricsEnabledLastFrame)
        {
            ResetMetricWindow();
            ResetPerformanceWindow();
        }
        _metricsEnabledLastFrame = collectMetrics;
        bool timelineSpringFallback = FleshSolverMath.ShouldUseTimelineSpringFallback(
            ThighPhysicsControllerPlugin.TimelinePlaybackSpringFallback.Value,
            ParamsRef.GamePhysics, TimelineConstraintBridge.IsTimelinePlaying());
        if (timelineSpringFallback && ThighPhysicsControllerPlugin.TimelineSpringFallbackAuto.Value)
        {
            // Auto mode: apply the fallback only to characters actually driven by
            // Timeline (NodesConstraints). The bridge caches this per character/frame.
            Transform characterRoot = ChaControlRef == null ? null : ChaControlRef.transform;
            timelineSpringFallback = TimelineConstraintBridge.ShouldYieldChainRotations(characterRoot);
        }
        if (timelineSpringFallback != _timelineSpringFallbackLastFrame)
        {
            _timelineSpringFallbackLastFrame = timelineSpringFallback;
            if (timelineSpringFallback)
            {
                // Timeline authors can drive limbs through GuideObject, IK, NodesConstraints
                // or custom interpolables. Their transforms are not reliably descendants
                // of ChaControl, so do not run the hierarchy-sensitive Chain solver while
                // Timeline owns the pose. Spring is independent and remains physically live.
                RemoveOwnedChainDeformation();
                _chainPoseSettleActive = false;
                _shapeRebasePending = false;
                ResetState();
            }
            else if (ParamsRef.GamePhysics)
            {
                BeginChainModeCapture(2);
            }
            UnityEngine.Debug.Log("SOMA_TIMELINE_SAFE part=" + PartId +
                " action=" + (timelineSpringFallback
                    ? "chain_to_spring"
                    : "spring_to_chain"));
        }
        if (UpdateExternalPoseSettle())
        {
            return;
        }
        if (ParamsRef.GamePhysics != _lastGamePhysics)
        {
            _lastGamePhysics = ParamsRef.GamePhysics;
            if (_lastGamePhysics)
            {
                BeginChainModeCapture(2);
            }
            else
            {
                _chainPoseSettleActive = false;
                _shapeRebasePending = false;
                RemoveOwnedChainDeformation();
                ResetState();
            }
        }
        if (ParamsRef.GamePhysics && !timelineSpringFallback)
        {
            long allocatedBefore = collectMetrics ? ReadAllocatedBytes() : -1L;
            long started = collectMetrics ? Stopwatch.GetTimestamp() : 0L;
            UpdateChainPhysics();
            if (collectMetrics)
            {
                RecordSolverDuration(started, allocatedBefore, "Chain");
                FlushMetrics(Mathf.Min(Time.deltaTime, 0.05f), "Chain");
            }
            return;
        }
        long springAllocatedBefore = collectMetrics ? ReadAllocatedBytes() : -1L;
        long springStarted = collectMetrics ? Stopwatch.GetTimestamp() : 0L;
        UpdateSpringPhysics();
        if (collectMetrics)
        {
            string springMode = timelineSpringFallback ? "Spring(TimelineSafe)" : "Spring";
            RecordSolverDuration(springStarted, springAllocatedBefore, springMode);
            FlushMetrics(Mathf.Min(Time.deltaTime, 0.05f), springMode);
        }
    }

    /// <summary>
    private void UpdateSpringPhysics()
    {
        ThighBoneParams shared = ParamsRef.Thigh00;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (dt <= 0f)
        {
            return;
        }
        _time += dt;
        float damping = shared.Damping;
        float elasticity = shared.Elasticity;
        float inert = shared.Inert;
        float stiffness = shared.Stiffness;
        float weight = ParamsRef.Weight;
        float step = dt * 60f;
        float smoothing = FleshSolverMath.AdjustPerFrameRate(
            FleshValue.Clamp(ParamsRef.MotionSmooth, 0.05f,
                FleshParameterRanges.MotionSmoothMax, 0.25f), dt);
        float maxOffset = 0.009f * (0.4f + weight);
        bool broken = false;

        for (int i = 0; i < _bones.Count; i++)
        {
            FleshBone flesh = _bones[i];
            flesh.PhysicsActive = false;
            try
            {
                if (flesh.Bone == null)
                {
                    broken = true;
                    continue;
                }
                Transform parent = flesh.Bone.parent;
                if (parent == null)
                {
                    continue;
                }
                Vector3 baseWorld = parent.TransformPoint(flesh.BaseLocal);
                if (IsNan(baseWorld) || IsNan(flesh.Offset) || IsNan(flesh.Position) ||
                    IsNan(flesh.AccelSmoothed) || IsNan(flesh.RotSmoothed) ||
                    IsNan(flesh.RotTarget) || IsNan(flesh.AngularVelBody) ||
                    IsNan(flesh.PrevBaseWorld) || IsNan(flesh.PrevBaseWorld2) ||
                    IsNan(flesh.PrevOffset) || IsNan(flesh.LastSag))
                {
                    ResetFleshBoneState(flesh);
                    continue;
                }
                if (flesh.Parent != parent)
                {
                    flesh.Parent = parent;
                    flesh.Bone.localPosition = flesh.PristineLocal;
                    flesh.Bone.localRotation = flesh.PristineRot;
                    flesh.AnimatedRotLocal = flesh.PristineRot;
                    flesh.LastSetRotLocal = flesh.PristineRot;
                    FleshStateReset.Spring(flesh, flesh.PristineLocal);
                    continue;
                }
                // Local-space drift check (same as chain mode): parent rotation must
                // not count as external movement, otherwise dancing re-anchors bake
                // our own offset into the base and the thighs deform progressively.
                Vector3 expectedLocal = flesh.BaseLocal + flesh.LastAppliedLocal;
                if ((flesh.Bone.localPosition - expectedLocal).magnitude > 0.005f)
                {
                    _metricReanchors++;
                    // The game or another plugin moved this bone: re-anchor and clear
                    // state. Adopt the incoming rotation instead of snapping it back
                    // to the card pose, otherwise H animations and body refreshes
                    // fight the solver and leave the limb twisted.
                    Vector3 reanchorLocal = flesh.Bone.localPosition;
                    flesh.LastReanchorTime = _time;
                    flesh.AnimatedRotLocal = flesh.Bone.localRotation;
                    flesh.LastSetRotLocal = flesh.AnimatedRotLocal;
                    FleshStateReset.Spring(flesh, reanchorLocal);
                    continue;
                }
                Vector3 baseDelta = baseWorld - flesh.PrevBaseWorld;
                if (baseDelta.magnitude > 0.15f)
                {
                    _metricReanchors++;
                    // Large teleport / scene switch: re-anchor without fighting the game.
                    flesh.Bone.localPosition = flesh.BaseLocal;
                    flesh.AnimatedRotLocal = flesh.Bone.localRotation;
                    flesh.LastSetRotLocal = flesh.AnimatedRotLocal;
                    FleshStateReset.Spring(flesh, flesh.BaseLocal);
                    flesh.LastReanchorTime = _time;
                    continue;
                }
                Vector3 accel = (baseWorld - 2f * flesh.PrevBaseWorld + flesh.PrevBaseWorld2) / (dt * dt);
                flesh.PrevBaseWorld2 = flesh.PrevBaseWorld;
                flesh.PrevBaseWorld = baseWorld;
                flesh.AccelSmoothed = Vector3.Lerp(
                    flesh.AccelSmoothed, accel,
                    smoothing);

                Transform character = ChaControlRef == null ? null : ChaControlRef.transform;
                if (character == null)
                {
                    continue;
                }
                float amp = ParamsRef.Bones.GetAmp(flesh.BoneIndex);
                if (amp <= 0.0001f)
                {
                    flesh.Bone.localPosition = flesh.PristineLocal;
                    flesh.Bone.localRotation = flesh.PristineRot;
                    FleshStateReset.Spring(flesh, flesh.PristineLocal);
                    continue;
                }
                flesh.PhysicsActive = true;

                // Background drift correction (spring only): if the base got baked
                // away from the card pose by repeated re-anchors, ease it back slowly.
                // Normal jiggle never triggers this (base drift stays ~0), and it
                // pauses for 1s after any external re-anchor so it never fights the
                // game or other plugins.
                if (ThighPhysicsControllerPlugin.AutoFixSpringDrift.Value)
                {
                    float baseDrift = (flesh.BaseLocal - flesh.PristineLocal).magnitude;
                    if (baseDrift > 0.005f && _time - flesh.LastReanchorTime > 1f)
                    {
                        flesh.DriftWatchTime += dt;
                        if (flesh.DriftWatchTime > 2f)
                        {
                            flesh.BaseLocal = Vector3.MoveTowards(
                                flesh.BaseLocal, flesh.PristineLocal, 0.0005f);
                        }
                    }
                    else
                    {
                        flesh.DriftWatchTime = 0f;
                    }
                }

                // Parent joint angular velocity (dance response): rotation deltas of the
                // parent transform produce a tangential lag drive.
                Quaternion prevParentRot = flesh.PrevParentRot;
                flesh.PrevParentRot = parent.rotation;
                Quaternion parentDelta = parent.rotation * Quaternion.Inverse(prevParentRot);
                float angle;
                Vector3 axis;
                parentDelta.ToAngleAxis(out angle, out axis);
                Vector3 angularVel = character.InverseTransformDirection(axis * angle) /
                                     Mathf.Max(0.25f, step);
                Vector3 angularAccel = angularVel - flesh.AngularVelBody;
                flesh.AngularVelBody = Vector3.Lerp(
                    flesh.AngularVelBody, angularVel,
                    smoothing);

                float motionGain = ParamsRef.MotionGain;
                // Unified with chain mode: gain=1 at default Weight/Inert is 1.0x,
                // 2=2x, 3=3x; Weight/Inert scale it identically in both modes.
                float danceFactor = FleshSolverMath.DanceResponseScale(motionGain, weight, inert);
                float springDrive = danceFactor * FleshSpringSolver.PartDriveScale(_partId);
                Vector3 vel = character.InverseTransformDirection(baseDelta);
                Vector3 gravityLocal = character.InverseTransformDirection(
                    new Vector3(0f, -ParamsRef.Gravity * 0.009f * amp, 0f));
                Vector3 accelLocal = character.InverseTransformDirection(flesh.AccelSmoothed);
                vel.x *= 1.25f;
                vel.z *= 1.25f;
                accelLocal.x *= 1.25f;
                accelLocal.z *= 1.25f;
                Vector3 drive = Vector3.ClampMagnitude(accelLocal, 40f) *
                                (0.00025f * amp * springDrive * step);
                Vector3 lever = flesh.Bone.position - parent.position;
                Vector3 tangential = Vector3.Cross(axis, lever);
                if (tangential.sqrMagnitude > 0.0001f)
                {
                    drive -= character.InverseTransformDirection(tangential) *
                              (angularAccel.magnitude * 0.035f * amp * springDrive);
                }
                vel *= amp * springDrive;

                float rotAmp = ParamsRef.Bones.GetRotAmp(flesh.BoneIndex);
                if (rotAmp > 0.0001f)
                {
                    float maxRot = 20f * rotAmp;
                    flesh.RotTarget = new Vector3(
                        Mathf.Clamp(accelLocal.z * 1.0f * springDrive, -maxRot, maxRot),
                        0f,
                        Mathf.Clamp(accelLocal.x * 1.0f * springDrive, -maxRot, maxRot));
                }
                else
                {
                    flesh.RotTarget = Vector3.zero;
                }

                Vector3 sag = gravityLocal / Mathf.Max(elasticity, 0.005f);
                if ((flesh.LastSag - sag).magnitude > 0.0015f)
                {
                    flesh.Offset = sag;
                    flesh.PrevOffset = sag;
                }
                flesh.LastSag = sag;
                float previousDt = FleshValue.Clamp(flesh.PreviousDt,
                    1f / 240f, 0.05f, 1f / 60f);
                float jitter = FleshValue.Clamp(ParamsRef.JitterFreq, 0f,
                    FleshParameterRanges.JitterFrequencyMax, 1f);
                // Integrate the visible motion around the gravity equilibrium.
                // Mixing gravity into Offset and then subtracting a separately
                // calculated sag at write time creates two different equilibria;
                // the mismatch can pin tight presets to the displacement limit.
                Vector3 dynamicOffset = flesh.Offset - sag;
                Vector3 previousDynamicOffset = flesh.PrevOffset - sag;
                Vector3 springVel = (dynamicOffset - previousDynamicOffset) *
                                    (dt / previousDt) * jitter;
                float velocityRetention = FleshSpringSolver.VelocityRetention(damping, dt);
                flesh.PrevOffset = sag + dynamicOffset + vel * inert;
                dynamicOffset += springVel * velocityRetention + vel * inert + drive;
                float returnStrength = FleshSolverMath.AdjustPerFrameRate(
                    Mathf.Clamp01(elasticity * jitter), dt);
                dynamicOffset += (Vector3.zero - dynamicOffset) * returnStrength;
                flesh.PreviousDt = dt;

                float limit = maxOffset * amp * (1f - stiffness * 0.4f);
                Vector3 offset = dynamicOffset;
                Vector3 axisMask = ParamsRef.Bones.GetAxis(flesh.BoneIndex);
                offset = Vector3.Scale(offset, axisMask);
                if (offset.magnitude > limit)
                {
                    offset = offset.normalized * limit;
                }
                flesh.Offset = sag + offset;

                if (offset.magnitude > FleshSpringSolver.ActivationThreshold(amp))
                {
                    Vector3 world = character.TransformDirection(offset);
                    Vector3 targetWorld = baseWorld + world;
                    flesh.Bone.localPosition = parent.InverseTransformPoint(targetWorld);
                    if (IsNan(flesh.Bone.localPosition))
                    {
                        ResetFleshBoneState(flesh);
                        continue;
                    }
                    flesh.PrevPosition = flesh.Position;
                    flesh.Position = targetWorld;
                    flesh.LastApplied = offset;
                    flesh.LastWorldApply = world;
                    flesh.LastAppliedLocal = flesh.Bone.localPosition - flesh.BaseLocal;
                }
                else
                {
                    flesh.Offset = sag;
                    flesh.PrevOffset = sag;
                    flesh.BaseLocal = flesh.Bone.localPosition;
                    flesh.PrevBaseWorld = flesh.Bone.position;
                    flesh.PrevBaseWorld2 = flesh.Bone.position;
                    flesh.LastApplied = Vector3.zero;
                    flesh.LastWorldApply = Vector3.zero;
                    flesh.LastAppliedLocal = Vector3.zero;
                    flesh.PrevPosition = flesh.Position = flesh.Bone.position;
                }
                if (_collectMetricsThisFrame)
                    RecordMetric(offset);
            }
            catch (Exception)
            {
                broken = true;
            }
        }

        if (broken)
        {
            _bones.Clear();
            _retryTimer = 0f;
            return;
        }

        for (int i = 0; i < _bones.Count; i++)
        {
            FleshBone flesh = _bones[i];
            if (flesh == null || flesh.Bone == null || !flesh.PhysicsActive)
            {
                continue;
            }
            bool activelyRotating = ParamsRef.Bones.GetRotCalc(flesh.BoneIndex) ||
                                    ParamsRef.Bones.GetRotAmp(flesh.BoneIndex) > 0.0001f;
            // Rotation ownership: when the animation or another plugin rotates the
            // bone away from Soma's last write, adopt that rotation as the animated
            // base instead of snapping it back to the card pose. Free-H animations
            // and BPC/body refreshes otherwise fight the solver and twist the limb.
            if (Quaternion.Angle(flesh.Bone.localRotation, flesh.LastSetRotLocal) > 2f)
            {
                flesh.AnimatedRotLocal = flesh.Bone.localRotation;
                flesh.LastSetRotLocal = flesh.AnimatedRotLocal;
                flesh.LastSetRot = flesh.Bone.localRotation;
            }
            if (ThighPhysicsControllerPlugin.DebugLogFlesh.Value &&
                activelyRotating &&
                Quaternion.Angle(flesh.Bone.localRotation, flesh.LastSetRotLocal) > 2f &&
                _time - flesh.LastOverwriteLogTime >= 2f)
            {
                flesh.LastOverwriteLogTime = _time;
                UnityEngine.Debug.Log("Flesh physics [" + flesh.Bone.name + "]: rotation overwritten by game, diff=" +
                          Quaternion.Angle(flesh.Bone.localRotation, flesh.LastSetRotLocal).ToString("F1"));
            }
            if (ParamsRef.Bones.GetRotCalc(flesh.BoneIndex) && flesh.AimChild != null)
            {
                // Base-frame aim (same as chain mode): never multiply onto the current
                // rotation, otherwise the child's own offset feeds back and the thigh
                // twists further with every dance beat. Clamp like chain (±12 deg).
                Transform rcParent = flesh.Bone.parent;
                Quaternion baseWorldRot = rcParent == null
                    ? flesh.PristineRot
                    : rcParent.rotation * flesh.PristineRot;
                Vector3 restDir = baseWorldRot * flesh.RestDirLocal;
                Vector3 aimDir = flesh.AimChild.position - flesh.Bone.position;
                if (restDir.sqrMagnitude > 0.0001f && aimDir.sqrMagnitude > 0.0001f)
                {
                    Quaternion align = Quaternion.FromToRotation(restDir, aimDir);
                    float rcAngle;
                    Vector3 rcAxis;
                    align.ToAngleAxis(out rcAngle, out rcAxis);
                    rcAngle = Mathf.Clamp(rcAngle * 0.5f, -12f, 12f);
                    flesh.Bone.rotation = Quaternion.AngleAxis(rcAngle, rcAxis) * baseWorldRot;
                    if (IsBadQuat(flesh.Bone.rotation))
                    {
                        ResetFleshBoneState(flesh);
                        continue;
                    }
                }
                flesh.LastSetRot = flesh.Bone.rotation;
                flesh.LastSetRotLocal = flesh.Bone.localRotation;
                continue;
            }
            float rotAmp = ParamsRef.Bones.GetRotAmp(flesh.BoneIndex);
            if (rotAmp > 0.0001f)
            {
                float rotationSmoothing = FleshSolverMath.AdjustPerFrameRate(0.25f, dt);
                flesh.RotSmoothed = Vector3.Lerp(
                    flesh.RotSmoothed, flesh.RotTarget, rotationSmoothing);
                Vector3 rotEuler = flesh.RotSmoothed;
                rotEuler.x = Mathf.Clamp(rotEuler.x, -30f, 30f);
                rotEuler.y = Mathf.Clamp(rotEuler.y, -30f, 30f);
                rotEuler.z = Mathf.Clamp(rotEuler.z, -30f, 30f);
                flesh.Bone.localRotation = flesh.AnimatedRotLocal * Quaternion.Euler(rotEuler);
                if (IsBadQuat(flesh.Bone.localRotation))
                {
                    ResetFleshBoneState(flesh);
                    continue;
                }
                flesh.LastSetRot = flesh.Bone.localRotation;
                flesh.LastSetRotLocal = flesh.Bone.localRotation;
            }
            else
            {
                flesh.RotSmoothed = Vector3.zero;
                flesh.Bone.localRotation = flesh.AnimatedRotLocal;
                flesh.LastSetRot = flesh.AnimatedRotLocal;
                flesh.LastSetRotLocal = flesh.AnimatedRotLocal;
            }
        }

        if (ThighPhysicsControllerPlugin.DebugLogFlesh.Value && _time - _lastLogTime > 2f)
        {
            _lastLogTime = _time;
            for (int i = 0; i < _bones.Count; i++)
            {
                FleshBone flesh = _bones[i];
                UnityEngine.Debug.Log("Flesh physics [" + flesh.Bone.name + "]: applied=" +
                          flesh.LastApplied.ToString("F5") + " mag=" +
                          flesh.LastApplied.magnitude.ToString("F5") + " rot=" +
                          flesh.RotSmoothed.ToString("F2"));
            }
        }
    }

    private void UpdateChainPhysics()
    {
        if (!_chainsBuilt || _chains.Count == 0)
        {
            BuildChains();
        }
        Transform character = ChaControlRef == null ? null : ChaControlRef.transform;
        if (character == null || _chains.Count == 0)
        {
            return;
        }
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        _chainTime += dt;
        ChainParams chainParams = ParamsRef.Chain;
        float weight = chainParams.Weight;
        float motionGain = ParamsRef.MotionGain;
        float motionTarget = FleshTuning.GetMotionTarget(ParamsRef);
        float motionFollow = FleshSolverMath.MotionFollowFraction(motionTarget);
        float targetRangeScale = FleshSolverMath.TargetRangeScale(weight, motionTarget);
        float solverDt = FleshChainSolver.NormalizeTimeStep(dt);
        float velocityRetention = FleshChainSolver.ComputeVelocityRetention(chainParams,
            solverDt);
        float segmentAxialStrength = FleshChainSolver.ComputeSegmentAxialStrength(
            chainParams, solverDt);
        float segmentLateralStrength = FleshChainSolver.ComputeSegmentLateralStrength(
            chainParams, solverDt);
        float segmentStiffness = Mathf.Clamp01(chainParams.Stiffness);
        float singleParticleReturnStrength = FleshChainSolver.ComputeSingleParticleReturnStrength(
            chainParams, solverDt);
        Vector3 gravity = new Vector3(0f, -chainParams.Gravity * 0.016f, 0f);
        bool logChain = ThighPhysicsControllerPlugin.DebugLogFlesh.Value &&
                        _chainTime - _chainLogTime >= 2f;
        bool yieldConstraintRotations =
            TimelineConstraintBridge.ShouldYieldChainRotations(character);
        if (yieldConstraintRotations != _constraintSafeRotationsLastFrame)
        {
            _constraintSafeRotationsLastFrame = yieldConstraintRotations;
            UnityEngine.Debug.Log("SOMA_CHAIN_CONSTRAINT_SAFE part=" + PartId +
                " action=" + (yieldConstraintRotations
                    ? "yield_rotation_keep_position"
                    : "resume_rotation"));
        }

        for (int i = 0; i < _chains.Count; i++)
        {
            SideChain chain = _chains[i];
            if (chain.Anchor == null || chain.Particles.Count < 1)
            {
                continue;
            }
            ChainParticle first = chain.Particles[0];
            if (first.Bone == null || first.ParentBone == null)
            {
                BuildChains();
                break;
            }
            // Timeline can directly key flesh bones every frame. First remove Soma's
            // previous output and adopt the current Timeline pose as the clean base.
            // Normal keyframe motion continues through the solver so Chain remains
            // visible; only a true pose cut/teleport clears the whole chain for one
            // frame. This avoids both feedback creep and permanent Timeline lockout.
            if (PrepareCleanChainBaseFrame(chain))
            {
                ReanchorWholeChain(chain);
                continue;
            }
            if (yieldConstraintRotations)
            {
                ClearChainRotationOutput(chain);
            }
            Vector3 anchorPos = chain.Anchor.position;
            Quaternion anchorRot = chain.Anchor.rotation;
            Vector3 rawAnchorMove = anchorPos - chain.PrevAnchorPos;
            float rawAnchorAngle = Quaternion.Angle(anchorRot, chain.PrevAnchorRot);
            // Moving the whole character through Timeline leaves every flesh bone's
            // local pose unchanged, so the local jump check above cannot see it. Do
            // not feed a root teleport into Verlet inertia: rebase for this frame and
            // resume normal Chain physics on the next one.
            if (FleshSolverMath.IsChainAnchorTeleport(rawAnchorMove.magnitude,
                rawAnchorAngle))
            {
                if (ThighPhysicsControllerPlugin.DebugLogFlesh.Value &&
                    _chainTime - _chainReanchorLogTime >= 2f)
                {
                    _chainReanchorLogTime = _chainTime;
                    UnityEngine.Debug.Log("SOMA_CHAIN_TELEPORT part=" + PartId +
                        " anchor=" + chain.Anchor.name +
                        " distance=" + rawAnchorMove.magnitude.ToString("F4") +
                        " angle=" + rawAnchorAngle.ToString("F2") +
                        " action=reanchor");
                }
                ReanchorWholeChain(chain);
                continue;
            }
            Vector3 anchorMove = rawAnchorMove;
            anchorMove = Vector3.ClampMagnitude(anchorMove, 0.30f);
            chain.PrevAnchorPos = anchorPos;
            // Angular velocity of the anchor (dance response): leg rotations drive
            // the chain with a tangential lag, like the spring mode's joint velocity.
            Quaternion prevAnchorRot = chain.PrevAnchorRot;
            chain.PrevAnchorRot = anchorRot;
            Quaternion anchorRotDelta = anchorRot * Quaternion.Inverse(prevAnchorRot);
            float anchorAngle;
            Vector3 anchorAxis;
            anchorRotDelta.ToAngleAxis(out anchorAngle, out anchorAxis);
            anchorAngle = FleshSolverMath.NormalizeSignedAngle(anchorAngle);
            Vector3 rawAnchorAngVel = anchorAxis * anchorAngle;
            if (IsNan(anchorPos) || IsNan(anchorMove) || IsNan(rawAnchorAngVel))
            {
                chain.PrevAnchorPos = chain.Anchor.position;
                chain.PrevAnchorRot = chain.Anchor.rotation;
                ResetChainInputHistory(chain);
                continue;
            }
            // MMD Director advances from a realtime timer while this solver runs once
            // per rendered frame. Normalize deltas to a 60 FPS reference. At high FPS
            // only, a three-sample median rejects isolated timer spikes without the
            // continuous low-pass attenuation that used to erase fast dance motion.
            float inputStep = Mathf.Clamp(dt * 60f, 0.25f, 3f);
            Vector3 moveAt60 = anchorMove / inputStep;
            Vector3 angularAt60 = rawAnchorAngVel / inputStep;
            Vector3 guardedMoveAt60 = moveAt60;
            Vector3 guardedAngularAt60 = angularAt60;
            if (inputStep < 0.75f && chain.AnchorInputSampleCount >= 2)
            {
                guardedMoveAt60 = FleshSolverMath.Median3(chain.PreviousAnchorMoveAt60,
                    chain.AnchorMoveAt60, moveAt60);
                guardedAngularAt60 = FleshSolverMath.Median3(
                    chain.PreviousAnchorAngularAt60, chain.AnchorAngularAt60, angularAt60);
            }
            chain.PreviousAnchorMoveAt60 = chain.AnchorMoveAt60;
            chain.PreviousAnchorAngularAt60 = chain.AnchorAngularAt60;
            chain.AnchorMoveAt60 = moveAt60;
            chain.AnchorAngularAt60 = angularAt60;
            if (chain.AnchorInputSampleCount < 2)
                chain.AnchorInputSampleCount++;
            anchorMove = guardedMoveAt60 * inputStep;
            Vector3 anchorAngVel = guardedAngularAt60 * inputStep;
            if (chain.Particles.Count == 1)
            {
                UpdateSingleParticleChain(chain, anchorPos, anchorMove, anchorAngVel,
                    gravity, weight, motionFollow, targetRangeScale, chainParams, character,
                    solverDt, velocityRetention, singleParticleReturnStrength);
                continue;
            }
            first.PrevPosition = first.Position;
            first.Position = first.Bone.position;
            first.BaseLocal = first.Bone.localPosition;
            first.PrevAnimatedLocal = first.Bone.localPosition;

            for (int j = 1; j < chain.Particles.Count; j++)
            {
                ChainParticle particle = chain.Particles[j];
                ChainParticle prev = chain.Particles[j - 1];
                if (particle.Bone == null || prev.Bone == null)
                {
                    continue;
                }
                Transform parent = particle.Bone.parent;
                Vector3 worldBase = parent == null
                    ? particle.Bone.position
                    : parent.TransformPoint(particle.BaseLocal);
                if (IsNan(particle.Position) || IsNan(particle.PrevPosition) || IsNan(worldBase) ||
                    IsNan(particle.Bone.localPosition) || IsBadQuat(particle.Bone.localRotation))
                {
                    ResetChainParticle(particle);
                    worldBase = parent == null
                        ? particle.Bone.position
                        : parent.TransformPoint(particle.BaseLocal);
                    particle.Position = particle.PrevPosition = worldBase;
                }

                // BaseLocal is the skeleton pose WITHOUT our own offset, stored in the
                // parent's local space. External-reset detection MUST happen in local
                // space too: in world space a parent rotation makes our own rotated
                // offset look like external motion and bakes deformation into the base.
                if ((particle.Bone.localPosition -
                     (particle.BaseLocal + particle.LastAppliedLocal)).magnitude > 0.005f)
                {
                    _metricReanchors++;
                    worldBase = FleshStateReset.ReanchorChain(particle);
                }
                if (particle.Bone.parent != particle.ParentBone)
                {
                    worldBase = FleshStateReset.ReanchorChain(particle);
                    chain.PrevAnchorPos = anchorPos;
                    ResetChainInputHistory(chain);
                }
                float externalDrift = (particle.Bone.localPosition - particle.PrevAnimatedLocal).magnitude;
                particle.PrevAnimatedLocal = particle.Bone.localPosition;
                if (externalDrift > 0.6f)
                {
                    _metricReanchors++;
                    if (ThighPhysicsControllerPlugin.DebugLogFlesh.Value &&
                        _chainTime - _chainReanchorLogTime >= 2f)
                    {
                        _chainReanchorLogTime = _chainTime;
                        UnityEngine.Debug.Log("Flesh physics [" + particle.Bone.name +
                            "]: chain re-anchored (teleport, drift=" +
                            externalDrift.ToString("F4") + ")");
                    }
                    worldBase = FleshStateReset.ReanchorChain(particle);
                    chain.PrevAnchorPos = anchorPos;
                    ResetChainInputHistory(chain);
                }
                float amp = ParamsRef.ChainBones.GetAmp(particle.BoneIndex);
                if (amp <= 0.0001f)
                {
                    particle.PrevPosition = particle.Position = worldBase;
                    continue;
                }
                // Rest geometry from the skeleton-anchored base (ABMX-friendly).
                Vector3 prevWorldBase = prev.Bone.parent == null
                    ? prev.Bone.position
                    : prev.Bone.parent.TransformPoint(prev.BaseLocal);
                Vector3 restNow = worldBase - prevWorldBase;
                particle.RestLength = restNow.magnitude;
                // Compute the rest direction in the PREV bone's BASE frame (not its
                // current, possibly RC-rotated frame), otherwise RC creates a feedback
                // loop: rotating the bone changes the rest direction and spins further.
                Transform prevParent = prev.Bone.parent;
                Quaternion prevBaseWorldRot = prevParent == null
                    ? prev.BaseRotLocal
                    : prevParent.rotation * prev.BaseRotLocal;
                particle.RestDirLocal = Quaternion.Inverse(prevBaseWorldRot) * restNow;
                // Semi-implicit Euler: anchor motion and gravity feed the particle's
                // VELOCITY (inertia), so the flesh lags continuously instead of being
                // dragged rigidly by the anchor.
                FleshChainSolver.Integrate(particle, anchorPos, anchorMove, anchorAngVel,
                    gravity, weight, motionFollow, solverDt, velocityRetention);
                // Anisotropic spring: hard along the bone axis (no stretching), soft
                // perpendicular to it (flesh sway). This is what makes it look like
                // fat swinging instead of a rubber chain wriggling.
                FleshChainSolver.ApplySegmentReturn(particle, prev, restNow,
                    segmentAxialStrength, segmentLateralStrength, segmentStiffness);
                // Leash: keep the particle near its skeleton-anchored base, scaled by amp
                // so the amplitude slider stays meaningful instead of being saturated.
                float distal = particle.BoneIndex == _distalIndex ? 0.6f : 1f;
                float leashLimit = (0.03f + 0.012f * amp) * distal * targetRangeScale;
                FleshChainSolver.ApplyLeash(particle, worldBase, leashLimit);
            }

            for (int j = 1; j < chain.Particles.Count; j++)
            {
                ChainParticle particle = chain.Particles[j];
                ChainParticle prev = chain.Particles[j - 1];
                if (particle.Bone == null || prev.Bone == null)
                {
                    continue;
                }
                string rcRotText = "";
                if (!yieldConstraintRotations && prev.BoneIndex >= 0 &&
                    ParamsRef.ChainBones.GetRotCalc(prev.BoneIndex) &&
                    ParamsRef.ChainBones.GetAmp(prev.BoneIndex) > 0.0001f)
                {
                    // BPC-style aim constraint: rotate the bone from its BASE rotation
                    // toward the next particle. Never multiply onto the current rotation,
                    // otherwise the angle accumulates and the leg deforms.
                    Transform prevParent = prev.Bone.parent;
                    Vector3 prevWorldBase = prevParent == null
                        ? prev.Bone.position
                        : prevParent.TransformPoint(prev.BaseLocal);
                    Quaternion baseWorldRot = prevParent == null
                        ? prev.BaseRotLocal
                        : prevParent.rotation * prev.BaseRotLocal;
                    Vector3 restDir = baseWorldRot * particle.RestDirLocal;
                    Vector3 aimDir = particle.Position - prevWorldBase;
                    if (restDir.sqrMagnitude > 0.0001f && aimDir.sqrMagnitude > 0.0001f)
                    {
                        Quaternion align = Quaternion.FromToRotation(restDir, aimDir);
                        float rcAngle;
                        Vector3 rcAxis;
                        align.ToAngleAxis(out rcAngle, out rcAxis);
                        // Real flesh twists a few degrees, never a full swing.
                        rcAngle = Mathf.Clamp(rcAngle * 0.5f, -12f, 12f);
                        Quaternion limited = Quaternion.AngleAxis(rcAngle, rcAxis);
                        prev.Bone.rotation = limited * baseWorldRot;
                        if (IsBadQuat(prev.Bone.rotation))
                        {
                            prev.Bone.localRotation = prev.BaseRotLocal;
                            prev.LastAppliedRotLocal = Quaternion.identity;
                        }
                        else
                        {
                            prev.LastAppliedRotLocal = Quaternion.Inverse(prev.BaseRotLocal) * prev.Bone.localRotation;
                        }
                        Vector3 euler = prev.Bone.localEulerAngles;
                        rcRotText = " rcRot=" + euler.ToString("F1");
                    }
                }
                float amp = ParamsRef.ChainBones.GetAmp(particle.BoneIndex);
                Transform parent = particle.Bone.parent;
                Vector3 worldBase = parent == null
                    ? particle.Bone.position
                    : parent.TransformPoint(particle.BaseLocal);
                if (amp <= 0.0001f)
                {
                    // Disabled bone: fully restore position and rotation instead of
                    // leaving the last written deformation frozen on the leg.
                    if (parent == null)
                    {
                        particle.Bone.position = worldBase;
                    }
                    else
                    {
                        particle.Bone.localPosition = particle.BaseLocal;
                    }
                    particle.Bone.localRotation = particle.BaseRotLocal;
                    particle.LastAppliedLocal = Vector3.zero;
                    particle.LastAppliedRotLocal = Quaternion.identity;
                    particle.RotSmoothed = Vector3.zero;
                    particle.RotTarget = Vector3.zero;
                    continue;
                }
                if (IsNan(particle.Position) || IsNan(worldBase))
                {
                    particle.Position = worldBase;
                    particle.LastAppliedLocal = Vector3.zero;
                }
                Vector3 delta = particle.Position - worldBase;
                // Rot support in chain mode: non-RC bones get a smooth tilt driven by
                // the particle offset (like spring mode's rotation), default 0.25.
                // NodesConstraints already owns the limb rotation on a constraint-safe
                // frame. Keep Chain's bounded position response without a second write.
                if (!yieldConstraintRotations &&
                    !ParamsRef.ChainBones.GetRotCalc(particle.BoneIndex))
                {
                    float rotAmp = ParamsRef.ChainBones.GetRotAmp(particle.BoneIndex);
                    if (rotAmp > 0.0001f)
                    {
                        Vector3 localOffset = character.InverseTransformDirection(delta);
                        float maxRot = 20f * rotAmp;
                        particle.RotTarget = new Vector3(
                            Mathf.Clamp(localOffset.z * 1.2f, -maxRot, maxRot),
                            0f,
                            Mathf.Clamp(-localOffset.x * 1.2f, -maxRot, maxRot));
                        particle.RotSmoothed = Vector3.Lerp(particle.RotSmoothed, particle.RotTarget, 0.25f);
                        particle.Bone.localRotation = particle.BaseRotLocal * Quaternion.Euler(particle.RotSmoothed);
                        particle.LastAppliedRotLocal =
                            Quaternion.Inverse(particle.BaseRotLocal) * particle.Bone.localRotation;
                    }
                    else
                    {
                        particle.RotSmoothed = Vector3.zero;
                        particle.Bone.localRotation = particle.BaseRotLocal;
                        particle.LastAppliedRotLocal = Quaternion.identity;
                    }
                }
                Vector3 axisMask = ParamsRef.ChainBones.GetAxis(particle.BoneIndex);
                Vector3 localDelta = character.InverseTransformDirection(delta);
                // When this bone uses RC, prefer rotation over translation so the leg
                // swings like flesh instead of squirming (wriggling) along the chain.
                float posScale = ParamsRef.ChainBones.GetRotCalc(particle.BoneIndex) &&
                                 !yieldConstraintRotations ? 0.5f : 1f;
                localDelta = Vector3.Scale(localDelta, axisMask) * amp * posScale;
                float distal = particle.BoneIndex == _distalIndex ? 0.6f : 1f;
                float writeLimit = (0.02f + 0.01f * amp) * distal * targetRangeScale;
                localDelta = Vector3.ClampMagnitude(localDelta, writeLimit);
                Vector3 worldDelta = character.TransformDirection(localDelta);
                if (parent == null)
                {
                    particle.Bone.position = worldBase + worldDelta;
                }
                else
                {
                    particle.Bone.localPosition = parent.InverseTransformPoint(worldBase + worldDelta);
                }
                if (IsNan(particle.Bone.localPosition) || IsBadQuat(particle.Bone.localRotation))
                {
                    ResetChainParticle(particle);
                    continue;
                }
                particle.LastAppliedLocal = particle.Bone.localPosition - particle.BaseLocal;
                if (_collectMetricsThisFrame)
                    RecordMetric(localDelta);
                if (logChain)
                {
                    UnityEngine.Debug.Log("Flesh physics [" + particle.Bone.name + "]: chain applied=" +
                        localDelta.ToString("F5") + " mag=" + localDelta.magnitude.ToString("F5") +
                        " off=" + delta.magnitude.ToString("F5") +
                        " anchor=" + anchorMove.magnitude.ToString("F5") +
                        " amp=" + amp.ToString("F3") +
                        " axis=(" + axisMask.x.ToString("F2") + "," + axisMask.y.ToString("F2") + "," +
                        axisMask.z.ToString("F2") + ")" +
                        " rc=" + (ParamsRef.ChainBones.GetRotCalc(particle.BoneIndex) ? "1" : "0") +
                        rcRotText);
                }
            }
        }
        if (logChain)
        {
            UnityEngine.Debug.Log("Flesh chain params: weight=" + chainParams.Weight.ToString("F3") +
                " gravity=" + chainParams.Gravity.ToString("F3") +
                " damping=" + chainParams.Damping.ToString("F3") +
                " elasticity=" + chainParams.Elasticity.ToString("F3") +
                " stiffness=" + chainParams.Stiffness.ToString("F3") +
                " inert=" + chainParams.Inert.ToString("F3") +
                " motionTarget=" + motionTarget.ToString("F3") +
                " follow=" + motionFollow.ToString("F3") +
                " motionRaw=" + motionGain.ToString("F3"));
            _chainLogTime = _chainTime;
        }
    }

    private static void ResetChainInputHistory(SideChain chain)
    {
        chain.AnchorMoveAt60 = Vector3.zero;
        chain.AnchorAngularAt60 = Vector3.zero;
        chain.PreviousAnchorMoveAt60 = Vector3.zero;
        chain.PreviousAnchorAngularAt60 = Vector3.zero;
        chain.AnchorInputSampleCount = 0;
    }

    private static void ClearChainRotationOutput(SideChain chain)
    {
        for (int i = 0; i < chain.Particles.Count; i++)
        {
            ChainParticle particle = chain.Particles[i];
            if (particle == null || particle.Bone == null)
            {
                continue;
            }
            particle.Bone.localRotation = particle.BaseRotLocal;
            particle.LastAppliedRotLocal = Quaternion.identity;
            particle.RotSmoothed = Vector3.zero;
            particle.RotTarget = Vector3.zero;
        }
    }

    private bool PrepareCleanChainBaseFrame(SideChain chain)
    {
        bool resetRequired = false;
        for (int i = 0; i < chain.Particles.Count; i++)
        {
            ChainParticle particle = chain.Particles[i];
            if (particle == null || particle.Bone == null)
            {
                continue;
            }
            Vector3 expectedLocal = particle.BaseLocal + particle.LastAppliedLocal;
            if ((particle.Bone.localPosition - expectedLocal).sqrMagnitude > 0.000001f)
            {
                Vector3 incomingLocal = particle.Bone.localPosition;
                if ((incomingLocal - particle.BaseLocal).sqrMagnitude >
                    FleshSolverMath.ChainTeleportDistance *
                    FleshSolverMath.ChainTeleportDistance)
                {
                    resetRequired = true;
                }
                if (_partId == FleshPartId.Belly)
                {
                    Vector3 safeLocal = FleshSolverMath.ClampBellyBase(
                        particle.SafeBaseLocal, incomingLocal);
                    if ((safeLocal - incomingLocal).sqrMagnitude > 0.00000001f)
                    {
                        // Write the guarded value before re-anchoring so the reset
                        // cannot capture the unsafe incoming position again.
                        incomingLocal = safeLocal;
                        particle.Bone.localPosition = safeLocal;
                        resetRequired = true;
                    }
                }
                particle.BaseLocal = incomingLocal;
            }
            else
            {
                // Remove Soma's previous local offset before evaluating any child.
                // Otherwise a deformed parent shifts/rotates the child's world base
                // and the chain slowly feeds its own output back into its rest pose.
                particle.Bone.localPosition = particle.BaseLocal;
            }
            Quaternion expectedRot = particle.BaseRotLocal * particle.LastAppliedRotLocal;
            if (Quaternion.Angle(particle.Bone.localRotation, expectedRot) >
                ExternalRotationThreshold)
            {
                Quaternion incomingRot = particle.Bone.localRotation;
                if (Quaternion.Angle(incomingRot, particle.BaseRotLocal) >
                    FleshSolverMath.ChainTeleportAngle)
                {
                    resetRequired = true;
                }
                particle.BaseRotLocal = incomingRot;
            }
            else
            {
                particle.Bone.localRotation = particle.BaseRotLocal;
            }
            particle.LastAppliedLocal = Vector3.zero;
            particle.LastAppliedRotLocal = Quaternion.identity;
        }
        return resetRequired;
    }

    private void ReanchorWholeChain(SideChain chain)
    {
        _metricReanchors++;
        for (int i = 0; i < chain.Particles.Count; i++)
        {
            ChainParticle particle = chain.Particles[i];
            if (particle != null && particle.Bone != null)
            {
                FleshStateReset.ReanchorChain(particle);
            }
        }
        if (chain.Anchor != null)
        {
            chain.PrevAnchorPos = chain.Anchor.position;
            chain.PrevAnchorRot = chain.Anchor.rotation;
        }
        ResetChainInputHistory(chain);
    }

    /// <summary>
    /// Chain integration for a single-particle chain (Belly: only cf_s_waist01 is
    /// fleshy; cf_s_spine03 was removed because it is rigid). The particle is driven
    /// by the anchor move + angular velocity like a chain child, but never re-read
    /// from the bone, otherwise our own write would feed back into the velocity.
    /// </summary>
    private void UpdateSingleParticleChain(SideChain chain, Vector3 anchorPos,
        Vector3 anchorMove, Vector3 anchorAngVel, Vector3 gravity, float weight,
        float motionFollow, float targetRangeScale, ChainParams chainParams,
        Transform character, float dt, float velocityRetention,
        float singleParticleReturnStrength)
    {
        ChainParticle particle = chain.Particles[0];
        if (particle == null || particle.Bone == null)
        {
            return;
        }
        Transform parent = particle.Bone.parent;
        Vector3 worldBase = parent == null
            ? particle.Bone.position
            : parent.TransformPoint(particle.BaseLocal);
        if (IsNan(worldBase) || IsNan(particle.Position) || IsNan(particle.PrevPosition) ||
            IsNan(particle.BaseLocal) || IsNan(particle.Bone.localPosition) || IsBadQuat(particle.Bone.localRotation))
        {
            ResetChainParticle(particle);
            worldBase = parent == null
                ? particle.Bone.position
                : parent.TransformPoint(particle.BaseLocal);
            if (IsNan(worldBase) || IsNan(particle.Position))
            {
                return;
            }
        }
        // Local-space external-move detection (same as multi-particle chains).
        if ((particle.Bone.localPosition -
             (particle.BaseLocal + particle.LastAppliedLocal)).magnitude > 0.005f)
        {
            _metricReanchors++;
            worldBase = FleshStateReset.ReanchorChain(particle);
        }
        float amp = ParamsRef.ChainBones.GetAmp(particle.BoneIndex);
        if (amp <= 0.0001f)
        {
            if (parent == null)
            {
                particle.Bone.position = worldBase;
            }
            else
            {
                particle.Bone.localPosition = particle.BaseLocal;
            }
            particle.LastAppliedLocal = Vector3.zero;
            return;
        }
        FleshChainSolver.Integrate(particle, anchorPos, anchorMove, anchorAngVel,
            gravity, weight, motionFollow, dt, velocityRetention);
        // A one-particle chain has no previous particle/rest segment to supply the
        // multi-particle solver's return force. Pull it toward the animated base here,
        // otherwise gravity pins Belly to its leash and Elasticity/Stiffness/JitterFreq
        // have no effect at all.
        FleshChainSolver.ApplySingleParticleReturn(particle, worldBase,
            singleParticleReturnStrength);
        Vector3 delta = particle.Position - worldBase;
        float leashLimit = (0.03f + 0.012f * amp) * targetRangeScale;
        FleshChainSolver.ApplyLeash(particle, worldBase, leashLimit);
        delta = particle.Position - worldBase;
        Vector3 axisMask = ParamsRef.ChainBones.GetAxis(particle.BoneIndex);
        Vector3 localDelta = character.InverseTransformDirection(delta);
        // Single-particle chain has no child to aim at, so RC cannot rotate; keep
        // the full translation (no 0.5x RC position discount).
        localDelta = Vector3.Scale(localDelta, axisMask) * amp;
        float writeLimit = (0.02f + 0.01f * amp) * targetRangeScale;
        localDelta = Vector3.ClampMagnitude(localDelta, writeLimit);
        Vector3 worldDelta = character.TransformDirection(localDelta);
        if (parent == null)
        {
            particle.Bone.position = worldBase + worldDelta;
        }
        else
        {
            particle.Bone.localPosition = parent.InverseTransformPoint(worldBase + worldDelta);
        }
        if (_partId == FleshPartId.Belly)
        {
            particle.Bone.localPosition = FleshSolverMath.ClampBellyLocal(
                particle.SafeBaseLocal, particle.Bone.localPosition);
        }
        if (IsNan(particle.Bone.localPosition) || IsBadQuat(particle.Bone.localRotation))
        {
            ResetChainParticle(particle);
        }
        else
        {
            particle.LastAppliedLocal = particle.Bone.localPosition - particle.BaseLocal;
            RecordMetric(localDelta);
        }
    }

}
