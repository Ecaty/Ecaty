using Ecaty.Flecs;
using System;
using System.IO;
using UnityEngine;

namespace Ecaty
{
    /// <summary>Opt-in standalone-player verification; inactive in normal use.</summary>
    public sealed class PlayerSmoke : MonoBehaviour
    {
        private int frames;
        private ulong testEntity, reactiveEntity;
        private float initialScale;
        private bool sawReactiveScale;
        private Quaternion startRotation;
        private FlecsWorld host;
        private void Update()
        {
            try
            {
                frames++;
                if (frames == 2)
                {
                    host = GetComponent<FlecsWorld>();
                    if (!UnityEngine.Object.FindAnyObjectByType<Ecaty.Agents.UI.AgentChatPanel>())
                        throw new Exception("Chat panel did not mount");
                    var renderer = UnityEngine.Object.FindAnyObjectByType<UnityEngine.UIElements.PanelRenderer>();
                    if (!renderer)
                        throw new Exception("PanelRenderer missing in player");
                    UnityEngine.UIElements.VisualElement ui = null;
                    UnityEngine.UIElements.PanelRenderer.VersionedUIReloadCallback capture = (r, tree, version) => ui = tree;
                    renderer.RegisterUIReloadCallback(capture);
                    renderer.UnregisterUIReloadCallback(capture);
                    if (ui == null || UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(ui, "composer") == null)
                        throw new Exception("Player chat UI was not attached through the reload callback");
                    if (UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Button>(ui, "provider-openrouter") == null ||
                        UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Button>(ui, "provider-chatgpt") == null ||
                        UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Button>(ui, "provider-gemini") == null)
                        throw new Exception("Provider account options missing in player");
                    var keyInput = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.TextField>(ui, "apiKeyInput");
                    if (keyInput == null || !keyInput.isPasswordField)
                        throw new Exception("Masked key input missing in player");
                    if (UnityEngine.Object.FindObjectsByType<TextMesh>().Length != 0)
                        throw new Exception("Unexpected world-space greeting");
                    var icons = Resources.Load<Font>("MaterialSymbolsRounded");
                    if (!icons || !icons.HasCharacter('\ue163'))
                        throw new Exception("Bundled icon font missing in player");
                    var browser = Ecaty.Agents.Diagnostics.AgentTraceBrowser.Current;
                    bool traceExpected = Debug.isDebugBuild;
                    if ((browser && browser.Server != null) != traceExpected || Ecaty.Agents.Diagnostics.AgentTrace.Enabled != traceExpected)
                        throw new Exception("Development-only browser trace gating failed");
                    if (host.Session == null || host.ViewCount != 3)
                        throw new Exception("Initial world not running");
                    host.Session.RunScript("RuntimeCheck { Position: {2,0,0}; Rotation: {0,0,0}; Spin: {0,90,0}; Primitive: {1} }");
                    host.SyncViews();
                    int count = host.Session.ReadViews(out var views);
                    for (int i = 0; i < count; i++)
                    if (host.Session.EntityName(views[i].Entity) == "RuntimeCheck")
                        testEntity = views[i].Entity;
                    if (testEntity == 0)
                        throw new Exception("Runtime entity missing");
                    startRotation = host.FindView(testEntity).transform.localRotation;
                    for (int i = 0; i < count; i++)
                    if (host.Session.EntityName(views[i].Entity) == "Sphere")
                        reactiveEntity = views[i].Entity;
                    initialScale = host.FindView(reactiveEntity).transform.localScale.x;
                    host.Session.SetManagedScript(EcatyAgentTools.BehaviorEntity, Resources.Load<TextAsset>("Runtime/Behaviors/ReactiveBehavior").text);
                }
                if (frames > 2 && host && host.FindView(reactiveEntity))
                    sawReactiveScale |= Math.Abs(host.FindView(reactiveEntity).transform.localScale.x - initialScale) > .0001f;
                if (frames < 80)
                    return;
                if (!sawReactiveScale)
                    throw new Exception("Reactive Flecs Script did not animate in the player");
                var view = host.FindView(testEntity);
                if (!view || view.transform.localPosition.x != 2 || Quaternion.Angle(startRotation, view.transform.localRotation) < 0.01f)
                    throw new Exception("Player Update/native system/Unity presentation did not advance");
                if (!host.Session.Query("Presented").Contains("frame"))
                    throw new Exception("Player presentation readback missing");
                Finish(true, "80 advancing frames; native Script/rotation, upstream reactive scale, Presented readback, PanelRenderer callback/chat, three provider accounts/masked key form, development-only browser trace gating, no greeting and bundled Google icons");
            }
            catch (Exception ex) { Finish(false, ex.ToString()); }
        }
        private void Finish(bool success, string detail)
        {
            enabled = false;
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-flecsSmokeResult");
            if (index >= 0 && index + 1 < args.Length)
                File.WriteAllText(args[index + 1], JsonUtility.ToJson(new Result { passed = success, frames = frames, detail = detail }, true));
            Debug.Log("FLECS_PLAYER_SMOKE=" + (success ? "PASS" : "FAIL") + " " + detail);
            Application.Quit(success ? 0 : 1);
        }
        [Serializable]
        private sealed class Result
        {
            public bool passed; public int frames; public string detail;
        }
    }
}
