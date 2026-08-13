using System;
using UnityEngine;

namespace ThighPhysicsController;

/// <summary>
/// Bridge for the main game's H scenes (free-H included). The H animation
/// drives limb poses continuously, so Soma must yield all rotation output
/// there (position-based flesh sway stays) or the chain's RC aim keeps the
/// legs twisted. Detection is by Unity scene name (H / HProc / FreeH /
/// HPointMove / HSceneResult / FreeHCharaSelect*) with a FreeHScene loader
/// fallback; no hard dependency on the H scene types.
/// </summary>
internal static class HSceneBridge
{
    private static bool _resolved;
    private static Type _freeHSceneType;
    private static Type _hSceneProcType;
    private static Type _hSpriteType;
    private static int _cachedFrame = -1;
    private static bool _cachedActive;
    private static bool _lastActive;
    private static bool _loggedTransition;
    private static string _lastSceneName = "";

    public static bool IsFreeHActive()
    {
        if (Time.frameCount == _cachedFrame)
        {
            return _cachedActive;
        }
        _cachedFrame = Time.frameCount;
        _cachedActive = Detect();
        if (_cachedActive != _lastActive || !_loggedTransition)
        {
            _lastActive = _cachedActive;
            _loggedTransition = true;
            UnityEngine.Debug.Log("SOMA_HSCENE_DETECT active=" + _cachedActive +
                                  " scene=" + _lastSceneName);
        }
        return _cachedActive;
    }

    private static bool Detect()
    {
        try
        {
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (sceneName != _lastSceneName)
            {
                _lastSceneName = sceneName;
                UnityEngine.Debug.Log("SOMA_HSCENE_DETECT scene changed to=" + sceneName);
            }
            if (sceneName == "H" || sceneName == "HProc" || sceneName == "FreeH" ||
                sceneName == "HPointMove" || sceneName == "HSceneResult" ||
                sceneName.StartsWith("FreeHCharaSelect"))
            {
                return true;
            }
        }
        catch
        {
            // SceneManager can throw while a scene transition is in flight.
        }
        if (!_resolved)
        {
            _resolved = true;
            _freeHSceneType = Type.GetType("FreeHScene, Assembly-CSharp", false);
            _hSceneProcType = Type.GetType("HSceneProc, Assembly-CSharp", false);
            _hSpriteType = Type.GetType("HSprite, Assembly-CSharp", false);
        }
        try
        {
            if ((_freeHSceneType != null &&
                 UnityEngine.Object.FindObjectOfType(_freeHSceneType) != null) ||
                (_hSceneProcType != null &&
                 UnityEngine.Object.FindObjectOfType(_hSceneProcType) != null) ||
                (_hSpriteType != null &&
                 UnityEngine.Object.FindObjectOfType(_hSpriteType) != null))
            {
                return true;
            }
        }
        catch
        {
            return false;
        }
        return false;
    }
}
