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
using UnityEngine;

namespace ThighPhysicsController;

[BepInDependency("marco.kkapi")]
[BepInPlugin("codex.koikatumanager.thighphysicscontroller", "Flesh Physics Controller", "0.8.11.0")]
[DefaultExecutionOrder(-1000)]
public class ThighPhysicsControllerPlugin : BaseUnityPlugin
{
    internal static ConfigEntry<KeyboardShortcut> WindowKey;
    internal static ConfigEntry<bool> AutoApply;
    internal static ConfigEntry<bool> ForceEnable;
    internal static ConfigEntry<bool> RememberPerCharacter;
    internal static ConfigEntry<bool> AutoFixSpringDrift;
    internal static ConfigEntry<string> DebugAutoLoadScene;
    internal static ConfigEntry<bool> DebugRegressionMotion;
    internal static ConfigEntry<bool> DebugRegressionSweepSoftness;
    internal static ConfigEntry<int> DebugRegressionSoftnessSteps;
    internal static ConfigEntry<float> DebugRegressionStageSeconds;
    internal static ConfigEntry<int> DebugRegressionTargetFps;
    internal static ConfigEntry<string> DebugRegressionFeelPreset;
    internal static ConfigEntry<string> DebugRegressionSolverMode;
    internal static ConfigEntry<float> DebugRegressionStrength;
    internal static ConfigEntry<float> DebugRegressionSoftness;
    internal static ConfigEntry<float> DebugRegressionResponse;
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
    private float _debugLoadTimer;
    private bool _debugLoadAttempted;
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

    private Harmony _harmony;
    private float _regressionTime;
    private bool _regressionWasActive;
    private int _regressionStage = -1;
    private bool _regressionFrameRateOverridden;
    private int _regressionPreviousTargetFps;
    private int _regressionPreviousVSync;
    private readonly Dictionary<int, RegressionAnchor> _regressionAnchors =
        new Dictionary<int, RegressionAnchor>();
    private readonly Dictionary<int, int> _regressionConfiguredStages =
        new Dictionary<int, int>();

    private sealed class RegressionAnchor
    {
        public Transform Bone;
        public Quaternion BaseRotation;
        public Vector3 BasePosition;
    }

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
        DebugAutoLoadScene = Config.Bind("Debug", "Auto load studio scene", string.Empty,
            "If set to a .png scene file, the plugin tries to load it shortly after CharaStudio starts. Used for sandbox tests.");
        DebugRegressionMotion = Config.Bind("Debug", "Run regression motion", false,
            "Drive thigh, arm, and belly anchors with deterministic sine rotations for repeatable physics tests.");
        DebugRegressionSweepSoftness = Config.Bind("Debug", "Regression softness sweep", false,
            "While regression motion runs, sweep evenly spaced softness points.");
        DebugRegressionSoftnessSteps = Config.Bind("Debug", "Regression softness steps", 3,
            "Number of evenly spaced softness points, clamped to 2..9.");
        DebugRegressionStageSeconds = Config.Bind("Debug", "Regression stage seconds", 17f,
            "Seconds per softness point, clamped to 7..60. Includes a two-second warmup.");
        DebugRegressionTargetFps = Config.Bind("Debug", "Regression target FPS", -1,
            "Positive values disable VSync and set Application.targetFrameRate during regression only.");
        DebugRegressionFeelPreset = Config.Bind("Debug", "Regression feel preset", "Default",
            "Default, Stable, Natural, or Dance. Applies one complete whole-body preset before overrides.");
        DebugRegressionSolverMode = Config.Bind("Debug", "Regression solver mode", "Default",
            "Default, Chain, or Spring. Default keeps the loaded card/default mode.");
        DebugRegressionStrength = Config.Bind("Debug", "Regression strength", -1f,
            "Override simple strength during regression; negative keeps each part's baseline.");
        DebugRegressionSoftness = Config.Bind("Debug", "Regression softness", -1f,
            "Override simple softness during regression; negative keeps each part's baseline.");
        DebugRegressionResponse = Config.Bind("Debug", "Regression motion response", -1f,
            "Override motion response during regression; negative keeps each part's baseline.");
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
        Logger.LogInfo("Flesh Physics Controller initialized (autoApply=" + AutoApply.Value +
                       ", forceEnable=" + ForceEnable.Value + ", presets=" + PresetDirectory.Value + ").");

