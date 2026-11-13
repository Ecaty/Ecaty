#if CAP_FLEXALON
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Game
{
    // Disposable presentation binding for one authoritative native gallery record.
    public sealed class GalleryPreviewEntry
    {
        public string Id;
        public int Order, Width, Depth, Kind;
        public bool Active;
        public Transform Anchor, Content;
        public GalleryMockPreview Preview;
        public JArray SavedFeatures;
        public float TableScale = 1;
        public Bounds NaturalBounds;
    }
}
#endif
