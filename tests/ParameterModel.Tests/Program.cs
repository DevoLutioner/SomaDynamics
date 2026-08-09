using System;
using System.Collections.Generic;
using ExtensibleSaveFormat;
using ThighPhysicsController;

internal static class Program
{
    private const float Epsilon = 0.0001f;
    private static int _assertions;

    private static int Main()
    {
        try
        {
            TestDefaultBaselines();
            TestNativeBodyDefaults();
            TestNativeBodyTargets();
            TestNativeBustProfilePersistence();
            TestStrengthRoundTrip();
            TestSoftnessRoundTripAndMonotonicity();
            TestFeelPresets();
            TestFiniteValueBoundary();
            TestSolverScalarMath();
            TestIndependentObjects();
            TestCardBoundsAndDeadKeys();
            Console.WriteLine("PASS ParameterModel.Tests (" + _assertions + " assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL ParameterModel.Tests: " + ex.Message);
            return 1;
        }
    }

    private static void TestDefaultBaselines()
    {
        Equal(61, ThighParams.DataVersion, "data version");
        AssertBaseline(FleshPartId.Thigh, 0.90f, 0.04f, 0.05f, 0.85f, 0.80f, 0.20f,
            new[] { 1f, 0.8f, 0.3f, 0.3f });
        AssertBaseline(FleshPartId.Arm, 0.70f, 0.05f, 0.25f, 0.90f, 0.80f, 0.15f,
            new[] { 2f, 0.6f, 0.6f, 0.072f });
        AssertBaseline(FleshPartId.Belly, 0.70f, 0.30f, 0.25f, 0.90f, 0.40f, 1.00f,
            new[] { 0.25f, 0.20f, 0.125f, 0.03f });
    }

    private static void TestNativeBodyDefaults()
    {
        foreach (FleshPartId part in new[] { FleshPartId.Breast, FleshPartId.Butt })
        {
            NativeBodyParams value = NativeBodyParams.CreateDefault(part);
            True(value.Enabled, part + " native enabled");
            Near(0.5f, value.Strength, part + " BPC Soft target strength midpoint");
            Near(0.75f, value.Softness, part + " BPC Soft softness pivot");
            Near(0.5f, value.MotionResponse, part + " BPC Soft motion midpoint");
            Near(0f, value.Gravity, part + " BPC Soft gravity");
            PluginData card = new PluginData();
            NativeBodyParams.WritePart(card.data, "native_", value);
            True(!card.data.ContainsKey("native_springMode"),
                part + " removed spring mode is not serialized");
            True(!card.data.ContainsKey("native_spring_enabled"),
                part + " removed spring parameters are not serialized");
            card.data["native_strength"] = 7f;
            card.data["native_softness"] = -2f;
            card.data["native_response"] = float.NaN;
            card.data["native_gravity"] = 9f;
            NativeBodyParams loaded = NativeBodyParams.CreateDefault(part);
            NativeBodyParams.ReadPart(card.data, "native_", loaded);
            Near(2f, loaded.Strength, part + " strength clamp");
            Near(0f, loaded.Softness, part + " softness clamp");
            Near(0.5f, loaded.MotionResponse, part + " response finite fallback");
            Near(0.003f, loaded.Gravity, part + " gravity clamp");
        }
    }