        try
        {
            _harmony = new Harmony("codex.koikatumanager.thighphysicscontroller.input");
            PatchInputMethod("GetAxis", typeof(string));
            PatchInputMethod("GetAxisRaw", typeof(string));
            PatchInputMethod("GetMouseButton", typeof(int));
            PatchInputMethod("GetMouseButtonDown", typeof(int));
            PatchInputMethod("GetMouseButtonUp", typeof(int));
            Logger.LogInfo("Input blocking patches installed.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Failed to install input blocking patches: " + ex.Message);
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
            _harmony.Patch(method, new HarmonyMethod(prefix));
        }
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
        }
        Vector2 mouse = _hasGuiMouse
            ? _lastGuiMouse
            : new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        _mouseOverWindow = _showWindow && _windowRect.Contains(mouse);

        _bypassInput = true;
        bool anyMouse = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
        _bypassInput = false;
        if (!_inputCaptured && _mouseOverWindow && anyMouse)
        {
            _inputCaptured = true;
        }
        if (_inputCaptured && !anyMouse)
        {
            _inputCaptured = false;
        }
        _blockInput = _inputCaptured;
        _blockScroll = _mouseOverWindow;

        TryDebugLoadScene();
        for (int i = Controllers.Count - 1; i >= 0; i--)
        {
            ThighController controller = Controllers[i];
            if (controller != null)
            {
                controller.UpdateTick();
            }
        }
    }

    private void LateUpdate()
    {
        // Animation has already written its pose. Run the deterministic driver before
        // the later-created jiggle components so every solver sees the same anchor move.
        UpdateRegressionMotion();
    }

    private void UpdateRegressionMotion()
    {
        if (!DebugRegressionMotion.Value)
        {
            if (_regressionWasActive)
            {
                RestoreRegressionAnchors();
                RestoreRegressionFrameRate();
                Logger.LogInfo("FPC regression motion stopped; anchor rotations restored.");
            }
            _regressionWasActive = false;
            return;
        }
        if (!_regressionWasActive)
        {
            _regressionTime = 0f;
            _regressionStage = -1;
            _regressionAnchors.Clear();
            _regressionConfiguredStages.Clear();
            ApplyRegressionFrameRate();
            Logger.LogInfo("FPC regression motion started (deterministic 0.5 Hz anchors).");
        }
        _regressionWasActive = true;
        if (Controllers.Count == 0)
        {
            return;
        }
        _regressionTime += Time.deltaTime;
        int softnessSteps = Mathf.Clamp(DebugRegressionSoftnessSteps.Value, 2, 9);
        float stageSeconds = Mathf.Clamp(DebugRegressionStageSeconds.Value, 7f, 60f);
        int stage = DebugRegressionSweepSoftness.Value
            ? Mathf.Min(softnessSteps - 1, Mathf.FloorToInt(_regressionTime / stageSeconds))
            : 0;
        if (stage != _regressionStage)
        {
            _regressionStage = stage;
            _regressionConfiguredStages.Clear();
            float stageSoftness = DebugRegressionSweepSoftness.Value
                ? stage / (float)(softnessSteps - 1)
                : DebugRegressionSoftness.Value;
            Logger.LogInfo("FPC_REGRESSION_STAGE index=" + stage +
                " softness=" + stageSoftness.ToString("F3") +
                " preset=" + DebugRegressionFeelPreset.Value +
                " solver=" + DebugRegressionSolverMode.Value);
        }
        float phase = _regressionTime * Mathf.PI;
        for (int i = 0; i < Controllers.Count; i++)
        {
            ThighController controller = Controllers[i];
            if (controller == null)
            {
                continue;
            }
            int controllerId = controller.GetInstanceID();
            int configuredStage;
            if (_regressionTime >= 1f &&
                (!_regressionConfiguredStages.TryGetValue(controllerId, out configuredStage) ||
                 configuredStage != stage))
            {
                ApplyRegressionParameters(controller, stage);
                _regressionConfiguredStages[controllerId] = stage;
            }
            DriveRegressionAnchor(controller, "cf_j_thigh00_L",
                new Vector3(Mathf.Sin(phase) * 12f, 0f, 0f), Vector3.zero);
            DriveRegressionAnchor(controller, "cf_j_thigh00_R",
                new Vector3(-Mathf.Sin(phase) * 12f, 0f, 0f), Vector3.zero);
            DriveRegressionAnchor(controller, "cf_j_arm00_L",
                new Vector3(0f, 0f, Mathf.Sin(phase + 1.2f) * 10f), Vector3.zero);
            DriveRegressionAnchor(controller, "cf_j_arm00_R",
                new Vector3(0f, 0f, -Mathf.Sin(phase + 1.2f) * 10f), Vector3.zero);
            bool useSpringDriver = string.Equals(DebugRegressionSolverMode.Value, "Spring",
                                       StringComparison.OrdinalIgnoreCase) ||
                                   (!string.Equals(DebugRegressionSolverMode.Value, "Chain",
                                        StringComparison.OrdinalIgnoreCase) &&
                                    !controller.GetParams(FleshPartId.Belly).GamePhysics);
            string bellyDriver = useSpringDriver
                ? "cf_j_waist01"
                : "cf_j_spine03";
            DriveRegressionAnchor(controller, bellyDriver,
                new Vector3(Mathf.Sin(phase + 2.4f) * 5f, 0f, 0f),
                new Vector3(Mathf.Sin(phase + 2.4f) * 0.008f, 0f, 0f));
        }
    }

    private void ApplyRegressionParameters(ThighController controller, int stage)
    {
        string solver = DebugRegressionSolverMode.Value == null
            ? "Default"
            : DebugRegressionSolverMode.Value.Trim();
        float softness = DebugRegressionSweepSoftness.Value
            ? stage / (float)(Mathf.Clamp(DebugRegressionSoftnessSteps.Value, 2, 9) - 1)
            : DebugRegressionSoftness.Value;
        for (int i = 0; i < 3; i++)
        {
            FleshPartId partId = (FleshPartId)i;
            ThighParams part;
            FleshFeelPreset feelPreset;
            if (TryParseFeelPreset(DebugRegressionFeelPreset.Value, out feelPreset))
            {
                part = FleshTuning.CreateFeelPreset(partId, feelPreset);
                controller.SetParams(partId, part);
            }
            else
            {
                part = controller.GetParams(partId);
            }
            if (solver.Equals("Chain", StringComparison.OrdinalIgnoreCase))
            {
                part.GamePhysics = true;
            }
            else if (solver.Equals("Spring", StringComparison.OrdinalIgnoreCase))
            {
                part.GamePhysics = false;
            }
            if (DebugRegressionStrength.Value >= 0f)
            {
                FleshTuning.SetStrength(part, DebugRegressionStrength.Value);
            }
            if (softness >= 0f)
            {
                FleshTuning.SetSoftness(part, partId, softness);
            }
            if (DebugRegressionResponse.Value >= 0f)
            {
                part.MotionGain = Mathf.Clamp(DebugRegressionResponse.Value, 0f, 5f);
            }
        }
        controller.ClearDeformation();
        controller.Apply(resetPosition: true);
    }

    private static bool TryParseFeelPreset(string value, out FleshFeelPreset preset)
    {
        if (string.Equals(value, "Stable", StringComparison.OrdinalIgnoreCase))
        {
            preset = FleshFeelPreset.Stable;
            return true;
        }
        if (string.Equals(value, "Natural", StringComparison.OrdinalIgnoreCase))
        {
            preset = FleshFeelPreset.Natural;
            return true;
        }
        if (string.Equals(value, "Dance", StringComparison.OrdinalIgnoreCase))
        {
            preset = FleshFeelPreset.Dance;
            return true;
        }
        preset = FleshFeelPreset.Natural;
        return false;
    }

    private void DriveRegressionAnchor(ThighController controller, string boneName, Vector3 euler,
        Vector3 positionOffset)
    {
        Transform bone = controller.FindBonePublic(boneName);
        if (bone == null)
        {
            return;
        }
        int id = bone.GetInstanceID();
        RegressionAnchor anchor;
        if (!_regressionAnchors.TryGetValue(id, out anchor) || anchor.Bone != bone)
        {
            anchor = new RegressionAnchor
            {
                Bone = bone,
                BaseRotation = bone.localRotation,
                BasePosition = bone.localPosition,
            };
            _regressionAnchors[id] = anchor;
        }
        bone.localRotation = anchor.BaseRotation * Quaternion.Euler(euler);
        bone.localPosition = anchor.BasePosition + positionOffset;
    }

    private void RestoreRegressionAnchors()
    {
        foreach (RegressionAnchor anchor in _regressionAnchors.Values)
        {
            if (anchor.Bone != null)
            {
                anchor.Bone.localRotation = anchor.BaseRotation;
                anchor.Bone.localPosition = anchor.BasePosition;
            }
        }
        _regressionAnchors.Clear();
    }

    private void ApplyRegressionFrameRate()
    {
        int target = DebugRegressionTargetFps.Value;
        if (target <= 0 || _regressionFrameRateOverridden)
        {
            return;
        }
        _regressionPreviousTargetFps = Application.targetFrameRate;
        _regressionPreviousVSync = QualitySettings.vSyncCount;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Mathf.Clamp(target, 15, 240);
        _regressionFrameRateOverridden = true;
        Logger.LogInfo("FPC regression target FPS=" + Application.targetFrameRate);
    }

    private void RestoreRegressionFrameRate()
    {
        if (!_regressionFrameRateOverridden)
        {
            return;
        }
        Application.targetFrameRate = _regressionPreviousTargetFps;
        QualitySettings.vSyncCount = _regressionPreviousVSync;
        _regressionFrameRateOverridden = false;
    }

    private void OnDestroy()
    {
        RestoreRegressionAnchors();
        RestoreRegressionFrameRate();
    }

    private void TryDebugLoadScene()
    {
        if (_debugLoadAttempted)
        {
            return;
        }
        string scenePath = DebugAutoLoadScene.Value;
        if (string.IsNullOrEmpty(scenePath) || !File.Exists(scenePath))
        {
            _debugLoadAttempted = true;
            return;
        }
        _debugLoadTimer += Time.deltaTime;
        if (_debugLoadTimer < 6f)
        {
            return;
        }
        if (_debugLoadTimer > 30f)
        {
            Logger.LogWarning("Debug auto-load scene: studio never became ready: " + scenePath);
            _debugLoadAttempted = true;
            return;
        }
        try
        {
            Type studioType = null;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    studioType = assemblies[i].GetType("Studio.Studio", false);
                }
                catch (Exception)
                {
                    studioType = null;
                }
                if (studioType != null)
                {
                    break;
                }
            }
            if (studioType == null)
            {
                return;
            }
            // Studio.Studio inherits Instance from Singleton<Studio>; plain GetProperty
            // does not return inherited static members, so include FlattenHierarchy.
            object studio = studioType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null, null);
            if (studio == null)
            {
                return;
            }
            MethodInfo addFemale = studioType.GetMethod("AddFemale");
            if (addFemale != null)
            {
                addFemale.Invoke(studio, new object[] { scenePath });
                Logger.LogInfo("Debug auto-load chara invoked (" + scenePath + ")");
                _debugLoadAttempted = true;
                return;
            }
            bool loaded = (bool)studioType.GetMethod("LoadScene").Invoke(studio, new object[] { scenePath });
            Logger.LogInfo("Debug auto-load scene result=" + loaded + " (" + scenePath + ")");
            if (loaded)
            {
                _debugLoadAttempted = true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Debug auto-load scene failed: " + ex.Message);
            _debugLoadAttempted = true;
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
            "Flesh Physics Controller", _windowStyle);
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
        string[] partNames = { "Thigh", "Arm", "Belly" };
        // Explicit toggle buttons instead of SelectionGrid: the grid click could be
        // eaten by the input-blocking patches, leaving the panel stuck on the wrong
        // part (e.g. Belly requested but Arm per-bone labels shown).
        GUILayout.BeginHorizontal();
        for (int p = 0; p < partNames.Length; p++)
        {
            bool isPart = _selectedPart == p;
            if (GUILayout.Toggle(isPart, partNames[p], GUILayout.Width(80f)) && !isPart)
            {
                _selectedPart = p;
            }
        }
        GUILayout.EndHorizontal();
        FleshPartId partId = (FleshPartId)_selectedPart;
        ThighParams part = controller.GetParams(partId);
        string partLabel = partNames[_selectedPart];

        part.Enabled = GUILayout.Toggle(part.Enabled, " " + partLabel + " physics enabled");

        GUILayout.BeginHorizontal();
        GUILayout.Label(_advancedMode ? "高级调节：完整求解器参数" : "简单调节：只保留三个手感参数");
        _advancedMode = GUILayout.Toggle(_advancedMode, "Advanced 高级", GUILayout.Width(120f));
        GUILayout.EndHorizontal();

        bool gamePhysics = part.GamePhysics;
        if (_advancedMode)
        {
            part.GamePhysics = GUILayout.Toggle(gamePhysics,
                " 舞蹈链式模式（关闭 = 日常弹簧模式）");
        }
        else
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("物理模式", GUILayout.Width(70f));
            if (GUILayout.Toggle(!gamePhysics, "日常弹簧 Spring", GUILayout.Width(150f)) && gamePhysics)
            {
                part.GamePhysics = false;
            }
            if (GUILayout.Toggle(gamePhysics, "舞蹈链式 Chain", GUILayout.Width(150f)) && !gamePhysics)
            {
                part.GamePhysics = true;
            }
            GUILayout.EndHorizontal();
        }
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
            GUILayout.Label("强度 Strength");
            float strength = FleshTuning.GetStrength(part);
            float newStrength = NumericSlider(ctrlId + "_simple_strength", strength, 0f, 1f, "");
            if (Mathf.Abs(newStrength - strength) > 0.00001f)
            {
                FleshTuning.SetStrength(part, newStrength);
            }

            GUILayout.Label("柔软度 Softness");
            float softness = FleshTuning.GetSoftness(part, partId);
            float newSoftness = NumericSlider(ctrlId + "_simple_softness", softness, 0f, 1f, "");
            if (Mathf.Abs(newSoftness - softness) > 0.00001f)
            {
                FleshTuning.SetSoftness(part, partId, newSoftness);
            }

            GUILayout.Label("动作响应 Motion response");
            part.MotionGain = NumericSlider(ctrlId + "_simple_motion", part.MotionGain, 0f, 3f, "");
            GUILayout.Label("柔软度会联动阻尼、惯性、回弹和频率；高级模式仍可逐项覆盖。",
                GUILayout.Width(500f));

            GUILayout.Space(6f);
            GUILayout.Label("整套手感预设（应用到大腿 / 手臂 / 小肚子）");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("稳定日常 Spring"))
            {
                ApplyFeelPreset(controller, FleshFeelPreset.Stable);
                GUILayout.EndHorizontal();
                return;
            }
            if (GUILayout.Button("常用自然 Chain"))
            {
                ApplyFeelPreset(controller, FleshFeelPreset.Natural);
                GUILayout.EndHorizontal();
                return;
            }
            if (GUILayout.Button("舞蹈增强 Chain"))
            {
                ApplyFeelPreset(controller, FleshFeelPreset.Dance);
                GUILayout.EndHorizontal();
                return;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply now"))
            {
                controller.Apply(resetPosition: false);
            }
            if (GUILayout.Button("Clear shape"))
            {
                controller.ClearDeformation();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            GUILayout.Label("参数实时生效，并在保存角色卡时自动写入。需要逐骨/轴向控制时开启高级模式。");
            return;
        }

        GUILayout.Label("Dance response");
        part.MotionGain = NumericSlider(ctrlId + "_mg", part.MotionGain, 0f, 5f, "");
        if (part.GamePhysics)
        {
            GUILayout.Space(6f);
            GUILayout.Label("Chain mode parameters");
            ChainParams chain = part.Chain;
            GUILayout.Label("Weight");
            chain.Weight = NumericSlider(ctrlId + "_cw", chain.Weight, 0f, 1f, "");
            GUILayout.Label("Gravity");
            chain.Gravity = NumericSlider(ctrlId + "_cg", chain.Gravity, -0.2f, 0.2f, "");
            chain.Damping = NumericSlider(ctrlId + "_cd", chain.Damping, 0f, 1f, "Damping");
            chain.Elasticity = NumericSlider(ctrlId + "_ce", chain.Elasticity, 0f, 1f, "Elasticity");
            chain.Stiffness = NumericSlider(ctrlId + "_cs", chain.Stiffness, 0f, 1f, "Stiffness");
            chain.Inert = NumericSlider(ctrlId + "_ci", chain.Inert, 0f, 1f, "Inert");
            chain.JitterFreq = NumericSlider(ctrlId + "_cjf", chain.JitterFreq, 0f, 2.5f,
                "Jitter freq");
        }
        else
        {
            GUILayout.Label("Weight");
            part.Weight = NumericSlider(ctrlId + "_w", part.Weight, 0f, 1f, "");
            GUILayout.Label("Gravity");
            part.Gravity = NumericSlider(ctrlId + "_g", part.Gravity, -0.2f, 0.2f, "");
            part.JitterFreq = NumericSlider(ctrlId + "_jf", part.JitterFreq, 0f, 2.5f,
                "Jitter freq");
            part.MotionSmooth = NumericSlider(ctrlId + "_ms", part.MotionSmooth, 0.05f, 0.5f,
                "Motion smooth");
            DrawBoneSection(ctrlId, partLabel + " flesh (shared)", part.Thigh00);
        }

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Apply now"))
        {
            controller.Apply(resetPosition: false);
        }
        if (GUILayout.Button("Reset to defaults"))
        {
            controller.SetParams(partId, ThighParams.CreatePartDefaults(partId));
            controller.ClearDeformation();
            controller.Apply(resetPosition: true);
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(4f);
        if (GUILayout.Button("Clear shape (restore card defaults)"))
        {
            controller.ClearDeformation();
        }

        GUILayout.Space(6f);
        GUILayout.Label("Presets");
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
            GUILayout.Label("No presets yet. Type a name and press Save preset.");
        }
        GUILayout.BeginHorizontal();
        _presetName = GUILayout.TextField(_presetName, 120);
        if (GUILayout.Button("Save preset"))
        {
            string targetPath;
            if (WindowsFileDialog.ShowSave(PresetDirectory.Value, EnsureXml(_presetName), out targetPath))
            {
                controller.SavePreset(targetPath);
            }
        }
        if (GUILayout.Button("Load preset") && presetFiles.Length > 0)
        {
            controller.LoadPreset(Path.Combine(PresetDirectory.Value, presetFiles[_presetIndex]));
            controller.Apply(resetPosition: true);
        }
        if (GUILayout.Button("Load from file..."))
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
        GUILayout.Space(4f);
        GUILayout.Label("Settings are saved to the card automatically on save.");
    }

    private static void ApplyFeelPreset(ThighController controller, FleshFeelPreset preset)
    {
        for (int i = 0; i < 3; i++)
        {
            FleshPartId part = (FleshPartId)i;
            controller.SetParams(part, FleshTuning.CreateFeelPreset(part, preset));
        }
        controller.ClearDeformation();
        controller.Apply(resetPosition: true);
    }

    private void DrawBoneSection(string ctrlId, string label, ThighBoneParams bone)
    {
        GUILayout.Space(4f);
        GUILayout.Label("Bone " + label);
        bone.Damping = NumericSlider(ctrlId + "_d", bone.Damping, 0f, 1f, "Damping");
        bone.Elasticity = NumericSlider(ctrlId + "_e", bone.Elasticity, 0f, 1f, "Elasticity");
        bone.Stiffness = NumericSlider(ctrlId + "_s", bone.Stiffness, 0f, 1f, "Stiffness");
        bone.Inert = NumericSlider(ctrlId + "_i", bone.Inert, 0f, 1f, "Inert");
        GUILayout.Label("Same particle model as the game breast/butt bones. React to movement only.",
            GUILayout.Width(400f));
    }

    private void DrawBoneAmounts(string ctrlId, ThighBoneAmounts bones, bool chainMode, string[] boneLabels)
    {
        GUILayout.Space(4f);
        GUILayout.Label("Per-bone (" + (chainMode ? "Chain" : "Spring") + "): Amp / Rot / RC / Axis (0 = freeze)");
        string modePrefix = chainMode ? "_c" : "_s";
        for (int r = 0; r < boneLabels.Length; r++)
        {
            int i = r;
            PerBoneAmount amount = bones.Get(i);
            GUILayout.BeginHorizontal();
            amount.Enabled = GUILayout.Toggle(amount.Enabled, boneLabels[r], GUILayout.Width(120f));
            GUILayout.Label("Amp", GUILayout.Width(30f));
            amount.Amp = NumericSlider(ctrlId + modePrefix + "_b" + i + "_a", amount.Amp, 0f, 2f, "");
            GUILayout.Label("Rot", GUILayout.Width(28f));
            amount.RotAmp = NumericField(ctrlId + modePrefix + "_b" + i + "_r", amount.RotAmp, 0f, 1f, 52f);
            amount.RotCalc = GUILayout.Toggle(amount.RotCalc, "RC", GUILayout.Width(38f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Space(124f);
            GUILayout.Label("X", GUILayout.Width(16f));
            amount.AxisX = NumericField(ctrlId + modePrefix + "_b" + i + "_x", amount.AxisX, 0f, 1f, 52f);
            GUILayout.Label("Y", GUILayout.Width(16f));
            amount.AxisY = NumericField(ctrlId + modePrefix + "_b" + i + "_y", amount.AxisY, 0f, 1f, 52f);
            GUILayout.Label("Z", GUILayout.Width(16f));
            amount.AxisZ = NumericField(ctrlId + modePrefix + "_b" + i + "_z", amount.AxisZ, 0f, 1f, 52f);
            GUILayout.EndHorizontal();
        }
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
}
