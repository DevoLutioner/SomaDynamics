using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ThighPhysicsController;

/// <summary>
/// One skeleton discovery pass. Reuse its index for state resets instead of
/// allocating renderer/bone arrays for every individual bone lookup.
/// </summary>
internal sealed class FleshBoneLookup
{
    private static readonly FieldInfo ParticlesField = typeof(DynamicBone)
        .GetField("m_Particles", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo ParticleTransformField = ResolveParticleTransform();
    private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
    private readonly Dictionary<string, int> _priorities = new Dictionary<string, int>();
    private readonly Dictionary<Transform, bool> _externalBones = new Dictionary<Transform, bool>();

    internal FleshBoneLookup(ChaControl control)
    {
        foreach (DynamicBone owner in control.GetComponentsInChildren<DynamicBone>(true))
            CaptureExternalBones(owner);

        foreach (SkinnedMeshRenderer renderer in control.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer == null)
                continue;
            Transform root = renderer.transform;
            while (root.parent != null)
                root = root.parent;
            bool active = renderer.gameObject.activeInHierarchy;
            int priority = active ? (root.name == "p_cf_body_00" ? 4 : 3) : 2;
            foreach (Transform bone in renderer.bones)
                Add(bone, priority);
        }
        foreach (Transform bone in control.GetComponentsInChildren<Transform>(true))
            Add(bone, bone.gameObject.activeInHierarchy ? 1 : 0);
    }

    internal Transform Find(string name)
    {
        Transform bone;
        return _bones.TryGetValue(name, out bone) ? bone : null;
    }

    internal bool ShouldYield(FleshChainDef chain, string side)
    {
        for (int i = 0; i < chain.BoneNameTemplates.Length; i++)
        {
            Transform bone = Find(chain.BoneNameTemplates[i].Replace("{side}", side));
            if (bone != null && _externalBones.ContainsKey(bone))
            {
                // Yield the whole affected side: Chain RC also rotates the parent
                // of the next particle, so dropping only one particle is unsafe.
                return true;
            }
        }
        return false;
    }

    private void Add(Transform bone, int priority)
    {
        if (bone == null || string.IsNullOrEmpty(bone.name))
            return;
        int previous;
        if (_priorities.TryGetValue(bone.name, out previous) && previous >= priority)
            return;
        _priorities[bone.name] = priority;
        _bones[bone.name] = bone;
    }

    private static FieldInfo ResolveParticleTransform()
    {
        Type particle = typeof(DynamicBone).GetNestedType("Particle", BindingFlags.NonPublic);
        return System.Object.ReferenceEquals(particle, null) ? null :
            particle.GetField("m_Transform", BindingFlags.Instance | BindingFlags.Public);
    }

    private void CaptureExternalBones(DynamicBone owner)
    {
        if (owner == null || owner.m_Root == null)
            return;
        // A temporarily disabled chain still owns its rest pose and may be
        // enabled later by BP. Never capture its current output as Soma's base.
        IList particles = System.Object.ReferenceEquals(ParticlesField, null) ? null :
            ParticlesField.GetValue(owner) as IList;
        if (particles != null && particles.Count > 0 &&
            !System.Object.ReferenceEquals(ParticleTransformField, null))
        {
            foreach (object particle in particles)
            {
                if (particle == null)
                    continue;
                Transform bone = ParticleTransformField.GetValue(particle) as Transform;
                if (bone != null)
                    _externalBones[bone] = true;
            }
            return;
        }
        // OnReload can precede DynamicBone.Start/SetupParticles. Use the declared
        // hierarchy until the next reload, respecting its excluded subtrees.
        CaptureDeclaredBones(owner.m_Root, owner.m_Exclusions);
    }

    private void CaptureDeclaredBones(Transform bone, List<Transform> exclusions)
    {
        if (bone == null || (exclusions != null && exclusions.Contains(bone)))
            return;
        _externalBones[bone] = true;
        for (int i = 0; i < bone.childCount; i++)
            CaptureDeclaredBones(bone.GetChild(i), exclusions);
    }
}