    private static void TestNativeBustProfilePersistence()
    {
        NativeBustProfile profile = new NativeBustProfile();
        profile.Bra[3].AdvancedOverride = true;
        profile.Bra[3].Bone1.Set(false, 0.11f, 0.22f, 0.33f, 0.44f);
        profile.Tops[5].Softness = 0.91f;
        PluginData card = new PluginData();
        NativeBustProfile.Write(card.data, "breast_", profile);
        NativeBustProfile loaded = new NativeBustProfile();
        NativeBustProfile.Read(card.data, "breast_", loaded, 61);
        True(loaded.Bra[3].AdvancedOverride, "bust advanced override persisted");
        True(!loaded.Bra[3].Bone1.IsRotationCalc, "bust rotation flag persisted");
        Near(0.11f, loaded.Bra[3].Bone1.Damping, "bust damping persisted");
        Near(0.22f, loaded.Bra[3].Bone1.Elasticity, "bust elasticity persisted");
        Near(0.33f, loaded.Bra[3].Bone1.Stiffness, "bust stiffness persisted");
        Near(0.44f, loaded.Bra[3].Bone1.Inert, "bust inert persisted");
        Near(0.91f, loaded.Tops[5].Softness, "tops coordinate persisted");
        Near(0.75f, loaded.Tops[4].Softness, "coordinate slots stay independent");

        NativeBodyParams source = NativeBodyParams.CreateDefault(FleshPartId.Breast);
        profile.SetAll(source);
        profile.Bra[0].Bone1.Damping = 0.99f;
        Near(0.05f, profile.Bra[1].Bone1.Damping, "set-all uses deep clones");
        Near(0.05f, profile.Naked.Bone1.Damping, "naked state clone is independent");

        PluginData legacy = new PluginData();
        legacy.data["breast_softness"] = 0.42f;
        NativeBustProfile migrated = new NativeBustProfile();
        NativeBustProfile.Read(legacy.data, "breast_", migrated, 57);
        Near(0.42f, migrated.Naked.Softness, "legacy naked migration");
        Near(0.42f, migrated.Bra[6].Softness, "legacy bra migration");
        Near(0.42f, migrated.Tops[6].Softness, "legacy tops migration");
        Near(0.5f, migrated.Naked.Strength,
            "missing legacy strength keeps current midpoint");
        Near(0.5f, migrated.Naked.MotionResponse,
            "missing legacy motion keeps current midpoint");

        Dictionary<string, object> v58 = new Dictionary<string, object>();
        v58["native_strength"] = 1f;
        v58["native_response"] = 0f;
        NativeBodyParams targetMigrated = NativeBodyParams.CreateDefault(FleshPartId.Butt);
        NativeBodyParams.ReadPart(v58, "native_", targetMigrated, 58);
        Near(0.5f, targetMigrated.Strength, "v58 BPC baseline becomes target midpoint");
        Near(1f, targetMigrated.MotionResponse,
            "v58 low inert request becomes high visible motion target");
        True(NativeBodyTuning.NormalizeBoneName("cf_j_siri_R_01") == "cf_j_siri_L_01",
            "right butt embedded side marker normalizes like BPC");
        True(NativeBodyTuning.NormalizeBoneName("cf_d_siri01_R") == "cf_d_siri01_L",
            "right butt suffix side marker normalizes like BPC");

        float damping = 0.05f;
        float elasticity = 0.08f;
        float stiffness = 0.07f;
        NativeBodyTuning.ApplyStrengthTarget(ref damping, ref elasticity, ref stiffness, 0.5f);
        Near(0.05f, damping, "native target midpoint preserves BPC damping");
        Near(0.08f, elasticity, "native target midpoint preserves BPC elasticity");
        Near(0.07f, stiffness, "native target midpoint preserves BPC stiffness");
        Near(0.5f, NativeBodyTuning.TargetInert(0.5f, 0.5f),
            "native motion midpoint preserves BPC inert");
        Near(0f, NativeBodyTuning.TargetInert(0.5f, 1f),
            "higher native motion target creates maximum lag");
        Near(0.8f, NativeBodyTuning.TargetInert(0.5f, 0f),
            "lower native motion target follows the body");

        float softAtOne = NativeBodyTuning.TuneSoftness(0.1f, 1f, 1.7f, 0.7f);
        float softAtTwo = NativeBodyTuning.TuneSoftness(0.1f, 2f, 1.7f, 0.7f);
        LessOrEqual(softAtTwo, softAtOne,
            "enhanced native softness continues beyond natural maximum");
        float motionDamping = 0.1f;
        float motionElasticity = 0.1f;
        float motionStiffness = 0.1f;
        NativeBodyTuning.ApplyMotionTarget(ref motionDamping, ref motionElasticity,
            ref motionStiffness, 2f);
        LessOrEqual(motionDamping, 0.055f,
            "enhanced native motion target lowers damping meaningfully");
    }

    private static void TestNativeBodyTargets()
    {
        foreach (FleshPartId part in new[] { FleshPartId.Breast, FleshPartId.Butt })
        {
            NativeBodyParams value = NativeBodyParams.CreateDefault(part);
            NativeBodyTuning.SetTargets(value, part, 2f, 2f, 2f);
            Near(2f, value.Strength, part + " enhanced strength target persists");
            Near(2f, value.Softness, part + " enhanced softness target persists");
            Near(2f, value.MotionResponse, part + " enhanced motion target persists");
            True(!value.AdvancedOverride,
                part + " target controls select recommended native mapping");
        }
    }

    private static void AssertBaseline(FleshPartId part, float weight, float damping,
        float elasticity, float stiffness, float inert, float frequency, float[] amps)
    {
        ThighParams p = ThighParams.CreatePartDefaults(part);
        True(p.Enabled, part + " enabled");
        True(p.GamePhysics, part + " defaults to chain");
        Near(weight, p.Chain.Weight, part + " weight");
        Near(damping, p.Chain.Damping, part + " damping");
        Near(elasticity, p.Chain.Elasticity, part + " elasticity");
        Near(stiffness, p.Chain.Stiffness, part + " stiffness");
        Near(inert, p.Chain.Inert, part + " inert");
        Near(frequency, p.Chain.JitterFreq, part + " frequency");
        for (int i = 0; i < amps.Length; i++)
        {
            Near(amps[i], p.ChainBones.Get(i).Amp, part + " amp " + i);
        }
        Near(part == FleshPartId.Thigh ? 0.8f : 0.7f, p.Weight, part + " spring weight");
        Near(0.18f, p.Thigh00.Damping, part + " spring damping");
        Near(0.10f, p.Thigh00.Elasticity, part + " spring elasticity");
        Near(0.12f, p.Thigh00.Stiffness, part + " spring stiffness");
        Near(0.35f, p.Thigh00.Inert, part + " spring inert");
        p.GamePhysics = false;
        Near(0.5f, FleshTuning.GetSoftness(p, part), part + " spring natural softness");
        p.GamePhysics = true;
    }

