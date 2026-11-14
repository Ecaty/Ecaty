using System.Runtime.InteropServices;

namespace Runtime.Capabilities
{
    // Managed mirror of the native Sound component (Native/Core/src/Sound.cpp). FlecsSession checks the size and every
    // field offset against the native layout the first time it is read.
    [StructLayout(LayoutKind.Sequential)]
    public struct SoundValues
    {
        /// <summary>The entity that holds the SoundClip.</summary>
        public ulong clip;
        /// <summary>-1 deeper and bigger, +1 higher and smaller.</summary>
        public float size;
        /// <summary>-1 smoother, +1 rougher.</summary>
        public float rough;
    }
}
