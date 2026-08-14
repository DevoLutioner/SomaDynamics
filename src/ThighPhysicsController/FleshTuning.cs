using UnityEngine;

namespace ThighPhysicsController;

internal enum FleshFeelPreset
{
    Stable,
    Natural,
    Dance,
}

/// <summary>
/// User-facing tuning layer. It keeps the UI small while translating one
/// "softness" value into a coherent group of solver parameters.
/// </summary>
internal static class FleshTuning
{
    public static float GetStrength(ThighParams p)
    {
        float value = p.GamePhysics ? p.Chain.Weight : p.Weight;
        return FleshValue.Clamp(value, 0f, FleshParameterRanges.WeightMax, 0.7f);
    }

    public static void SetStrength(ThighParams p, float value)
    {
        value = FleshValue.Clamp(value, 0f, FleshParameterRanges.WeightMax, GetStrength(p));
        if (p.GamePhysics)
        {
            p.Chain.Weight = value;
        }
        else
        {
            p.Weight = value;
        }
    }

    public static float GetMotionTarget(ThighParams p)
    {
        return FleshValue.Clamp(p.MotionGain / 5f, 0f,
            FleshParameterRanges.TargetMax, 0.2f);
    }

    public static void SetMotionTarget(ThighParams p, float value)
    {
        p.MotionGain = FleshValue.Clamp(value, 0f, FleshParameterRanges.TargetMax,
            GetMotionTarget(p)) * 5f;
    }

    public static float GetSoftness(ThighParams p, FleshPartId part)
    {
        float inert = p.GamePhysics ? p.Chain.Inert : p.Thigh00.Inert;
        inert = FleshValue.Clamp(inert, 0f, 1.5f,
            p.GamePhysics ? 0.40f : 0.35f);
        if (p.GamePhysics)
        {
            // Old chain default (0.40) is the middle point; the user's tuned
            // thigh/arm value (0.80) is the soft endpoint.
            float chainSoftInert = SoftChainInert(part);
            if (inert <= 0.40f)
                return 0.5f * Mathf.InverseLerp(0.10f, 0.40f, inert);
            if (inert <= chainSoftInert)
                return 0.5f + 0.5f * Mathf.InverseLerp(0.40f, chainSoftInert, inert);
            return 1f + Mathf.InverseLerp(chainSoftInert, 1.5f, inert);
        }
        // The established spring value (0.35) is the middle point.
        float springSoftInert = SoftSpringInert(part);
        if (inert <= 0.35f)
            return 0.5f * Mathf.InverseLerp(0.15f, 0.35f, inert);
        if (inert <= springSoftInert)
            return 0.5f + 0.5f * Mathf.InverseLerp(0.35f, springSoftInert, inert);
        return 1f + Mathf.InverseLerp(springSoftInert, 1.5f, inert);
    }

    public static void SetSoftness(ThighParams p, FleshPartId part, float value)
    {
        float softness = FleshValue.Clamp(value, 0f, FleshParameterRanges.TargetMax,
            GetSoftness(p, part));
        if (p.GamePhysics)
        {
            ChainParams chain = p.Chain;
            if (softness <= 1f)
            {
                chain.Damping = Piecewise(softness, 0.55f, 0.30f, SoftChainDamping(part));
                chain.Elasticity = Piecewise(softness, 0.40f, 0.25f,
                    SoftChainElasticity(part));
                chain.Stiffness = Piecewise(softness, 0.98f, 0.90f,
                    SoftChainStiffness(part));
                chain.Inert = Piecewise(softness, 0.10f, 0.40f, SoftChainInert(part));
                chain.JitterFreq = Piecewise(softness, 1.50f, 1.00f,
                    SoftChainFrequency(part));
            }
            else
            {
                float extra = softness - 1f;
                chain.Damping = Mathf.Lerp(SoftChainDamping(part), 0.015f, extra);
                float softElasticity = SoftChainElasticity(part) * 0.55f;
                if (softElasticity < 0.02f)
                    softElasticity = 0.02f;
                float softStiffness = SoftChainStiffness(part) * 0.60f;
                if (softStiffness < 0.45f)
                    softStiffness = 0.45f;
                chain.Elasticity = Mathf.Lerp(SoftChainElasticity(part),
                    softElasticity, extra);
                chain.Stiffness = Mathf.Lerp(SoftChainStiffness(part),
                    softStiffness, extra);
                chain.Inert = Mathf.Lerp(SoftChainInert(part), 1.5f, extra);
                chain.JitterFreq = Mathf.Lerp(SoftChainFrequency(part), 0.05f, extra);
            }
            return;
        }

        ThighBoneParams spring = p.Thigh00;
        if (softness <= 1f)
        {
            spring.Damping = Piecewise(softness, 0.35f, 0.18f, 0.10f);
            spring.Elasticity = Piecewise(softness, 0.22f, 0.10f, 0.05f);
            spring.Stiffness = Piecewise(softness, 0.30f, 0.12f, 0.04f);
            spring.Inert = Piecewise(softness, 0.15f, 0.35f, SoftSpringInert(part));
        }
        else
        {
            float extra = softness - 1f;
            spring.Damping = Mathf.Lerp(0.10f, 0.04f, extra);
            spring.Elasticity = Mathf.Lerp(0.05f, 0.025f, extra);
            spring.Stiffness = Mathf.Lerp(0.04f, 0.015f, extra);
            spring.Inert = Mathf.Lerp(SoftSpringInert(part), 1.5f, extra);
        }
        // In the legacy spring integrator this value multiplies retained
        // displacement; values above 1 inject energy rather than merely raising
        // a physical frequency. Keep the simple control inside the neutral range.
        p.JitterFreq = softness <= 1f
            ? Piecewise(softness, 1.00f, 1.00f, 0.60f)
            : Mathf.Lerp(0.60f, 0.35f, softness - 1f);
        p.MotionSmooth = softness <= 1f
            ? Piecewise(softness, 0.40f, 0.25f, 0.15f)
            : Mathf.Lerp(0.15f, 0.08f, softness - 1f);
    }

