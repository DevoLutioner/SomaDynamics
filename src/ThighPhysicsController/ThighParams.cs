using System.Collections.Generic;
using ExtensibleSaveFormat;

namespace ThighPhysicsController;

public sealed class ThighParams
{
    public const string DataKey = "codex.koikatumanager.thighphysicscontroller";

    // v54 was used by the archived/broken 0.9.0 collider build; keep it skipped.
    // v60 briefly contained experimental breast/butt spring fields. v61 removes
    // them; older cards remain compatible because unknown keys are ignored.
    public const int DataVersion = 61;

    public bool Enabled = true;

    public bool GamePhysics;

    public float Gravity;

    public float Weight = 0.5f;

    /// <summary>Dance/motion response multiplier, 0..10 (1 = natural reference).</summary>
    public float MotionGain = 1f;

    /// <summary>Spring jitter/oscillation frequency (0..5, 1 = default).</summary>
    public float JitterFreq = 1f;

    /// <summary>Spring motion-response smoothing (0.05..0.5; lower = smoother).</summary>
    public float MotionSmooth = 0.25f;

    public ThighBoneParams Thigh00 = new ThighBoneParams();

    public ThighBoneAmounts Bones = new ThighBoneAmounts();

    /// <summary>Per-bone settings used only by chain mode (kept separate from spring Bones).</summary>
    public ThighBoneAmounts ChainBones = new ThighBoneAmounts();

    public ChainParams Chain = new ChainParams();

    public static ThighParams CreateDefault()
    {
        ThighParams p = new ThighParams();
        p.Enabled = true;
        p.GamePhysics = true;
        p.Gravity = 0.05f;
        p.Weight = 0.8f;
        p.MotionGain = 1f;
        p.JitterFreq = 1f;
        p.MotionSmooth = 0.25f;
        p.Thigh00.Damping = 0.18f;
        p.Thigh00.Elasticity = 0.10f;
        p.Thigh00.Stiffness = 0.12f;
        p.Thigh00.Inert = 0.35f;
        p.Chain = new ChainParams
        {
            Weight = 0.9f,
            Gravity = 0.05f,
            Damping = 0.04f,
            Elasticity = 0.08f,
            Stiffness = 0.85f,
            Inert = 0.80f,
            JitterFreq = 0.30f,
        };
        p.Bones.SetDefaults();
        p.ChainBones.SetChainDefaults();
        SetChainAmps(p.ChainBones, 1f, 0.8f, 0.3f, 0.3f);
        return p;
    }

    public static ThighParams CreatePartDefaults(FleshPartId part)
    {
        ThighParams p = CreateDefault();
        float scale = part == FleshPartId.Arm ? 0.6f : part == FleshPartId.Belly ? 0.25f : 1f;
        if (scale < 1f)
        {
            for (int i = 0; i < 4; i++)
            {
                p.Bones.Get(i).Amp *= scale;
                p.ChainBones.Get(i).Amp *= scale;
            }
        }
        if (part == FleshPartId.Arm)
        {
            p.Weight = 0.7f;
            p.Chain.Weight = 0.7f;
            p.Chain.Damping = 0.05f;
            p.Chain.Elasticity = 0.25f;
            p.Chain.Stiffness = 0.90f;
            p.Chain.Inert = 0.80f;
            p.Chain.JitterFreq = 0.15f;
            SetChainAmps(p.ChainBones, 2f, 0.6f, 0.6f, 0.072f);
        }
        else if (part == FleshPartId.Belly)
        {
            p.Weight = 0.7f;
            p.MotionGain = 1.0864f;
            p.Chain.Weight = 0.7f;
            p.Chain.Damping = 0.30f;
            p.Chain.Elasticity = 0.25f;
            p.Chain.Stiffness = 0.90f;
            p.Chain.Inert = 0.40f;
            p.Chain.JitterFreq = 1f;
            SetChainAmps(p.ChainBones, 0.25f, 0.20f, 0.125f, 0.03f);
        }
        return p;
    }

