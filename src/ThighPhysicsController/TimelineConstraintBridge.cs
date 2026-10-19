using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ThighPhysicsController;

/// <summary>
/// Optional, reflection-only compatibility bridge for Timeline + NodesConstraints.
/// Soma must not take a hard dependency on either plugin because it also runs in
/// Maker and in installations where one or both plugins are absent.
/// </summary>
internal static class TimelineConstraintBridge
{
    private struct CharacterFrameState
    {
        public int Frame;
        public bool YieldRotations;
    }

    private static readonly Dictionary<int, CharacterFrameState> CharacterStates =
        new Dictionary<int, CharacterFrameState>();

    private static bool _metadataResolved;
    private static bool _available;
    private static bool _scanDisabled;
    private static bool _warningLogged;
    private static int _timelinePlayingFrame = -1;
    private static bool _timelinePlaying;
    private static PropertyInfo _timelineIsPlaying;
    private static FieldInfo _nodesSelf;
    private static FieldInfo _constraints;
    private static Type _constraintType;
    private static FieldInfo _constraintEnabled;
    private static FieldInfo _constraintParentTransform;
    private static FieldInfo _constraintChildTransform;

    /// <summary>
    /// Returns true only while Timeline is playing and an enabled NodesConstraints
    /// constraint touches this character. The result is cached per character/frame
    /// because one character owns several Soma part components.
    /// </summary>
    public static bool ShouldYieldChainRotations(Transform characterRoot)
    {
        if (characterRoot == null)
        {
            return false;
        }
        // The normal game path does not use Timeline. Resolve and read the global
        // playback flag once per frame, then skip the per-character dictionary and
        // constraint scan entirely while it is idle.
        if (!IsTimelinePlaying())
        {
            return false;
        }

        int characterId = characterRoot.GetInstanceID();
        CharacterFrameState cached;
        if (CharacterStates.TryGetValue(characterId, out cached) &&
            cached.Frame == Time.frameCount)
        {
            return cached.YieldRotations;
        }

        bool result = Detect(characterRoot);
        CharacterStates[characterId] = new CharacterFrameState
        {
            Frame = Time.frameCount,
            YieldRotations = result
        };
        return result;
    }

    public static bool IsTimelinePlaying()
    {
        int frame = Time.frameCount;
        if (_timelinePlayingFrame == frame)
        {
            return _timelinePlaying;
        }

        _timelinePlayingFrame = frame;
        ResolveMetadata();
        if (_timelineIsPlaying == null)
        {
            _timelinePlaying = false;
            return _timelinePlaying;
        }
        try
        {
            object value = _timelineIsPlaying.GetValue(null, null);
            _timelinePlaying = value is bool && (bool)value;
        }
        catch
        {
            // Timeline may not have completed Awake yet. A later frame can retry.
            _timelinePlaying = false;
        }
        return _timelinePlaying;
    }

    private static bool Detect(Transform characterRoot)
    {
        ResolveMetadata();
        if (!_available || _scanDisabled || !IsTimelinePlaying())
        {
            return false;
        }

        try
        {
            object nodesInstance = _nodesSelf.GetValue(null);
            if (nodesInstance == null)
            {
                return false;
            }
            IList constraints = _constraints.GetValue(nodesInstance) as IList;
            if (constraints == null)
            {
                return false;
            }

            for (int i = 0; i < constraints.Count; i++)
            {
                object constraint = constraints[i];
                if (constraint == null)
                {
                    continue;
                }
                if (!ResolveConstraintFields(constraint.GetType()))
                {
                    return false;
                }
                object enabledValue = _constraintEnabled.GetValue(constraint);
                if (!(enabledValue is bool) || !(bool)enabledValue)
                {
                    continue;
                }
                Transform parent = _constraintParentTransform.GetValue(constraint) as Transform;
                Transform child = _constraintChildTransform.GetValue(constraint) as Transform;
                if (BelongsToCharacter(parent, characterRoot) ||
                    BelongsToCharacter(child, characterRoot))
                {
                    return FleshSolverMath.ShouldYieldConstraintRotation(
                        timelinePlaying: true, activeCharacterConstraint: true);
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            DisableScan("constraint scan failed", ex);
            return false;
        }
    }

    private static void ResolveMetadata()
    {
        if (_metadataResolved)
        {
            return;
        }
        _metadataResolved = true;

        Type timelineType = FindLoadedType("Timeline.Timeline",
            "Timeline.Timeline,Timeline");
        Type nodesType = FindLoadedType("NodesConstraints.NodesConstraints",
            "NodesConstraints.NodesConstraints,NodesConstraints");
        if (timelineType != null)
        {
            _timelineIsPlaying = timelineType.GetProperty("isPlaying",
                BindingFlags.Public | BindingFlags.Static);
        }
        if (_timelineIsPlaying == null)
        {
            WarnOnce("Timeline playback metadata was unavailable");
            return;
        }
        if (nodesType == null)
        {
            return;
        }
        _nodesSelf = nodesType.GetField("_self",
            BindingFlags.NonPublic | BindingFlags.Static);
        _constraints = nodesType.GetField("_constraints",
            BindingFlags.NonPublic | BindingFlags.Instance);
        _available = _timelineIsPlaying != null && _nodesSelf != null &&
                     _constraints != null;
        if (!_available)
        {
            WarnOnce("optional Timeline/NodesConstraints metadata was incomplete; " +
                     "constraint-safe Chain rotation is unavailable");
        }
    }

    private static Type FindLoadedType(string fullName, string qualifiedName)
    {
        Type type = Type.GetType(qualifiedName, false);
        if (type != null)
        {
            return type;
        }
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            type = assemblies[i].GetType(fullName, false);
            if (type != null)
            {
                return type;
            }
        }
        return null;
    }

    private static bool ResolveConstraintFields(Type type)
    {
        if (_constraintType == type && _constraintEnabled != null &&
            _constraintParentTransform != null && _constraintChildTransform != null)
        {
            return true;
        }

        _constraintType = type;
        _constraintEnabled = type.GetField("enabled",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        _constraintParentTransform = type.GetField("parentTransform",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        _constraintChildTransform = type.GetField("childTransform",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (_constraintEnabled != null && _constraintParentTransform != null &&
            _constraintChildTransform != null)
        {
            return true;
        }

        DisableScan("constraint fields were not compatible", null);
        return false;
    }

    private static bool BelongsToCharacter(Transform candidate, Transform characterRoot)
    {
        return candidate != null &&
               (candidate == characterRoot || candidate.IsChildOf(characterRoot));
    }

    private static void DisableScan(string reason, Exception ex)
    {
        _scanDisabled = true;
        WarnOnce("optional Timeline/NodesConstraints bridge disabled: " + reason +
                 (ex == null ? "" : " (" + ex.GetType().Name + ": " + ex.Message + ")"));
    }

    private static void WarnOnce(string message)
    {
        if (_warningLogged)
        {
            return;
        }
        _warningLogged = true;
        UnityEngine.Debug.LogWarning("SOMA_CHAIN_CONSTRAINT_SAFE " + message);
    }
}
