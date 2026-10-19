using System;
using System.Reflection;
using ThighPhysicsController;
using UnityEngine;

internal static class Program
{
    private static int _failures;
    private static readonly MethodInfo LateUpdate = typeof(ThighFleshJiggle).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

    private static int Main()
    {
        Run("missing skeleton stops repeated discovery", MissingSkeleton);
        Run("BP-owned Spring bone survives solver and clear", () => ExternalOwner(false));
        Run("BP-owned Chain bone survives solver and clear", () => ExternalOwner(true));
        Run("ordinary Spring physics remains active", () => NormalPhysics(false));
        Run("ordinary Chain physics remains active", () => NormalPhysics(true));
        Run("resets reuse the skeleton index", CachedResets);
        Run("delayed skeleton can recover on explicit reset", DelayedSkeleton);
        Run("inactive skeleton retries remain bounded and recover", InactiveSkeleton);
        Run("BP protection leaves the other side active", OtherSide);
        Run("late unshared side can appear beside a protected side", LateOtherSide);
        Run("unrelated DynamicBone does not reserve thigh bones", UnrelatedOwner);
        Run("declared exclusions preserve normal thigh physics", ExcludedSubtree);
        Run("Spring drift watchdog has no managed allocation loop", DriftAllocations);
        Console.WriteLine(_failures == 0 ? "PASS RuntimeIntegration.Tests" : "FAIL RuntimeIntegration.Tests (" + _failures + " cases)");
        return _failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failures++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); }
    }

    private static void Tick(ThighFleshJiggle component, int frames)
    {
        Action update = (Action)LateUpdate.CreateDelegate(typeof(Action), component);
        for (int i = 0; i < frames; i++)
        {
            Time.frameCount++;
            Time.realtimeSinceStartup += Time.deltaTime;
            update();
        }
    }

    private static void MissingSkeleton()
    {
        GameObject root = new GameObject("unsupported skeleton");
        ChaControl control = root.AddComponent<ChaControl>();
        ThighFleshJiggle component = root.AddComponent<ThighFleshJiggle>();
        ThighParams value = ThighParams.CreateDefault();
        component.Initialize(control, value, FleshPartId.Thigh);
        root.HierarchyScans = 0;
        ThighPhysicsControllerPlugin.Initializations = 0;
        Tick(component, 1800);
        Console.WriteLine("  30 s: hierarchy scans=" + root.HierarchyScans + ", rebuilds=" + ThighPhysicsControllerPlugin.Initializations);
        Assert(root.HierarchyScans <= 10, "empty skeleton repeatedly traversed the hierarchy: " + root.HierarchyScans);
    }

    private static void ExternalOwner(bool chain)
    {
        GameObject root = new GameObject("character");
        ChaControl control = root.AddComponent<ChaControl>();
        Transform anchor = Bone(root.transform, "cf_j_thigh00_L", Vector3.zero);
        Transform bone = Bone(anchor, "cf_s_thigh01_L", new Vector3(0f, -0.1f, 0f));
        Transform next = Bone(bone, "cf_s_thigh02_L", new Vector3(0f, -0.1f, 0f));
        DynamicBone bp = root.AddComponent<DynamicBone>();
        bp.name = "BP shared thigh";
        bp.m_Root = anchor;
        bp.AddParticle(anchor);
        bp.AddParticle(bone);
        bp.AddParticle(next);
        ThighParams value = ThighParams.CreateDefault();
        value.GamePhysics = chain;
        ThighFleshJiggle component = root.AddComponent<ThighFleshJiggle>();
        component.Initialize(control, value, FleshPartId.Thigh);
        Vector3 external = bone.localPosition + new Vector3(0.004f, 0f, 0f);
        bone.localPosition = external;
        Quaternion externalRotation = Quaternion.Euler(new Vector3(0f, 0f, 20f));
        bone.localRotation = externalRotation;
        Tick(component, 240);
        Console.WriteLine("  external x=" + external.x + ", actual x=" + bone.localPosition.x);
        Assert((bone.localPosition - external).magnitude < 0.000001f, "Soma overwrote a DynamicBone-owned transform");
        Assert(Quaternion.Angle(bone.localRotation, externalRotation) < 0.1f, "Soma rotated a DynamicBone-owned transform");
        component.ClearDeformation();
        Assert((bone.localPosition - external).magnitude < 0.000001f, "Clear shape overwrote the external owner");
        component.PrepareForExternalShapeChange(2);
        Tick(component, 5);
        component.ResetState();
        Assert((bone.localPosition - external).magnitude < 0.000001f, "shape rebase or reset overwrote the external owner");
        Assert(Quaternion.Angle(bone.localRotation, externalRotation) < 0.1f, "shape rebase or reset rotated the external owner");
    }

    private static ThighFleshJiggle CreatePart(ChaControl control, bool chain)
    {
        ThighParams value = ThighParams.CreateDefault();
        value.GamePhysics = chain;
        ThighFleshJiggle component = control.gameObject.AddComponent<ThighFleshJiggle>();
        component.Initialize(control, value, FleshPartId.Thigh);
        return component;
    }

    private static Transform Thigh(Transform root, string side)
    {
        Transform anchor = Bone(root, "cf_j_thigh00_" + side, Vector3.zero);
        Transform first = Bone(anchor, "cf_s_thigh01_" + side, new Vector3(0f, -0.1f, 0f));
        Transform second = Bone(first, "cf_s_thigh02_" + side, new Vector3(0f, -0.1f, 0f));
        Transform third = Bone(second, "cf_s_thigh03_" + side, new Vector3(0f, -0.1f, 0f));
        Bone(third, "cf_s_leg02_" + side, new Vector3(0f, -0.1f, 0f));
        return first;
    }

    private static float Animate(ThighFleshJiggle component, Transform first)
    {
        // The first Chain particle is kinematic; measure the simulated children.
        System.Collections.Generic.List<Transform> bones = new System.Collections.Generic.List<Transform>();
        for (Transform current = first; current != null; current = current.childCount == 0 ? null : current.GetChild(0))
            bones.Add(current);
        Vector3[] rest = bones.ConvertAll(bone => bone.localPosition).ToArray();
        float peak = 0f;
        for (int frame = 0; frame < 180; frame++)
        {
            first.parent.localPosition = new Vector3((float)Math.Sin(frame * 0.15f) * 0.03f, 0f, 0f);
            Tick(component, 1);
            for (int i = 0; i < bones.Count; i++)
                peak = Math.Max(peak, (bones[i].localPosition - rest[i]).magnitude);
        }
        return peak;
    }

    private static void NormalPhysics(bool chain)
    {
        GameObject root = new GameObject("ordinary skeleton");
        ChaControl control = root.AddComponent<ChaControl>();
        Transform first = Thigh(root.transform, "L");
        ThighFleshJiggle component = CreatePart(control, chain);
        Vector3 rest = first.localPosition;
        float peak = Animate(component, first);
        Assert(peak > 0.00001f, "normal physics stopped producing motion");
        component.ClearDeformation();
        Assert((first.localPosition - rest).magnitude < 0.000001f, "normal clear did not restore the rest pose");
    }

    private static void CachedResets()
    {
        GameObject root = new GameObject("cached skeleton");
        ChaControl control = root.AddComponent<ChaControl>();
        Thigh(root.transform, "L");
        ThighFleshJiggle component = CreatePart(control, true);
        root.HierarchyScans = 0;
        for (int i = 0; i < 20; i++) { component.ClearDeformation(); component.ResetState(); }
        Assert(root.HierarchyScans == 0, "ordinary resets rescanned the character: " + root.HierarchyScans);
    }

    private static void DelayedSkeleton()
    {
        GameObject root = new GameObject("late body");
        ChaControl control = root.AddComponent<ChaControl>();
        ThighFleshJiggle component = CreatePart(control, false);
        Tick(component, 300);
        Transform first = Thigh(root.transform, "L");
        component.ResetState();
        Assert(Animate(component, first) > 0.00001f, "explicit reset did not rediscover a late skeleton");
    }

    private static void InactiveSkeleton()
    {
        GameObject root = new GameObject("inactive body");
        ChaControl control = root.AddComponent<ChaControl>();
        Transform first = Thigh(root.transform, "L");
        ThighFleshJiggle component = CreatePart(control, true);
        first.parent.gameObject.activeSelf = false;
        root.HierarchyScans = 0;
        Tick(component, 1800);
        Assert(root.HierarchyScans <= 9, "inactive skeleton retried indefinitely");
        first.parent.gameObject.activeSelf = true;
        Tick(component, 1);
        Assert(Animate(component, first) > 0.00001f, "reactivated skeleton lost physics");
        first.parent.gameObject.activeSelf = false;
        root.HierarchyScans = 0;
        Tick(component, 1800);
        Assert(root.HierarchyScans == 9, "recovered skeleton did not reset its retry budget");
    }

    private static void OtherSide()
    {
        GameObject root = new GameObject("one shared side");
        ChaControl control = root.AddComponent<ChaControl>();
        Transform left = Thigh(root.transform, "L");
        Transform right = Thigh(root.transform, "R");
        DynamicBone bp = root.AddComponent<DynamicBone>();
        bp.m_Root = left.parent;
        bp.AddParticle(left);
        bp.enabled = false;
        ThighFleshJiggle component = CreatePart(control, true);
        Vector3 external = left.localPosition + new Vector3(0.004f, 0f, 0f);
        left.localPosition = external;
        bp.enabled = true;
        Assert(Animate(component, right) > 0.00001f, "protection disabled the unshared side");
        component.ClearDeformation();
        Assert((left.localPosition - external).magnitude < 0.000001f, "late-enabled owner lost its rest pose");
    }

    private static void UnrelatedOwner()
    {
        GameObject root = new GameObject("unrelated chain");
        ChaControl control = root.AddComponent<ChaControl>();
        Transform thigh = Thigh(root.transform, "L");
        Transform unrelated = Bone(root.transform, "other dynamic bone", Vector3.zero);
        DynamicBone owner = root.AddComponent<DynamicBone>();
        owner.m_Root = root.transform;
        owner.AddParticle(unrelated);
        Assert(Animate(CreatePart(control, true), thigh) > 0.00001f, "ownership used ancestry instead of actual particles");
    }

    private static void LateOtherSide()
    {
        GameObject root = new GameObject("partially loaded shared body");
        ChaControl control = root.AddComponent<ChaControl>();
        Transform left = Thigh(root.transform, "L");
        DynamicBone owner = root.AddComponent<DynamicBone>();
        owner.m_Root = left.parent;
        owner.AddParticle(left);
        ThighFleshJiggle component = CreatePart(control, true);
        Transform right = Thigh(root.transform, "R");
        Tick(component, 60);
        Assert(Animate(component, right) > 0.00001f, "protected side prevented retrying an unshared side that loaded later");
    }

    private static void ExcludedSubtree()
    {
        GameObject root = new GameObject("excluded thigh");
        ChaControl control = root.AddComponent<ChaControl>();
        Transform thigh = Thigh(root.transform, "L");
        DynamicBone owner = root.AddComponent<DynamicBone>();
        owner.m_Root = root.transform;
        owner.m_Exclusions = new System.Collections.Generic.List<Transform> { thigh.parent };
        Assert(Animate(CreatePart(control, true), thigh) > 0.00001f, "declared exclusions were ignored before particles initialized");
    }

    private static void DriftAllocations()
    {
        GameObject root = new GameObject("watchdog baseline");
        ChaControl control = root.AddComponent<ChaControl>();
        Thigh(root.transform, "L");
        ThighFleshJiggle component = CreatePart(control, false);
        Tick(component, 180);
        Action update = (Action)LateUpdate.CreateDelegate(typeof(Action), component);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 600; frame++) update();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine("  600 steady Spring frames with watchdog: managed bytes=" + allocated);
        Assert(allocated == 0, "steady Spring/watchdog path allocates under the managed test runtime");
    }

    private static Transform Bone(Transform parent, string name, Vector3 position)
    {
        Transform result = new GameObject(name).transform;
        result.parent = parent;
        result.localPosition = position;
        return result;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
