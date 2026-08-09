using UnityEngine;
using System;

namespace ThighPhysicsController;

/// <summary>Shared, side-effect-free scalar mappings used by both solvers.</summary>
internal static class FleshSolverMath
{
    /// <summary>Maps Quaternion.ToAngleAxis output to a signed shortest-path angle.</summary>
    public static float NormalizeSignedAngle(float angle)
    {
        if (float.IsNaN(angle) || float.IsInfinity(angle))
        {
            return 0f;
        }
        angle %= 360f;
        if (angle > 180f)
        {
            angle -= 360f;
        }
        else if (angle < -180f)
        {
            angle += 360f;
        }
        return angle;
    }

    /// <summary>Returns the middle value without averaging away valid motion.</summary>
    public static float Median3(float a, float b, float c)
    {
        if (a > b)
        {
            float swap = a;
            a = b;
            b = swap;
        }
        if (b > c)
        {
            b = c;
        }
        return a > b ? a : b;
    }

    public static Vector3 Median3(Vector3 a, Vector3 b, Vector3 c)
    {
        return new Vector3(Median3(a.x, b.x, c.x), Median3(a.y, b.y, c.y),
            Median3(a.z, b.z, c.z));
    }

    public static float DanceResponseScale(float gain, float weight, float inert)
    {
        gain = FleshValue.Clamp(gain, 0f, FleshParameterRanges.MotionGainMax, 1f);
        weight = FleshValue.Clamp(weight, 0f, FleshParameterRanges.WeightMax, 0.7f);
        inert = FleshValue.Clamp(inert, 0f, 1.5f, 0.4f);
        return gain * (weight / 0.8f) * ((0.25f + inert) / 0.6f);
    }

    /// <summary>
    /// Fraction of animated root motion carried into a chain particle. A larger
    /// target deliberately means less following and therefore more visible lag.
    /// </summary>
    public static float MotionFollowFraction(float target)
    {
        target = FleshValue.Clamp(target, 0f, FleshParameterRanges.TargetMax, 0.2f);
        return Mathf.Lerp(0.92f, 0.05f, Mathf.Clamp01(target));
    }

    public static float TargetRangeScale(float strength, float motionTarget)
    {
        strength = FleshValue.Clamp(strength, 0f, FleshParameterRanges.TargetMax, 0.7f);
        motionTarget = FleshValue.Clamp(motionTarget, 0f,
            FleshParameterRanges.TargetMax, 0.2f);
        float naturalBlend = Mathf.Clamp01(strength) * 0.55f +
                             Mathf.Clamp01(motionTarget) * 0.45f;
        float naturalRange = Mathf.Lerp(0.85f, 1.35f, naturalBlend);
        float strengthExtra = strength > 1f ? strength - 1f : 0f;
        float motionExtra = motionTarget > 1f ? motionTarget - 1f : 0f;
        float extra = Mathf.Clamp01(strengthExtra * 0.55f + motionExtra * 0.45f);
        return Mathf.Lerp(naturalRange, 2f, extra);
    }

    public static float SingleParticleReturnStrength(ChainParams parameters)
    {
        float jitter = FleshValue.Clamp(parameters.JitterFreq, 0f,
            FleshParameterRanges.JitterFrequencyMax, 1f);
        float elasticity = FleshValue.Clamp(parameters.Elasticity, 0f, 1f, 0.25f);
        float stiffness = FleshValue.Clamp(parameters.Stiffness, 0f, 1f, 0.9f);
        return Mathf.Clamp01((elasticity * 0.75f + stiffness * 0.10f) * jitter);
    }

    public static float AdjustPerFrameRate(float referenceStrength, float dt)
    {
        referenceStrength = FleshValue.Clamp(referenceStrength, 0f, 1f, 0f);
        dt = FleshValue.Clamp(dt, 1f / 240f, 0.05f, 1f / 60f);
        double referenceSteps = dt * 60d;
        return 1f - (float)Math.Pow(1f - referenceStrength, referenceSteps);
    }

    public static float ChainMotionTimeScale(FleshPartId part, float dt)
    {
        if (part != FleshPartId.Thigh)
        {
            return 1f;
        }
        dt = FleshValue.Clamp(dt, 1f / 240f, 0.05f, 1f / 60f);
        float step = dt * 60f;
        if (step >= 1f)
        {
            return Mathf.Lerp(1f, 1.4f, Mathf.Clamp01(step - 1f));
        }
        return Mathf.Lerp(0.8f, 1f, Mathf.InverseLerp(0.25f, 1f, step));
    }
}
