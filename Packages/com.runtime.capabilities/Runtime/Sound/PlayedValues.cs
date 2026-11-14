using System.Runtime.InteropServices;

namespace Runtime.Capabilities
{
    // Managed mirror of the native Played component (Native/Core/src/Sound.cpp). A rule's plays verb adds one to the count.
    [StructLayout(LayoutKind.Sequential)]
    public struct PlayedValues
    {
        public int count;
    }
}