    private static void TestStrengthRoundTrip()
    {
        foreach (bool chain in new[] { false, true })
        {
            ThighParams p = ThighParams.CreateDefault();
            p.GamePhysics = chain;
            FleshTuning.SetStrength(p, 0.63f);
            Near(0.63f, FleshTuning.GetStrength(p), "strength round trip " + chain);
            FleshTuning.SetStrength(p, 4f);
            Near(2f, FleshTuning.GetStrength(p), "strength upper clamp " + chain);
            FleshTuning.SetStrength(p, -1f);
            Near(0f, FleshTuning.GetStrength(p), "strength lower clamp " + chain);
            FleshTuning.SetMotionTarget(p, 0.63f);
            Near(0.63f, FleshTuning.GetMotionTarget(p), "motion target round trip " + chain);
            Near(3.15f, p.MotionGain, "motion target maps to raw compatibility gain " + chain);
            FleshTuning.SetMotionTarget(p, 4f);
            Near(2f, FleshTuning.GetMotionTarget(p), "motion target upper clamp " + chain);
            Near(10f, p.MotionGain, "motion target expanded raw upper " + chain);
            FleshTuning.SetMotionTarget(p, -1f);
            Near(0f, FleshTuning.GetMotionTarget(p), "motion target lower clamp " + chain);
        }
    }

    private static void TestSoftnessRoundTripAndMonotonicity()
    {
        float[] points = { 0f, 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f };
        foreach (FleshPartId part in new[] { FleshPartId.Thigh, FleshPartId.Arm, FleshPartId.Belly })
        {
            foreach (bool chain in new[] { false, true })
            {
                float previousDamping = float.MaxValue;
                float previousElasticity = float.MaxValue;
                float previousStiffness = float.MaxValue;
                float previousInert = float.MinValue;
                float previousFrequency = float.MaxValue;
                foreach (float point in points)
                {
                    ThighParams p = ThighParams.CreatePartDefaults(part);
                    p.GamePhysics = chain;
                    float originalStrength = FleshTuning.GetStrength(p);
                    float originalMotion = p.MotionGain;
                    FleshTuning.SetSoftness(p, part, point);
                    Near(point, FleshTuning.GetSoftness(p, part),
                        part + " softness round trip chain=" + chain + " point=" + point);
                    Near(originalStrength, FleshTuning.GetStrength(p), "softness preserves strength");
                    Near(originalMotion, p.MotionGain, "softness preserves motion");

                    float damping = chain ? p.Chain.Damping : p.Thigh00.Damping;
                    float elasticity = chain ? p.Chain.Elasticity : p.Thigh00.Elasticity;
                    float stiffness = chain ? p.Chain.Stiffness : p.Thigh00.Stiffness;
                    float inert = chain ? p.Chain.Inert : p.Thigh00.Inert;
                    float frequency = chain ? p.Chain.JitterFreq : p.JitterFreq;
                    LessOrEqual(damping, previousDamping, "damping non-increasing");
                    LessOrEqual(elasticity, previousElasticity, "elasticity non-increasing");
                    LessOrEqual(stiffness, previousStiffness, "stiffness non-increasing");
                    GreaterOrEqual(inert, previousInert, "inert non-decreasing");
                    LessOrEqual(frequency, previousFrequency, "frequency non-increasing");
                    InRange(damping, 0f, 1f, "damping range");
                    InRange(elasticity, 0f, 1f, "elasticity range");
                    InRange(stiffness, 0f, 1f, "stiffness range");
                    InRange(inert, 0f, 1.5f, "inert range");
                    InRange(frequency, 0f, FleshParameterRanges.JitterFrequencyMax,
                        "frequency range");
                    previousDamping = damping;
                    previousElasticity = elasticity;
                    previousStiffness = stiffness;
                    previousInert = inert;
                    previousFrequency = frequency;
                }

                ThighParams enhanced = ThighParams.CreatePartDefaults(part);
                enhanced.GamePhysics = chain;
                FleshTuning.SetSoftness(enhanced, part, 2f);
                PluginData card = new PluginData();
                card.data["v"] = ThighParams.DataVersion;
                ThighParams.WritePart(card.data, "roundtrip_", enhanced);
                ThighParams reloaded = ThighParams.CreatePartDefaults(part);
                ThighParams.ReadPart(card.data, "roundtrip_", reloaded,
                    ThighParams.DataVersion);
                Near(2f, FleshTuning.GetSoftness(reloaded, part),
                    part + " enhanced softness survives card round trip chain=" + chain);
            }
        }
    }

