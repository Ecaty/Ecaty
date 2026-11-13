using Runtime.Agents.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Editor
{
    public static class ChatLoopSetup
    {
        public static void Prepare()
        {
            const string folder = "Assets/__Common/Agent/";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/__Common/Agent/Presentation/PanelSettings.asset");
            if (!panel)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, "Assets/__Common/Agent/Presentation/PanelSettings.asset");
            }
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/__Common/Theme/Styles/Theme.tss");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1280, 720);
            EditorUtility.SetDirty(panel);
            var assets = AssetDatabase.LoadAssetAtPath<AgentUiAssets>(folder + "Agent.asset");
            if (!assets)
            {
                assets = ScriptableObject.CreateInstance<AgentUiAssets>();
                AssetDatabase.CreateAsset(assets, folder + "Agent.asset");
            }
            assets.Layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.runtime.ai-agent/Runtime/UI/AgentChat.uxml");
            assets.Panel = panel;
            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssetIfDirty(panel);
            AssetDatabase.SaveAssetIfDirty(assets);
        }
    }
}
