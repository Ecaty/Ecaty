using Ecaty.Agents.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ecaty.Editor
{
    public static class ChatLoopSetup
    {
        public static void Prepare()
        {
            const string folder = "Assets/Ecaty/Resources/";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(folder + "EcatyAgentPanel.asset");
            if (!panel) { panel = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(panel, folder + "EcatyAgentPanel.asset"); }
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/Ecaty/UI/EcatyTheme.tss");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1280, 720);
            panel.sortingOrder = 100;
            EditorUtility.SetDirty(panel);
            var assets = AssetDatabase.LoadAssetAtPath<AgentUiAssets>(folder + "EcatyAgentUi.asset");
            if (!assets) { assets = ScriptableObject.CreateInstance<AgentUiAssets>(); AssetDatabase.CreateAsset(assets, folder + "EcatyAgentUi.asset"); }
            assets.Layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.ecaty.ai-agent/Runtime/UI/AgentChat.uxml");
            assets.Panel = panel;
            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
        }
    }
}