    private static void TestIndependentObjects()
    {
        ThighParams first = FleshTuning.CreateFeelPreset(FleshPartId.Thigh, FleshFeelPreset.Natural);
        ThighParams second = FleshTuning.CreateFeelPreset(FleshPartId.Thigh, FleshFeelPreset.Natural);
        True(!ReferenceEquals(first, second), "preset instances are independent");
        True(!ReferenceEquals(first.Chain, second.Chain), "chain instances are independent");
        first.Chain.Weight = 0f;
        Near(0.92f, second.Chain.Weight, "preset mutation isolation");
    }

    private static void TestFeelPresets()
    {
        FleshPartId[] parts = { FleshPartId.Thigh, FleshPartId.Arm, FleshPartId.Belly };
        float[] stableStrength = { 0.78f, 0.68f, 0.65f };
        float[] naturalStrength = { 0.92f, 0.80f, 0.78f };
        float[] danceStrength = { 1.08f, 0.95f, 0.90f };
        float[] danceMotion = { 0.65f, 0.65f, 0.60f };
        float[][] springBase =
        {
            new[] { 1.00f, 0.30f, 0.18f, 0.03f },
            new[] { 1.4143f, 0.18f, 0.108f, 0.018f },
            new[] { 1.1857f, 0.075f, 0.045f, 0.0075f }
        };
        float[][] chainBase =
        {
            new[] { 1.50f, 1.20f, 0.30f, 0.80f },
            new[] { 0.80f, 0.60f, 0.60f, 0.072f },
            new[] { 1.00f, 0.20f, 0.125f, 0.03f }
        };

        for (int i = 0; i < parts.Length; i++)
        {
            ThighParams stable = FleshTuning.CreateFeelPreset(parts[i], FleshFeelPreset.Stable);
            ThighParams natural = FleshTuning.CreateFeelPreset(parts[i], FleshFeelPreset.Natural);
            ThighParams dance = FleshTuning.CreateFeelPreset(parts[i], FleshFeelPreset.Dance);

            True(!stable.GamePhysics, parts[i] + " stable preset uses spring");
            True(natural.GamePhysics && dance.GamePhysics,
                parts[i] + " natural and dance presets use chain");
            Near(0.45f, FleshTuning.GetSoftness(stable, parts[i]), parts[i] + " stable softness");
            Near(stableStrength[i], FleshTuning.GetStrength(stable), parts[i] + " stable strength");
            Near(0.30f, FleshTuning.GetMotionTarget(stable), parts[i] + " stable motion target");
            Near(0.80f, FleshTuning.GetSoftness(natural, parts[i]),
                parts[i] + " natural softness");
            Near(naturalStrength[i], FleshTuning.GetStrength(natural), parts[i] + " natural strength");
            Near(0.45f, FleshTuning.GetMotionTarget(natural), parts[i] + " natural motion target");
            Near(1.10f, FleshTuning.GetSoftness(dance, parts[i]), parts[i] + " dance softness");
            Near(danceStrength[i], FleshTuning.GetStrength(dance), parts[i] + " dance strength");
            Near(danceMotion[i],
                FleshTuning.GetMotionTarget(dance), parts[i] + " dance motion target");

            for (int bone = 0; bone < 4; bone++)
            {
                Near(springBase[i][bone] * 0.75f,
                    stable.Bones.Get(bone).Amp,
                    parts[i] + " low spring amp " + bone);
                Near(springBase[i][bone],
                    natural.Bones.Get(bone).Amp,
                    parts[i] + " medium spring amp " + bone);
                float highSpringAmp = parts[i] == FleshPartId.Thigh && bone == 1
                    ? 0.50f : springBase[i][bone] * 1.30f;
                float highChainAmp = parts[i] == FleshPartId.Thigh && bone == 1
                    ? 0.50f : chainBase[i][bone] * 1.30f;
                Near(highSpringAmp,
                    dance.Bones.Get(bone).Amp,
                    parts[i] + " high spring amp " + bone);
                Near(chainBase[i][bone] * 0.75f,
                    stable.ChainBones.Get(bone).Amp,
                    parts[i] + " low chain amp " + bone);
                Near(chainBase[i][bone],
                    natural.ChainBones.Get(bone).Amp,
                    parts[i] + " medium chain amp " + bone);
                Near(highChainAmp,
                    dance.ChainBones.Get(bone).Amp,
                    parts[i] + " high chain amp " + bone);
                if (parts[i] != FleshPartId.Thigh || bone != 1)
                {
                    True(stable.Bones.Get(bone).Amp < natural.Bones.Get(bone).Amp &&
                         natural.Bones.Get(bone).Amp < dance.Bones.Get(bone).Amp,
                        parts[i] + " spring amp level monotonic " + bone);
                    True(stable.ChainBones.Get(bone).Amp <
                             natural.ChainBones.Get(bone).Amp &&
                         natural.ChainBones.Get(bone).Amp <
                             dance.ChainBones.Get(bone).Amp,
                        parts[i] + " chain amp level monotonic " + bone);
                }
            }

            foreach (ThighParams level in new[] { stable, natural, dance })
            {
                float expectedStrength = FleshTuning.GetStrength(level);
                float expectedSoftness = FleshTuning.GetSoftness(level, parts[i]);
                level.GamePhysics = !level.GamePhysics;
                Near(expectedStrength, FleshTuning.GetStrength(level),
                    parts[i] + " strength survives solver switch");
                Near(expectedSoftness, FleshTuning.GetSoftness(level, parts[i]),
                    parts[i] + " softness survives solver switch");
            }
        }

        NativeBodyParams breastLow = NativeBodyTuning.CreateFeelPreset(
            FleshPartId.Breast, FleshFeelPreset.Stable);
        NativeBodyParams breastMedium = NativeBodyTuning.CreateFeelPreset(
            FleshPartId.Breast, FleshFeelPreset.Natural);
        NativeBodyParams breastHigh = NativeBodyTuning.CreateFeelPreset(
            FleshPartId.Breast, FleshFeelPreset.Dance);
        NativeBodyParams buttLow = NativeBodyTuning.CreateFeelPreset(
            FleshPartId.Butt, FleshFeelPreset.Stable);
        NativeBodyParams buttMedium = NativeBodyTuning.CreateFeelPreset(
            FleshPartId.Butt, FleshFeelPreset.Natural);
        NativeBodyParams buttHigh = NativeBodyTuning.CreateFeelPreset(
            FleshPartId.Butt, FleshFeelPreset.Dance);
        Near(0.40f, breastLow.Strength, "breast low strength");
        Near(0.50f, breastMedium.Strength, "breast medium strength capped for collision safety");
        Near(0.60f, breastHigh.Strength, "breast high strength capped for collision safety");
        Near(0.38f, buttLow.Strength, "butt low strength");
        Near(0.58f, buttMedium.Strength, "butt medium strength");
        Near(0.82f, buttHigh.Strength, "butt high strength");
        Near(0.50f, breastLow.Softness, "native low softness");
        Near(0.75f, breastMedium.Softness, "native medium softness");
        Near(0.95f, breastHigh.Softness, "native high softness");
        Near(0.40f, buttLow.MotionResponse, "native low motion");
        Near(0.60f, buttMedium.MotionResponse, "native medium motion");
        Near(0.75f, buttHigh.MotionResponse, "native high motion");
    }