    public static ThighParams CreateFeelPreset(FleshPartId part, FleshFeelPreset preset)
    {
        ThighParams p = ThighParams.CreatePartDefaults(part);
        float strength;
        float softness;
        float motion;
        switch (preset)
        {
            case FleshFeelPreset.Stable:
                p.GamePhysics = false;
                strength = part == FleshPartId.Thigh ? 0.78f :
                    part == FleshPartId.Arm ? 0.68f : 0.65f;
                softness = 0.45f;
                motion = 0.30f;
                break;
            case FleshFeelPreset.Dance:
                ApplyUserHighSnapshot(p, part);
                return p;
            default:
                strength = part == FleshPartId.Thigh ? 0.92f :
                    part == FleshPartId.Arm ? 0.80f : 0.78f;
                softness = 0.80f;
                motion = 0.45f;
                break;
        }
        ApplyLevelTargets(p, part, strength, softness, motion);
        ApplyLevelAmplitudes(p, part, preset);
        return p;
    }

    /// <summary>
    /// Exact built-in High snapshot from the user's MyPreset1.xml. Both solver
    /// parameter sets are populated so selecting High remains mode-independent.
    /// </summary>
    private static void ApplyUserHighSnapshot(ThighParams p, FleshPartId part)
    {
        p.GamePhysics = true;
        p.Gravity = 0.05f;
        p.Weight = part == FleshPartId.Thigh ? 1.08f :
            part == FleshPartId.Arm ? 0.95f : 0.90f;
        p.MotionGain = part == FleshPartId.Belly ? 3.00f : 3.25f;
        p.JitterFreq = 0.575f;
        p.MotionSmooth = 0.143f;

        p.Thigh00.Damping = 0.094f;
        p.Thigh00.Elasticity = 0.0475f;
        p.Thigh00.Stiffness = 0.0375f;
        p.Thigh00.Inert = part == FleshPartId.Belly ? 0.645f : 0.735f;

        p.Chain.Weight = 1.00f;
        p.Chain.Gravity = 0.05f;
        p.Chain.Damping = part == FleshPartId.Thigh ? 0.04f :
            part == FleshPartId.Arm ? 0.05f : 0.12f;
        p.Chain.Elasticity = part == FleshPartId.Thigh ? 0.05f :
            part == FleshPartId.Arm ? 0.25f : 0.12f;
        p.Chain.Stiffness = part == FleshPartId.Thigh ? 0.85f :
            part == FleshPartId.Arm ? 0.90f : 0.88f;
        p.Chain.Inert = part == FleshPartId.Belly ? 0.65f : 0.80f;
        p.Chain.JitterFreq = part == FleshPartId.Thigh ? 2.00f :
            part == FleshPartId.Arm ? 0.15f : 0.50f;

        float[] spring = part == FleshPartId.Thigh
            ? new[] { 1.3000f, 0.5000f, 0.2340f, 0.0390f }
            : part == FleshPartId.Arm
                ? new[] { 1.8386f, 0.2340f, 0.1404f, 0.0234f }
                : new[] { 1.5414f, 0.0975f, 0.0585f, 0.0097f };
        float[] chain = part == FleshPartId.Thigh
            ? new[] { 1.9500f, 0.5000f, 0.3500f, 0.4500f }
            : part == FleshPartId.Arm
                ? new[] { 1.0400f, 0.7800f, 0.7800f, 0.0936f }
                : new[] { 1.3000f, 0.2600f, 0.1625f, 0.0390f };
        for (int i = 0; i < 4; i++)
        {
            PerBoneAmount springBone = p.Bones.Get(i);
            springBone.Enabled = true;
            springBone.Amp = spring[i];
            springBone.AxisX = 1f;
            springBone.AxisY = part == FleshPartId.Thigh ? 0f : 1f;
            springBone.AxisZ = 1f;
            springBone.RotAmp = 0.25f;
            springBone.RotCalc = true;

            PerBoneAmount chainBone = p.ChainBones.Get(i);
            chainBone.Enabled = true;
            chainBone.Amp = chain[i];
            chainBone.AxisX = 1f;
            chainBone.AxisY = 1f;
            chainBone.AxisZ = 1f;
            chainBone.RotCalc = true;
        }
    }

