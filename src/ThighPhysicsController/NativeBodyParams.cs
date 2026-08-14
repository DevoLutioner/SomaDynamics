using System.Collections.Generic;

namespace ThighPhysicsController;

/// <summary>
/// Compact user model for the game's native breast/butt DynamicBone_Ver02 chains.
/// At the natural midpoint these values reproduce the user's former BPC Soft.xml.
/// </summary>
public sealed class NativeBodyParams
{
    public bool Enabled = true;
    public float Strength = 0.5f;
    public float Softness = 0.75f;
    public float MotionResponse = 0.5f;
    public float Gravity;
    public bool AdvancedOverride;
    public NativeBoneParams Bone0 = new NativeBoneParams();
    public NativeBoneParams Bone1 = new NativeBoneParams();
    public NativeBoneParams Bone2 = new NativeBoneParams();

    public static NativeBodyParams CreateDefault(FleshPartId part)
    {
        NativeBodyParams value = new NativeBodyParams
        {
            Enabled = true,
            Strength = 0.5f,
            Softness = 0.75f,
            MotionResponse = 0.5f,
            Gravity = 0f,
        };
        if (part == FleshPartId.Breast)
        {
            value.Bone0.Set(false, 0.08f, 0.15f, 0.25f, 0.60f);
            value.Bone1.Set(true, 0.05f, 0.08f, 0.07f, 0.50f);
            value.Bone2.Set(true, 0.05f, 0.08f, 0.07f, 0.50f);
        }
        else
        {
            value.Bone0.Set(false, 0.03f, 0.12f, 0.09f, 0.25f);
            value.Bone1.Set(true, 0.03f, 0.08f, 0.05f, 0.25f);
            value.Bone2.Set(true, 0.03f, 0.08f, 0.05f, 0.25f);
        }
        return value;
    }

    public static void WritePart(Dictionary<string, object> data, string prefix,
        NativeBodyParams value)
    {
        data[prefix + "enabled"] = value.Enabled;
        data[prefix + "strength"] = value.Strength;
        data[prefix + "softness"] = value.Softness;
        data[prefix + "response"] = value.MotionResponse;
        data[prefix + "gravity"] = value.Gravity;
        data[prefix + "advanced"] = value.AdvancedOverride;
        WriteBone(data, prefix + "bone0_", value.Bone0);
        WriteBone(data, prefix + "bone1_", value.Bone1);
        WriteBone(data, prefix + "bone2_", value.Bone2);
    }

    public static void ReadPart(Dictionary<string, object> data, string prefix,
        NativeBodyParams value, int dataVersion = int.MaxValue)
    {
        if (data.ContainsKey(prefix + "enabled"))
            value.Enabled = FleshValue.ConvertBoolean(data[prefix + "enabled"], value.Enabled);
        if (data.ContainsKey(prefix + "strength"))
            value.Strength = ReadFloat(data, prefix + "strength", 0f,
                FleshParameterRanges.TargetMax, value.Strength);
        if (data.ContainsKey(prefix + "softness"))
            value.Softness = ReadFloat(data, prefix + "softness", 0f,
                FleshParameterRanges.TargetMax, value.Softness);
        if (data.ContainsKey(prefix + "response"))
            value.MotionResponse = ReadFloat(data, prefix + "response", 0f,
                FleshParameterRanges.TargetMax, value.MotionResponse);
        if (data.ContainsKey(prefix + "gravity"))
            value.Gravity = ReadFloat(data, prefix + "gravity",
                -FleshParameterRanges.NativeGravityMax,
                FleshParameterRanges.NativeGravityMax, value.Gravity);
        if (data.ContainsKey(prefix + "advanced"))
            value.AdvancedOverride = FleshValue.ConvertBoolean(data[prefix + "advanced"],
                value.AdvancedOverride);
        ReadBone(data, prefix + "bone0_", value.Bone0);
        ReadBone(data, prefix + "bone1_", value.Bone1);
        ReadBone(data, prefix + "bone2_", value.Bone2);
        if (dataVersion < 59)
        {
            // v58 simple controls exposed implementation details: Strength=1 was
            // merely the BPC baseline and larger Inert meant less visible swing.
            // Preserve the old physical result while migrating to centered,
            // directionally consistent 0..1 target controls.
            if (data.ContainsKey(prefix + "strength"))
                value.Strength = FleshValue.Clamp(value.Strength * 0.5f, 0f, 1f, 0.5f);
            if (data.ContainsKey(prefix + "response"))
                value.MotionResponse = FleshValue.Clamp(1f - value.MotionResponse * 0.5f,
                    0f, FleshParameterRanges.TargetMax, 0.5f);
        }
    }