    private static void TestCardBoundsAndDeadKeys()
    {
        PluginData written = new PluginData();
        ThighParams.CreateDefault().WriteData(written);
        foreach (string suffix in new[] { "_rot", "_rad", "_lever", "_speed", "_sway", "_drive", "_spring", "_pdamp" })
        {
            True(!written.data.ContainsKey("t00" + suffix), "dead key omitted: " + suffix);
        }

        PluginData hostile = new PluginData();
        Dictionary<string, object> data = hostile.data;
        data["v"] = 56;
        data["gravity"] = 99f;
        data["weight"] = -4f;
        data["mg"] = 99f;
        data["jf"] = -2f;
        data["ms"] = 5f;
        data["c_w"] = 9f;
        data["c_g"] = -9f;
        data["c_d"] = 9f;
        data["c_e"] = -9f;
        data["c_s"] = 9f;
        data["c_i"] = -9f;
        data["c_jf"] = 9f;
        data["t00_damp"] = 9f;
        data["t00_elas"] = -9f;
        data["t00_stif"] = 9f;
        data["t00_inert"] = -9f;
        data["t00_rad"] = 123f; // legacy key must be ignored safely

        ThighParams p = ThighParams.CreateDefault();
        p.ReadData(hostile);
        Near(0.4f, p.Gravity, "card gravity clamp");
        Near(0f, p.Weight, "card weight clamp");
        Near(10f, p.MotionGain, "card motion clamp");
        Near(0f, p.JitterFreq, "card frequency clamp");
        Near(1f, p.MotionSmooth, "card smoothing clamp");
        Near(2f, p.Chain.Weight, "card chain weight clamp");
        Near(-0.4f, p.Chain.Gravity, "card chain gravity clamp");
        Near(1f, p.Chain.Damping, "card chain damping clamp");
        Near(0f, p.Chain.Elasticity, "card chain elasticity clamp");
        Near(1f, p.Chain.Stiffness, "card chain stiffness clamp");
        Near(0f, p.Chain.Inert, "card chain inert clamp");
        Near(5f, p.Chain.JitterFreq, "card chain frequency clamp");
        Near(1f, p.Thigh00.Damping, "card spring damping clamp");
        Near(0f, p.Thigh00.Elasticity, "card spring elasticity clamp");
        Near(1f, p.Thigh00.Stiffness, "card spring stiffness clamp");
        Near(0f, p.Thigh00.Inert, "card spring inert clamp");

        PluginData invalid = new PluginData();
        invalid.data["v"] = 56;
        invalid.data["enabled"] = "not-a-bool";
        invalid.data["gravity"] = float.NaN;
        invalid.data["weight"] = float.PositiveInfinity;
        invalid.data["mg"] = "not-a-number";
        invalid.data["jf"] = float.NegativeInfinity;
        invalid.data["ms"] = float.NaN;
        invalid.data["c_w"] = float.NaN;
        invalid.data["c_g"] = float.PositiveInfinity;
        invalid.data["c_d"] = "broken";
        invalid.data["c_e"] = float.NaN;
        invalid.data["c_s"] = float.PositiveInfinity;
        invalid.data["c_i"] = float.NegativeInfinity;
        invalid.data["c_jf"] = float.NaN;
        invalid.data["t00_damp"] = float.NaN;
        invalid.data["t00_elas"] = float.PositiveInfinity;
        invalid.data["t00_stif"] = "broken";
        invalid.data["t00_inert"] = float.NegativeInfinity;
        invalid.data["b0_amp"] = float.NaN;
        invalid.data["b0_ax"] = float.PositiveInfinity;
        invalid.data["b0_ay"] = "broken";
        invalid.data["b0_az"] = float.NegativeInfinity;
        invalid.data["b0_rot"] = float.NaN;
        invalid.data["cb0_amp"] = float.NaN;

        ThighParams safe = ThighParams.CreateDefault();
        safe.ReadData(invalid);
        True(safe.Enabled, "malformed card boolean preserves default");
        Near(0.05f, safe.Gravity, "NaN card gravity preserves default");
        Near(0.8f, safe.Weight, "infinite card weight preserves default");
        Near(1f, safe.MotionGain, "malformed card motion preserves default");
        Near(1f, safe.JitterFreq, "infinite card frequency preserves default");
        Near(0.25f, safe.MotionSmooth, "NaN card smoothing preserves default");
        Near(0.9f, safe.Chain.Weight, "NaN chain weight preserves default");
        Near(0.05f, safe.Chain.Gravity, "infinite chain gravity preserves default");
        Near(0.04f, safe.Chain.Damping, "malformed chain damping preserves default");
        Near(0.05f, safe.Chain.Elasticity, "NaN chain elasticity preserves default");
        Near(0.85f, safe.Chain.Stiffness, "infinite chain stiffness preserves default");
        Near(0.80f, safe.Chain.Inert, "infinite chain inert preserves default");
        Near(0.20f, safe.Chain.JitterFreq, "NaN chain frequency preserves default");
        Near(0.18f, safe.Thigh00.Damping, "NaN spring damping preserves default");
        Near(0.10f, safe.Thigh00.Elasticity, "infinite spring elasticity preserves default");
        Near(0.12f, safe.Thigh00.Stiffness, "malformed spring stiffness preserves default");
        Near(0.35f, safe.Thigh00.Inert, "infinite spring inert preserves default");
        Near(1f, safe.Bones.Get(0).Amp, "NaN bone amp preserves default");
        Near(1f, safe.Bones.Get(0).AxisX, "infinite bone axis preserves default");
        Near(1f, safe.Bones.Get(0).AxisY, "malformed bone axis preserves default");
        Near(1f, safe.Bones.Get(0).AxisZ, "negative infinite bone axis preserves default");
        Near(0.25f, safe.Bones.Get(0).RotAmp, "NaN bone rotation preserves default");
        Near(1f, safe.ChainBones.Get(0).Amp, "NaN chain bone amp preserves default");

        PluginData legacy = new PluginData();
        legacy.data["v"] = 50;
        legacy.data["weight"] = 0.1f;
        legacy.data["jf"] = 0.2f;
        legacy.data["ms"] = 0.4f;
        legacy.data["t00_damp"] = 0.99f;
        legacy.data["t00_elas"] = 0.99f;
        legacy.data["t00_stif"] = 0.99f;
        legacy.data["t00_inert"] = 0.99f;
        ThighParams migrated = ThighParams.CreateDefault();
        migrated.ReadData(legacy);
        Near(0.8f, migrated.Weight, "legacy spring weight migrates to current midpoint");
        Near(0.18f, migrated.Thigh00.Damping, "legacy spring damping migration");
        Near(0.10f, migrated.Thigh00.Elasticity, "legacy spring elasticity migration");
        Near(0.12f, migrated.Thigh00.Stiffness, "legacy spring stiffness migration");
        Near(0.35f, migrated.Thigh00.Inert, "legacy spring inert migration");
        Near(1f, migrated.JitterFreq, "legacy spring frequency migration");
        Near(0.25f, migrated.MotionSmooth, "legacy spring smoothing migration");
    }

