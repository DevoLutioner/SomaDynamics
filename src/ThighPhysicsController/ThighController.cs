using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using ExtensibleSaveFormat;
using KKAPI;
using KKAPI.Chara;
using UnityEngine;

namespace ThighPhysicsController;

public sealed class ThighController : CharaCustomFunctionController
{
    public ThighParams Params = ThighParams.CreateDefault();

    public ThighParams ArmParams = ThighParams.CreateDefault();

    public ThighParams BellyParams = ThighParams.CreateDefault();

    public NativeBustProfile BreastProfile = new NativeBustProfile();

    public NativeBodyParams ButtParams = NativeBodyParams.CreateDefault(FleshPartId.Butt);

    private readonly List<ThighFleshJiggle> _flesh = new List<ThighFleshJiggle>();

    private bool _fleshReady;

    private int _pendingApplyFrames;

    private int _pendingNativeApplyFrames;

    private bool _skeletonDumped;

    private bool _paramsLoaded;

    private NativeDynamicBoneBridge _nativeBody;

    public ThighParams GetParams(FleshPartId part)
    {
        return part switch
        {
            FleshPartId.Arm => ArmParams,
            FleshPartId.Belly => BellyParams,
            _ => Params,
        };
    }

    public void SetParams(FleshPartId part, ThighParams value)
    {
        switch (part)
        {
            case FleshPartId.Arm:
                ArmParams = value;
                break;
            case FleshPartId.Belly:
                BellyParams = value;
                break;
            default:
                Params = value;
                break;
        }
        RememberPart(part);
    }

    public NativeBodyParams GetNativeParams(FleshPartId part)
    {
        return part == FleshPartId.Butt ? ButtParams :
            BreastProfile.Get(CurrentCoordinateIndex, CurrentBustWearState);
    }

    public void SetNativeParams(FleshPartId part, NativeBodyParams value)
    {
        if (part == FleshPartId.Butt)
            ButtParams = value;
        else
            BreastProfile.Set(CurrentCoordinateIndex, CurrentBustWearState, value);
        RememberPart(part);
        RequestNativeReapply(1);
    }

    public int CurrentCoordinateIndex
    {
        get
        {
            if (ChaControl == null || ((ChaInfo)ChaControl).fileStatus == null)
                return 0;
            int value = (int)((ChaInfo)ChaControl).fileStatus.coordinateType;
            return value < 0 ? 0 : value > 6 ? 6 : value;
        }
    }

    /// <summary>0=naked, 1=bra, 2=tops; matches BPC's wear-state selection.</summary>
    public int CurrentBustWearState
    {
        get
        {
            if (ChaControl == null || ((ChaInfo)ChaControl).fileStatus == null ||
                ((ChaInfo)ChaControl).fileStatus.clothesState == null)
                return 0;
            byte[] state = ((ChaInfo)ChaControl).fileStatus.clothesState;
            if (state.Length > 0 && state[0] == 0)
                return 2;
            if (state.Length > 2 && state[2] == 0)
                return 1;
            return 0;
        }
    }

    public string CurrentBustStateLabel
    {
        get
        {
            string wear = CurrentBustWearState == 2 ? "Tops" :
                CurrentBustWearState == 1 ? "Bra" : "Naked";
            return wear + " / Coordinate " + CurrentCoordinateIndex;
        }
    }

    internal void CopyCurrentBreastToAllStates()
    {
        BreastProfile.SetAll(GetNativeParams(FleshPartId.Breast));
        RememberPart(FleshPartId.Breast);
        RequestNativeReapply(1);
    }

    internal void SetWholeBodyTargets(float strength, float softness, float motion,
        bool setStrength, bool setSoftness, bool setMotion)
    {
        for (int i = 0; i < 3; i++)
        {
            FleshPartId part = (FleshPartId)i;
            ThighParams value = GetParams(part);
            if (setStrength)
                FleshTuning.SetStrength(value, strength);
            if (setSoftness)
                FleshTuning.SetSoftness(value, part, softness);
            if (setMotion)
                FleshTuning.SetMotionTarget(value, motion);
            RememberPart(part);
        }
        BreastProfile.SetTargetsAll(strength, softness, motion, setStrength, setSoftness,
            setMotion);
        NativeBodyTuning.SetTargets(ButtParams, FleshPartId.Butt,
            setStrength ? strength : ButtParams.Strength,
            setSoftness ? softness : ButtParams.Softness,
            setMotion ? motion : ButtParams.MotionResponse);
        RememberPart(FleshPartId.Breast);
        RememberPart(FleshPartId.Butt);
        Apply(resetPosition: false);
    }

