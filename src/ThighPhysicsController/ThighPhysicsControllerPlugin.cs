using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using KKAPI.Chara;
using KKAPI.Studio.SaveLoad;
using Studio;
using UnityEngine;

namespace ThighPhysicsController;

[BepInDependency("marco.kkapi")]
[BepInPlugin("codex.koikatumanager.thighphysicscontroller", "Soma Dynamics", "1.0.2.2")]
[DefaultExecutionOrder(-1000)]
public class ThighPhysicsControllerPlugin : BaseUnityPlugin
{
    internal static ConfigEntry<KeyboardShortcut> WindowKey;
    internal static ConfigEntry<bool> AutoApply;
    internal static ConfigEntry<bool> ForceEnable;
    internal static ConfigEntry<bool> RememberPerCharacter;
    internal static ConfigEntry<bool> AutoFixSpringDrift;
    internal static ConfigEntry<bool> AutoResetPoseOnStudioChange;
    internal static ConfigEntry<bool> DebugCollectMetrics;
    internal static ConfigEntry<bool> DebugLogFlesh;
    internal static ConfigEntry<bool> DebugDumpSkeleton;
    internal static ConfigEntry<string> PresetDirectory;

    internal static readonly List<ThighController> Controllers = new List<ThighController>();
    internal static readonly Dictionary<string, FleshProfile> MemoryProfiles =
        new Dictionary<string, FleshProfile>();

    private Rect _windowRect = new Rect(20f, 20f, 560f, 680f);
    private bool _showWindow;
    private int _selected = -1;
    private int _selectedInstanceId = -1;
    private int _selectedPart;
    private int _presetIndex;
    private string _presetName = "MyPreset.xml";
    private bool _advancedMode;
    private Vector2 _scroll = Vector2.zero;
    private GUIStyle _windowStyle;

    private readonly Dictionary<string, string> _editBuffers = new Dictionary<string, string>();
    private readonly Dictionary<string, float> _lastValues = new Dictionary<string, float>();

    private static bool _blockInput;
    private static bool _blockScroll;
    private static bool _inputCaptured;
    private static bool _bypassInput;
    private static bool _mouseOverWindow;
    private static Vector2 _lastGuiMouse;
    private static bool _hasGuiMouse;
    private static ManualLogSource _runtimeLog;
    private static bool _loggedBustSoftGuard;
    private static bool _loggedBustGravityGuard;

    private Harmony _harmony;
    private Harmony _inputHarmony;
    private void Awake()
    {
        WindowKey = Config.Bind("General", "Window key",
            new KeyboardShortcut(KeyCode.Insert),
            "Toggle the flesh physics window.");
        AutoApply = Config.Bind("General", "Auto apply on load", true,
            "Create and apply thigh dynamic bones on every character load.");
        ForceEnable = Config.Bind("General", "Force enable", true,
            "Re-enable flesh physics even when the card disabled it.");
        RememberPerCharacter = Config.Bind("General", "Remember per-character settings", true,
            "Keep this session's flesh physics settings per character " +
            "(name+sex+personality) and sync same-name characters in the scene.");
        AutoFixSpringDrift = Config.Bind("General", "Auto fix spring drift", true,
            "Slowly ease spring-mode base drift back to the card pose so dancing " +
            "does not progressively deform the thighs.");
        AutoResetPoseOnStudioChange = Config.Bind("General",
            "Auto reset pose on Studio character or animation change", true,
            "Remove Soma's own deformation before Studio changes a character or animation, " +
            "then adopt the settled Timeline/Animator pose as the Chain rest frame.");
        DebugCollectMetrics = Config.Bind("Debug", "Collect runtime metrics", false,
            "Log five-second FPC_METRIC windows with mean/RMS/peak offsets and safety reset counts.");
        DebugLogFlesh = Config.Bind("Debug", "Log flesh physics", false,
            "Log flesh physics bone offsets every two seconds.");
        DebugDumpSkeleton = Config.Bind("Debug", "Dump skeleton bones", false,
            "Log all leg/hip/body deformation bone names once per character.");
        PresetDirectory = Config.Bind("Presets", "Preset directory",
            Path.Combine(Path.GetDirectoryName(typeof(ThighPhysicsControllerPlugin).Assembly.Location), "Presets"),
            "Folder for flesh physics XML presets.");
        Directory.CreateDirectory(PresetDirectory.Value);

        CharacterApi.RegisterExtraBehaviour<ThighController>("codex.koikatumanager.thighphysicscontroller");
        _runtimeLog = Logger;
        Logger.LogInfo("Soma Dynamics initialized (autoApply=" + AutoApply.Value +
                       ", forceEnable=" + ForceEnable.Value + ", presets=" + PresetDirectory.Value + ").");

        try
        {
            // The BPC-compatible guards below use Harmony attributes. They must be
            // registered separately from the manually patched Unity input methods.
            // Without this call, BustSoft/BustGravity can overwrite FPC's values
            // after a body/collision refresh even though the patch classes compile.
            _harmony = new Harmony("codex.koikatumanager.thighphysicscontroller.runtime");
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            StudioSaveLoadApi.SceneLoad += OnStudioSceneLoad;
            Logger.LogInfo("Native breast and Studio pose-change patches installed.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Failed to install FPC Harmony patches: " + ex);
        }
    }

