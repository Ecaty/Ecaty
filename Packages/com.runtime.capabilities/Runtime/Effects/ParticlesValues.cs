using System.Runtime.InteropServices;

namespace Runtime.Capabilities
{
    // Managed mirror of the native Particles component (Native/Core/src/Places.cpp). FlecsSession checks the size and every
    // field offset against the native layout the first time it is read.
    [StructLayout(LayoutKind.Sequential)]
    public struct ParticlesValues
    {
        /// <summary>Particles per second.</summary>
        public float rate;
        /// <summary>Seconds a particle lives.</summary>
        public float life;
    }
}
