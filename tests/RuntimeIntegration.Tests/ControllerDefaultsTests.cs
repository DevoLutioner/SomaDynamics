using System;
using System.IO;
using ExtensibleSaveFormat;
using ThighPhysicsController;
using UnityEngine;

internal static class ControllerDefaultsTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("male card defaults override force-enable and global defaults", MaleCardDefaults);
        run("male defaults skip native takeover and delayed reapply", NoMaleNativeTakeover);
        run("female defaults retain their configured behavior", FemaleDefaults);
        run("male default-off can be opted out", MaleOptOut);
        run("male manual enable survives an outfit reload", ManualEnableSurvivesOutfit);
        run("fresh male card reload reapplies male default-off", FreshMaleReload);
        run("global enable defaults keep male parts off", GlobalDefaults);
        run("new male defaults do not disable a manually enabled namesake", MemoryIsolation);
        run("automatic preset cannot enable male parts", AutomaticPreset);
        run("enabling male default-off restores an active native bridge", EnablePolicyNow);
        run("male setting update leaves loaded female choices alone", LoadedFemaleUnaffected);
        run("isolated male load still remembers edited tuning", IsolatedTuningRemembered);
    }

    private static void Reset()
    {
        ThighPhysicsControllerPlugin.MemoryProfiles.Clear();
        ThighPhysicsControllerPlugin.Controllers.Clear();
        ThighPhysicsControllerPlugin.AutoApply.Value = true;
        ThighPhysicsControllerPlugin.ForceEnable.Value = true;
        ThighPhysicsControllerPlugin.RememberPerCharacter.Value = true;
        ThighPhysicsControllerPlugin.DisableMaleByDefault.Value = true;
        ThighPhysicsControllerPlugin.ApplyDefaultsToAllCharacters.Value = true;
        ThighPhysicsControllerPlugin.DefaultThighEnabled.Value = true;
        ThighPhysicsControllerPlugin.DefaultArmEnabled.Value = true;
        ThighPhysicsControllerPlugin.DefaultBellyEnabled.Value = true;
        ThighPhysicsControllerPlugin.DefaultBreastEnabled.Value = true;
        ThighPhysicsControllerPlugin.DefaultButtEnabled.Value = true;
        ThighPhysicsControllerPlugin.DefaultPreset.Value = "";
        NativeDynamicBoneBridge.Created = 0;
        NativeDynamicBoneBridge.ApplyCalls = 0;
        NativeDynamicBoneBridge.Restores = 0;
    }

    private static ThighController Create(byte sex, bool card = true)
    {
        GameObject root = new GameObject("defaults test");
        ChaControl character = root.AddComponent<ChaControl>();
        character.chaFile.parameter.sex = sex;
        character.chaFile.parameter.fullname = "Same name";
        ThighController controller = root.AddComponent<ThighController>();
        controller.ChaControl = character;
        if (card)
        {
            PluginData data = new PluginData();
            ThighParams.CreateDefault().WriteData(data);
            ThighParams.WritePart(data.data, "arm_", ThighParams.CreatePartDefaults(FleshPartId.Arm));
            ThighParams.WritePart(data.data, "belly_", ThighParams.CreatePartDefaults(FleshPartId.Belly));
            NativeBustProfile.Write(data.data, "breast_", new NativeBustProfile());
            NativeBodyParams.WritePart(data.data, "butt_", NativeBodyParams.CreateDefault(FleshPartId.Butt));
            controller.TestCardData = data;
        }
        return controller;
    }

    private static void AllOff(ThighController controller)
    {
        Assert(!controller.Params.Enabled && !controller.ArmParams.Enabled && !controller.BellyParams.Enabled,
            "male flesh parts were automatically enabled");
        Assert(!controller.ButtParams.Enabled, "male butt takeover was automatically enabled");
        for (int coordinate = 0; coordinate < 7; coordinate++)
            for (int wear = 0; wear < 3; wear++)
                Assert(!controller.BreastProfile.Get(coordinate, wear).Enabled,
                    "male breast takeover remained enabled for coordinate/wear " + coordinate + "/" + wear);
    }

    private static void MaleCardDefaults()
    {
        Reset();
        ThighPhysicsControllerPlugin.ApplyDefaultsToAllCharacters.Value = false;
        ThighController controller = Create(0);
        controller.ReloadForTest(false);
        Assert(controller.IsMale, "test sex must match KK male=0");
        AllOff(controller);
        Assert(Math.Abs(controller.ButtParams.Softness - 0.75f) < 0.0001f, "default-off changed saved tuning");
    }

    private static void NoMaleNativeTakeover()
    {
        Reset();
        ThighController controller = Create(0);
        controller.ReloadForTest(false);
        for (int frame = 0; frame < 35; frame++) controller.UpdateTick();
        Assert(NativeDynamicBoneBridge.Created == 0 && NativeDynamicBoneBridge.ApplyCalls == 0,
            "disabled male parts still create/apply a native bridge");
    }

    private static void FemaleDefaults()
    {
        Reset();
        ThighPhysicsControllerPlugin.DefaultArmEnabled.Value = false;
        ThighController controller = Create(1);
        controller.ReloadForTest(false);
        Assert(!controller.IsMale, "test sex must match KK female=1");
        Assert(controller.Params.Enabled && !controller.ArmParams.Enabled && controller.BellyParams.Enabled &&
            controller.GetNativeParams(FleshPartId.Breast).Enabled && controller.ButtParams.Enabled,
            "male policy changed female part defaults");
    }

    private static void MaleOptOut()
    {
        Reset();
        ThighPhysicsControllerPlugin.DisableMaleByDefault.Value = false;
        ThighController controller = Create(0);
        controller.ReloadForTest(false);
        Assert(controller.Params.Enabled && controller.ArmParams.Enabled && controller.BellyParams.Enabled &&
            controller.GetNativeParams(FleshPartId.Breast).Enabled && controller.ButtParams.Enabled,
            "opting out did not restore the configured defaults");
    }

    private static void ManualEnableSurvivesOutfit()
    {
        Reset();
        ThighController controller = Create(0);
        controller.ReloadForTest(false);
        controller.ButtParams.Enabled = true;
        controller.ReloadForTest(true);
        Assert(controller.ButtParams.Enabled, "outfit refresh revoked an explicit male opt-in");
        Assert(!controller.GetNativeParams(FleshPartId.Breast).Enabled, "outfit refresh enabled unrelated breast states");
    }

    private static void FreshMaleReload()
    {
        Reset();
        ThighController controller = Create(0);
        controller.ReloadForTest(false);
        controller.ButtParams.Enabled = true;
        controller.ReloadForTest(false);
        AllOff(controller);
    }

    private static void GlobalDefaults()
    {
        Reset();
        ThighController controller = Create(0);
        controller.ReloadForTest(false);
        controller.BreastProfile.SetEnabledAll(true);
        controller.ButtParams.Enabled = true;
        controller.ApplyGlobalDefaultPartEnables();
        AllOff(controller);
    }

    private static void MemoryIsolation()
    {
        Reset();
        ThighController first = Create(0);
        first.ReloadForTest(false);
        NativeBodyParams explicitButt = first.ButtParams.Clone();
        explicitButt.Enabled = true;
        explicitButt.Strength = 1.3f;
        first.SetNativeParams(FleshPartId.Butt, explicitButt);
        ThighController second = Create(0);
        second.ReloadForTest(false);
        AllOff(second);
        Assert(first.ButtParams.Enabled, "loading a namesake revoked the first character's manual opt-in");
        Assert(Math.Abs(second.ButtParams.Strength - 1.3f) < 0.0001f, "default-off lost remembered numeric tuning");
    }

    private static void AutomaticPreset()
    {
        Reset();
        string directory = Path.Combine(Path.GetTempPath(), "soma-male-defaults-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string preset = Path.Combine(directory, "Enabled.xml");
        try
        {
            File.WriteAllText(preset, "<XMLParamThigh Version=\"4\"><BreastPart><Enabled>true</Enabled></BreastPart><ButtPart><Enabled>true</Enabled></ButtPart></XMLParamThigh>", new System.Text.UTF8Encoding(false));
            ThighPhysicsControllerPlugin.PresetDirectory.Value = directory;
            ThighPhysicsControllerPlugin.DefaultPreset.Value = "Enabled.xml";
            ThighController controller = Create(0, false);
            controller.ReloadForTest(false);
            AllOff(controller);
        }
        finally
        {
            File.Delete(preset);
            Directory.Delete(directory, false);
            ThighPhysicsControllerPlugin.DefaultPreset.Value = "";
            ThighPhysicsControllerPlugin.PresetDirectory.Value = Path.GetTempPath();
        }
    }

    private static void EnablePolicyNow()
    {
        Reset();
        ThighPhysicsControllerPlugin.DisableMaleByDefault.Value = false;
        ThighController controller = Create(0);
        controller.ReloadForTest(false);
        Assert(NativeDynamicBoneBridge.Created == 1, "native setup did not establish a restore target");
        ThighPhysicsControllerPlugin.DisableMaleByDefault.Value = true;
        Assert(controller.ApplyMaleDefaultOffNow(), "loaded male was not handled when enabling the policy");
        AllOff(controller);
        int previousApplyCalls = NativeDynamicBoneBridge.ApplyCalls;
        for (int frame = 0; frame < 35; frame++) controller.UpdateTick();
        Assert(NativeDynamicBoneBridge.Restores == 1, "native defaults were not restored exactly once");
        Assert(NativeDynamicBoneBridge.Created == 1 && NativeDynamicBoneBridge.ApplyCalls == previousApplyCalls,
            "delayed work re-created takeover after disabling all parts");
    }

    private static void LoadedFemaleUnaffected()
    {
        Reset();
        ThighController controller = Create(1);
        controller.ReloadForTest(false);
        controller.ButtParams.Enabled = false;
        int applyCalls = NativeDynamicBoneBridge.ApplyCalls;
        Assert(!controller.ApplyMaleDefaultOffNow(), "female was handled by the male setting update");
        Assert(controller.GetNativeParams(FleshPartId.Breast).Enabled && !controller.ButtParams.Enabled &&
            NativeDynamicBoneBridge.ApplyCalls == applyCalls, "male setting update changed loaded female choices");
    }

    private static void IsolatedTuningRemembered()
    {
        Reset();
        Create(0).ReloadForTest(false);
        ThighController second = Create(0);
        second.ReloadForTest(false);
        second.ButtParams.Softness = 0.4f;
        ThighController third = Create(0);
        third.ReloadForTest(false);
        AllOff(third);
        Assert(Math.Abs(third.ButtParams.Softness - 0.4f) < 0.0001f, "isolated load detached active tuning from session memory");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