    private void PatchInputMethod(string methodName, Type parameterType)
    {
        MethodInfo method = AccessTools.Method(typeof(Input), methodName, new[] { parameterType }, null);
        if (method == null)
        {
            return;
        }
        MethodInfo prefix = methodName.StartsWith("GetMouse")
            ? typeof(ThighPhysicsControllerPlugin).GetMethod("MouseButtonPrefix",
                BindingFlags.Static | BindingFlags.NonPublic)
            : typeof(ThighPhysicsControllerPlugin).GetMethod("AxisPrefix",
                BindingFlags.Static | BindingFlags.NonPublic);
        if (prefix != null)
        {
            _inputHarmony.Patch(method, new HarmonyMethod(prefix));
        }
    }

    private void SetInputPatches(bool enabled)
    {
        if (enabled)
        {
            if (_inputHarmony != null)
                return;
            _inputHarmony = new Harmony("codex.koikatumanager.thighphysicscontroller.input");
            PatchInputMethod("GetAxis", typeof(string));
            PatchInputMethod("GetAxisRaw", typeof(string));
            PatchInputMethod("GetMouseButton", typeof(int));
            PatchInputMethod("GetMouseButtonDown", typeof(int));
            PatchInputMethod("GetMouseButtonUp", typeof(int));
            return;
        }
        if (_inputHarmony == null)
            return;
        _inputHarmony.UnpatchSelf();
        _inputHarmony = null;
    }

    private static bool AxisPrefix(string axisName, ref float __result)
    {
        if (_bypassInput)
        {
            return true;
        }
        if (_blockScroll && axisName == "Mouse ScrollWheel")
        {
            __result = 0f;
            return false;
        }
        if ((_blockInput || _mouseOverWindow) && (axisName == "Mouse X" || axisName == "Mouse Y"))
        {
            __result = 0f;
            return false;
        }
        return true;
    }

    private static bool MouseButtonPrefix(ref bool __result)
    {
        if (_bypassInput)
        {
            return true;
        }
        if (_blockInput || _mouseOverWindow)
        {
            __result = false;
            return false;
        }
        return true;
    }

    private void Update()
    {
        KeyboardShortcut shortcut = WindowKey.Value;
        if (shortcut.IsDown())
        {
            _showWindow = !_showWindow;
            SetInputPatches(_showWindow);
        }
        if (_showWindow)
        {
            Vector2 mouse = _hasGuiMouse
                ? _lastGuiMouse
                : new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            _mouseOverWindow = _windowRect.Contains(mouse);

            _bypassInput = true;
            bool anyMouse = Input.GetMouseButton(0) || Input.GetMouseButton(1) ||
                            Input.GetMouseButton(2);
            _bypassInput = false;
            if (!_inputCaptured && _mouseOverWindow && anyMouse)
                _inputCaptured = true;
            if (_inputCaptured && !anyMouse)
                _inputCaptured = false;
            _blockInput = _inputCaptured;
            _blockScroll = _mouseOverWindow;
        }
        else
        {
            _inputCaptured = false;
            _mouseOverWindow = false;
            _blockInput = false;
            _blockScroll = false;
        }

        for (int i = Controllers.Count - 1; i >= 0; i--)
        {
            ThighController controller = Controllers[i];
            if (controller != null)
            {
                controller.UpdateTick();
            }
        }
    }

    private void OnDestroy()
    {
        StudioSaveLoadApi.SceneLoad -= OnStudioSceneLoad;
        SetInputPatches(false);
        if (_harmony != null)
        {
            _harmony.UnpatchSelf();
            _harmony = null;
        }
        _runtimeLog = null;
    }

    private static ThighController FindController(ChaControl chaControl)
    {
        if (chaControl == null)
            return null;
        for (int i = Controllers.Count - 1; i >= 0; i--)
        {
            ThighController controller = Controllers[i];
            if (controller != null && controller.ChaControlMatches(chaControl))
                return controller;
        }
        return null;
    }

    private static void PrepareForStudioPoseChange(OCIChar character)
    {
        if (character == null || !AutoResetPoseOnStudioChange.Value)
            return;
        ThighController controller = FindController(character.charInfo);
        if (controller != null)
            controller.PrepareForStudioPoseChange(2);
    }

    private static void OnStudioSceneLoad(object sender, SceneLoadEventArgs args)
    {
        if (!AutoResetPoseOnStudioChange.Value)
            return;
        int prepared = 0;
        for (int i = Controllers.Count - 1; i >= 0; i--)
        {
            ThighController controller = Controllers[i];
            if (controller == null)
                continue;
            controller.PrepareForStudioPoseChange(2);
            prepared++;
        }
        _runtimeLog?.LogInfo("SOMA_SCENE_REBASE operation=" + args.Operation +
            " controllers=" + prepared);
    }

    [HarmonyPatch(typeof(OCIChar), "LoadAnime")]
    private static class StudioLoadAnimePatch
    {
        [HarmonyPrefix]
        private static void Prefix(OCIChar __instance)
        {
            PrepareForStudioPoseChange(__instance);
        }
    }

    [HarmonyPatch(typeof(OCIChar), "RestartAnime")]
    private static class StudioRestartAnimePatch
    {
        [HarmonyPrefix]
        private static void Prefix(OCIChar __instance)
        {
            PrepareForStudioPoseChange(__instance);
        }
    }

