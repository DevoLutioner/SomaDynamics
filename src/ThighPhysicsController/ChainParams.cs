namespace ThighPhysicsController;

/// <summary>
/// Dedicated parameter set for "Game DynamicBone chain physics" mode.
/// Kept separate from the spring-mode params so the two modes never share sliders
/// with different meanings.
/// </summary>
public sealed class ChainParams
{
    /// <summary>Overall chain drive gain (anchor move + gravity).</summary>
    public float Weight = 0.8f;

    public float Gravity = 0.05f;

    /// <summary>Velocity retention factor (higher stops faster).</summary>
    public float Damping = 0.35f;

    /// <summary>Spring pull toward the rest direction per frame.</summary>
    public float Elasticity = 0.25f;

    /// <summary>Length constraint tightness; 1 = keeps exact rest length.</summary>
    public float Stiffness = 0.9f;

    /// <summary>ObjectMove (anchor motion) inertia.</summary>
    public float Inert = 0.35f;

    /// <summary>Chain jitter/oscillation frequency (0..2.5, 1 = default).</summary>
    public float JitterFreq = 1f;

}