    public NativeBodyParams Clone()
    {
        return new NativeBodyParams
        {
            Enabled = Enabled,
            Strength = Strength,
            Softness = Softness,
            MotionResponse = MotionResponse,
            Gravity = Gravity,
            AdvancedOverride = AdvancedOverride,
            Bone0 = Bone0.Clone(),
            Bone1 = Bone1.Clone(),
            Bone2 = Bone2.Clone(),
        };
    }

    public NativeBoneParams GetBone(int index)
    {
        return index == 0 ? Bone0 : index == 1 ? Bone1 : Bone2;
    }

    private static void WriteBone(Dictionary<string, object> data, string prefix,
        NativeBoneParams bone)
    {
        data[prefix + "rotation"] = bone.IsRotationCalc;
        data[prefix + "damping"] = bone.Damping;
        data[prefix + "elasticity"] = bone.Elasticity;
        data[prefix + "stiffness"] = bone.Stiffness;
        data[prefix + "inert"] = bone.Inert;
    }

    private static void ReadBone(Dictionary<string, object> data, string prefix,
        NativeBoneParams bone)
    {
        if (data.ContainsKey(prefix + "rotation"))
            bone.IsRotationCalc = FleshValue.ConvertBoolean(data[prefix + "rotation"],
                bone.IsRotationCalc);
        if (data.ContainsKey(prefix + "damping"))
            bone.Damping = ReadFloat(data, prefix + "damping", 0f, 1f, bone.Damping);
        if (data.ContainsKey(prefix + "elasticity"))
            bone.Elasticity = ReadFloat(data, prefix + "elasticity", 0f, 1f, bone.Elasticity);
        if (data.ContainsKey(prefix + "stiffness"))
            bone.Stiffness = ReadFloat(data, prefix + "stiffness", 0f, 1f, bone.Stiffness);
        if (data.ContainsKey(prefix + "inert"))
            bone.Inert = ReadFloat(data, prefix + "inert", 0f, 1f, bone.Inert);
    }

    private static float ReadFloat(Dictionary<string, object> data, string key,
        float min, float max, float fallback)
    {
        return FleshValue.ConvertClamped(data[key], min, max, fallback);
    }
}

public sealed class NativeBoneParams
{
    public bool IsRotationCalc;
    public float Damping;
    public float Elasticity;
    public float Stiffness;
    public float Inert;

    public void Set(bool rotation, float damping, float elasticity, float stiffness,
        float inert)
    {
        IsRotationCalc = rotation;
        Damping = damping;
        Elasticity = elasticity;
        Stiffness = stiffness;
        Inert = inert;
    }

    public NativeBoneParams Clone()
    {
        var value = new NativeBoneParams();
        value.Set(IsRotationCalc, Damping, Elasticity, Stiffness, Inert);
        return value;
    }
}

/// <summary>Full BPC-compatible bust state: naked plus bra/tops for 7 coordinates.</summary>
public sealed class NativeBustProfile
{
    public NativeBodyParams Naked = NativeBodyParams.CreateDefault(FleshPartId.Breast);
    public NativeBodyParams[] Bra = CreateStates();
    public NativeBodyParams[] Tops = CreateStates();

    private static NativeBodyParams[] CreateStates()
    {
        var values = new NativeBodyParams[7];
        for (int i = 0; i < values.Length; i++)
            values[i] = NativeBodyParams.CreateDefault(FleshPartId.Breast);
        return values;
    }

    public NativeBodyParams Get(int coordinate, int wearState)
    {
        coordinate = coordinate < 0 ? 0 : coordinate > 6 ? 6 : coordinate;
        return wearState == 0 ? Naked : wearState == 1 ? Bra[coordinate] : Tops[coordinate];
    }