    [HarmonyPatch(typeof(OCIChar), "ChangeChara")]
    private static class StudioChangeCharaPatch
    {
        [HarmonyPrefix]
        private static void Prefix(OCIChar __instance)
        {
            PrepareForStudioPoseChange(__instance);
        }
    }

    private void OnGUI()
    {
        if (!_showWindow)
        {
            return;
        }
        GUI.matrix = Matrix4x4.identity;
        if (_windowStyle == null)
        {
            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.fontSize = 12;
            _windowStyle.alignment = TextAnchor.UpperCenter;
        }
        _windowRect = GUI.Window(GetWindowId(), _windowRect, WindowFunction,
            "Soma Dynamics / 形体动力学控制器", _windowStyle);
        if (Event.current != null)
        {
            _lastGuiMouse = Event.current.mousePosition;
            _hasGuiMouse = true;
        }
        if (Event.current != null && Event.current.type == EventType.Repaint)
        {
            _mouseOverWindow = _showWindow && _windowRect.Contains(_lastGuiMouse);
            _blockScroll = _mouseOverWindow;
        }
    }

    private static int GetWindowId()
    {
        return Mathf.Abs("ThighPhysicsController".GetHashCode()) % 900000;
    }

    private void WindowFunction(int windowId)
    {
        GUILayout.BeginVertical();
        _scroll = GUILayout.BeginScrollView(_scroll);
        if (Controllers.Count == 0)
        {
            GUILayout.Label("No characters loaded. Open the maker or load a scene.");
        }
        else
        {
            int femaleCount = 0;
            int maleCount = 0;
            for (int i = 0; i < Controllers.Count; i++)
            {
                if (Controllers[i].IsMale)
                {
                    maleCount++;
                }
                else
                {
                    femaleCount++;
                }
            }
            GUILayout.Label("女性角色 (" + femaleCount + ")");
            for (int i = 0; i < Controllers.Count; i++)
            {
                ThighController candidate = Controllers[i];
                if (!candidate.IsMale)
                {
                    DrawCharacterRow(i, candidate);
                }
            }
            GUILayout.Label("男性角色 (" + maleCount + ")");
            for (int i = 0; i < Controllers.Count; i++)
            {
                ThighController candidate = Controllers[i];
                if (candidate.IsMale)
                {
                    DrawCharacterRow(i, candidate);
                }
            }
            if (_selected < 0 || _selected >= Controllers.Count)
            {
                _selected = 0;
                _selectedInstanceId = Controllers.Count > 0
                    ? Controllers[0].GetInstanceID()
                    : -1;
            }
            ThighController controller = Controllers[_selected];
            if (controller != null)
            {
                DrawControllerPanel(controller);
            }
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
    }

    private void DrawCharacterRow(int index, ThighController controller)
    {
        bool selected = _selectedInstanceId >= 0 &&
                        controller.GetInstanceID() == _selectedInstanceId;
        string label = (selected ? "▶ " : "   ") + "[" + index + "] " +
                       controller.DisplayName;
        if (GUILayout.Button(label))
        {
            _selected = index;
            _selectedInstanceId = controller.GetInstanceID();
        }
    }

    private void DrawControllerPanel(ThighController controller)
    {
        GUILayout.Space(6f);
        DrawWholeBodyPanel(controller);
        GUILayout.Space(8f);
        string[] partNames = { "大腿 Thigh", "手臂 Arm", "腹部 Belly", "胸部 Breast", "臀部 Butt" };
        // Explicit toggle buttons instead of SelectionGrid: the grid click could be
        // eaten by the input-blocking patches, leaving the panel stuck on the wrong
        // part (e.g. Belly requested but Arm per-bone labels shown).
        GUILayout.BeginHorizontal();
        for (int p = 0; p < partNames.Length; p++)
        {
            bool isPart = _selectedPart == p;
            if (GUILayout.Toggle(isPart, partNames[p], GUILayout.Width(100f)) && !isPart)
            {
                _selectedPart = p;
            }
        }
        GUILayout.EndHorizontal();
        FleshPartId partId = (FleshPartId)_selectedPart;
        if (partId == FleshPartId.Breast || partId == FleshPartId.Butt)
        {
            DrawNativeBodyPanel(controller, partId, partNames[_selectedPart]);
            return;
        }
        ThighParams part = controller.GetParams(partId);
        string partLabel = partNames[_selectedPart];

        bool oldPartEnabled = part.Enabled;
        bool newPartEnabled = GUILayout.Toggle(part.Enabled,
            " 启用 " + partLabel + " 物理  Enable");
        if (newPartEnabled != oldPartEnabled)
        {
            part.Enabled = newPartEnabled;
            controller.Apply(resetPosition: false);
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label(_advancedMode ? "高级参数  Advanced parameters" : "基础参数  Essential controls");
        _advancedMode = GUILayout.Toggle(_advancedMode, "高级 Advanced", GUILayout.Width(120f));
        GUILayout.EndHorizontal();

        bool gamePhysics = part.GamePhysics;
        GUILayout.BeginHorizontal();
        GUILayout.Label("求解模式  Solver", GUILayout.Width(110f));
        if (GUILayout.Button((!gamePhysics ? "● " : "○ ") + "弹簧 Spring",
                GUILayout.Width(145f)) && gamePhysics)
        {
            part.GamePhysics = false;
        }
        if (GUILayout.Button((gamePhysics ? "● " : "○ ") + "链式 Chain",
                GUILayout.Width(145f)) && !gamePhysics)
        {
            part.GamePhysics = true;
        }
        GUILayout.EndHorizontal();
        if (gamePhysics != part.GamePhysics)
        {
            // Clear the previous mode's deformation first, otherwise the chain
            // captures the deformed pose as its base and never rebounds.
            controller.ClearDeformation();
            controller.Apply(resetPosition: true);
        }

        string ctrlId = "c" + controller.GetInstanceID() + "_p" + _selectedPart;
        if (!_advancedMode)
        {
            GUILayout.Space(6f);
            GUILayout.Label("摆动强度  Swing");
            float strength = FleshTuning.GetStrength(part);
            float newStrength = NumericSlider(ctrlId + "_simple_strength", strength, 0f,
                FleshParameterRanges.TargetMax, "");
            if (Mathf.Abs(newStrength - strength) > 0.00001f)
            {
                FleshTuning.SetStrength(part, newStrength);
            }

            GUILayout.Label("柔顺度  Softness");
            float softness = FleshTuning.GetSoftness(part, partId);
            float newSoftness = NumericSlider(ctrlId + "_simple_softness", softness, 0f,
                FleshParameterRanges.TargetMax, "");
            if (Mathf.Abs(newSoftness - softness) > 0.00001f)
            {
                FleshTuning.SetSoftness(part, partId, newSoftness);
            }

            GUILayout.Label("动作响应  Motion response");
            float motionTarget = FleshTuning.GetMotionTarget(part);
            float newMotionTarget = NumericSlider(ctrlId + "_simple_motion", motionTarget,
                0f, FleshParameterRanges.TargetMax, "");
            if (Mathf.Abs(newMotionTarget - motionTarget) > 0.00001f)
                FleshTuning.SetMotionTarget(part, newMotionTarget);
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("恢复姿态  Restore pose"))
                controller.ClearDeformation();
            if (GUILayout.Button("恢复推荐基线  Reset"))
            {
                controller.SetParams(partId, ThighParams.CreatePartDefaults(partId));
                controller.ClearDeformation();
                controller.Apply(resetPosition: true);
            }
            GUILayout.EndHorizontal();
            return;
        }

        GUILayout.Label("动作增益  Motion gain (0–10)");
        part.MotionGain = NumericSlider(ctrlId + "_mg", part.MotionGain, 0f,
            FleshParameterRanges.MotionGainMax, "");
        if (part.GamePhysics)
        {
            GUILayout.Space(6f);
            GUILayout.Label("链式求解参数  Chain solver");
            ChainParams chain = part.Chain;
            GUILayout.Label("Weight");
            chain.Weight = NumericSlider(ctrlId + "_cw", chain.Weight, 0f,
                FleshParameterRanges.WeightMax, "");
            GUILayout.Label("Gravity");
            chain.Gravity = NumericSlider(ctrlId + "_cg", chain.Gravity,
                -FleshParameterRanges.GravityMax, FleshParameterRanges.GravityMax, "");
            chain.Damping = NumericSlider(ctrlId + "_cd", chain.Damping, 0f, 1f, "Damping");
            chain.Elasticity = NumericSlider(ctrlId + "_ce", chain.Elasticity, 0f, 1f, "Elasticity");
            chain.Stiffness = NumericSlider(ctrlId + "_cs", chain.Stiffness, 0f, 1f, "Stiffness");
            chain.Inert = NumericSlider(ctrlId + "_ci", chain.Inert, 0f,
                FleshParameterRanges.CustomInertMax, "Inert");
            chain.JitterFreq = NumericSlider(ctrlId + "_cjf", chain.JitterFreq, 0f,
                FleshParameterRanges.JitterFrequencyMax,
                "Jitter freq");
        }
        else
        {
            GUILayout.Label("Weight");
            part.Weight = NumericSlider(ctrlId + "_w", part.Weight, 0f,
                FleshParameterRanges.WeightMax, "");
            GUILayout.Label("Gravity");
            part.Gravity = NumericSlider(ctrlId + "_g", part.Gravity,
                -FleshParameterRanges.GravityMax, FleshParameterRanges.GravityMax, "");
            part.JitterFreq = NumericSlider(ctrlId + "_jf", part.JitterFreq, 0f,
                FleshParameterRanges.JitterFrequencyMax,
                "Jitter freq");
            part.MotionSmooth = NumericSlider(ctrlId + "_ms", part.MotionSmooth, 0.05f,
                FleshParameterRanges.MotionSmoothMax,
                "Motion smooth");
            DrawBoneSection(ctrlId, partLabel + " flesh (shared)", part.Thigh00);
        }

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("恢复姿态  Restore pose"))
            controller.ClearDeformation();
        if (GUILayout.Button("恢复推荐基线  Reset"))
        {
            controller.SetParams(partId, ThighParams.CreatePartDefaults(partId));
            controller.ClearDeformation();
            controller.Apply(resetPosition: true);
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        GUILayout.Label("配置文件（全角色）  Profile import / export");
        string[] presetFiles = GetPresetFiles();
        if (presetFiles.Length > 0)
        {
            if (_presetIndex >= presetFiles.Length)
            {
                _presetIndex = 0;
            }
            _presetIndex = GUILayout.SelectionGrid(_presetIndex, presetFiles, 1);
        }
        else
        {
            GUILayout.Label("暂无配置文件  No profiles");
        }
        GUILayout.BeginHorizontal();
        _presetName = GUILayout.TextField(_presetName, 120);
        if (GUILayout.Button("导出  Export"))
        {
            string targetPath;
            if (WindowsFileDialog.ShowSave(PresetDirectory.Value, EnsureXml(_presetName), out targetPath))
            {
                controller.SavePreset(targetPath);
            }
        }
        if (GUILayout.Button("导入所选  Import") && presetFiles.Length > 0)
        {
            controller.LoadPreset(Path.Combine(PresetDirectory.Value, presetFiles[_presetIndex]));
            controller.Apply(resetPosition: true);
        }
        if (GUILayout.Button("浏览文件  Browse..."))
        {
            string sourcePath;
            if (WindowsFileDialog.ShowOpen(PresetDirectory.Value, out sourcePath))
            {
                controller.LoadPreset(sourcePath);
                controller.Apply(resetPosition: true);
            }
        }
        GUILayout.EndHorizontal();

        DrawBoneAmounts(ctrlId,
            part.GamePhysics ? part.ChainBones : part.Bones,
            part.GamePhysics,
            GetPartBoneLabels(partId));
    }

    private void DrawWholeBodyPanel(ThighController controller)
    {
        string ctrlId = "c" + controller.GetInstanceID() + "_whole";
        NativeBodyParams breast = controller.GetNativeParams(FleshPartId.Breast);
        NativeBodyParams butt = controller.GetNativeParams(FleshPartId.Butt);
        float strength = breast.Strength + butt.Strength;
        float softness = breast.Softness + butt.Softness;
        float motion = breast.MotionResponse + butt.MotionResponse;
        for (int i = 0; i < 3; i++)
        {
            FleshPartId part = (FleshPartId)i;
            ThighParams value = controller.GetParams(part);
            strength += FleshTuning.GetStrength(value);
            softness += FleshTuning.GetSoftness(value, part);
            motion += FleshTuning.GetMotionTarget(value);
        }
        strength /= 5f;
        softness /= 5f;
        motion /= 5f;

        GUILayout.Label("全身控制  Global controls");
        GUILayout.Label("统一调整五个部位；部位页用于局部修正。0–1 常用，1–2 增强。",
            GUILayout.Width(520f));
        GUILayout.Label("摆动强度  Swing");
        float newStrength = NumericSlider(ctrlId + "_strength", strength, 0f,
            FleshParameterRanges.TargetMax, "");
        GUILayout.Label("柔顺度  Softness");
        float newSoftness = NumericSlider(ctrlId + "_softness", softness, 0f,
            FleshParameterRanges.TargetMax, "");
        GUILayout.Label("动作响应  Motion response");
        float newMotion = NumericSlider(ctrlId + "_motion", motion, 0f,
            FleshParameterRanges.TargetMax, "");
        bool setStrength = Mathf.Abs(newStrength - strength) > 0.00001f;
        bool setSoftness = Mathf.Abs(newSoftness - softness) > 0.00001f;
        bool setMotion = Mathf.Abs(newMotion - motion) > 0.00001f;
        if (setStrength || setSoftness || setMotion)
        {
            controller.SetWholeBodyTargets(newStrength, newSoftness, newMotion,
                setStrength, setSoftness, setMotion);
        }

        GUILayout.Label("全身预设  Global level");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("低  Low"))
        {
            ApplyFeelPreset(controller, FleshFeelPreset.Stable);
            GUILayout.EndHorizontal();
            return;
        }
        if (GUILayout.Button("中  Medium"))
        {
            ApplyFeelPreset(controller, FleshFeelPreset.Natural);
            GUILayout.EndHorizontal();
            return;
        }
        if (GUILayout.Button("高  High"))
        {
            ApplyFeelPreset(controller, FleshFeelPreset.Dance);
            GUILayout.EndHorizontal();
            return;
        }
        GUILayout.EndHorizontal();
        GUILayout.Label("实时生效 · 随角色卡保存  Live · Saved with character card");
    }