    /// <summary>
    /// Stable per-character identity used by the session memory and same-name sync:
    /// fullname + sex + personality. Falls back to the scene object name.
    /// </summary>
    public string IdentityKey
    {
        get
        {
            if (ChaControl == null)
            {
                return "";
            }
            ChaFile file = ((ChaInfo)ChaControl).chaFile;
            if (file != null && file.parameter != null)
            {
                return file.parameter.fullname + "|" + file.parameter.sex + "|" +
                       file.parameter.personality;
            }
            return ChaControl.name;
        }
    }

    public bool IsMale
    {
        get
        {
            if (ChaControl == null)
            {
                return false;
            }
            ChaFile file = ((ChaInfo)ChaControl).chaFile;
            return file != null && file.parameter != null && file.parameter.sex == 0;
        }
    }

    public string DisplayName
    {
        get
        {
            if (ChaControl == null)
            {
                return "?";
            }
            ChaFile file = ((ChaInfo)ChaControl).chaFile;
            if (file != null && file.parameter != null &&
                !string.IsNullOrEmpty(file.parameter.fullname))
            {
                return file.parameter.fullname;
            }
            return ChaControl.name;
        }
    }

    protected override void OnReload(GameMode currentGameMode, bool maintainState)
    {
        if (ThighPhysicsControllerPlugin.AutoApply.Value)
        {
            // Character, coordinate and body reloads can replace the skeleton while
            // this controller survives. Restore the old deformation before releasing
            // its references, then rebuild against the new pristine transforms.
            RemoveFlesh();
            if (_nativeBody != null)
            {
                _nativeBody.RestoreAll();
                _nativeBody = null;
            }
            // maintainState=true means the card data did not change (scene refresh,
            // coordinate/outfit change); keep the current settings in that case.
            if (!maintainState || !_paramsLoaded)
            {
                LoadParamsFromCardOrDefault();
            }
            _skeletonDumped = false;
            Apply(resetPosition: true);
            PrepareForStudioPoseChange(2);
            RequestNativeReapply(30);
        }
    }

    protected override void OnCardBeingSaved(GameMode currentGameMode)
    {
        PluginData data = GetExtendedData();
        if (data == null)
        {
            data = new PluginData();
        }
        if (data.data == null)
        {
            data.data = new Dictionary<string, object>();
        }
        Params.WriteData(data);
        ThighParams.WritePart(data.data, "arm_", ArmParams);
        ThighParams.WritePart(data.data, "belly_", BellyParams);
        NativeBustProfile.Write(data.data, "breast_", BreastProfile);
        NativeBodyParams.WritePart(data.data, "butt_", ButtParams);
        SetExtendedData(data);
    }

    protected override void OnDestroy()
    {
        RemoveFlesh();
        if (_nativeBody != null)
            _nativeBody.RestoreAll();
        ThighPhysicsControllerPlugin.Controllers.Remove(this);
        base.OnDestroy();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (!ThighPhysicsControllerPlugin.Controllers.Contains(this))
        {
            ThighPhysicsControllerPlugin.Controllers.Add(this);
        }
    }