    public void Set(int coordinate, int wearState, NativeBodyParams value)
    {
        coordinate = coordinate < 0 ? 0 : coordinate > 6 ? 6 : coordinate;
        if (wearState == 0)
            Naked = value;
        else if (wearState == 1)
            Bra[coordinate] = value;
        else
            Tops[coordinate] = value;
    }

    public void SetAll(NativeBodyParams value)
    {
        Naked = value.Clone();
        for (int i = 0; i < 7; i++)
        {
            Bra[i] = value.Clone();
            Tops[i] = value.Clone();
        }
    }

    public void SetEnabledAll(bool enabled)
    {
        Naked.Enabled = enabled;
        for (int i = 0; i < 7; i++)
        {
            Bra[i].Enabled = enabled;
            Tops[i].Enabled = enabled;
        }
    }

    public void SetTargetsAll(float strength, float softness, float motion,
        bool setStrength, bool setSoftness, bool setMotion)
    {
        SetSelectedTargets(Naked, strength, softness, motion, setStrength, setSoftness,
            setMotion);
        for (int i = 0; i < 7; i++)
        {
            SetSelectedTargets(Bra[i], strength, softness, motion, setStrength,
                setSoftness, setMotion);
            SetSelectedTargets(Tops[i], strength, softness, motion, setStrength,
                setSoftness, setMotion);
        }
    }

    public void SetGravityAll(float gravity)
    {
        Naked.Gravity = gravity;
        for (int i = 0; i < 7; i++)
        {
            Bra[i].Gravity = gravity;
            Tops[i].Gravity = gravity;
        }
    }

    private static void SetSelectedTargets(NativeBodyParams value, float strength,
        float softness, float motion, bool setStrength, bool setSoftness, bool setMotion)
    {
        NativeBodyTuning.SetTargets(value, FleshPartId.Breast,
            setStrength ? strength : value.Strength,
            setSoftness ? softness : value.Softness,
            setMotion ? motion : value.MotionResponse);
    }

    public NativeBustProfile Clone()
    {
        var value = new NativeBustProfile();
        value.Naked = Naked.Clone();
        for (int i = 0; i < 7; i++)
        {
            value.Bra[i] = Bra[i].Clone();
            value.Tops[i] = Tops[i].Clone();
        }
        return value;
    }

    public static void Write(Dictionary<string, object> data, string prefix,
        NativeBustProfile profile)
    {
        NativeBodyParams.WritePart(data, prefix + "naked_", profile.Naked);
        for (int i = 0; i < 7; i++)
        {
            NativeBodyParams.WritePart(data, prefix + "c" + i + "_bra_", profile.Bra[i]);
            NativeBodyParams.WritePart(data, prefix + "c" + i + "_tops_", profile.Tops[i]);
        }
    }

    public static void Read(Dictionary<string, object> data, string prefix,
        NativeBustProfile profile, int dataVersion)
    {
        if (dataVersion < 58)
        {
            NativeBodyParams legacy = NativeBodyParams.CreateDefault(FleshPartId.Breast);
            NativeBodyParams.ReadPart(data, "breast_", legacy, dataVersion);
            profile.SetAll(legacy);
            return;
        }
        NativeBodyParams.ReadPart(data, prefix + "naked_", profile.Naked, dataVersion);
        for (int i = 0; i < 7; i++)
        {
            NativeBodyParams.ReadPart(data, prefix + "c" + i + "_bra_", profile.Bra[i],
                dataVersion);
            NativeBodyParams.ReadPart(data, prefix + "c" + i + "_tops_", profile.Tops[i],
                dataVersion);
        }
    }
}

internal static class NativeBodyTuning
{
    internal static string NormalizeBoneName(string name)
    {
        return (name ?? string.Empty).Replace("_R", "_L");
    }

    internal static float TuneSoftness(float baseline, float softness,
        float tightScale, float softScale)
    {
        softness = FleshValue.Clamp(softness, 0f, FleshParameterRanges.TargetMax, 0.75f);
        float scale;
        if (softness <= 0.75f)
            scale = UnityEngine.Mathf.Lerp(tightScale, 1f, softness / 0.75f);
        else if (softness <= 1f)
            scale = UnityEngine.Mathf.Lerp(1f, softScale, (softness - 0.75f) / 0.25f);
        else
            scale = UnityEngine.Mathf.Lerp(softScale, softScale * 0.45f, softness - 1f);
        return UnityEngine.Mathf.Clamp01(baseline * scale);
    }