    private void DrawNativeBodyPanel(ThighController controller, FleshPartId partId,
        string partLabel)
    {
        NativeBodyParams part = controller.GetNativeParams(partId);
        string ctrlId = "c" + controller.GetInstanceID() + "_p" + _selectedPart;
        bool oldEnabled = part.Enabled;
        float oldStrength = part.Strength;
        float oldSoftness = part.Softness;
        float oldResponse = part.MotionResponse;
        GUILayout.Space(6f);
        GUILayout.Label("原生碰撞链  Native DynamicBone");
        bool newEnabled = GUILayout.Toggle(part.Enabled,
            " 启用参数接管  Enable override");
        if (newEnabled != oldEnabled)
        {
            part.Enabled = newEnabled;
        }
        if (partId == FleshPartId.Breast)
            GUILayout.Label("当前状态  Current: " + controller.CurrentBustStateLabel,
                GUILayout.Width(500f));

        GUILayout.Space(6f);
        GUILayout.Label("摆动强度  Swing");
        part.Strength = NumericSlider(ctrlId + "_native_strength", part.Strength, 0f,
            FleshParameterRanges.TargetMax, "");
        GUILayout.Label("柔顺度  Softness");
        part.Softness = NumericSlider(ctrlId + "_native_softness", part.Softness, 0f,
            FleshParameterRanges.TargetMax, "");
        GUILayout.Label("动作响应  Motion response");
        part.MotionResponse = NumericSlider(ctrlId + "_native_response",
            part.MotionResponse, 0f, FleshParameterRanges.TargetMax, "");
        bool simpleChanged = Mathf.Abs(part.Strength - oldStrength) > 0.00001f ||
                             Mathf.Abs(part.Softness - oldSoftness) > 0.00001f ||
                             Mathf.Abs(part.MotionResponse - oldResponse) > 0.00001f;
        if (simpleChanged)
            part.AdvancedOverride = false;

        GUILayout.BeginHorizontal();
        GUILayout.Label(_advancedMode ? "高级参数  Advanced parameters" :
            "基础参数  Essential controls");
        _advancedMode = GUILayout.Toggle(_advancedMode, "高级 Advanced", GUILayout.Width(120f));
        GUILayout.EndHorizontal();
        bool advancedChanged = false;
        if (_advancedMode)
        {
            float oldGravity = part.Gravity;
            GUILayout.Label("重力  Gravity");
            part.Gravity = NumericSlider(ctrlId + "_native_gravity", part.Gravity,
                -FleshParameterRanges.NativeGravityMax,
                FleshParameterRanges.NativeGravityMax, "");
            bool rawChanged = Mathf.Abs(part.Gravity - oldGravity) > 0.000001f;
            string[] labels = partId == FleshPartId.Breast
                ? new[] { "cf_j_bust01", "cf_j_bust02", "cf_j_bust03" }
                : new[] { "cf_d_siri01", "cf_j_siri_01" };
            for (int i = 0; i < labels.Length; i++)
                rawChanged |= DrawNativeBoneEditor(ctrlId, i, labels[i], part.GetBone(i));
            if (rawChanged)
                part.AdvancedOverride = true;
            advancedChanged = rawChanged;
            GUILayout.BeginHorizontal();
            GUILayout.Label(part.AdvancedOverride ? "逐骨覆盖  Per-bone override" :
                "推荐映射  Recommended mapping");
            if (part.AdvancedOverride &&
                GUILayout.Button("恢复推荐映射  Use mapping", GUILayout.Width(165f)))
            {
                part.AdvancedOverride = false;
                simpleChanged = true;
            }
            GUILayout.EndHorizontal();
            if (partId == FleshPartId.Breast &&
                GUILayout.Button("应用到全部胸部状态  Apply to all breast states"))
            {
                controller.CopyCurrentBreastToAllStates();
            }
        }

        if (oldEnabled != part.Enabled || simpleChanged || advancedChanged)
            controller.RequestNativeReapply(1);

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("恢复推荐基线  Reset"))
        {
            controller.SetNativeParams(partId, NativeBodyParams.CreateDefault(partId));
            controller.Apply(resetPosition: true);
            GUILayout.EndHorizontal();
            return;
        }
        GUILayout.EndHorizontal();
        GUILayout.Label("保留游戏碰撞；参数实时生效并随角色卡保存。",
            GUILayout.Width(500f));
    }

    private bool DrawNativeBoneEditor(string ctrlId, int index, string label,
        NativeBoneParams bone)
    {
        bool changed = false;
        GUILayout.Space(4f);
        GUILayout.Label(label);
        bool rotation = GUILayout.Toggle(bone.IsRotationCalc, " RotationCalc");
        if (rotation != bone.IsRotationCalc)
        {
            bone.IsRotationCalc = rotation;
            changed = true;
        }
        float value = NumericSlider(ctrlId + "_nb" + index + "_d", bone.Damping,
            0f, 1f, "Damping");
        changed |= Mathf.Abs(value - bone.Damping) > 0.00001f;
        bone.Damping = value;
        value = NumericSlider(ctrlId + "_nb" + index + "_e", bone.Elasticity,
            0f, 1f, "Elasticity");
        changed |= Mathf.Abs(value - bone.Elasticity) > 0.00001f;
        bone.Elasticity = value;
        value = NumericSlider(ctrlId + "_nb" + index + "_s", bone.Stiffness,
            0f, 1f, "Stiffness");
        changed |= Mathf.Abs(value - bone.Stiffness) > 0.00001f;
        bone.Stiffness = value;
        value = NumericSlider(ctrlId + "_nb" + index + "_i", bone.Inert,
            0f, 1f, "Inert");
        changed |= Mathf.Abs(value - bone.Inert) > 0.00001f;
        bone.Inert = value;
        return changed;
    }

    private static void ApplyFeelPreset(ThighController controller, FleshFeelPreset preset)
    {
        for (int i = 0; i < 3; i++)
        {
            FleshPartId part = (FleshPartId)i;
            ThighParams current = controller.GetParams(part).Clone();
            ThighParams level = FleshTuning.CreateFeelPreset(part, preset);
            FleshTuning.ApplyLevelTargets(current, part,
                FleshTuning.GetStrength(level),
                FleshTuning.GetSoftness(level, part),
                FleshTuning.GetMotionTarget(level));
            FleshTuning.ApplyLevelAmplitudes(current, part, preset);
            controller.SetParams(part, current);
        }
        NativeBodyParams breastLevel = NativeBodyTuning.CreateFeelPreset(
            FleshPartId.Breast, preset);
        controller.BreastProfile.SetTargetsAll(breastLevel.Strength,
            breastLevel.Softness, breastLevel.MotionResponse, true, true, true);
        NativeBodyParams buttLevel = NativeBodyTuning.CreateFeelPreset(FleshPartId.Butt,
            preset);
        NativeBodyTuning.SetTargets(controller.ButtParams, FleshPartId.Butt,
            buttLevel.Strength, buttLevel.Softness, buttLevel.MotionResponse);
        controller.RequestNativeReapply(1);
        controller.Apply(resetPosition: false);
        string levelName = preset == FleshFeelPreset.Stable ? "Low" :
            preset == FleshFeelPreset.Natural ? "Medium" : "High";
        UnityEngine.Debug.Log("FPC_PRESET_APPLY level=" + levelName +
            " scope=WholeBody modes=Preserved");
    }

    private bool DrawBoneSection(string ctrlId, string label, ThighBoneParams bone)
    {
        bool changed = false;
        GUILayout.Space(4f);
        GUILayout.Label("共享骨骼参数  Shared bone · " + label);
        float value = NumericSlider(ctrlId + "_d", bone.Damping, 0f, 1f, "Damping");
        changed |= Mathf.Abs(value - bone.Damping) > 0.00001f;
        bone.Damping = value;
        value = NumericSlider(ctrlId + "_e", bone.Elasticity, 0f, 1f, "Elasticity");
        changed |= Mathf.Abs(value - bone.Elasticity) > 0.00001f;
        bone.Elasticity = value;
        value = NumericSlider(ctrlId + "_s", bone.Stiffness, 0f, 1f, "Stiffness");
        changed |= Mathf.Abs(value - bone.Stiffness) > 0.00001f;
        bone.Stiffness = value;
        value = NumericSlider(ctrlId + "_i", bone.Inert, 0f,
            FleshParameterRanges.CustomInertMax, "Inert");
        changed |= Mathf.Abs(value - bone.Inert) > 0.00001f;
        bone.Inert = value;
        GUILayout.Label("参数实时生效；安全限制由求解器统一处理。",
            GUILayout.Width(400f));
        return changed;
    }

    private bool DrawBoneAmounts(string ctrlId, ThighBoneAmounts bones, bool chainMode,
        string[] boneLabels)
    {
        bool changed = false;
        GUILayout.Space(4f);
        GUILayout.Label("逐骨参数  Per-bone · " + (chainMode ? "Chain" : "Spring") +
            " · Amp / Rot / RC / Axis (0 = freeze)");
        string modePrefix = chainMode ? "_c" : "_s";
        for (int r = 0; r < boneLabels.Length; r++)
        {
            int i = r;
            PerBoneAmount amount = bones.Get(i);
            GUILayout.BeginHorizontal();
            bool enabled = GUILayout.Toggle(amount.Enabled, boneLabels[r], GUILayout.Width(120f));
            changed |= enabled != amount.Enabled;
            amount.Enabled = enabled;
            GUILayout.Label("Amp", GUILayout.Width(30f));
            float value = NumericSlider(ctrlId + modePrefix + "_b" + i + "_a",
                amount.Amp, 0f, FleshParameterRanges.BoneAmplitudeMax, "");
            changed |= Mathf.Abs(value - amount.Amp) > 0.00001f;
            amount.Amp = value;
            GUILayout.Label("Rot", GUILayout.Width(28f));
            value = NumericField(ctrlId + modePrefix + "_b" + i + "_r", amount.RotAmp,
                0f, FleshParameterRanges.RotationAmplitudeMax, 52f);
            changed |= Mathf.Abs(value - amount.RotAmp) > 0.00001f;
            amount.RotAmp = value;
            bool rotCalc = GUILayout.Toggle(amount.RotCalc, "RC", GUILayout.Width(38f));
            changed |= rotCalc != amount.RotCalc;
            amount.RotCalc = rotCalc;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Space(124f);
            GUILayout.Label("X", GUILayout.Width(16f));
            value = NumericField(ctrlId + modePrefix + "_b" + i + "_x", amount.AxisX,
                0f, FleshParameterRanges.AxisScaleMax, 52f);
            changed |= Mathf.Abs(value - amount.AxisX) > 0.00001f;
            amount.AxisX = value;
            GUILayout.Label("Y", GUILayout.Width(16f));
            value = NumericField(ctrlId + modePrefix + "_b" + i + "_y", amount.AxisY,
                0f, FleshParameterRanges.AxisScaleMax, 52f);
            changed |= Mathf.Abs(value - amount.AxisY) > 0.00001f;
            amount.AxisY = value;
            GUILayout.Label("Z", GUILayout.Width(16f));
            value = NumericField(ctrlId + modePrefix + "_b" + i + "_z", amount.AxisZ,
                0f, FleshParameterRanges.AxisScaleMax, 52f);
            changed |= Mathf.Abs(value - amount.AxisZ) > 0.00001f;
            amount.AxisZ = value;
            GUILayout.EndHorizontal();
        }
        return changed;
    }

    private string[] GetPartBoneLabels(FleshPartId part)
    {
        FleshPartDef def = FleshPartDef.Get(part);
        List<string> labels = new List<string>();
        for (int c = 0; c < def.Chains.Length; c++)
        {
            for (int b = 0; b < def.Chains[c].BoneNameTemplates.Length; b++)
            {
                labels.Add(def.Chains[c].BoneNameTemplates[b]
                    .Replace("{side}", "")
                    .Replace("cf_s_", "")
                    .Trim('_'));
            }
        }
        return labels.ToArray();
    }

    private float NumericSlider(string id, float value, float min, float max, string label)
    {
        GUILayout.BeginHorizontal();
        if (label.Length > 0)
        {
            GUILayout.Label(label, GUILayout.Width(80f));
        }
        float sliderValue = GUILayout.HorizontalSlider(value, min, max);
        float result = NumericCore(id, value, min, max, 72f, sliderValue, true);
        GUILayout.EndHorizontal();
        return result;
    }

    private float NumericField(string id, float value, float min, float max, float width)
    {
        return NumericCore(id, value, min, max, width, value, false);
    }

    private float NumericCore(string id, float value, float min, float max, float width,
        float sliderValue, bool hasSlider)
    {
        value = FleshValue.Clamp(value, min, max, min);
        sliderValue = FleshValue.Clamp(sliderValue, min, max, value);
        float last;
        if (!_lastValues.TryGetValue(id, out last) || Mathf.Abs(last - value) > 0.00001f)
        {
            _editBuffers[id] = value.ToString();
        }
        _lastValues[id] = value;
        bool sliderMoved = false;
        if (hasSlider && Mathf.Abs(sliderValue - value) > 0.00001f)
        {
            value = sliderValue;
            sliderMoved = true;
            _editBuffers[id] = value.ToString();
        }
        if (sliderMoved)
        {
            GUI.SetNextControlName("TextField");
            GUILayout.TextField(value.ToString(), GUILayout.Width(width));
        }
        else
        {
            string buffer;
            if (!_editBuffers.TryGetValue(id, out buffer))
            {
                buffer = value.ToString();
                _editBuffers[id] = buffer;
            }
            GUI.SetNextControlName("TextField");
            string text = GUILayout.TextField(buffer, GUILayout.Width(width));
            _editBuffers[id] = text;
            float parsed;
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
                FleshValue.IsFinite(parsed))
            {
                if (Mathf.Abs(parsed - value) > 0.00001f)
                {
                    value = FleshValue.Clamp(parsed, min, max, value);
                    _editBuffers[id] = value.ToString();
                }
            }
            else if (GUI.GetNameOfFocusedControl() != "TextField")
            {
                _editBuffers[id] = value.ToString();
            }
        }
        _lastValues[id] = value;
        return value;
    }

    private string[] GetPresetFiles()
    {
        if (!Directory.Exists(PresetDirectory.Value))
        {
            return new string[0];
        }
        string[] files = Directory.GetFiles(PresetDirectory.Value, "*.xml");
        for (int i = 0; i < files.Length; i++)
        {
            files[i] = Path.GetFileName(files[i]);
        }
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return files;
    }

    private static string EnsureXml(string name)
    {
        string text = name == null ? string.Empty : name.Trim();
        // Never let a preset name escape the preset directory or create nested paths.
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
        {
            text = text.Replace(invalid[i], '_');
        }
        text = text.Replace('\\', '_').Replace('/', '_').Trim();
        if (text.Length == 0)
        {
            return "MyPreset.xml";
        }
        if (!text.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            text += ".xml";
        }
        return text;
    }

    private static ThighController FindBustOwner(BustSoft value)
    {
        for (int i = 0; i < Controllers.Count; i++)
        {
            ThighController controller = Controllers[i];
            if (controller != null && controller.OwnsBustSoft(value))
                return controller;
        }
        return null;
    }

    private static ThighController FindBustOwner(BustGravity value)
    {
        for (int i = 0; i < Controllers.Count; i++)
        {
            ThighController controller = Controllers[i];
            if (controller != null && controller.OwnsBustGravity(value))
                return controller;
        }
        return null;
    }

    [HarmonyPatch(typeof(BustSoft), "ReCalc")]
    private static class BustSoftReCalcPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(BustSoft __instance)
        {
            // Full BPC behavior: do not let the game's body-shape/collision refresh
            // overwrite FPC's active custom breast DynamicBone parameters.
            if (FindBustOwner(__instance) == null)
                return true;
            if (!_loggedBustSoftGuard)
            {
                _loggedBustSoftGuard = true;
                _runtimeLog?.LogInfo("FPC_NATIVE_GUARD blocked BustSoft.ReCalc overwrite.");
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(BustGravity), "ReCalc")]
    private static class BustGravityReCalcPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(BustGravity __instance)
        {
            if (FindBustOwner(__instance) == null)
                return true;
            if (!_loggedBustGravityGuard)
            {
                _loggedBustGravityGuard = true;
                _runtimeLog?.LogInfo("FPC_NATIVE_GUARD blocked BustGravity.ReCalc overwrite.");
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ChaControl), "SetClothesState")]
    private static class ClothesStateChangedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ChaControl __instance)
        {
            for (int i = 0; i < Controllers.Count; i++)
            {
                ThighController controller = Controllers[i];
                if (controller != null && controller.ChaControlMatches(__instance))
                {
                    controller.OnClothesStateChanged();
                    break;
                }
            }
        }
    }
}