    /// <summary>Writes the same user-level target into both solver parameter sets.</summary>
    public static void ApplyLevelTargets(ThighParams p, FleshPartId part,
        float strength, float softness, float motion)
    {
        if (p == null)
            return;
        bool chainMode = p.GamePhysics;
        p.GamePhysics = false;
        SetStrength(p, strength);
        SetSoftness(p, part, softness);
        p.GamePhysics = true;
        SetStrength(p, strength);
        SetSoftness(p, part, softness);
        p.GamePhysics = chainMode;
        SetMotionTarget(p, motion);
    }

    /// <summary>
    /// Applies a visible low/medium/high range to both solver parameter sets.
    /// Solver mode and per-bone enable/axis choices remain independent, so changing
    /// mode after selecting a level does not lose that level's amplitude.
    /// </summary>
    public static void ApplyLevelAmplitudes(ThighParams p, FleshPartId part,
        FleshFeelPreset preset)
    {
        if (p == null)
            return;
        float scale = preset == FleshFeelPreset.Stable ? 0.75f :
            preset == FleshFeelPreset.Natural ? 1f : 1.30f;
        float[] spring = part == FleshPartId.Thigh
            ? new[] { 1.00f, 0.30f, 0.18f, 0.03f }
            : part == FleshPartId.Arm
                ? new[] { 1.4143f, 0.18f, 0.108f, 0.018f }
                : new[] { 1.1857f, 0.075f, 0.045f, 0.0075f };
        float[] chain = part == FleshPartId.Thigh
            ? new[] { 1.50f, 1.20f, 0.30f, 0.80f }
            : part == FleshPartId.Arm
                ? new[] { 0.80f, 0.60f, 0.60f, 0.072f }
                : new[] { 1.00f, 0.20f, 0.125f, 0.03f };
        ScaleAmplitudes(p.Bones, spring, scale);
        ScaleAmplitudes(p.ChainBones, chain, scale);
        if (part == FleshPartId.Thigh && preset == FleshFeelPreset.Dance)
        {
            // Tested high-level structural guard: High pins Thigh02 to 0.50.
            p.Bones.Thigh02.Amp = 0.50f;
            p.ChainBones.Thigh02.Amp = 0.50f;
        }
        else if (part == FleshPartId.Thigh && preset == FleshFeelPreset.Natural)
        {
            // Medium keeps MyPreset-derived values, but Thigh02 must never
            // exceed 0.50 to prevent thigh collapse (user-requested cap).
            if (p.Bones.Thigh02.Amp > 0.50f)
            {
                p.Bones.Thigh02.Amp = 0.50f;
            }
            if (p.ChainBones.Thigh02.Amp > 0.50f)
            {
                p.ChainBones.Thigh02.Amp = 0.50f;
            }
        }
    }

    private static void ScaleAmplitudes(ThighBoneAmounts target,
        float[] baseline, float scale)
    {
        for (int i = 0; i < 4; i++)
        {
            float value = baseline[i] * scale;
            target.Get(i).Amp = FleshValue.Clamp(value, 0f,
                FleshParameterRanges.BoneAmplitudeMax, baseline[i]);
        }
    }

    private static float Piecewise(float value, float tight, float middle, float soft)
    {
        return value <= 0.5f
            ? Mathf.Lerp(tight, middle, value * 2f)
            : Mathf.Lerp(middle, soft, (value - 0.5f) * 2f);
    }

    private static float SoftChainDamping(FleshPartId part)
    {
        return part == FleshPartId.Thigh ? 0.04f : part == FleshPartId.Arm ? 0.05f : 0.12f;
    }

    private static float SoftChainElasticity(FleshPartId part)
    {
        return part == FleshPartId.Thigh ? 0.05f : part == FleshPartId.Arm ? 0.25f : 0.12f;
    }

    private static float SoftChainStiffness(FleshPartId part)
    {
        return part == FleshPartId.Thigh ? 0.85f : part == FleshPartId.Arm ? 0.90f : 0.88f;
    }

    private static float SoftChainInert(FleshPartId part)
    {
        return part == FleshPartId.Belly ? 0.65f : 0.80f;
    }

    private static float SoftSpringInert(FleshPartId part)
    {
        return part == FleshPartId.Belly ? 0.55f : 0.65f;
    }

    private static float SoftChainFrequency(FleshPartId part)
    {
        return part == FleshPartId.Thigh ? 0.20f : part == FleshPartId.Arm ? 0.15f : 0.50f;
    }
}