    private static void TestFiniteValueBoundary()
    {
        Near(0.4f, FleshValue.Clamp(float.NaN, 0f, 1f, 0.4f), "NaN uses fallback");
        Near(0.4f, FleshValue.Clamp(float.PositiveInfinity, 0f, 1f, 0.4f),
            "positive infinity uses fallback");
        Near(0.4f, FleshValue.Clamp(float.NegativeInfinity, 0f, 1f, 0.4f),
            "negative infinity uses fallback");
        Near(1f, FleshValue.Clamp(4f, 0f, 1f, 0.4f), "finite upper clamp");
        Near(0f, FleshValue.Clamp(-4f, 0f, 1f, 0.4f), "finite lower clamp");
        Near(0.3f, FleshValue.ConvertClamped("not-a-number", 0f, 1f, 0.3f),
            "malformed conversion uses fallback");
        True(FleshValue.ConvertBoolean("broken", true), "malformed boolean uses fallback");
        Equal(56, FleshValue.ConvertInt32("broken", 56), "malformed integer uses fallback");

        ThighParams p = ThighParams.CreatePartDefaults(FleshPartId.Thigh);
        FleshTuning.SetStrength(p, float.NaN);
        Near(0.9f, FleshTuning.GetStrength(p), "NaN strength preserves current value");
        FleshTuning.SetSoftness(p, FleshPartId.Thigh, float.PositiveInfinity);
        Near(1f, FleshTuning.GetSoftness(p, FleshPartId.Thigh),
            "infinite softness preserves current value");
    }

