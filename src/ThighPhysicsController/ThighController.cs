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

    private readonly List<ThighFleshJiggle> _flesh = new List<ThighFleshJiggle>();

    private bool _fleshReady;

    private int _pendingApplyFrames;

    private bool _skeletonDumped;

    private bool _paramsLoaded;

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
            // maintainState=true means the card data did not change (scene refresh,
            // coordinate/outfit change); keep the current settings in that case.
            if (!maintainState || !_paramsLoaded)
            {
                LoadParamsFromCardOrDefault();
            }
            _skeletonDumped = false;
            Apply(resetPosition: true);
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
        SetExtendedData(data);
    }

    protected override void OnDestroy()
    {
        RemoveFlesh();
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
        }
        else
        {
            PluginData extended = GetExtendedData();
            if (extended != null && extended.data != null && extended.data.Count > 0)
            {
                Params = ThighParams.CreateDefault();
                Params.ReadData(extended);
            }
            else
            {
                Params = ThighParams.CreateDefault();
            }
            ArmParams = ThighParams.CreatePartDefaults(FleshPartId.Arm);
            BellyParams = ThighParams.CreatePartDefaults(FleshPartId.Belly);
            if (extended != null && extended.data != null)
            {
                int version = 0;
                if (extended.data.ContainsKey("v"))
                {
                    version = Convert.ToInt32(extended.data["v"]);
                }
                ThighParams.ReadPart(extended.data, "arm_", ArmParams, version);
                ThighParams.ReadPart(extended.data, "belly_", BellyParams, version);
            }
            if (useMemory)
            {
                ThighPhysicsControllerPlugin.MemoryProfiles[key] = new FleshProfile
                {
                    Thigh = Params,
                    Arm = ArmParams,
                    Belly = BellyParams,
                };
            }
        }
        if (ThighPhysicsControllerPlugin.ForceEnable.Value)
        {
            Params.Enabled = true;
            ArmParams.Enabled = true;
            BellyParams.Enabled = true;
        }
        _paramsLoaded = true;
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
        if (!Params.Enabled)
        {
            RemoveFlesh();
            _fleshReady = false;
        }
        else
        {
            EnsureFlesh();
            ApplyFlesh(resetPosition);
        }
    }

    internal void UpdateTick()
    {
        if (_pendingApplyFrames > 0)
        {
            _pendingApplyFrames--;
            if (_pendingApplyFrames == 0 && isActiveAndEnabled)
            {
                Apply(resetPosition: false);
            }
        }
    }

    internal void ClearDeformation()
    {
        for (int i = 0; i < _flesh.Count; i++)
        {
            ThighFleshJiggle jiggle = _flesh[i];
            if (jiggle != null)
            {
                jiggle.ClearDeformation();
            }
        }
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
            jiggle.ParamsRef = GetParams(jiggle.PartId);
            jiggle.enabled = true;
            if (resetPosition)
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

    private Transform FindBone(string boneName)
    {
        if (ChaControl == null)
        {
            return null;
        }
        Transform[] transforms = ChaControl.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == boneName)
            {
                return transforms[i];
            }
        }
        return null;
    }

    internal Transform FindBonePublic(string boneName)
    {
        return FindBone(boneName);
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
                writer.WriteAttributeString("Version", "1");
                WriteParamBody(writer, "cf_j_thigh00_L", Params);
                writer.WriteStartElement("ArmPart");
                WriteParamBody(writer, "cf_j_arm00_L", ArmParams);
                writer.WriteEndElement();
                writer.WriteStartElement("BellyPart");
                WriteParamBody(writer, "cf_j_spine03", BellyParams);
                writer.WriteEndElement();
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
            p.Gravity = ReadFiniteText(gravity, -0.2f, 0.2f, p.Gravity);
        }
        string weight = GetChildText(node, "Weight");
        if (weight.Length > 0)
        {
            p.Weight = ReadFiniteText(weight, 0f, 1f, p.Weight);
        }
        string motionGain = GetChildText(node, "MotionGain");
        if (motionGain.Length > 0)
        {
            p.MotionGain = ReadFiniteText(motionGain, 0f, 5f, p.MotionGain);
        }
        p.JitterFreq = FleshValue.Clamp(GetFloat(node, "JitterFreq", p.JitterFreq),
            0f, 2.5f, p.JitterFreq);
        p.MotionSmooth = FleshValue.Clamp(GetFloat(node, "MotionSmooth", p.MotionSmooth),
            0.05f, 0.5f, p.MotionSmooth);
        string gamePhysics = GetChildText(node, "GamePhysics");
        if (gamePhysics.Length > 0)
        {
            p.GamePhysics = gamePhysics == "true";
        }
        XmlNode chainParameters = node.SelectSingleNode("ChainParameters");
        if (chainParameters != null)
        {
            p.Chain.Weight = ReadFiniteChild(chainParameters, "Weight", 0f, 1f, p.Chain.Weight);
            p.Chain.Gravity = ReadFiniteChild(chainParameters, "Gravity", -0.2f, 0.2f, p.Chain.Gravity);
            p.Chain.Damping = ReadFiniteChild(chainParameters, "Damping", 0f, 1f, p.Chain.Damping);
            p.Chain.Elasticity = ReadFiniteChild(chainParameters, "Elasticity", 0f, 1f, p.Chain.Elasticity);
            p.Chain.Stiffness = ReadFiniteChild(chainParameters, "Stiffness", 0f, 1f, p.Chain.Stiffness);
            p.Chain.Inert = ReadFiniteChild(chainParameters, "Inert", 0f, 1f, p.Chain.Inert);
            p.Chain.JitterFreq = ReadFiniteChild(chainParameters, "JitterFreq", 0f, 2.5f,
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
                p.Thigh00.Inert = ReadFiniteChild(child, "Inert", 0f, 1f, p.Thigh00.Inert);
            }
        }
        ReadBoneAmps(node, "BoneAmps", p.Bones);
        ReadBoneAmps(node, "ChainBoneAmps", p.ChainBones);
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
                amount.Amp = ReadFiniteText(amp, 0f, 2f, amount.Amp);
            }
            amount.AxisX = ReadClampedAttr(child, "AxisX", 0f, 1f, amount.AxisX);
            amount.AxisY = ReadClampedAttr(child, "AxisY", 0f, 1f, amount.AxisY);
            amount.AxisZ = ReadClampedAttr(child, "AxisZ", 0f, 1f, amount.AxisZ);
            if (elementName == "BoneAmps")
            {
                amount.RotAmp = ReadClampedAttr(child, "Rot", 0f, 1f, amount.RotAmp);
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
}