    public ThighParams Clone()
    {
        ThighParams value = new ThighParams
        {
            Enabled = Enabled,
            GamePhysics = GamePhysics,
            Gravity = Gravity,
            Weight = Weight,
            MotionGain = MotionGain,
            JitterFreq = JitterFreq,
            MotionSmooth = MotionSmooth,
            Thigh00 = new ThighBoneParams
            {
                Damping = Thigh00.Damping,
                Elasticity = Thigh00.Elasticity,
                Stiffness = Thigh00.Stiffness,
                Inert = Thigh00.Inert,
            },
            Chain = new ChainParams
            {
                Weight = Chain.Weight,
                Gravity = Chain.Gravity,
                Damping = Chain.Damping,
                Elasticity = Chain.Elasticity,
                Stiffness = Chain.Stiffness,
                Inert = Chain.Inert,
                JitterFreq = Chain.JitterFreq,
            },
        };
        CopyAmounts(Bones, value.Bones);
        CopyAmounts(ChainBones, value.ChainBones);
        return value;
    }

    private static void CopyAmounts(ThighBoneAmounts source, ThighBoneAmounts target)
    {
        for (int i = 0; i < 4; i++)
        {
            PerBoneAmount from = source.Get(i);
            PerBoneAmount to = target.Get(i);
            to.Enabled = from.Enabled;
            to.Amp = from.Amp;
            to.AxisX = from.AxisX;
            to.AxisY = from.AxisY;
            to.AxisZ = from.AxisZ;
            to.RotAmp = from.RotAmp;
            to.RotCalc = from.RotCalc;
        }
    }

    private static void SetChainAmps(ThighBoneAmounts bones, float first, float second,
        float third, float fourth)
    {
        float[] values = { first, second, third, fourth };
        for (int i = 0; i < values.Length; i++)
        {
            PerBoneAmount bone = bones.Get(i);
            bone.Enabled = true;
            bone.Amp = values[i];
            bone.AxisX = 1f;
            bone.AxisY = 1f;
            bone.AxisZ = 1f;
            bone.RotCalc = true;
        }
    }

    public void WriteData(PluginData data)
    {
        WritePart(data.data, "", this);
    }

    public void ReadData(PluginData data)
    {
        int version = 0;
        if (data.data.ContainsKey("v"))
        {
            version = FleshValue.ConvertInt32(data.data["v"], 0);
        }
        ReadPart(data.data, "", this, version);
    }

    public static void WritePart(Dictionary<string, object> data, string prefix, ThighParams p)
    {
        if (prefix.Length == 0)
        {
            data["v"] = DataVersion;
        }
        data[prefix + "enabled"] = p.Enabled;
        data[prefix + "gp"] = p.GamePhysics;
        data[prefix + "gravity"] = p.Gravity;
        data[prefix + "weight"] = p.Weight;
        data[prefix + "mg"] = p.MotionGain;
        data[prefix + "jf"] = p.JitterFreq;
        data[prefix + "ms"] = p.MotionSmooth;
        data[prefix + "c_w"] = p.Chain.Weight;
        data[prefix + "c_g"] = p.Chain.Gravity;
        data[prefix + "c_d"] = p.Chain.Damping;
        data[prefix + "c_e"] = p.Chain.Elasticity;
        data[prefix + "c_s"] = p.Chain.Stiffness;
        data[prefix + "c_i"] = p.Chain.Inert;
        data[prefix + "c_jf"] = p.Chain.JitterFreq;
        WriteBone(data, prefix + "t00", p.Thigh00);
        WriteBoneAmounts(data, prefix, p.Bones);
        WriteChainBoneAmounts(data, prefix, p.ChainBones);
    }