    internal static float TargetInert(float baseline, float motionTarget)
    {
        motionTarget = FleshValue.Clamp(motionTarget, 0f,
            FleshParameterRanges.TargetMax, 0.5f);
        float normalized = motionTarget < 1f ? motionTarget : 1f;
        float scale = normalized <= 0.5f
            ? UnityEngine.Mathf.Lerp(1.6f, 1f, normalized * 2f)
            : UnityEngine.Mathf.Lerp(1f, 0f, (normalized - 0.5f) * 2f);
        return UnityEngine.Mathf.Clamp01(baseline * scale);
    }

    internal static void ApplyStrengthTarget(ref float damping, ref float elasticity,
        ref float stiffness, float strength)
    {
        strength = FleshValue.Clamp(strength, 0f, FleshParameterRanges.TargetMax, 0.5f);
        if (strength < 0.5f)
        {
            float t = strength * 2f;
            damping = UnityEngine.Mathf.Lerp(0.35f, damping, t);
            elasticity = UnityEngine.Mathf.Lerp(0.30f, elasticity, t);
            stiffness = UnityEngine.Mathf.Lerp(0.85f, stiffness, t);
            return;
        }
        float enhanced = UnityEngine.Mathf.Clamp01((strength - 0.5f) * 2f);
        damping = UnityEngine.Mathf.Lerp(damping, damping * 0.55f, enhanced);
        elasticity = UnityEngine.Mathf.Lerp(elasticity, elasticity * 0.75f, enhanced);
        stiffness = UnityEngine.Mathf.Lerp(stiffness, stiffness * 0.45f, enhanced);
        if (strength > 1f)
        {
            float extra = strength - 1f;
            damping = UnityEngine.Mathf.Lerp(damping, damping * 0.35f, extra);
            elasticity = UnityEngine.Mathf.Lerp(elasticity, elasticity * 0.55f, extra);
            stiffness = UnityEngine.Mathf.Lerp(stiffness, stiffness * 0.30f, extra);
        }
    }

    internal static void ApplyMotionTarget(ref float damping, ref float elasticity,
        ref float stiffness, float motionTarget)
    {
        motionTarget = FleshValue.Clamp(motionTarget, 0f,
            FleshParameterRanges.TargetMax, 0.5f);
        if (motionTarget <= 1f)
            return;
        float extra = motionTarget - 1f;
        damping = UnityEngine.Mathf.Lerp(damping, damping * 0.55f, extra);
        elasticity = UnityEngine.Mathf.Lerp(elasticity, elasticity * 0.75f, extra);
        stiffness = UnityEngine.Mathf.Lerp(stiffness, stiffness * 0.60f, extra);
    }

    internal static void SetTargets(NativeBodyParams value, FleshPartId part,
        float strength, float softness, float motion)
    {
        if (value == null)
            return;
        value.Strength = FleshValue.Clamp(strength, 0f,
            FleshParameterRanges.TargetMax, value.Strength);
        value.Softness = FleshValue.Clamp(softness, 0f,
            FleshParameterRanges.TargetMax, value.Softness);
        value.MotionResponse = FleshValue.Clamp(motion, 0f,
            FleshParameterRanges.TargetMax, value.MotionResponse);
        value.AdvancedOverride = false;
    }

    internal static NativeBodyParams CreateFeelPreset(FleshPartId part, FleshFeelPreset preset)
    {
        NativeBodyParams value = NativeBodyParams.CreateDefault(part);
        switch (preset)
        {
            case FleshFeelPreset.Stable:
                value.Strength = part == FleshPartId.Breast ? 0.40f : 0.38f;
                value.Softness = 0.50f;
                value.MotionResponse = 0.40f;
                break;
            case FleshFeelPreset.Dance:
                value.Strength = part == FleshPartId.Breast ? 0.95f : 1.20f;
                value.Softness = part == FleshPartId.Breast ? 0.90f : 1.20f;
                value.MotionResponse = part == FleshPartId.Breast ? 0.78f : 0.75f;
                break;
            default:
                value.Strength = part == FleshPartId.Breast ? 0.50f : 0.58f;
                value.Softness = 0.75f;
                value.MotionResponse = 0.60f;
                break;
        }
        return value;
    }
}
