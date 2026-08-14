using System.Collections.Generic;

namespace ExtensibleSaveFormat
{
    public sealed class PluginData
    {
        public Dictionary<string, object> data = new Dictionary<string, object>();
    }
}

namespace UnityEngine
{
    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public float magnitude => (float)System.Math.Sqrt(x * x + y * y + z * z);

        public static Vector3 operator +(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        public static Vector3 operator -(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        }

        public static Vector3 operator *(Vector3 value, float scale)
        {
            return new Vector3(value.x * scale, value.y * scale, value.z * scale);
        }

        public static Vector3 ClampMagnitude(Vector3 value, float maxLength)
        {
            float length = value.magnitude;
            return length > maxLength && length > 0f
                ? value * (maxLength / length)
                : value;
        }
    }

    public static class Mathf
    {
        public static float Clamp(float value, float min, float max)
        {
            return value < min ? min : value > max ? max : value;
        }

        public static float Clamp01(float value)
        {
            return Clamp(value, 0f, 1f);
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * Clamp01(t);
        }

        public static float InverseLerp(float a, float b, float value)
        {
            if (a == b)
            {
                return 0f;
            }
            return Clamp01((value - a) / (b - a));
        }
    }
}
