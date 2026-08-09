using System;

namespace ThighPhysicsController;

/// <summary>
/// Single boundary for values coming from cards, XML, config, or text fields.
/// Unity's Mathf.Clamp deliberately returns NaN unchanged, so clamping alone is
/// not enough to protect a physics solver from non-finite external data.
/// </summary>
internal static class FleshValue
{
    public static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public static float Clamp(float value, float min, float max, float fallback)
    {
        if (!IsFinite(fallback))
        {
            fallback = min;
        }
        if (!IsFinite(value))
        {
            value = fallback;
        }
        return value < min ? min : value > max ? max : value;
    }

    public static float ConvertClamped(object value, float min, float max, float fallback)
    {
        try
        {
            return Clamp(Convert.ToSingle(value), min, max, fallback);
        }
        catch (Exception)
        {
            return Clamp(fallback, min, max, min);
        }
    }

    public static bool ConvertBoolean(object value, bool fallback)
    {
        try
        {
            return Convert.ToBoolean(value);
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    public static int ConvertInt32(object value, int fallback)
    {
        try
        {
            return Convert.ToInt32(value);
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
