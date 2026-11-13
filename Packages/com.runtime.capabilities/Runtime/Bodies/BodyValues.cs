using System.Runtime.InteropServices;
using UnityEngine;

namespace Runtime.Capabilities
{
    // Managed mirrors of the native places components (Native/Core/src/Places.cpp). FlecsSession checks the size and every
    // field offset against the native layout the first time a mirror is read or written.
    [StructLayout(LayoutKind.Sequential)]
    public struct Float3
    {
        public float x, y, z;

        public Vector3 ToVector() => new Vector3(x, y, z);
    }

    /// <summary>Free: a body that moves. Density is relative to a fluid's density (1 is water).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FreeValues
    {
        public float density;
    }

    /// <summary>
    /// The forces a free body gets from the places it is inside. The native struct has 4 bytes of padding before the
    /// 64-bit volume, so the offsets are explicit. Volume is the Float place the body is in, or 0.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct EffectiveForceValues
    {
        [FieldOffset(0)] public Float3 gravity;
        [FieldOffset(12)] public Float3 wind;
        [FieldOffset(24)] public float drag;
        [FieldOffset(28)] public float fluid;
        [FieldOffset(32)] public float density;
        [FieldOffset(40)] public ulong volume;

        public static bool Same(in EffectiveForceValues a, in EffectiveForceValues b) =>
            a.gravity.x == b.gravity.x && a.gravity.y == b.gravity.y && a.gravity.z == b.gravity.z
            && a.wind.x == b.wind.x && a.wind.y == b.wind.y && a.wind.z == b.wind.z
            && a.drag == b.drag && a.fluid == b.fluid && a.density == b.density && a.volume == b.volume;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PositionValues
    {
        public float x, y, z;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OrientationValues
    {
        public float x, y, z, w;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PrimitiveValues
    {
        public int kind;
    }
}
