namespace ThighPhysicsController;

/// <summary>
/// Authoritative UI, card and preset ranges. Effect multipliers deliberately have
/// headroom above the natural 0..1 target range; normalized solver coefficients
/// stay inside the domains enforced by Unity and DynamicBone_Ver02.
/// </summary>
internal static class FleshParameterRanges
{
    public const float TargetMax = 2f;
    public const float MotionGainMax = 10f;
    public const float WeightMax = 2f;
    public const float GravityMax = 0.4f;
    public const float JitterFrequencyMax = 5f;
    public const float MotionSmoothMax = 1f;
    public const float BoneAmplitudeMax = 4f;
    public const float AxisScaleMax = 2f;
    public const float RotationAmplitudeMax = 2f;
    public const float NativeGravityMax = 0.003f;
    public const float CustomInertMax = 1.5f;

    // DynamicBone_Ver02 and the stable numerical integrators use normalized
    // coefficients. The game itself Clamp01s these values when a pattern applies.
    public const float NormalizedCoefficientMax = 1f;
}