    public static void ReadPart(Dictionary<string, object> data, string prefix, ThighParams p, int version)
    {
        if (data.ContainsKey(prefix + "enabled"))
        {
            p.Enabled = FleshValue.ConvertBoolean(data[prefix + "enabled"], p.Enabled);
        }
        if (data.ContainsKey(prefix + "gp"))
        {
            p.GamePhysics = FleshValue.ConvertBoolean(data[prefix + "gp"], p.GamePhysics);
        }
        if (data.ContainsKey(prefix + "gravity"))
        {
            p.Gravity = ReadFloat(data, prefix + "gravity", -FleshParameterRanges.GravityMax,
                FleshParameterRanges.GravityMax, p.Gravity);
        }
        if (data.ContainsKey(prefix + "weight"))
        {
            p.Weight = ReadFloat(data, prefix + "weight", 0f,
                FleshParameterRanges.WeightMax, p.Weight);
        }
        if (data.ContainsKey(prefix + "mg"))
        {
            p.MotionGain = ReadFloat(data, prefix + "mg", 0f,
                FleshParameterRanges.MotionGainMax, p.MotionGain);
        }
        if (data.ContainsKey(prefix + "jf"))
        {
            p.JitterFreq = ReadFloat(data, prefix + "jf", 0f,
                FleshParameterRanges.JitterFrequencyMax, p.JitterFreq);
        }
        if (data.ContainsKey(prefix + "ms"))
        {
            p.MotionSmooth = ReadFloat(data, prefix + "ms", 0.05f,
                FleshParameterRanges.MotionSmoothMax, p.MotionSmooth);
        }
        if (data.ContainsKey(prefix + "c_w"))
        {
            p.Chain.Weight = ReadFloat(data, prefix + "c_w", 0f,
                FleshParameterRanges.WeightMax, p.Chain.Weight);
        }
        if (data.ContainsKey(prefix + "c_g"))
        {
            p.Chain.Gravity = ReadFloat(data, prefix + "c_g",
                -FleshParameterRanges.GravityMax, FleshParameterRanges.GravityMax,
                p.Chain.Gravity);
        }
        if (data.ContainsKey(prefix + "c_d"))
        {
            p.Chain.Damping = ReadFloat(data, prefix + "c_d", 0f, 1f, p.Chain.Damping);
        }
        if (data.ContainsKey(prefix + "c_e"))
        {
            p.Chain.Elasticity = ReadFloat(data, prefix + "c_e", 0f, 1f, p.Chain.Elasticity);
        }
        if (data.ContainsKey(prefix + "c_s"))
        {
            p.Chain.Stiffness = ReadFloat(data, prefix + "c_s", 0f, 1f, p.Chain.Stiffness);
        }
        if (data.ContainsKey(prefix + "c_i"))
        {
            p.Chain.Inert = ReadFloat(data, prefix + "c_i", 0f,
                FleshParameterRanges.CustomInertMax, p.Chain.Inert);
        }
        if (data.ContainsKey(prefix + "c_jf"))
        {
            p.Chain.JitterFreq = ReadFloat(data, prefix + "c_jf", 0f,
                FleshParameterRanges.JitterFrequencyMax, p.Chain.JitterFreq);
        }
        ReadBone(data, prefix + "t00", p.Thigh00);
        if (version < 53)
        {
            // v52 and earlier: KneeF was index 3 (b3), Leg02 was index 4 (b4).
            // KneeF is removed; Leg02 is now index 3 (b3). Migrate from b4.
            ReadLegacyBoneAmounts(data, prefix, p.Bones);
            ReadLegacyChainBoneAmounts(data, prefix, p.ChainBones);
        }
        else
        {
            ReadBoneAmounts(data, prefix, p.Bones);
            ReadChainBoneAmounts(data, prefix, p.ChainBones);
        }
        if (version < 51)
        {
            // Migration for cards saved before 0.5.0: use the current stable
            // spring midpoint instead of reviving obsolete under-damped values.
            p.Weight = 0.8f;
            p.Thigh00.Damping = 0.18f;
            p.Thigh00.Elasticity = 0.10f;
            p.Thigh00.Stiffness = 0.12f;
            p.Thigh00.Inert = 0.35f;
            p.JitterFreq = 1f;
            p.MotionSmooth = 0.25f;
        }
    }

    private static void WriteBone(Dictionary<string, object> data, string prefix, ThighBoneParams bone)
    {
        data[prefix + "_damp"] = bone.Damping;
        data[prefix + "_elas"] = bone.Elasticity;
        data[prefix + "_stif"] = bone.Stiffness;
        data[prefix + "_inert"] = bone.Inert;
    }

    private static void ReadBone(Dictionary<string, object> data, string prefix, ThighBoneParams bone)
    {
        if (data.ContainsKey(prefix + "_damp"))
        {
            bone.Damping = ReadFloat(data, prefix + "_damp", 0f, 1f, bone.Damping);
        }
        if (data.ContainsKey(prefix + "_elas"))
        {
            bone.Elasticity = ReadFloat(data, prefix + "_elas", 0f, 1f, bone.Elasticity);
        }
        if (data.ContainsKey(prefix + "_stif"))
        {
            bone.Stiffness = ReadFloat(data, prefix + "_stif", 0f, 1f, bone.Stiffness);
        }
        if (data.ContainsKey(prefix + "_inert"))
        {
            bone.Inert = ReadFloat(data, prefix + "_inert", 0f,
                FleshParameterRanges.CustomInertMax, bone.Inert);
        }
    }

