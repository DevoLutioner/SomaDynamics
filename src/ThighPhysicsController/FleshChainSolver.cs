using UnityEngine;

namespace ThighPhysicsController;

/// <summary>
/// Reusable chain integration primitives. Transform discovery, re-anchoring and
/// bone writes stay in the component; numerical integration lives here so the
/// single- and multi-particle paths cannot silently diverge.
/// </summary>
internal static class FleshChainSolver
{
    public static void Integrate(ChainParticle particle, Vector3 anchorPos,
        Vector3 anchorMove, Vector3 anchorAngularVelocity, Vector3 gravity,
        float weight, float motionFollow, ChainParams parameters, float dt)
    {
        dt = FleshValue.Clamp(dt, 1f / 240f, 0.05f, 1f / 60f);
        float previousDt = FleshValue.Clamp(particle.PreviousDt,
            1f / 240f, 0.05f, 1f / 60f);
        float step = dt * 60f;
        float displacementScale = dt / previousDt;
        float retentionAtReference = 1f -
            FleshValue.Clamp(parameters.Damping, 0f, 1f, 0.3f);
        float retention = (float)System.Math.Pow(retentionAtReference, step);
        Vector3 velocity = (particle.Position - particle.PrevPosition) *
                           displacementScale * retention;
        velocity = Vector3.ClampMagnitude(velocity, 0.10f * Mathf.Max(0.25f, step));
        Vector3 radial = particle.Position - anchorPos;
        // Convert MMD's per-frame skeletal rotation to the same world-distance
        // representation produced when Studio's translation gizmo moves the actor.
        Vector3 angularMove = Vector3.ClampMagnitude(Vector3.Cross(
            anchorAngularVelocity * Mathf.Deg2Rad, radial), 0.08f);
        velocity += (anchorMove + angularMove) * motionFollow +
                    gravity * weight * step;
        velocity = Vector3.ClampMagnitude(velocity, 0.22f * Mathf.Max(0.25f, step));
        particle.PrevPosition = particle.Position;
        particle.Position += velocity;
        particle.PreviousDt = dt;
    }

    public static void ApplySegmentReturn(ChainParticle particle, ChainParticle previous,
        Vector3 restDirection, ChainParams parameters, float dt)
    {
        Vector3 target = previous.Position + restDirection;
        Vector3 toTarget = target - particle.Position;
        Vector3 axialDirection = restDirection.sqrMagnitude > 0.0001f
            ? restDirection.normalized
            : Vector3.zero;
        float axialDot = Vector3.Dot(toTarget, axialDirection);
        float jitter = FleshValue.Clamp(parameters.JitterFreq, 0f,
            FleshParameterRanges.JitterFrequencyMax, 1f);
        float axialStrength = FleshSolverMath.AdjustPerFrameRate(
            Mathf.Clamp01(parameters.Stiffness * jitter), dt);
        float lateralStrength = FleshSolverMath.AdjustPerFrameRate(
            Mathf.Clamp01(parameters.Elasticity * jitter), dt);
        particle.Position += axialDirection * (axialDot * axialStrength);
        particle.Position += (toTarget - axialDirection * axialDot) * lateralStrength;

        float maxLength = Mathf.Lerp(particle.RestLength * 1.25f,
            particle.RestLength, parameters.Stiffness);
        Vector3 delta = target - particle.Position;
        float magnitude = delta.magnitude;
        if (magnitude > maxLength && magnitude > 0.0001f)
        {
            particle.Position += delta * ((magnitude - maxLength) / magnitude);
        }
    }

    public static void ApplySingleParticleReturn(ChainParticle particle,
        Vector3 worldBase, ChainParams parameters, float dt)
    {
        float strength = FleshSolverMath.AdjustPerFrameRate(
            FleshSolverMath.SingleParticleReturnStrength(parameters), dt);
        particle.Position += (worldBase - particle.Position) * strength;
    }

    public static void ApplyLeash(ChainParticle particle, Vector3 worldBase, float limit)
    {
        Vector3 offset = particle.Position - worldBase;
        if (offset.magnitude > limit)
        {
            particle.Position = worldBase + offset.normalized * limit;
        }
    }
}
