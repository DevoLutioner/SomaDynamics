using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace ExtensibleSaveFormat
{
    public sealed class PluginData
    {
        public Dictionary<string, object> data = new Dictionary<string, object>();
    }
}

// Only the Unity boundary is simulated. The actual production component, state,
// solvers, safety checks and parameter model are compiled into this executable.
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder : Attribute
    {
        public DefaultExecutionOrder(int order) { }
    }

    public class Object
    {
        public string name;
        public int GetInstanceID() { return GetHashCode(); }
        public static void Destroy(Object value)
        {
            GameObject obj = value as GameObject;
            if (obj != null) { obj.activeSelf = false; obj.transform.parent = null; }
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
        {
            return gameObject.GetComponentsInChildren<T>(includeInactive);
        }
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled { get { return enabled && gameObject.activeInHierarchy; } }
    }

    public class MonoBehaviour : Behaviour { }

    public sealed class GameObject : Object
    {
        public readonly Transform transform;
        public bool activeSelf = true;
        public int HierarchyScans;
        private readonly List<Component> _components = new List<Component>();
        public GameObject(string name = "Object")
        {
            this.name = name;
            transform = new Transform { gameObject = this, name = name };
            _components.Add(transform);
        }
        public bool activeInHierarchy
        {
            get { return activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy); }
        }
        public T AddComponent<T>() where T : Component, new()
        {
            T value = new T { gameObject = this, name = name };
            _components.Add(value);
            return value;
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
        {
            HierarchyScans++;
            List<T> result = new List<T>();
            Collect(this, includeInactive, result);
            return result.ToArray();
        }
        private static void Collect<T>(GameObject current, bool includeInactive, List<T> result) where T : Component
        {
            if (!includeInactive && !current.activeInHierarchy) return;
            result.AddRange(current._components.OfType<T>());
            foreach (Transform child in current.transform.Children) Collect(child.gameObject, includeInactive, result);
        }
    }

    public sealed class Transform : Component
    {
        public readonly List<Transform> Children = new List<Transform>();
        private Transform _parent;
        public Transform parent
        {
            get { return _parent; }
            set { if (_parent != null) _parent.Children.Remove(this); _parent = value; if (value != null) value.Children.Add(this); }
        }
        public int childCount { get { return Children.Count; } }
        public Transform GetChild(int index) { return Children[index]; }
        public void SetParent(Transform value, bool worldPositionStays) { parent = value; }
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localEulerAngles { get { return localRotation.eulerAngles; } }
        public Vector3 localScale = Vector3.one;
        public Vector3 position
        {
            get { return parent == null ? localPosition : parent.TransformPoint(localPosition); }
            set { localPosition = parent == null ? value : parent.InverseTransformPoint(value); }
        }
        public Quaternion rotation
        {
            get { return parent == null ? localRotation : parent.rotation * localRotation; }
            set { localRotation = parent == null ? value : Quaternion.Inverse(parent.rotation) * value; }
        }
        public Vector3 TransformPoint(Vector3 value) { return position + TransformDirection(Vector3.Scale(value, localScale)); }
        public Vector3 InverseTransformPoint(Vector3 value) { return InverseTransformDirection(value - position); }
        public Vector3 TransformDirection(Vector3 value) { return rotation * value; }
        public Vector3 InverseTransformDirection(Vector3 value) { return Quaternion.Inverse(rotation) * value; }
        public bool IsChildOf(Transform value)
        {
            for (Transform current = this; current != null; current = current.parent) if (current == value) return true;
            return false;
        }
    }

    public sealed class SkinnedMeshRenderer : Component
    {
        public Transform[] bones = new Transform[0];
    }

    public static class Time
    {
        public static float deltaTime = 1f / 60f;
        public static int frameCount;
        public static float realtimeSinceStartup;
    }

    public static class Debug
    {
        public static void Log(object value) { }
        public static void LogWarning(object value) { }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 one { get { return new Vector3(1f, 1f, 1f); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector3 normalized { get { return magnitude > 0f ? this / magnitude : zero; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator -(Vector3 a) { return zero - a; }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static Vector3 operator *(float a, Vector3 b) { return b * a; }
        public static Vector3 operator /(Vector3 a, float b) { return a * (1f / b); }
        public static Vector3 Scale(Vector3 a, Vector3 b) { return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z); }
        public static float Dot(Vector3 a, Vector3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        public static Vector3 Cross(Vector3 a, Vector3 b) { return new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        public static Vector3 ClampMagnitude(Vector3 a, float max) { return a.magnitude > max ? a.normalized * max : a; }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { return a + (b - a) * Mathf.Clamp01(t); }
        public static Vector3 MoveTowards(Vector3 a, Vector3 b, float step) { return a + ClampMagnitude(b - a, step); }
        public string ToString(string format) { return string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", x.ToString(format), y.ToString(format), z.ToString(format)); }
        internal NVector3 Native { get { return new NVector3(x, y, z); } }
        internal static Vector3 From(NVector3 v) { return new Vector3(v.X, v.Y, v.Z); }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity { get { return new Quaternion(0f, 0f, 0f, 1f); } }
        private NQuaternion Native { get { return new NQuaternion(x, y, z, w); } }
        private static Quaternion From(NQuaternion v) { return new Quaternion(v.X, v.Y, v.Z, v.W); }
        public static Quaternion operator *(Quaternion a, Quaternion b) { return From(a.Native * b.Native); }
        public static Vector3 operator *(Quaternion a, Vector3 b) { return Vector3.From(NVector3.Transform(b.Native, a.Native)); }
        public static Quaternion Inverse(Quaternion a) { return From(NQuaternion.Inverse(a.Native)); }
        public static Quaternion AngleAxis(float angle, Vector3 axis) { return axis.sqrMagnitude == 0f ? identity : From(NQuaternion.CreateFromAxisAngle(axis.normalized.Native, angle * Mathf.Deg2Rad)); }
        public static Quaternion Euler(Vector3 value) { return From(NQuaternion.CreateFromYawPitchRoll(value.y * Mathf.Deg2Rad, value.x * Mathf.Deg2Rad, value.z * Mathf.Deg2Rad)); }
        public static Quaternion FromToRotation(Vector3 a, Vector3 b)
        {
            if (a.sqrMagnitude == 0f || b.sqrMagnitude == 0f) return identity;
            float dot = Mathf.Clamp(Vector3.Dot(a.normalized, b.normalized), -1f, 1f);
            Vector3 cross = Vector3.Cross(a.normalized, b.normalized);
            if (cross.sqrMagnitude < 0.00000001f) return identity;
            return AngleAxis((float)Math.Acos(dot) / Mathf.Deg2Rad, cross);
        }
        public static float Angle(Quaternion a, Quaternion b) { return 2f * (float)Math.Acos(Math.Min(1f, Math.Abs(NQuaternion.Dot(a.Native, b.Native)))) / Mathf.Deg2Rad; }
        public void ToAngleAxis(out float angle, out Vector3 axis)
        {
            angle = 2f * (float)Math.Acos(Mathf.Clamp(w, -1f, 1f)) / Mathf.Deg2Rad;
            axis = new Vector3(x, y, z).normalized;
            if (axis.sqrMagnitude == 0f) axis = new Vector3(1f, 0f, 0f);
        }
        public Vector3 eulerAngles { get { ToAngleAxis(out float angle, out Vector3 axis); return axis * angle; } }
    }

    public static class Mathf
    {
        public const float Deg2Rad = (float)Math.PI / 180f;
        public static float Clamp(float v, float min, float max) { return Math.Min(max, Math.Max(min, v)); }
        public static float Clamp01(float v) { return Clamp(v, 0f, 1f); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float InverseLerp(float a, float b, float v) { return a == b ? 0f : Clamp01((v - a) / (b - a)); }
    }
}

namespace UnityEngine.Profiling
{
    public static class Profiler
    {
        public static long GetMonoUsedSizeLong() { return GC.GetTotalMemory(false); }
    }
}

namespace KKAPI
{
    public enum GameMode { Studio }
}

namespace KKAPI.Chara
{
    public class CharaCustomFunctionController : UnityEngine.MonoBehaviour
    {
        public ChaControl ChaControl;
        public ExtensibleSaveFormat.PluginData TestCardData;
        protected ExtensibleSaveFormat.PluginData GetExtendedData() { return TestCardData; }
        protected void SetExtendedData(ExtensibleSaveFormat.PluginData data) { TestCardData = data; }
        protected virtual void OnReload(KKAPI.GameMode mode, bool maintainState) { }
        protected virtual void OnCardBeingSaved(KKAPI.GameMode mode) { }
        protected virtual void OnDestroy() { }
        protected virtual void OnEnable() { }
        public void ReloadForTest(bool maintainState) { OnReload(KKAPI.GameMode.Studio, maintainState); }
    }
}

public class ChaInfo : UnityEngine.Component
{
    public ChaFile chaFile = new ChaFile();
    public ChaFileStatus fileStatus = new ChaFileStatus();
}
public sealed class ChaControl : ChaInfo
{
    public BustSoft bustSoft;
    public BustGravity bustGravity;
}
public sealed class ChaFile { public ChaFileParameter parameter = new ChaFileParameter(); }
public sealed class ChaFileParameter { public string fullname = "Test"; public byte sex = 1; public int personality; }
public sealed class ChaFileStatus { public int coordinateType; public byte[] clothesState = new byte[3]; }
public sealed class BustSoft { }
public sealed class BustGravity { }

public sealed class DynamicBone : UnityEngine.Behaviour
{
    public UnityEngine.Transform m_Root;
    public List<UnityEngine.Transform> m_Exclusions;
    // Same private field names and collection shape as the installed KK DynamicBone.
    private readonly List<Particle> m_Particles = new List<Particle>();
    private sealed class Particle { public UnityEngine.Transform m_Transform; }
    public void AddParticle(UnityEngine.Transform transform) { m_Particles.Add(new Particle { m_Transform = transform }); }
}

namespace ThighPhysicsController
{
    internal sealed class ConfigValue<T>
    {
        public T Value;
        public ConfigValue(T value) { Value = value; }
    }
    internal static class ThighPhysicsControllerPlugin
    {
        public static readonly ConfigValue<bool> AutoApply = new ConfigValue<bool>(true);
        public static readonly ConfigValue<bool> ForceEnable = new ConfigValue<bool>(true);
        public static readonly ConfigValue<bool> RememberPerCharacter = new ConfigValue<bool>(true);
        public static readonly ConfigValue<bool> DisableMaleByDefault = new ConfigValue<bool>(true);
        public static readonly ConfigValue<bool> DefaultThighEnabled = new ConfigValue<bool>(true);
        public static readonly ConfigValue<bool> DefaultArmEnabled = new ConfigValue<bool>(true);
        public static readonly ConfigValue<bool> DefaultBellyEnabled = new ConfigValue<bool>(true);
        public static readonly ConfigValue<bool> DefaultBreastEnabled = new ConfigValue<bool>(true);
        public static readonly ConfigValue<bool> DefaultButtEnabled = new ConfigValue<bool>(true);
        public static readonly ConfigValue<bool> ApplyDefaultsToAllCharacters = new ConfigValue<bool>(true);
        public static readonly ConfigValue<bool> DebugDumpSkeleton = new ConfigValue<bool>(false);
        public static readonly ConfigValue<string> DefaultPreset = new ConfigValue<string>("");
        public static readonly ConfigValue<string> PresetDirectory = new ConfigValue<string>(System.IO.Path.GetTempPath());
        public static readonly List<ThighController> Controllers = new List<ThighController>();
        public static readonly Dictionary<string, FleshProfile> MemoryProfiles = new Dictionary<string, FleshProfile>();
        public static readonly ConfigValue<bool> AutoFixSpringDrift = new ConfigValue<bool>(true);
        public static readonly ConfigValue<bool> DebugCollectMetrics = new ConfigValue<bool>(false);
        public static readonly ConfigValue<bool> DebugLogFlesh = new ConfigValue<bool>(false);
        public static readonly ConfigValue<bool> TimelinePlaybackSpringFallback = new ConfigValue<bool>(false);
        public static readonly ConfigValue<bool> TimelineSpringFallbackAuto = new ConfigValue<bool>(false);
        public static int Initializations;
        public static void LogRuntime(string message) { if (message.StartsWith("SOMA_RUNTIME_INIT")) Initializations++; }
    }
    internal static class TimelineConstraintBridge
    {
        public static bool IsTimelinePlaying() { return false; }
        public static bool ShouldYieldChainRotations(UnityEngine.Transform root) { return false; }
    }
    internal sealed class NativeDynamicBoneBridge
    {
        public static int Created;
        public static int ApplyCalls;
        public static int Restores;
        public NativeDynamicBoneBridge(ChaControl control) { Created++; }
        public void Apply(FleshPartId part, NativeBodyParams value) { ApplyCalls++; }
        public void RestoreAll() { Restores++; }
    }
}
