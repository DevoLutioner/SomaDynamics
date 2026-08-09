using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ThighPhysicsController;

/// <summary>
/// Owns the game's native breast/butt DynamicBone_Ver02 chains. This is the
/// small, compatibility-oriented subset previously supplied by BPC.
/// </summary>
internal sealed class NativeDynamicBoneBridge
{
    private static readonly FieldInfo ParticlesField = typeof(DynamicBone_Ver02)
        .GetField("Particles", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly ChaControl _control;
    private readonly Dictionary<int, TargetSnapshot> _original =
        new Dictionary<int, TargetSnapshot>();
    private readonly Dictionary<int, int> _loggedMode = new Dictionary<int, int>();

    internal NativeDynamicBoneBridge(ChaControl control)
    {
        _control = control;
    }

    internal void Apply(FleshPartId part, NativeBodyParams value)
    {
        if (_control == null || value == null)
            return;
        int firstKind = part == FleshPartId.Breast ? 0 : 2;
        ApplyTarget(_control.getDynamicBoneBust((ChaInfo.DynamicBoneKind)firstKind), part, value,
            firstKind);
        ApplyTarget(_control.getDynamicBoneBust((ChaInfo.DynamicBoneKind)(firstKind + 1)), part,
            value, firstKind + 1);
    }

    internal void RestoreAll()
    {
        foreach (TargetSnapshot snapshot in _original.Values)
            snapshot.Restore();
        _original.Clear();
        if (_control != null)
            _control.ReSetupDynamicBoneBust(0);
    }

    private void ApplyTarget(DynamicBone_Ver02 target, FleshPartId part, NativeBodyParams value,
        int kind)
    {
        if (target == null)
            return;
        int id = target.GetInstanceID();
        if (!_original.ContainsKey(id))
            _original[id] = new TargetSnapshot(target, ParticlesField);
        if (!value.Enabled)
        {
            _original[id].Restore();
            LogMode(id, part, kind, "Original", 0, value);
            return;
        }

        if (!target.enabled)
            target.enabled = true;

        target.setGravity(0, new Vector3(0f,
            FleshValue.Clamp(value.Gravity, -FleshParameterRanges.NativeGravityMax,
                FleshParameterRanges.NativeGravityMax, 0f), 0f), true);
        int matched = part == FleshPartId.Breast
            ? ApplyBreast(target, value)
            : ApplyButt(target, value);
        LogMode(id, part, kind, "NativeChain", matched, value);
    }

    private void LogMode(int id, FleshPartId part, int kind, string mode, int matched,
        NativeBodyParams value)
    {
        int modeKey = mode == "NativeChain" ? 1 : 0;
        int previous;
        if (_loggedMode.TryGetValue(id, out previous) && previous == modeKey)
            return;
        _loggedMode[id] = modeKey;
        UnityEngine.Debug.Log("FPC_NATIVE_APPLY part=" + part + " kind=" + kind +
            " mode=" + mode + " matched=" + matched +
            " strength=" + value.Strength.ToString("F3") +
            " softness=" + value.Softness.ToString("F3") +
            " motion_target=" + value.MotionResponse.ToString("F3"));
    }

    private static int ApplyBreast(DynamicBone_Ver02 target, NativeBodyParams value)
    {
        if (target.Patterns == null || target.Patterns.Count == 0)
            return 0;
        DynamicBone_Ver02.BonePtn pattern = target.Patterns[0];
        int matched = 0;
        for (int i = 0; i < pattern.Params.Count; i++)
        {
            DynamicBone_Ver02.BoneParameter parameter = pattern.Params[i];
            NativeBoneSpec spec;
            int boneIndex;
            if (!TryGetSpec(FleshPartId.Breast, parameter.Name, value, out spec, out boneIndex))
                continue;
            ApplySpec(parameter, spec, value);
            matched++;
        }
        // Match BPC exactly: after editing named parameters, copy the entire table
        // to the live particle pattern before selecting pattern zero.
        for (int i = 0; i < pattern.Params.Count && i < pattern.ParticlePtns.Count; i++)
            CopyToParticlePattern(pattern.Params[i], pattern.ParticlePtns[i]);
        target.setPtn(0, true);
        return matched;
    }

    private static int ApplyButt(DynamicBone_Ver02 target, NativeBodyParams value)
    {
        var particles = ParticlesField == null
            ? null
            : ParticlesField.GetValue(target) as List<DynamicBone_Ver02.Particle>;
        if (particles == null)
            return 0;
        int matched = 0;
        for (int i = 0; i < particles.Count; i++)
        {
            DynamicBone_Ver02.Particle particle = particles[i];
            if (particle == null || particle.refTrans == null)
                continue;
            NativeBoneSpec spec;
            int boneIndex;
            if (!TryGetSpec(FleshPartId.Butt, particle.refTrans.name, value, out spec,
                    out boneIndex))
                continue;
            ApplySpec(particle, spec, value);
            matched++;
        }
        return matched;
    }

    private static bool TryGetSpec(FleshPartId part, string name, NativeBodyParams value,
        out NativeBoneSpec spec, out int boneIndex)
    {
        string key = NativeBodyTuning.NormalizeBoneName(name);
        if (part == FleshPartId.Breast)
        {
            if (key == "cf_j_bust01_L")
            {
                spec = new NativeBoneSpec(false, 0.08f, 0.15f, 0.25f, 0.60f);
                boneIndex = 0;
                OverrideSpec(value, boneIndex, ref spec);
                return true;
            }
            if (key == "cf_j_bust02_L" || key == "cf_j_bust03_L")
            {
                spec = new NativeBoneSpec(true, 0.05f, 0.08f, 0.07f, 0.50f);
                boneIndex = key == "cf_j_bust02_L" ? 1 : 2;
                OverrideSpec(value, boneIndex, ref spec);
                return true;
            }
        }
        else
        {
            if (key == "cf_d_siri01_L")
            {
                spec = new NativeBoneSpec(false, 0.03f, 0.12f, 0.09f, 0.25f);
                boneIndex = 0;
                OverrideSpec(value, boneIndex, ref spec);
                return true;
            }
            if (key == "cf_j_siri_L_01")
            {
                spec = new NativeBoneSpec(true, 0.03f, 0.08f, 0.05f, 0.25f);
                boneIndex = 1;
                OverrideSpec(value, boneIndex, ref spec);
                return true;
            }
        }
        spec = default(NativeBoneSpec);
        boneIndex = -1;
        return false;
    }

    private static void OverrideSpec(NativeBodyParams value, int boneIndex,
        ref NativeBoneSpec spec)
    {
        if (value == null || !value.AdvancedOverride)
            return;
        NativeBoneParams bone = value.GetBone(boneIndex);
        spec = new NativeBoneSpec(bone.IsRotationCalc, bone.Damping, bone.Elasticity,
            bone.Stiffness, bone.Inert);
    }

    private static void ApplySpec(DynamicBone_Ver02.BoneParameter target,
        NativeBoneSpec spec, NativeBodyParams value)
    {
        if (value.AdvancedOverride)
        {
            target.IsRotationCalc = spec.Rotation;
            target.Damping = spec.Damping;
            target.Elasticity = spec.Elasticity;
            target.Stiffness = spec.Stiffness;
            target.Inert = spec.Inert;
            return;
        }
        target.IsRotationCalc = spec.Rotation;
        target.Damping = NativeBodyTuning.TuneSoftness(spec.Damping, value.Softness,
            1.70f, 0.70f);
        target.Elasticity = NativeBodyTuning.TuneSoftness(spec.Elasticity, value.Softness,
            1.35f, 0.75f);
        target.Stiffness = NativeBodyTuning.TuneSoftness(spec.Stiffness, value.Softness,
            1.60f, 0.70f);
        target.Inert = NativeBodyTuning.TargetInert(spec.Inert, value.MotionResponse);
        NativeBodyTuning.ApplyStrengthTarget(ref target.Damping, ref target.Elasticity,
            ref target.Stiffness, value.Strength);
        NativeBodyTuning.ApplyMotionTarget(ref target.Damping, ref target.Elasticity,
            ref target.Stiffness, value.MotionResponse);
    }

    private static void ApplySpec(DynamicBone_Ver02.Particle target,
        NativeBoneSpec spec, NativeBodyParams value)
    {
        if (value.AdvancedOverride)
        {
            target.IsRotationCalc = spec.Rotation;
            target.Damping = spec.Damping;
            target.Elasticity = spec.Elasticity;
            target.Stiffness = spec.Stiffness;
            target.Inert = spec.Inert;
            return;
        }
        target.IsRotationCalc = spec.Rotation;
        target.Damping = NativeBodyTuning.TuneSoftness(spec.Damping, value.Softness,
            1.70f, 0.70f);
        target.Elasticity = NativeBodyTuning.TuneSoftness(spec.Elasticity, value.Softness,
            1.35f, 0.75f);
        target.Stiffness = NativeBodyTuning.TuneSoftness(spec.Stiffness, value.Softness,
            1.60f, 0.70f);
        target.Inert = NativeBodyTuning.TargetInert(spec.Inert, value.MotionResponse);
        NativeBodyTuning.ApplyStrengthTarget(ref target.Damping, ref target.Elasticity,
            ref target.Stiffness, value.Strength);
        NativeBodyTuning.ApplyMotionTarget(ref target.Damping, ref target.Elasticity,
            ref target.Stiffness, value.MotionResponse);
    }

    private static void CopyToParticlePattern(DynamicBone_Ver02.BoneParameter source,
        DynamicBone_Ver02.ParticlePtn target)
    {
        target.IsRotationCalc = source.IsRotationCalc;
        target.Damping = source.Damping;
        target.Elasticity = source.Elasticity;
        target.Stiffness = source.Stiffness;
        target.Inert = source.Inert;
    }

    private struct NativeBoneSpec
    {
        public readonly bool Rotation;
        public readonly float Damping;
        public readonly float Elasticity;
        public readonly float Stiffness;
        public readonly float Inert;

        public NativeBoneSpec(bool rotation, float damping, float elasticity,
            float stiffness, float inert)
        {
            Rotation = rotation;
            Damping = damping;
            Elasticity = elasticity;
            Stiffness = stiffness;
            Inert = inert;
        }
    }

    private sealed class TargetSnapshot
    {
        private readonly DynamicBone_Ver02 _target;
        private readonly bool _enabled;
        private readonly Vector3 _gravity;
        private readonly List<BoneState> _parameters = new List<BoneState>();
        private readonly List<ParticleState> _particles = new List<ParticleState>();

        public TargetSnapshot(DynamicBone_Ver02 target, FieldInfo particlesField)
        {
            _target = target;
            _enabled = target.enabled;
            _gravity = target.Gravity;
            if (target.Patterns != null && target.Patterns.Count > 0)
            {
                foreach (DynamicBone_Ver02.BoneParameter parameter in target.Patterns[0].Params)
                    _parameters.Add(new BoneState(parameter));
            }
            var particles = particlesField == null ? null :
                particlesField.GetValue(target) as List<DynamicBone_Ver02.Particle>;
            if (particles != null)
            {
                foreach (DynamicBone_Ver02.Particle particle in particles)
                    _particles.Add(new ParticleState(particle));
            }
        }

        public void Restore()
        {
            if (_target == null)
                return;
            _target.setGravity(0, _gravity, true);
            if (_target.Patterns != null && _target.Patterns.Count > 0)
            {
                foreach (DynamicBone_Ver02.BoneParameter parameter in _target.Patterns[0].Params)
                {
                    BoneState state = _parameters.Find(x => x.Name == parameter.Name);
                    if (state != null)
                        state.Apply(parameter);
                }
                _target.setPtn(0, true);
            }
            var particles = ParticlesField == null ? null :
                ParticlesField.GetValue(_target) as List<DynamicBone_Ver02.Particle>;
            if (particles != null)
            {
                foreach (DynamicBone_Ver02.Particle particle in particles)
                {
                    string name = particle == null || particle.refTrans == null
                        ? string.Empty : particle.refTrans.name;
                    ParticleState state = _particles.Find(x => x.Name == name);
                    if (state != null)
                        state.Apply(particle);
                }
            }
            _target.enabled = _enabled;
        }
    }

    private sealed class BoneState
    {
        public readonly string Name;
        private readonly bool _rotation;
        private readonly float _damping;
        private readonly float _elasticity;
        private readonly float _stiffness;
        private readonly float _inert;

        public BoneState(DynamicBone_Ver02.BoneParameter value)
        {
            Name = value.Name;
            _rotation = value.IsRotationCalc;
            _damping = value.Damping;
            _elasticity = value.Elasticity;
            _stiffness = value.Stiffness;
            _inert = value.Inert;
        }

        public void Apply(DynamicBone_Ver02.BoneParameter value)
        {
            value.IsRotationCalc = _rotation;
            value.Damping = _damping;
            value.Elasticity = _elasticity;
            value.Stiffness = _stiffness;
            value.Inert = _inert;
        }
    }

    private sealed class ParticleState
    {
        public readonly string Name;
        private readonly bool _rotation;
        private readonly float _damping;
        private readonly float _elasticity;
        private readonly float _stiffness;
        private readonly float _inert;

        public ParticleState(DynamicBone_Ver02.Particle value)
        {
            Name = value == null || value.refTrans == null ? string.Empty : value.refTrans.name;
            _rotation = value != null && value.IsRotationCalc;
            _damping = value == null ? 0f : value.Damping;
            _elasticity = value == null ? 0f : value.Elasticity;
            _stiffness = value == null ? 0f : value.Stiffness;
            _inert = value == null ? 0f : value.Inert;
        }

        public void Apply(DynamicBone_Ver02.Particle value)
        {
            if (value == null)
                return;
            value.IsRotationCalc = _rotation;
            value.Damping = _damping;
            value.Elasticity = _elasticity;
            value.Stiffness = _stiffness;
            value.Inert = _inert;
        }
    }
}