    private static void WriteBoneAmounts(Dictionary<string, object> data, string prefix, ThighBoneAmounts bones)
    {
        for (int i = 0; i < 4; i++)
        {
            PerBoneAmount amount = bones.Get(i);
            data[prefix + "b" + i + "_en"] = amount.Enabled;
            data[prefix + "b" + i + "_amp"] = amount.Amp;
            data[prefix + "b" + i + "_ax"] = amount.AxisX;
            data[prefix + "b" + i + "_ay"] = amount.AxisY;
            data[prefix + "b" + i + "_az"] = amount.AxisZ;
            data[prefix + "b" + i + "_rot"] = amount.RotAmp;
            data[prefix + "b" + i + "_rc"] = amount.RotCalc;
        }
    }

    private static void ReadBoneAmounts(Dictionary<string, object> data, string prefix, ThighBoneAmounts bones)
    {
        for (int i = 0; i < 4; i++)
        {
            PerBoneAmount amount = bones.Get(i);
            if (data.ContainsKey(prefix + "b" + i + "_en"))
            {
                amount.Enabled = FleshValue.ConvertBoolean(data[prefix + "b" + i + "_en"], amount.Enabled);
            }
            if (data.ContainsKey(prefix + "b" + i + "_amp"))
            {
                amount.Amp = ReadFloat(data, prefix + "b" + i + "_amp", 0f,
                    FleshParameterRanges.BoneAmplitudeMax, amount.Amp);
            }
            if (data.ContainsKey(prefix + "b" + i + "_ax"))
            {
                amount.AxisX = ReadFloat(data, prefix + "b" + i + "_ax", 0f,
                    FleshParameterRanges.AxisScaleMax, amount.AxisX);
            }
            if (data.ContainsKey(prefix + "b" + i + "_ay"))
            {
                amount.AxisY = ReadFloat(data, prefix + "b" + i + "_ay", 0f,
                    FleshParameterRanges.AxisScaleMax, amount.AxisY);
            }
            if (data.ContainsKey(prefix + "b" + i + "_az"))
            {
                amount.AxisZ = ReadFloat(data, prefix + "b" + i + "_az", 0f,
                    FleshParameterRanges.AxisScaleMax, amount.AxisZ);
            }
            if (data.ContainsKey(prefix + "b" + i + "_rot"))
            {
                amount.RotAmp = ReadFloat(data, prefix + "b" + i + "_rot", 0f,
                    FleshParameterRanges.RotationAmplitudeMax, amount.RotAmp);
            }
            if (data.ContainsKey(prefix + "b" + i + "_rc"))
            {
                amount.RotCalc = FleshValue.ConvertBoolean(data[prefix + "b" + i + "_rc"], amount.RotCalc);
            }
        }
    }

    private static void ReadLegacyBoneAmounts(Dictionary<string, object> data, string prefix, ThighBoneAmounts bones)
    {
        // Old layout: b0..b2 same, b3 = KneeF (dropped), b4 = Leg02.
        ReadBoneAmounts(data, prefix, bones);
        PerBoneAmount leg = bones.Get(3);
        if (data.ContainsKey(prefix + "b4_en"))
        {
            leg.Enabled = FleshValue.ConvertBoolean(data[prefix + "b4_en"], leg.Enabled);
        }
        if (data.ContainsKey(prefix + "b4_amp"))
        {
            leg.Amp = ReadFloat(data, prefix + "b4_amp", 0f,
                FleshParameterRanges.BoneAmplitudeMax, leg.Amp);
        }
        if (data.ContainsKey(prefix + "b4_ax"))
        {
            leg.AxisX = ReadFloat(data, prefix + "b4_ax", 0f,
                FleshParameterRanges.AxisScaleMax, leg.AxisX);
        }
        if (data.ContainsKey(prefix + "b4_ay"))
        {
            leg.AxisY = ReadFloat(data, prefix + "b4_ay", 0f,
                FleshParameterRanges.AxisScaleMax, leg.AxisY);
        }
        if (data.ContainsKey(prefix + "b4_az"))
        {
            leg.AxisZ = ReadFloat(data, prefix + "b4_az", 0f,
                FleshParameterRanges.AxisScaleMax, leg.AxisZ);
        }
        if (data.ContainsKey(prefix + "b4_rot"))
        {
            leg.RotAmp = ReadFloat(data, prefix + "b4_rot", 0f,
                FleshParameterRanges.RotationAmplitudeMax, leg.RotAmp);
        }
        if (data.ContainsKey(prefix + "b4_rc"))
        {
            leg.RotCalc = FleshValue.ConvertBoolean(data[prefix + "b4_rc"], leg.RotCalc);
        }
    }

