namespace ThighPhysicsController;

/// <summary>Shared scalar policy for spring integration and bone writes.</summary>
internal static class FleshSpringSolver
{
    /// <summary>
    /// Suppresses sub-pixel noise without disabling low-amplitude parts. The old
    /// fixed 0.3 mm threshold was larger than Belly's useful offset after its 0.25x
    /// per-bone amplitude, so Belly spring motion was snapped to zero every frame.
    /// </summary>
    public static float ActivationThreshold(float amplitude)
    {
        amplitude = FleshValue.Clamp(amplitude, 0f, 2f, 1f);
        float scale = amplitude < 0.15f ? 0.15f : amplitude > 1f ? 1f : amplitude;
        return 0.0003f * scale;
    }

    /// <summary>
    /// Belly's default spring Amp is 0.25, and the spring drive already multiplies
    /// by Amp before the final write limit. Compensate that input attenuation once;
    /// the output remains bounded by the original per-bone amplitude limit.
    /// </summary>
    public static float PartDriveScale(FleshPartId part)
    {
        return part == FleshPartId.Belly ? 4f : 1f;
    }

    /// <summary>
    /// Converts the user-facing damping strength into a frame-rate-independent
    /// velocity retention. Preserve the full 0..1 damping range: the previous
    /// 0.20 cap made every tighter value behave identically and allowed the hard
    /// end of the simple softness control to ring more than the middle.
    /// </summary>
    public static float VelocityRetention(float damping, float dt)
    {
        damping = FleshValue.Clamp(damping, 0f, 1f, 0.18f);
        return 1f - FleshSolverMath.AdjustPerFrameRate(damping, dt);
    }
}