    private void LoadParamsFromCardOrDefault()
    {
        string key = IdentityKey;
        bool useMemory = ThighPhysicsControllerPlugin.RememberPerCharacter.Value && key.Length > 0;
        FleshProfile profile = null;
        if (useMemory && ThighPhysicsControllerPlugin.MemoryProfiles.TryGetValue(key, out profile))
        {
            Params = profile.Thigh;
            ArmParams = profile.Arm;
            BellyParams = profile.Belly;
            BreastProfile = profile.Breast;
            ButtParams = profile.Butt;
            ApplyDefaultPartEnables(hadCardData: true);
        }
        else
        {
            PluginData extended = GetExtendedData();
            bool hadCardData = extended != null && extended.data != null && extended.data.Count > 0;
            Params = ThighParams.CreateDefault();
            if (hadCardData)
            {
                Params.ReadData(extended);
            }
            ArmParams = ThighParams.CreatePartDefaults(FleshPartId.Arm);
            BellyParams = ThighParams.CreatePartDefaults(FleshPartId.Belly);
            BreastProfile = new NativeBustProfile();
            ButtParams = NativeBodyParams.CreateDefault(FleshPartId.Butt);
            if (extended != null && extended.data != null)
            {
                int version = 0;
                if (extended.data.ContainsKey("v"))
                {
                    version = Convert.ToInt32(extended.data["v"]);
                }
                ThighParams.ReadPart(extended.data, "arm_", ArmParams, version);
                ThighParams.ReadPart(extended.data, "belly_", BellyParams, version);
                NativeBustProfile.Read(extended.data, "breast_", BreastProfile, version);
                NativeBodyParams.ReadPart(extended.data, "butt_", ButtParams, version);
            }
            // 默认预设只对没有卡数据的角色自动应用,已有卡数据的角色保持卡片值。
            if (!hadCardData)
            {
                ApplyDefaultPresetIfConfigured();
            }
            ApplyDefaultPartEnables(hadCardData);
            if (useMemory)
            {
                ThighPhysicsControllerPlugin.MemoryProfiles[key] = new FleshProfile
                {
                    Thigh = Params,
                    Arm = ArmParams,
                    Belly = BellyParams,
                    Breast = BreastProfile,
                    Butt = ButtParams,
                };
            }
        }
        if (ThighPhysicsControllerPlugin.ForceEnable.Value)
        {
            Params.Enabled = true;
            ArmParams.Enabled = true;
            BellyParams.Enabled = true;
            BreastProfile.SetEnabledAll(true);
            ButtParams.Enabled = true;
        }
        _paramsLoaded = true;
    }

    private void ApplyDefaultPresetIfConfigured()
    {
        string presetName = ThighPhysicsControllerPlugin.DefaultPreset.Value;
        if (string.IsNullOrEmpty(presetName))
        {
            return;
        }
        presetName = presetName.Trim();
        if (presetName.Length == 0)
        {
            return;
        }
        // The default preset is a bare file name; reject anything path-shaped.
        if (presetName.IndexOfAny(new[] { '\\', '/', ':' }) >= 0)
        {
            Debug.LogWarning("FPC_PRESET_APPLY default preset name ignored " +
                             "(contains path characters): " + presetName);
            return;
        }
        string path = Path.Combine(ThighPhysicsControllerPlugin.PresetDirectory.Value, presetName);
        if (!File.Exists(path))
        {
            Debug.LogWarning("FPC_PRESET_APPLY default preset not found: " + path);
            return;
        }
        try
        {
            LoadPreset(path);
            Debug.Log("FPC_PRESET_APPLY default preset applied: " + presetName);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("FPC_PRESET_APPLY default preset failed: " + ex.Message);
        }
    }

    private void ApplyDefaultPartEnables(bool hadCardData)
    {
        if (hadCardData && !ThighPhysicsControllerPlugin.ApplyDefaultsToAllCharacters.Value)
        {
            return;
        }
        Params.Enabled = ThighPhysicsControllerPlugin.DefaultThighEnabled.Value;
        ArmParams.Enabled = ThighPhysicsControllerPlugin.DefaultArmEnabled.Value;
        BellyParams.Enabled = ThighPhysicsControllerPlugin.DefaultBellyEnabled.Value;
        BreastProfile.SetEnabledAll(ThighPhysicsControllerPlugin.DefaultBreastEnabled.Value);
        ButtParams.Enabled = ThighPhysicsControllerPlugin.DefaultButtEnabled.Value;
    }

    private void RememberPart(FleshPartId part)
    {
        if (!ThighPhysicsControllerPlugin.RememberPerCharacter.Value)
        {
            return;
        }
        string key = IdentityKey;
        if (key.Length == 0)
        {
            return;
        }
        FleshProfile profile;
        if (!ThighPhysicsControllerPlugin.MemoryProfiles.TryGetValue(key, out profile))
        {
            profile = new FleshProfile
            {
                Thigh = Params,
                Arm = ArmParams,
                Belly = BellyParams,
                Breast = BreastProfile,
                Butt = ButtParams,
            };
            ThighPhysicsControllerPlugin.MemoryProfiles[key] = profile;
        }
        switch (part)
        {
            case FleshPartId.Arm:
                profile.Arm = ArmParams;
                break;
            case FleshPartId.Belly:
                profile.Belly = BellyParams;
                break;
            case FleshPartId.Breast:
                profile.Breast = BreastProfile;
                break;
            case FleshPartId.Butt:
                profile.Butt = ButtParams;
                break;
            default:
                profile.Thigh = Params;
                break;
        }
    }

