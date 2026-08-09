namespace ThighPhysicsController;

public enum FleshPartId
{
    Thigh = 0,
    Arm = 1,
    Belly = 2,
    Breast = 3,
    Butt = 4,
}

public sealed class FleshChainDef
{
    /// <summary>Anchor bone name; "{side}" is replaced with L/R for paired parts.</summary>
    public string AnchorTemplate;

    /// <summary>Flesh bone name templates ("{side}" replaced for paired parts).</summary>
    public string[] BoneNameTemplates;

    /// <summary>Per-part bone indexes (0..n-1).</summary>
    public int[] BoneIndexes;

    public bool Paired;
}

public sealed class FleshPartDef
{
    public FleshPartId Id;
    public string DisplayName;
    public string DataPrefix;
    public string XmlSection;
    public FleshChainDef[] Chains;

    public static FleshPartDef Get(FleshPartId id)
    {
        switch (id)
        {
            case FleshPartId.Arm:
                return new FleshPartDef
                {
                    Id = FleshPartId.Arm,
                    DisplayName = "Arm",
                    DataPrefix = "arm_",
                    XmlSection = "ArmPart",
                    Chains = new[]
                    {
                        new FleshChainDef
                        {
                            AnchorTemplate = "cf_j_arm00_{side}",
                            BoneNameTemplates = new[] { "cf_s_arm01_{side}", "cf_s_arm02_{side}", "cf_s_arm03_{side}" },
                            BoneIndexes = new[] { 0, 1, 2 },
                            Paired = true,
                        },
                    },
                };
            case FleshPartId.Belly:
                return new FleshPartDef
                {
                    Id = FleshPartId.Belly,
                    DisplayName = "Belly",
                    DataPrefix = "belly_",
                    XmlSection = "BellyPart",
                    Chains = new[]
                    {
                        new FleshChainDef
                        {
                            AnchorTemplate = "cf_j_spine03",
                            // Only cf_s_waist01 is fleshy. cf_s_spine03 is a rigid
                            // upper-spine bone (visually near the shoulders) and is
                            // excluded so the belly does not wobble it. cf_s_waist02
                            // is also excluded on purpose: it is a structural bone
                            // whose children include cf_s_leg_L/R (the legs), so
                            // displacing it during dance breaks the body mesh.
                            BoneNameTemplates = new[] { "cf_s_waist01" },
                            BoneIndexes = new[] { 0 },
                            Paired = false,
                        },
                    },
                };
            default:
                return new FleshPartDef
                {
                    Id = FleshPartId.Thigh,
                    DisplayName = "Thigh",
                    DataPrefix = "",
                    XmlSection = "",
                    Chains = new[]
                    {
                        new FleshChainDef
                        {
                            AnchorTemplate = "cf_j_thigh00_{side}",
                            BoneNameTemplates = new[]
                            {
                                "cf_s_thigh01_{side}", "cf_s_thigh02_{side}",
                                "cf_s_thigh03_{side}", "cf_s_leg02_{side}",
                            },
                            BoneIndexes = new[] { 0, 1, 2, 3 },
                            Paired = true,
                        },
                    },
                };
        }
    }
}
