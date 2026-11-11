using System;
using System.IO;
using System.Linq;
using Ecaty.Agents.UI;
using Ecaty.Flecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Ecaty.Editor
{
    // Manual, opt-in real-provider check. Requires the owner to connect in the app first.
    public static class LiveProviderVerification
    {
        public static bool Running
        {
            get; private set;
        }
        public static async void Start()
        {
            if (Running)
                return;
            Running = true;
            var report = new JObject { ["passed"] = false, ["transport"] = "live service", ["checkedAt"] = DateTimeOffset.UtcNow.ToString("O") };
            try
            {
                var panel = UnityEngine.Object.FindAnyObjectByType<AgentChatPanel>();
                if (!panel || !panel.Controller.IsConnected || panel.Controller.IsBusy)
                    throw new InvalidOperationException("Connect a provider in Play mode before starting this check.");
                report["provider"] = panel.Controller.ProviderId;
                var reply = await panel.Controller.SendAsync("Make the cube blue and twice its original size. Leave the sphere unchanged.");
                if (reply == null)
                    throw new InvalidOperationException("Live request failed. Check the app's connection message; no response content is saved here.");
                var world = UnityEngine.Object.FindAnyObjectByType<FlecsWorld>();
                world.Session.ReadViews(out var views);
                var cube = views.Take(world.ViewCount).Single(v => world.Session.EntityName(v.Entity) == "Cube");
                var actual = world.FindView(cube.Entity);
                var block = new MaterialPropertyBlock();
                actual.GetComponentInChildren<Renderer>().GetPropertyBlock(block);
                if (Vector3.Distance(actual.transform.localScale, Vector3.one * 2) > .001f || block.GetColor("_BaseColor").b < .9f || block.GetColor("_BaseColor").r > .15f)
                    throw new InvalidOperationException("The provider replied, but the actual cube did not match the requested change.");
                if (!world.Session.Query("Presented").Contains("scale"))
                    throw new InvalidOperationException("Presentation readback missing.");
                report["passed"] = true;
                report["checks"] = new JArray("owner-authorized live inference", "actual rendered cube color/scale", "native Presented readback");
            }
            catch (Exception e) { report["error"] = e is InvalidOperationException ? e.Message : "Live check failed; inspect local developer diagnostics."; }
            finally
            {
                File.WriteAllText("Verification/live-provider-checks.json", report.ToString(Formatting.Indented));
                Running = false;
            }
        }
    }
}
