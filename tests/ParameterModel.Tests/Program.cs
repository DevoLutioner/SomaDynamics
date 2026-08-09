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
        Equal(56, ThighParams.DataVersion, "data version");
        AssertBaseline(FleshPartId.Thigh, 0.90f, 0.04f, 0.05f, 0.85f, 0.80f, 0.20f,
            new[] { 1f, 0.8f, 0.3f, 0.3f });
        AssertBaseline(FleshPartId.Arm, 0.70f, 0.05f, 0.25f, 0.90f, 0.80f, 0.15f,
            new[] { 2f, 0.6f, 0.6f, 0.072f });
        AssertBaseline(FleshPartId.Belly, 0.70f, 0.30f, 0.25f, 0.90f, 0.40f, 1.00f,
            new[] { 0.25f, 0.20f, 0.125f, 0.03f });
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
            Near(1f, FleshTuning.GetStrength(p), "strength upper clamp " + chain);
            FleshTuning.SetStrength(p, -1f);
            Near(0f, FleshTuning.GetStrength(p), "strength lower clamp " + chain);
        }
    }

    private static void TestSoftnessRoundTripAndMonotonicity()
    {
        float[] points = { 0f, 0.25f, 0.5f, 0.75f, 1f };
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
                    InRange(inert, 0f, 1f, "inert range");
                    InRange(frequency, 0f, 2.5f, "frequency range");
                    previousDamping = damping;
                    previousElasticity = elasticity;
                    previousStiffness = stiffness;
                    previousInert = inert;
                    previousFrequency = frequency;
                }
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
        Near(0.9f, second.Chain.Weight, "preset mutation isolation");
    }

    private static void TestFeelPresets()
    {
        FleshPartId[] parts = { FleshPartId.Thigh, FleshPartId.Arm, FleshPartId.Belly };
        float[] stableStrength = { 0.75f, 0.60f, 0.55f };
        float[] naturalStrength = { 0.90f, 0.70f, 0.70f };
        float[] naturalSoftness = { 1f, 1f, 0.5f };
        float[] naturalMotion = { 1f, 1f, 1.0864f };
        float[] danceStrength = { 0.95f, 0.80f, 0.70f };
        float[] danceMotion = { 1.50f, 1.50f, 1.20f };

        for (int i = 0; i < parts.Length; i++)
        {
            ThighParams stable = FleshTuning.CreateFeelPreset(parts[i], FleshFeelPreset.Stable);
            ThighParams natural = FleshTuning.CreateFeelPreset(parts[i], FleshFeelPreset.Natural);
            ThighParams dance = FleshTuning.CreateFeelPreset(parts[i], FleshFeelPreset.Dance);

            True(!stable.GamePhysics, parts[i] + " stable preset uses spring");
            True(natural.GamePhysics && dance.GamePhysics,
                parts[i] + " natural and dance presets use chain");
            Near(0f, FleshTuning.GetSoftness(stable, parts[i]), parts[i] + " stable softness");
            Near(stableStrength[i], FleshTuning.GetStrength(stable), parts[i] + " stable strength");
            Near(0.75f, stable.MotionGain, parts[i] + " stable motion");
            Near(naturalSoftness[i], FleshTuning.GetSoftness(natural, parts[i]),
                parts[i] + " natural softness");
            Near(naturalStrength[i], FleshTuning.GetStrength(natural), parts[i] + " natural strength");
            Near(naturalMotion[i], natural.MotionGain, parts[i] + " natural motion");
            Near(1f, FleshTuning.GetSoftness(dance, parts[i]), parts[i] + " dance softness");
            Near(danceStrength[i], FleshTuning.GetStrength(dance), parts[i] + " dance strength");
            Near(danceMotion[i], dance.MotionGain, parts[i] + " dance motion");
        }
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
        Near(0.2f, p.Gravity, "card gravity clamp");
        Near(0f, p.Weight, "card weight clamp");
        Near(5f, p.MotionGain, "card motion clamp");
        Near(0f, p.JitterFreq, "card frequency clamp");
        Near(0.5f, p.MotionSmooth, "card smoothing clamp");
        Near(1f, p.Chain.Weight, "card chain weight clamp");
        Near(-0.2f, p.Chain.Gravity, "card chain gravity clamp");
        Near(1f, p.Chain.Damping, "card chain damping clamp");
        Near(0f, p.Chain.Elasticity, "card chain elasticity clamp");
        Near(1f, p.Chain.Stiffness, "card chain stiffness clamp");
        Near(0f, p.Chain.Inert, "card chain inert clamp");
        Near(2.5f, p.Chain.JitterFreq, "card chain frequency clamp");
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
        Near(1f, FleshSolverMath.DanceResponseScale(1f, 0.8f, 0.35f),
            "dance response reference is one");
        Near(2f, FleshSolverMath.DanceResponseScale(2f, 0.8f, 0.35f),
            "dance response gain is linear");
        Near(FleshSolverMath.DanceResponseScale(1f, 0.8f, 0.35f),
            FleshSolverMath.DanceResponseScale(float.NaN, 0.8f, 0.35f),
            "invalid dance gain uses safe reference");

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
