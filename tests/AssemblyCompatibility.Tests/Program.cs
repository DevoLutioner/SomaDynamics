using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 2 || (args[0] != "KK" && args[0] != "KKS"))
        {
            Console.Error.WriteLine("Usage: AssemblyCompatibility.Tests KK|KKS <ThighPhysicsController.dll>");
            return 2;
        }

        try
        {
            return Check(args[0], args[1]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL AssemblyCompatibility.Tests: " + ex.Message);
            return 1;
        }
    }

    private static int Check(string game, string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        AssemblyDefinition assembly = metadata.GetAssemblyDefinition();
        var references = new Dictionary<string, Version>(StringComparer.Ordinal);
        foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
        {
            AssemblyReference reference = metadata.GetAssemblyReference(handle);
            references.Add(metadata.GetString(reference.Name), reference.Version);
        }

        var failures = new List<string>();
        bool sunshine = game == "KKS";
        Require(metadata.GetString(assembly.Name) == "ThighPhysicsController",
            "input is not the Soma Dynamics plugin assembly", failures);
        Require(references.ContainsKey(sunshine ? "KKSAPI" : "KKAPI"),
            "missing " + (sunshine ? "KKSAPI" : "KKAPI") + " dependency", failures);
        Require(!references.ContainsKey(sunshine ? "KKAPI" : "KKSAPI"),
            "references the other game's character API", failures);
        Require(references.ContainsKey(sunshine ? "KKS_ExtensibleSaveFormat" : "ExtensibleSaveFormat"),
            "missing the " + game + " ExtensibleSaveFormat dependency", failures);
        Require(!references.ContainsKey(sunshine ? "ExtensibleSaveFormat" : "KKS_ExtensibleSaveFormat"),
            "references the other game's ExtensibleSaveFormat", failures);
        Require(references.TryGetValue("mscorlib", out Version runtime) && runtime.Major == (sunshine ? 4 : 2),
            "mscorlib must target the " + game + " runtime (major " + (sunshine ? 4 : 2) + ")", failures);

        string expectedUnity = sunshine ? "UnityEngine.CoreModule" : "UnityEngine";
        Require(references.ContainsKey(expectedUnity), "missing " + expectedUnity + " dependency", failures);
        bool foundUnityType = false;
        var incompatibleUnityTypes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (TypeReferenceHandle handle in metadata.TypeReferences)
        {
            TypeReference type = metadata.GetTypeReference(handle);
            string ns = metadata.GetString(type.Namespace);
            if (ns != "UnityEngine" && !ns.StartsWith("UnityEngine.", StringComparison.Ordinal)) continue;
            foundUnityType = true;
            string scope = GetAssemblyScope(metadata, type.ResolutionScope);
            // KK and KKS expose the same C# namespaces, but their Unity types live
            // in different assemblies. A successful compile alone cannot catch a
            // KK DLL accidentally shipped in a KKS package (issue #15).
            bool valid = sunshine
                ? scope.StartsWith("UnityEngine.", StringComparison.Ordinal)
                : scope == "UnityEngine";
            if (!valid)
            {
                if (!incompatibleUnityTypes.TryGetValue(scope, out List<string> names))
                {
                    names = new List<string>();
                    incompatibleUnityTypes.Add(scope, names);
                }
                names.Add(ns + "." + metadata.GetString(type.Name));
            }
        }
        Require(foundUnityType, "plugin contains no UnityEngine type references", failures);
        foreach (var mismatch in incompatibleUnityTypes)
            failures.Add(mismatch.Value.Count + " UnityEngine types resolve through " + mismatch.Key
                + ", incompatible with " + game + " (e.g. " + string.Join(", ", mismatch.Value.Take(3)) + ")");

        Console.WriteLine("Assembly: " + metadata.GetString(assembly.Name) + " " + assembly.Version);
        Console.WriteLine("References: " + string.Join(", ", references.OrderBy(x => x.Key)
            .Select(x => x.Key + " " + x.Value)));
        foreach (string failure in failures) Console.Error.WriteLine("FAIL " + failure);
        Console.WriteLine((failures.Count == 0 ? "PASS " : "FAIL ") + "AssemblyCompatibility.Tests " + game);
        return failures.Count == 0 ? 0 : 1;
    }

    private static string GetAssemblyScope(MetadataReader metadata, EntityHandle scope)
    {
        while (scope.Kind == HandleKind.TypeReference)
            scope = metadata.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;
        return scope.Kind == HandleKind.AssemblyReference
            ? metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)scope).Name)
            : scope.Kind.ToString();
    }

    private static void Require(bool condition, string message, List<string> failures)
    {
        if (!condition) failures.Add(message);
    }
}
