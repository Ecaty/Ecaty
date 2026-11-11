using System;
using Ecaty.Agents.UI;
using Ecaty.Flecs.Capabilities;
using UnityEngine;
using UnityEngine.Rendering;
namespace Ecaty
{
    // Application wiring only: actual values/assets remain owned by their modules.
    [CreateAssetMenu(menuName = "Runtime/Composition", fileName = "RuntimeComposition")]
    public sealed class RuntimeComposition : ScriptableObject
    {
        [Tooltip("Generated from Gallery.asset (Editor-only authoring). Players load gallery values from this Flecs Script.")]
        public TextAsset GalleryConfig;
        public EcatyTheme Theme;
        public AgentUiAssets AgentUi;
        public RuntimeDioramaAssets World;
        public CapabilityAssets Capabilities;
        public VolumeProfile WorldLook;
        static RuntimeComposition current;
        public static RuntimeComposition Current
        {
            get
            {
                if (!current)
                    current = Resources.Load<RuntimeComposition>("RuntimeComposition");
                if (!current)
                    throw new InvalidOperationException("Startup composition references are missing.");
                return current;
            }
        }
        // Used by the asset-reference audit. No Inspector reflection or runtime data is stored here.
        public UnityEngine.Object ResolveReference(string field) => field switch
        {
            "GalleryConfig" => GalleryConfig,
            "Theme" => Theme,
            "AgentUi" => AgentUi,
            "AgentPanel" => AgentUi ? AgentUi.Panel : null,
            "World" => World,
            "Capabilities" => Capabilities,
            "WorldLook" => WorldLook,
            _ => throw new ArgumentException("Unknown composition reference: " + field)
        };
    }
}