    private static void TestSolverScalarMath()
    {
        Near(-1f, FleshSolverMath.NormalizeSignedAngle(359f),
            "angle-axis 359 degrees follows the -1 degree shortest path");
        Near(1f, FleshSolverMath.NormalizeSignedAngle(-359f),
            "angle-axis -359 degrees follows the +1 degree shortest path");
        Near(0f, FleshSolverMath.NormalizeSignedAngle(float.NaN),
            "non-finite angle-axis input is neutralized");
        Near(0.011f, FleshSolverMath.Median3(0.010f, 0.200f, 0.011f),
            "median guard rejects a positive one-frame spike");
        Near(-0.011f, FleshSolverMath.Median3(-0.010f, -0.200f, -0.011f),
            "median guard rejects a negative one-frame spike");
        Near(0.011f, FleshSolverMath.Median3(0.010f, 0.011f, 0.012f),
            "median guard preserves the middle of smooth monotonic motion");
        UnityEngine.Vector3 guarded = FleshSolverMath.Median3(
            new UnityEngine.Vector3(0.010f, -0.010f, 0.020f),
            new UnityEngine.Vector3(0.500f, -0.011f, 0.021f),
            new UnityEngine.Vector3(0.011f, -0.500f, 0.022f));
        Near(0.011f, guarded.x, "vector median guards translation spike");
        Near(-0.011f, guarded.y, "vector median guards angular spike");
        Near(0.021f, guarded.z, "vector median preserves smooth component");
        Near(1f, FleshSolverMath.DanceResponseScale(1f, 0.8f, 0.35f),
            "dance response reference is one");
        Near(2f, FleshSolverMath.DanceResponseScale(2f, 0.8f, 0.35f),
            "dance response gain is linear");
        Near(FleshSolverMath.DanceResponseScale(1f, 0.8f, 0.35f),
            FleshSolverMath.DanceResponseScale(float.NaN, 0.8f, 0.35f),
            "invalid dance gain uses safe reference");
        Near(0.92f, FleshSolverMath.MotionFollowFraction(0f),
            "zero motion target follows animated movement");
        Near(0.485f, FleshSolverMath.MotionFollowFraction(0.5f),
            "middle motion target creates balanced lag");
        Near(0.05f, FleshSolverMath.MotionFollowFraction(1f),
            "maximum motion target preserves world inertia");
        Near(0.05f, FleshSolverMath.MotionFollowFraction(2f),
            "enhanced motion target keeps anti-snap follow floor");
        Near(1.35f, FleshSolverMath.TargetRangeScale(1f, 1f),
            "natural target keeps established visible range");
        Near(2f, FleshSolverMath.TargetRangeScale(2f, 2f),
            "enhanced target receives expanded safe visible range");
        float previousFollow = 1f;
        float previousRange = 0f;
        for (int i = 0; i <= 20; i++)
        {
            float target = i / 10f;
            float follow = FleshSolverMath.MotionFollowFraction(target);
            float range = FleshSolverMath.TargetRangeScale(0.8f, target);
            LessOrEqual(follow, previousFollow,
                "motion target monotonically reduces rigid following " + i);
            GreaterOrEqual(range, previousRange,
                "motion target monotonically expands safe visible range " + i);
            GreaterOrEqual(follow, 0.05f, "motion follow keeps anti-snap floor " + i);
            LessOrEqual(range, 2f, "target visible range stays inside safety cap " + i);
            previousFollow = follow;
            previousRange = range;
        }

        ChainParams thigh = ThighParams.CreatePartDefaults(FleshPartId.Thigh).Chain;
        ChainParams belly = ThighParams.CreatePartDefaults(FleshPartId.Belly).Chain;
        Near(0.0245f, FleshSolverMath.SingleParticleReturnStrength(thigh),
            "thigh scalar return reference");
        Near(0.2775f, FleshSolverMath.SingleParticleReturnStrength(belly),
            "belly scalar return reference");
        belly.JitterFreq = float.NaN;
        Near(0.2775f, FleshSolverMath.SingleParticleReturnStrength(belly),
            "invalid return frequency uses safe fallback");
        Near(0.5f, FleshSolverMath.AdjustPerFrameRate(0.5f, 1f / 60f),
            "frame rate adjustment preserves 60 FPS strength");
        Near(0.75f, FleshSolverMath.AdjustPerFrameRate(0.5f, 1f / 30f),
            "frame rate adjustment composes two reference steps");
        Near(0.2928932f, FleshSolverMath.AdjustPerFrameRate(0.5f, 1f / 120f),
            "frame rate adjustment splits a reference step");
        Near(0f, FleshSolverMath.AdjustPerFrameRate(float.NaN, 1f / 60f),
            "invalid reference strength uses safe zero");
        Near(1.4f, FleshSolverMath.ChainMotionTimeScale(FleshPartId.Thigh, 1f / 30f),
            "thigh chain low FPS drive compensation");
        Near(1f, FleshSolverMath.ChainMotionTimeScale(FleshPartId.Thigh, 1f / 60f),
            "thigh chain reference FPS drive compensation");
        Near(0.8352f, FleshSolverMath.ChainMotionTimeScale(FleshPartId.Thigh, 1f / 157f),
            "thigh chain high FPS drive compensation");
        Near(1f, FleshSolverMath.ChainMotionTimeScale(FleshPartId.Arm, 1f / 30f),
            "arm chain needs no topology compensation");
        Near(0.0003f, FleshSpringSolver.ActivationThreshold(1f),
            "spring activation threshold at full amplitude");
        Near(0.000075f, FleshSpringSolver.ActivationThreshold(0.25f),
            "spring activation threshold scales for belly");
        Near(0.000045f, FleshSpringSolver.ActivationThreshold(0f),
            "spring activation threshold keeps a noise floor");
        Near(1f, FleshSpringSolver.PartDriveScale(FleshPartId.Thigh),
            "thigh spring drive scale");
        Near(1f, FleshSpringSolver.PartDriveScale(FleshPartId.Arm),
            "arm spring drive scale");
        Near(4f, FleshSpringSolver.PartDriveScale(FleshPartId.Belly),
            "belly spring drive compensation");
        Near(0.65f, FleshSpringSolver.VelocityRetention(0.35f, 1f / 60f),
            "spring damping keeps its full range at 60 FPS");
        Near(0.4225f, FleshSpringSolver.VelocityRetention(0.35f, 1f / 30f),
            "spring damping composes at 30 FPS");
        Near(0.8062258f, FleshSpringSolver.VelocityRetention(0.35f, 1f / 120f),
            "spring damping composes at 120 FPS");
    }

    private static void Near(float expected, float actual, string message)
    {
        _assertions++;
        if (Math.Abs(expected - actual) > Epsilon)
        {
            throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
        }
    }

    private static void Equal(int expected, int actual, string message)
    {
        _assertions++;
        if (expected != actual)
        {
            throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
        }
    }

    private static void True(bool value, string message)
    {
        _assertions++;
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void LessOrEqual(float value, float limit, string message)
    {
        _assertions++;
        if (value > limit + Epsilon)
        {
            throw new InvalidOperationException(message + ": " + value + " > " + limit);
        }
    }

    private static void GreaterOrEqual(float value, float limit, string message)
    {
        _assertions++;
        if (value + Epsilon < limit)
        {
            throw new InvalidOperationException(message + ": " + value + " < " + limit);
        }
    }

    private static void InRange(float value, float min, float max, string message)
    {
        _assertions++;
        if (value < min - Epsilon || value > max + Epsilon)
        {
            throw new InvalidOperationException(message + ": " + value + " outside " + min + ".." + max);
        }
    }
}