    private void DumpSkeleton()
    {
        if (ChaControl == null)
        {
            return;
        }
        Transform[] transforms = ChaControl.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            string name = transforms[i].name;
            if (name.IndexOf("cf_d_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("thigh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("siri", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("asi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("momo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("leg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("arm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("bust", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("spine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("waist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("kosi", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Transform parent = transforms[i].parent;
                UnityEngine.Debug.Log(string.Concat(
                    "Skeleton [", DisplayName, "]: ", name,
                    " parent=", parent == null ? "-" : parent.name,
                    " localPos=", transforms[i].localPosition.ToString("F4")));
            }
        }
    }

    internal void Apply(bool resetPosition)
    {
        if (ChaControl == null)
        {
            return;
        }
        if (ThighPhysicsControllerPlugin.DebugDumpSkeleton.Value && !_skeletonDumped)
        {
            _skeletonDumped = true;
            DumpSkeleton();
        }
        if (!Params.Enabled && !ArmParams.Enabled && !BellyParams.Enabled)
        {
            RemoveFlesh();
        }
        else
        {
            EnsureFlesh();
            ApplyFlesh(resetPosition);
        }
        if (_nativeBody == null)
            _nativeBody = new NativeDynamicBoneBridge(ChaControl);
        _nativeBody.Apply(FleshPartId.Breast, GetNativeParams(FleshPartId.Breast));
        _nativeBody.Apply(FleshPartId.Butt, ButtParams);
    }

    internal void UpdateTick()
    {
        if (_pendingNativeApplyFrames > 0)
        {
            _pendingNativeApplyFrames--;
            if (_pendingNativeApplyFrames == 0 && isActiveAndEnabled)
                ApplyNativeBody();
        }
        if (_pendingApplyFrames > 0)
        {
            _pendingApplyFrames--;
            if (_pendingApplyFrames == 0 && isActiveAndEnabled)
            {
                Apply(resetPosition: false);
            }
        }
    }

    internal void PrepareForStudioPoseChange(int settleFrames)
    {
        if (!isActiveAndEnabled)
            return;
        // Chain mode yields until the incoming Animator/Timeline pose is visible,
        // then captures that pose as its new rest frame. Spring keeps its established
        // reset path because it does not exhibit the Timeline twist.
        for (int i = 0; i < _flesh.Count; i++)
        {
            ThighFleshJiggle jiggle = _flesh[i];
            if (jiggle != null)
                jiggle.PrepareForExternalPoseChange(settleFrames);
        }
        RequestNativeReapply(settleFrames + 1);
    }

    private void RestorePoseAndResetState()
    {
        for (int i = 0; i < _flesh.Count; i++)
        {
            ThighFleshJiggle jiggle = _flesh[i];
            if (jiggle != null)
                jiggle.RestorePoseAndResetState();
        }
    }

    internal void RequestNativeReapply(int delayFrames)
    {
        _pendingNativeApplyFrames = Math.Max(_pendingNativeApplyFrames,
            Math.Max(1, delayFrames));
    }

    internal void OnClothesStateChanged()
    {
        RequestNativeReapply(2);
    }

    internal bool OwnsBustSoft(BustSoft value)
    {
        return ChaControl != null && ChaControl.bustSoft == value &&
               GetNativeParams(FleshPartId.Breast).Enabled;
    }

    internal bool OwnsBustGravity(BustGravity value)
    {
        return ChaControl != null && ChaControl.bustGravity == value &&
               GetNativeParams(FleshPartId.Breast).Enabled;
    }

    internal bool ChaControlMatches(ChaControl value)
    {
        return ChaControl == value;
    }

    private void ApplyNativeBody()
    {
        Apply(resetPosition: false);
    }

    internal void ClearDeformation()
    {
        RestorePoseAndResetState();
        _pendingApplyFrames = 2;
    }

    private void EnsureFlesh()
    {
        if (!_fleshReady || _flesh.Count < 3)
        {
            RemoveFlesh();
            GameObject holder = new GameObject("ThighFlesh");
            holder.transform.SetParent(ChaControl.transform, false);
            AddFlesh(holder, FleshPartId.Thigh);
            AddFlesh(holder, FleshPartId.Arm);
            AddFlesh(holder, FleshPartId.Belly);
            _fleshReady = true;
        }
    }

    private void AddFlesh(GameObject holder, FleshPartId part)
    {
        ThighFleshJiggle jiggle = holder.AddComponent<ThighFleshJiggle>();
        jiggle.Initialize(ChaControl, GetParams(part), part);
        _flesh.Add(jiggle);
    }

    private void ApplyFlesh(bool resetPosition)
    {
        for (int i = 0; i < _flesh.Count; i++)
        {
            ThighFleshJiggle jiggle = _flesh[i];
            if (jiggle == null)
            {
                continue;
            }
            ThighParams activeParams = GetParams(jiggle.PartId);
            if (!activeParams.Enabled)
            {
                if (jiggle.enabled)
                    jiggle.ClearDeformation();
                jiggle.enabled = false;
                continue;
            }
            bool wasEnabled = jiggle.enabled;
            jiggle.ParamsRef = activeParams;
            jiggle.enabled = true;
            if (resetPosition || !wasEnabled)
            {
                jiggle.ResetState();
            }
        }
    }

    private void RemoveFlesh()
    {
        foreach (ThighFleshJiggle jiggle in _flesh)
        {
            if (jiggle != null && jiggle.gameObject != null)
            {
                // Restore the pose before destroying the physics, otherwise the last
                // deformation stays baked on the bones (and a later re-enable records
                // it as the "pristine" pose, making Clear shape unable to fix it).
                jiggle.ClearDeformation();
                Destroy(jiggle.gameObject);
            }
        }
        _flesh.Clear();
        _fleshReady = false;
    }

    internal void SavePreset(string path)
    {
        try
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            using (XmlWriter writer = XmlWriter.Create(path, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("XMLParamThigh");
                writer.WriteAttributeString("Version", "4");
                WriteParamBody(writer, "cf_j_thigh00_L", Params);
                writer.WriteStartElement("ArmPart");
                WriteParamBody(writer, "cf_j_arm00_L", ArmParams);
                writer.WriteEndElement();
                writer.WriteStartElement("BellyPart");
                WriteParamBody(writer, "cf_j_spine03", BellyParams);
                writer.WriteEndElement();
                WriteNativeBody(writer, "BreastPart", GetNativeParams(FleshPartId.Breast));
                WriteNativeBody(writer, "ButtPart", ButtParams);
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
            UnityEngine.Debug.Log("Thigh preset saved: " + path);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("Failed to save thigh preset: " + ex.Message);
        }
    }

    private static void WriteParamBody(XmlWriter writer, string partBoneName, ThighParams p)
    {
        writer.WriteElementString("Gravity", p.Gravity.ToString("0.0000"));
        writer.WriteElementString("Weight", p.Weight.ToString("0.0000"));
        writer.WriteElementString("GamePhysics", p.GamePhysics ? "true" : "false");
        writer.WriteElementString("MotionGain", p.MotionGain.ToString("0.0000"));
        writer.WriteElementString("JitterFreq", p.JitterFreq.ToString("0.0000"));
        writer.WriteElementString("MotionSmooth", p.MotionSmooth.ToString("0.0000"));
        writer.WriteStartElement("ChainParameters");
        WriteChain(writer, p.Chain);
        writer.WriteEndElement();
        writer.WriteStartElement("ParameterSets");
        WriteBoneSet(writer, partBoneName, p.Thigh00);
        writer.WriteEndElement();
        WriteBoneAmps(writer, "BoneAmps", p.Bones);
        WriteBoneAmps(writer, "ChainBoneAmps", p.ChainBones);
    }

    private static void WriteNativeBody(XmlWriter writer, string elementName, NativeBodyParams p)
    {
        writer.WriteStartElement(elementName);
        writer.WriteElementString("Enabled", p.Enabled ? "true" : "false");
        writer.WriteElementString("Strength", p.Strength.ToString("0.0000"));
        writer.WriteElementString("Softness", p.Softness.ToString("0.0000"));
        writer.WriteElementString("MotionResponse", p.MotionResponse.ToString("0.0000"));
        writer.WriteElementString("Gravity", p.Gravity.ToString("0.000000"));
        writer.WriteEndElement();
    }

    private static void WriteBoneAmps(XmlWriter writer, string elementName, ThighBoneAmounts bones)
    {
        writer.WriteStartElement(elementName);
        for (int i = 0; i < 4; i++)
        {
            PerBoneAmount amount = bones.Get(i);
            writer.WriteStartElement("Bone");
            writer.WriteAttributeString("Type", BoneTypeName(i));
            writer.WriteAttributeString("Enabled", amount.Enabled ? "true" : "false");
            writer.WriteAttributeString("Amp", amount.Amp.ToString("0.0000"));
            writer.WriteAttributeString("AxisX", amount.AxisX.ToString("0.0000"));
            writer.WriteAttributeString("AxisY", amount.AxisY.ToString("0.0000"));
            writer.WriteAttributeString("AxisZ", amount.AxisZ.ToString("0.0000"));
            if (elementName == "BoneAmps")
            {
                writer.WriteAttributeString("Rot", amount.RotAmp.ToString("0.0000"));
            }
            writer.WriteAttributeString("RotCalc", amount.RotCalc ? "true" : "false");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    internal void LoadPreset(string path)
    {
        try
        {
            XmlDocument document = new XmlDocument();
            document.Load(path);
            XmlElement root = document.DocumentElement;
            if (root == null || root.Name != "XMLParamThigh")
            {
                // InvalidDataException does not exist in the game's System.dll; use
                // an mscorlib exception so the panel never hits a TypeLoadException.
                throw new InvalidOperationException("Not a flesh physics preset: " + path);
            }
            int presetVersion = 1;
            int parsedPresetVersion;
            if (int.TryParse(root.GetAttribute("Version"), out parsedPresetVersion))
                presetVersion = Math.Max(1, parsedPresetVersion);
            ReadParamBody(root, Params);
            XmlNode armNode = root.SelectSingleNode("ArmPart");
            if (armNode != null)
            {
                ReadParamBody(armNode, ArmParams);
            }
            XmlNode bellyNode = root.SelectSingleNode("BellyPart");
            if (bellyNode != null)
            {
                ReadParamBody(bellyNode, BellyParams);
            }
            XmlNode breastNode = root.SelectSingleNode("BreastPart");
            if (breastNode != null)
                ReadNativeBody(breastNode, GetNativeParams(FleshPartId.Breast), presetVersion);
            XmlNode buttNode = root.SelectSingleNode("ButtPart");
            if (buttNode != null)
                ReadNativeBody(buttNode, ButtParams, presetVersion);
            UnityEngine.Debug.Log("Thigh preset loaded: " + path);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("Failed to load thigh preset: " + ex.Message);
        }
    }

    private static void ReadParamBody(XmlNode node, ThighParams p)
    {
        string gravity = GetChildText(node, "Gravity");
        if (gravity.Length > 0)
        {
            p.Gravity = ReadFiniteText(gravity, -FleshParameterRanges.GravityMax,
                FleshParameterRanges.GravityMax, p.Gravity);
        }
        string weight = GetChildText(node, "Weight");
        if (weight.Length > 0)
        {
            p.Weight = ReadFiniteText(weight, 0f, FleshParameterRanges.WeightMax,
                p.Weight);
        }
        string motionGain = GetChildText(node, "MotionGain");
        if (motionGain.Length > 0)
        {
            p.MotionGain = ReadFiniteText(motionGain, 0f,
                FleshParameterRanges.MotionGainMax, p.MotionGain);
        }
        p.JitterFreq = FleshValue.Clamp(GetFloat(node, "JitterFreq", p.JitterFreq),
            0f, FleshParameterRanges.JitterFrequencyMax, p.JitterFreq);
        p.MotionSmooth = FleshValue.Clamp(GetFloat(node, "MotionSmooth", p.MotionSmooth),
            0.05f, FleshParameterRanges.MotionSmoothMax, p.MotionSmooth);
        string gamePhysics = GetChildText(node, "GamePhysics");
        if (gamePhysics.Length > 0)
        {
            p.GamePhysics = gamePhysics == "true";
        }
        XmlNode chainParameters = node.SelectSingleNode("ChainParameters");
        if (chainParameters != null)
        {
            p.Chain.Weight = ReadFiniteChild(chainParameters, "Weight", 0f,
                FleshParameterRanges.WeightMax, p.Chain.Weight);
            p.Chain.Gravity = ReadFiniteChild(chainParameters, "Gravity",
                -FleshParameterRanges.GravityMax, FleshParameterRanges.GravityMax,
                p.Chain.Gravity);
            p.Chain.Damping = ReadFiniteChild(chainParameters, "Damping", 0f, 1f, p.Chain.Damping);
            p.Chain.Elasticity = ReadFiniteChild(chainParameters, "Elasticity", 0f, 1f, p.Chain.Elasticity);
            p.Chain.Stiffness = ReadFiniteChild(chainParameters, "Stiffness", 0f, 1f, p.Chain.Stiffness);
            p.Chain.Inert = ReadFiniteChild(chainParameters, "Inert", 0f,
                FleshParameterRanges.CustomInertMax, p.Chain.Inert);
            p.Chain.JitterFreq = ReadFiniteChild(chainParameters, "JitterFreq", 0f,
                FleshParameterRanges.JitterFrequencyMax,
                p.Chain.JitterFreq);
        }
        XmlNode parameterSets = node.SelectSingleNode("ParameterSets");
        if (parameterSets != null)
        {
            foreach (XmlNode child in parameterSets.ChildNodes)
            {
                if (child.Name != "ParameterSet")
                {
                    continue;
                }
                p.Thigh00.Damping = ReadFiniteChild(child, "Damping", 0f, 1f, p.Thigh00.Damping);
                p.Thigh00.Elasticity = ReadFiniteChild(child, "Elasticity", 0f, 1f,
                    p.Thigh00.Elasticity);
                p.Thigh00.Stiffness = ReadFiniteChild(child, "Stiffness", 0f, 1f,
                    p.Thigh00.Stiffness);
                p.Thigh00.Inert = ReadFiniteChild(child, "Inert", 0f,
                    FleshParameterRanges.CustomInertMax, p.Thigh00.Inert);
            }
        }
        ReadBoneAmps(node, "BoneAmps", p.Bones);
        ReadBoneAmps(node, "ChainBoneAmps", p.ChainBones);
    }

    private static void ReadNativeBody(XmlNode node, NativeBodyParams p, int presetVersion)
    {
        string enabled = GetChildText(node, "Enabled");
        if (enabled.Length > 0)
            p.Enabled = string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase);
        float strength = ReadFiniteChild(node, "Strength", 0f,
            presetVersion < 2 ? 1f : FleshParameterRanges.TargetMax, p.Strength);
        p.Softness = ReadFiniteChild(node, "Softness", 0f,
            FleshParameterRanges.TargetMax, p.Softness);
        float motion = ReadFiniteChild(node, "MotionResponse", 0f,
            presetVersion < 2 ? 2f : FleshParameterRanges.TargetMax, p.MotionResponse);
        if (presetVersion < 2)
        {
            strength *= 0.5f;
            motion = 1f - motion * 0.5f;
        }
        p.Strength = FleshValue.Clamp(strength, 0f, FleshParameterRanges.TargetMax,
            p.Strength);
        p.MotionResponse = FleshValue.Clamp(motion, 0f,
            FleshParameterRanges.TargetMax, p.MotionResponse);
        p.Gravity = ReadFiniteChild(node, "Gravity",
            -FleshParameterRanges.NativeGravityMax,
            FleshParameterRanges.NativeGravityMax, p.Gravity);
    }

    private static void ReadBoneAmps(XmlNode node, string elementName, ThighBoneAmounts bones)
    {
        XmlNode boneAmps = node.SelectSingleNode(elementName);
        if (boneAmps == null)
        {
            return;
        }
        foreach (XmlNode child in boneAmps.ChildNodes)
        {
            if (child.Name != "Bone")
            {
                continue;
            }
            int index = BoneTypeIndex(GetAttribute(child, "Type"));
            if (index < 0)
            {
                continue;
            }
            PerBoneAmount amount = bones.Get(index);
            string enabled = GetAttribute(child, "Enabled");
            if (enabled.Length > 0)
            {
                amount.Enabled = enabled == "true";
            }
            string amp = GetAttribute(child, "Amp");
            if (amp.Length > 0)
            {
                amount.Amp = ReadFiniteText(amp, 0f,
                    FleshParameterRanges.BoneAmplitudeMax, amount.Amp);
            }
            amount.AxisX = ReadClampedAttr(child, "AxisX", 0f,
                FleshParameterRanges.AxisScaleMax, amount.AxisX);
            amount.AxisY = ReadClampedAttr(child, "AxisY", 0f,
                FleshParameterRanges.AxisScaleMax, amount.AxisY);
            amount.AxisZ = ReadClampedAttr(child, "AxisZ", 0f,
                FleshParameterRanges.AxisScaleMax, amount.AxisZ);
            if (elementName == "BoneAmps")
            {
                amount.RotAmp = ReadClampedAttr(child, "Rot", 0f,
                    FleshParameterRanges.RotationAmplitudeMax, amount.RotAmp);
            }
            string rotCalc = GetAttribute(child, "RotCalc");
            if (rotCalc.Length > 0)
            {
                amount.RotCalc = rotCalc == "true";
            }
        }
    }

    private static void WriteBoneSet(XmlWriter writer, string partName, ThighBoneParams bone)
    {
        writer.WriteStartElement("ParameterSet");
        writer.WriteAttributeString("PartName", partName);
        writer.WriteElementString("Damping", bone.Damping.ToString("0.0000"));
        writer.WriteElementString("Elasticity", bone.Elasticity.ToString("0.0000"));
        writer.WriteElementString("Stiffness", bone.Stiffness.ToString("0.0000"));
        writer.WriteElementString("Inert", bone.Inert.ToString("0.0000"));
        writer.WriteEndElement();
    }

    private static void WriteChain(XmlWriter writer, ChainParams chain)
    {
        writer.WriteElementString("Weight", chain.Weight.ToString("0.0000"));
        writer.WriteElementString("Gravity", chain.Gravity.ToString("0.0000"));
        writer.WriteElementString("Damping", chain.Damping.ToString("0.0000"));
        writer.WriteElementString("Elasticity", chain.Elasticity.ToString("0.0000"));
        writer.WriteElementString("Stiffness", chain.Stiffness.ToString("0.0000"));
        writer.WriteElementString("Inert", chain.Inert.ToString("0.0000"));
        writer.WriteElementString("JitterFreq", chain.JitterFreq.ToString("0.0000"));
    }

    private static string BoneTypeName(int index)
    {
        return index switch
        {
            0 => "Thigh01",
            1 => "Thigh02",
            2 => "Thigh03",
            _ => "Leg02",
        };
    }

    private static int BoneTypeIndex(string type)
    {
        for (int i = 0; i < 4; i++)
        {
            if (string.Equals(BoneTypeName(i), type, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private static float ReadClampedAttr(XmlNode node, string name, float min, float max, float fallback)
    {
        string value = GetAttribute(node, name);
        if (value.Length == 0)
        {
            return fallback;
        }
        float parsed;
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            return FleshValue.Clamp(parsed, min, max, fallback);
        }
        return fallback;
    }

    private static string GetChildText(XmlNode parent, string name)
    {
        XmlNode node = parent.SelectSingleNode(name);
        return node == null ? string.Empty : node.InnerText;
    }

    private static string GetAttribute(XmlNode node, string name)
    {
        XmlAttribute attribute = node.Attributes[name];
        return attribute == null ? string.Empty : attribute.Value;
    }

    private static float GetFloat(XmlNode node, string name, float fallback)
    {
        string text = GetChildText(node, name);
        if (text.Length == 0)
        {
            return fallback;
        }
        float parsed;
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
            FleshValue.IsFinite(parsed))
        {
            return parsed;
        }
        return fallback;
    }

    private static float ReadFiniteChild(XmlNode node, string name,
        float min, float max, float fallback)
    {
        return FleshValue.Clamp(GetFloat(node, name, fallback), min, max, fallback);
    }

    private static float ReadFiniteText(string text, float min, float max, float fallback)
    {
        float parsed;
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
            ? FleshValue.Clamp(parsed, min, max, fallback)
            : fallback;
    }
}

/// <summary>
/// Session-memory profile for one character identity: one shared parameter set per
/// part, so same-name characters in the scene edit the same objects (auto-sync).
/// </summary>
internal sealed class FleshProfile
{
    public ThighParams Thigh;
    public ThighParams Arm;
    public ThighParams Belly;
    public NativeBustProfile Breast;
    public NativeBodyParams Butt;
}
