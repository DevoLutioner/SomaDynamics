using UnityEngine;
using System;

namespace ThighPhysicsController;

/// <summary>Shared, side-effect-free scalar mappings used by both solvers.</summary>
internal static class FleshSolverMath
{
    public static float DanceResponseScale(float gain, float weight, float inert)
    {
        gain = FleshValue.Clamp(gain, 0f, 5f, 1f);
        weight = FleshValue.Clamp(weight, 0f, 1f, 0.7f);
        inert = FleshValue.Clamp(inert, 0f, 1f, 0.4f);
        return gain * (weight / 0.8f) * ((0.25f + inert) / 0.6f);
    }

    public static float SingleParticleReturnStrength(ChainParams parameters)
    {
        float jitter = FleshValue.Clamp(parameters.JitterFreq, 0f, 2.5f, 1f);
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
