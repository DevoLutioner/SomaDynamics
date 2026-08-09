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
        return FleshValue.Clamp(value, 0f, 1f, 0.7f);
    }

    public static void SetStrength(ThighParams p, float value)
    {
        value = FleshValue.Clamp(value, 0f, 1f, GetStrength(p));
        if (p.GamePhysics)
        {
            p.Chain.Weight = value;
        }
        else
        {
            p.Weight = value;
        }
    }

    public static float GetSoftness(ThighParams p, FleshPartId part)
    {
        float inert = p.GamePhysics ? p.Chain.Inert : p.Thigh00.Inert;
        inert = FleshValue.Clamp(inert, 0f, 1f, p.GamePhysics ? 0.40f : 0.35f);
        if (p.GamePhysics)
        {
            // Old chain default (0.40) is the middle point; the user's tuned
            // thigh/arm value (0.80) is the soft endpoint.
            float chainSoftInert = SoftChainInert(part);
            return inert <= 0.40f
                ? 0.5f * Mathf.InverseLerp(0.10f, 0.40f, inert)
                : 0.5f + 0.5f * Mathf.InverseLerp(0.40f, chainSoftInert, inert);
        }
        // The established spring value (0.35) is the middle point.
        float springSoftInert = SoftSpringInert(part);
        return inert <= 0.35f
            ? 0.5f * Mathf.InverseLerp(0.15f, 0.35f, inert)
            : 0.5f + 0.5f * Mathf.InverseLerp(0.35f, springSoftInert, inert);
    }

    public static void SetSoftness(ThighParams p, FleshPartId part, float value)
    {
        float softness = FleshValue.Clamp(value, 0f, 1f, GetSoftness(p, part));
        if (p.GamePhysics)
        {
            ChainParams chain = p.Chain;
            chain.Damping = Piecewise(softness, 0.55f, 0.30f, SoftChainDamping(part));
            chain.Elasticity = Piecewise(softness, 0.40f, 0.25f, SoftChainElasticity(part));
            chain.Stiffness = Piecewise(softness, 0.98f, 0.90f, SoftChainStiffness(part));
            chain.Inert = Piecewise(softness, 0.10f, 0.40f, SoftChainInert(part));
            chain.JitterFreq = Piecewise(softness, 1.50f, 1.00f, SoftChainFrequency(part));
            return;
        }

        ThighBoneParams spring = p.Thigh00;
        spring.Damping = Piecewise(softness, 0.35f, 0.18f, 0.10f);
        spring.Elasticity = Piecewise(softness, 0.22f, 0.10f, 0.05f);
        spring.Stiffness = Piecewise(softness, 0.30f, 0.12f, 0.04f);
        spring.Inert = Piecewise(softness, 0.15f, 0.35f, SoftSpringInert(part));
        // In the legacy spring integrator this value multiplies retained
        // displacement; values above 1 inject energy rather than merely raising
        // a physical frequency. Keep the simple control inside the neutral range.
        p.JitterFreq = Piecewise(softness, 1.00f, 1.00f, 0.60f);
        p.MotionSmooth = Piecewise(softness, 0.40f, 0.25f, 0.15f);
    }

    public static ThighParams CreateFeelPreset(FleshPartId part, FleshFeelPreset preset)
    {
        ThighParams p = ThighParams.CreatePartDefaults(part);
        switch (preset)
        {
            case FleshFeelPreset.Stable:
                // Stable is the daily-use preset: select the responsive spring
                // solver explicitly instead of silently forcing every preset to
                // the dance-oriented chain solver.
                p.GamePhysics = false;
                SetSoftness(p, part, 0f);
                SetStrength(p, part == FleshPartId.Thigh ? 0.75f :
                    part == FleshPartId.Arm ? 0.60f : 0.55f);
                p.MotionGain = 0.75f;
                break;
            case FleshFeelPreset.Dance:
                SetSoftness(p, part, 1f);
                SetStrength(p, part == FleshPartId.Thigh ? 0.95f :
                    part == FleshPartId.Arm ? 0.80f : 0.70f);
                p.MotionGain = part == FleshPartId.Belly ? 1.20f : 1.50f;
                break;
        }
        return p;
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