    private static void WriteChainBoneAmounts(Dictionary<string, object> data, string prefix, ThighBoneAmounts bones)
    {
        for (int i = 0; i < 4; i++)
        {
            PerBoneAmount amount = bones.Get(i);
            data[prefix + "cb" + i + "_en"] = amount.Enabled;
            data[prefix + "cb" + i + "_amp"] = amount.Amp;
            data[prefix + "cb" + i + "_ax"] = amount.AxisX;
            data[prefix + "cb" + i + "_ay"] = amount.AxisY;
            data[prefix + "cb" + i + "_az"] = amount.AxisZ;
            data[prefix + "cb" + i + "_rc"] = amount.RotCalc;
        }
    }

    private static void ReadChainBoneAmounts(Dictionary<string, object> data, string prefix, ThighBoneAmounts bones)
    {
        for (int i = 0; i < 4; i++)
        {
            PerBoneAmount amount = bones.Get(i);
            if (data.ContainsKey(prefix + "cb" + i + "_en"))
            {
                amount.Enabled = FleshValue.ConvertBoolean(data[prefix + "cb" + i + "_en"], amount.Enabled);
            }
            if (data.ContainsKey(prefix + "cb" + i + "_amp"))
            {
                amount.Amp = ReadFloat(data, prefix + "cb" + i + "_amp", 0f,
                    FleshParameterRanges.BoneAmplitudeMax, amount.Amp);
            }
            if (data.ContainsKey(prefix + "cb" + i + "_ax"))
            {
                amount.AxisX = ReadFloat(data, prefix + "cb" + i + "_ax", 0f,
                    FleshParameterRanges.AxisScaleMax, amount.AxisX);
            }
            if (data.ContainsKey(prefix + "cb" + i + "_ay"))
            {
                amount.AxisY = ReadFloat(data, prefix + "cb" + i + "_ay", 0f,
                    FleshParameterRanges.AxisScaleMax, amount.AxisY);
            }
            if (data.ContainsKey(prefix + "cb" + i + "_az"))
            {
                amount.AxisZ = ReadFloat(data, prefix + "cb" + i + "_az", 0f,
                    FleshParameterRanges.AxisScaleMax, amount.AxisZ);
            }
            if (data.ContainsKey(prefix + "cb" + i + "_rc"))
            {
                amount.RotCalc = FleshValue.ConvertBoolean(data[prefix + "cb" + i + "_rc"], amount.RotCalc);
            }
        }
    }

    private static void ReadLegacyChainBoneAmounts(Dictionary<string, object> data, string prefix, ThighBoneAmounts bones)
    {
        ReadChainBoneAmounts(data, prefix, bones);
        PerBoneAmount leg = bones.Get(3);
        if (data.ContainsKey(prefix + "cb4_en"))
        {
            leg.Enabled = FleshValue.ConvertBoolean(data[prefix + "cb4_en"], leg.Enabled);
        }
        if (data.ContainsKey(prefix + "cb4_amp"))
        {
            leg.Amp = ReadFloat(data, prefix + "cb4_amp", 0f,
                FleshParameterRanges.BoneAmplitudeMax, leg.Amp);
        }
        if (data.ContainsKey(prefix + "cb4_ax"))
        {
            leg.AxisX = ReadFloat(data, prefix + "cb4_ax", 0f,
                FleshParameterRanges.AxisScaleMax, leg.AxisX);
        }
        if (data.ContainsKey(prefix + "cb4_ay"))
        {
            leg.AxisY = ReadFloat(data, prefix + "cb4_ay", 0f,
                FleshParameterRanges.AxisScaleMax, leg.AxisY);
        }
        if (data.ContainsKey(prefix + "cb4_az"))
        {
            leg.AxisZ = ReadFloat(data, prefix + "cb4_az", 0f,
                FleshParameterRanges.AxisScaleMax, leg.AxisZ);
        }
        if (data.ContainsKey(prefix + "cb4_rc"))
        {
            leg.RotCalc = FleshValue.ConvertBoolean(data[prefix + "cb4_rc"], leg.RotCalc);
        }
    }

    private static float ReadFloat(Dictionary<string, object> data, string key,
        float min, float max, float fallback)
    {
        object value;
        return data.TryGetValue(key, out value)
            ? FleshValue.ConvertClamped(value, min, max, fallback)
            : fallback;
    }
}
