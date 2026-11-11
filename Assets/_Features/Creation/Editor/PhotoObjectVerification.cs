using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ecaty.Agents;
using Ecaty.Agents.Providers.Gemini;
using Ecaty.Agents.Security;
using Ecaty.Flecs;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Ecaty.Editor
{
    public static class PhotoObjectVerification
    {
        public static void Run()
        {
            string save = Path.Combine(Path.GetTempPath(), "EcatyPhoto-" + Guid.NewGuid().ToString("N") + ".json");
            var report = new JObject { ["passed"] = false };
            GameObject root = null;
            try
            {
                const string recipe = @"{""version"":3,""parts"":[
                    {""id"":""shell"",""parent"":null,""shape"":""pivot"",""position"":[0,0,0]},
                    {""id"":""body"",""parent"":""shell"",""shape"":""ellipsoid"",""position"":[0,0,0],""size"":[1,0.65,1],""rotation"":[0,0,0],""color"":[0.8,0.4,0.2]},
                    {""id"":""upper"",""parent"":""shell"",""shape"":""pivot"",""position"":[0.42,0,0],""rotation"":[0,0,-12]},
                    {""id"":""knee"",""parent"":""upper"",""shape"":""pivot"",""position"":[0.8,-0.07,0]},
                    {""id"":""lower"",""parent"":""knee"",""shape"":""box"",""position"":[0.97,-0.2,0],""size"":[0.5,0.22,0.23],""rotation"":[0,0,-30],""color"":[0.2,0.5,0.8]},
                    {""id"":""foot"",""parent"":""knee"",""shape"":""pivot"",""position"":[1.14,-0.34,0]},
                    {""id"":""toe"",""parent"":""foot"",""shape"":""ellipsoid"",""position"":[1.22,-0.36,0],""size"":[0.3,0.18,0.23],""rotation"":[0,0,0],""color"":[0.8,0.4,0.2]}]}";
                root = new GameObject("Live photo verification");
                var world = root.AddComponent<FlecsWorld>();
                world.Initialize(0);
                var view = root.AddComponent<ClayCreation>();
                Require(root.transform.childCount == 0 && ((JArray)JObject.Parse(view.Inspect())["parts"]).Count == 0,
                    "A new photo adapter restored a previous object.");
                var applied = JObject.Parse(view.Apply(recipe, save));
                Require((int)applied["brushes"] == 3 && (int)applied["pivots"] == 4, "Incorrect live brush counts.");

                var surface = root.transform.Find("PhotoClay");
                var shell = surface.Find("part_shell");
                var upper = shell.Find("part_upper");
                var knee = upper.Find("part_knee");
                var foot = knee.Find("part_foot");
                var toe = foot.Find("part_toe");
                Require(knee.Find("part_lower") && toe && !root.GetComponentInChildren<SkinnedMeshRenderer>() &&
                    !root.GetComponentInChildren<MeshFilter>(), "Expected one brush hierarchy with live rendering.");
                const string toePath = "PhotoClay.part_shell.part_upper.part_knee.part_foot.part_toe";
                var before = JObject.Parse(world.Session.EntityJson(toePath));
                Require(before["components"]?["ClayStyle"] != null, "Flecs did not contain the brush in its actual parent hierarchy.");
                world.SyncViews();
                Require(root.transform.childCount == 1, "The generic Flecs view made a second photo hierarchy.");

                var mud = surface.GetComponent<RuntimeMudRenderer>();
                Require(((Behaviour)mud).isActiveAndEnabled, "MudBun was not rendering live.");
                var material = mud.GetType().GetField("RenderMaterialMesh").GetValue(mud) as Material;
                Require(material && material.shader.isSupported, "Live MudBun material missing.");
                typeof(MudBun.MudRendererBase).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(mud, null);
                Require((int)mud.GetType().GetProperty("NumVerticesGenerated").GetValue(mud) > 0, "Live MudBun generated no geometry.");
                var restBounds = SnapshotBounds(mud);
                var restToe = toe.position;
                knee.localRotation *= Quaternion.Euler(0, 0, -65);
                world.SyncViews();
                var posedToe = toe.position;
                var posedBounds = SnapshotBounds(mud);
                Require(Vector3.Distance(restToe, posedToe) > .2f && Vector3.Distance(restBounds.size, posedBounds.size) > .08f,
                    "Moving the pivot did not change child brushes and generated geometry.");

                var edited = JObject.Parse(recipe);
                edited["parts"][1]["color"] = new JArray(0.1, 0.8, 0.4);
                view.Apply(edited.ToString(), save);
                Require(JToken.DeepEquals(before["id"], JObject.Parse(world.Session.EntityJson(toePath))["id"]) &&
                    foot.Find("part_toe") == toe && Vector3.Distance(toe.position, posedToe) < .001f,
                    "A material edit replaced the brush or reset the animation pose.");

                var undersized = (JObject)edited.DeepClone();
                undersized["parts"][1]["size"][2] = 0.045;
                undersized["parts"][6]["size"][0] = 0.085;
                string sizeError = null;
                try
                {
                    view.Apply(undersized.ToString(), null);
                }
                catch (ArgumentException error) { sizeError = error.Message; }
                Require(sizeError != null && sizeError.Contains("body.size.z=0.045") && sizeError.Contains("toe.size.x=0.085") &&
                    JToken.DeepEquals(edited, JObject.Parse(view.Inspect())), "Validation lost rejected brush dimensions or changed the object.");

                var cyclic = (JObject)edited.DeepClone();
                cyclic["parts"][3]["parent"] = "foot";
                bool rejected = false;
                try
                {
                    view.Apply(cyclic.ToString(), null);
                }
                catch (ArgumentException) { rejected = true; }
                Require(rejected && JToken.DeepEquals(edited, JObject.Parse(view.Inspect())), "A cyclic recipe changed the object.");
                report["fimpossibleBinding"] = VerifyAnimatorReferences(surface, shell, upper, knee, foot);

                var reparented = (JObject)edited.DeepClone();
                reparented["parts"][6]["parent"] = "shell";
                view.Apply(reparented.ToString(), null);
                Require(shell.Find("part_toe") == toe &&
                    JObject.Parse(world.Session.EntityJson("PhotoClay.part_shell.part_toe"))["components"]?["ClayStyle"] != null,
                    "Reparenting did not update both hierarchies.");
                UnityEngine.Object.DestroyImmediate(root);
                root = new GameObject("Explicit photo reload verification");
                world = root.AddComponent<FlecsWorld>();
                world.Initialize(0);
                view = root.AddComponent<ClayCreation>();
                Require(root.transform.childCount == 0, "A new session contained the saved character.");
                view.Apply(File.ReadAllText(save), null);
                Require(JToken.DeepEquals(edited, JObject.Parse(view.Inspect())) &&
                    root.transform.Find("PhotoClay/part_shell/part_upper/part_knee/part_foot/part_toe"),
                    "Explicit reload lost the brush hierarchy.");

                const string legacy = @"{""version"":2,""parts"":[
                    {""id"":""body"",""shape"":""ellipsoid"",""position"":[0,0,0],""size"":[1,1,1],""rotation"":[0,0,0],""color"":[1,0,0]}],
                    ""bones"":[{""id"":""root"",""parent"":null,""position"":[0,0,0],""radius"":0.5}]}";
                view.Apply(legacy, null);
                Require(root.transform.Find("PhotoClay/part_pivot_0/part_body"), "Explicit import of an old rig failed.");
                var legacyFlat = JObject.Parse(legacy);
                legacyFlat["version"] = 1;
                legacyFlat.Remove("bones");
                view.Apply(legacyFlat.ToString(), null);
                Require(root.transform.Find("PhotoClay/part_body"), "Explicit import of an old flat recipe failed.");

                VerifyLocalRecipe(view, root.transform);
                VerifyImageRequest();
                report["graphics"] = SystemInfo.graphicsDeviceType.ToString();
                report["passed"] = true;
                report["checks"] = new JArray("single Flecs and Unity brush tree with live MudBun vertices",
                    "pivot moves visible child brushes and changes generated geometry", "material edits preserve active pose",
                    "invalid sizes name all affected axes, invalid cycle rejected, reparent preserves Unity brush", "fresh session empty, explicit current and legacy import works",
                    "parent-local pivots and flat dye with matching surface bounds",
                    "Gemini JPEG request through synthetic transport");
            }
            catch (Exception ex) { report["error"] = ex.ToString(); throw; }
            finally
            {
                if (root)
                    UnityEngine.Object.DestroyImmediate(root);
                if (File.Exists(save))
                    File.Delete(save);
                File.WriteAllText("Verification/photo-object-checks.json", report.ToString());
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void VerifyLocalRecipe(ClayCreation view, Transform root)
        {
            var recipe = JObject.Parse(@"{
                ""version"":4,""parts"":[
                    {""id"":""root"",""parent"":null,""shape"":""pivot"",""localPosition"":[0,0.4,0]},
                    {""id"":""head"",""parent"":""root"",""shape"":""pivot"",""localPosition"":[0.42,0.55,0],""localRotation"":[0,0,20]},
                    {""id"":""shell"",""parent"":""head"",""shape"":""ellipsoid"",""localPosition"":[0,0.15,0],""localRotation"":[0,0,0],""size"":[1,1,1],""color"":[0.2,0.4,0.7]}]}");
            view.Apply(recipe.ToString(), null);
            var surface = root.Find("PhotoClay");
            var head = surface.Find("part_root/part_head");
            var shell = head.Find("part_shell");
            Require(Vector3.Distance(head.localPosition, new Vector3(.42f, .55f, 0)) < .001f &&
                Vector3.Distance(shell.position, head.TransformPoint(0, .15f, 0)) < .001f,
                "The local offsets of the photo recipe were not preserved.");
            var mud = surface.GetComponent<RuntimeMudRenderer>();
            var before = SnapshotBounds(mud);

            var paint = JObject.Parse(@"{
                ""id"":""ink"",""parent"":""head"",""shape"":""ellipsoid"",""operation"":""dye"",
                ""localPosition"":[0,0.15,-0.46],""localRotation"":[0,0,0],""size"":[0.38,0.32,0.24],
                ""color"":[1,0,0],""blend"":0.01}");
            ((JArray)recipe["parts"]).Insert(2, paint);
            view.Apply(recipe.ToString(), null);
            ((JArray)recipe["parts"]).Remove(paint);
            ((JArray)recipe["parts"]).Add(paint);
            view.Apply(recipe.ToString(), null);
            var ink = head.Find("part_ink");
            Require(ink.GetSiblingIndex() > shell.GetSiblingIndex(), "Recipe paint order was not applied.");
            var after = SnapshotBounds(mud, colors => Require(colors.Any(c => c.r > .7f && c.r > c.g + .3f),
                "Dye did not change the visible surface color."));
            Require(Vector3.Distance(before.min, after.min) < .01f && Vector3.Distance(before.max, after.max) < .01f,
                "A flat marking added geometry to the surface.");
        }

        private static Bounds SnapshotBounds(Component mud, Action<Color[]> inspectColors = null)
        {
            // Read back a temporary mesh for the test while the product uses live rendering.
            var meshType = typeof(MudBun.MudRendererBase).GetNestedType("GeneratedMeshType");
            var generate = mud.GetType().GetMethod("GenerateMesh", new[] {
                meshType, typeof(bool), typeof(Mesh), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
            });
            var snapshot = (Mesh)generate.Invoke(mud, new object[] {
                Enum.Parse(meshType, "Standard"), false, null, false, false, false, false
            });
            try
            {
                Require(snapshot && snapshot.vertexCount > 0, "MudBun readback was empty.");
                inspectColors?.Invoke(snapshot.colors);
                return snapshot.bounds;
            }
            finally { if (snapshot) UnityEngine.Object.DestroyImmediate(snapshot); }
        }

        private static bool VerifyAnimatorReferences(Transform surface, Transform shell, Transform upper, Transform knee, Transform foot)
        {
            Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
            var legsType = Find("FIMSpace.FProceduralAnimation.LegsAnimator");
            var leaningType = Find("FIMSpace.LeaningAnimator");
            if (legsType == null || leaningType == null)
                return false;
            var setup = new GameObject("Optional animation bindings");
            setup.SetActive(false);
            try
            {
                var legs = setup.AddComponent(legsType);
                legsType.GetField("Hips").SetValue(legs, shell);
                legsType.GetMethod("Legs_AddNewLeg").Invoke(legs, null);
                var leg = ((IList)legsType.GetField("Legs").GetValue(legs))[0];
                leg.GetType().GetField("BoneStart").SetValue(leg, upper);
                leg.GetType().GetField("BoneMid").SetValue(leg, knee);
                leg.GetType().GetField("BoneEnd").SetValue(leg, foot);
                var leaning = setup.AddComponent(leaningType);
                var parameters = leaningType.GetProperty("Parameters").GetValue(leaning);
                parameters.GetType().GetField("BaseTransform").SetValue(parameters, surface);
                parameters.GetType().GetField("SpineStart").SetValue(parameters, shell);
                Require((Transform)legsType.GetField("Hips").GetValue(legs) == shell &&
                    (Transform)leg.GetType().GetField("BoneEnd").GetValue(leg) == foot &&
                    (Transform)parameters.GetType().GetField("SpineStart").GetValue(parameters) == shell,
                    "FImpossible could not reference the brush pivots.");
                return true;
            }
            finally { UnityEngine.Object.DestroyImmediate(setup); }
        }

        private static void VerifyImageRequest()
        {
            var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            byte[] jpeg;
            try
            {
                image.SetPixels(new[] { Color.red, Color.blue, Color.white, Color.black });
                image.Apply();
                jpeg = ImageConversion.EncodeToJPG(image);
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
            using (var handler = new ImageFixture(jpeg))
            using (var http = new HttpClient(handler))
            using (var provider = new GeminiProvider(new FixtureStore(), http))
            {
                var request = new AgentRequest("", new[] { new AgentMessage(AgentRole.User, "Make it shiny silver.") }, null, jpeg);
                string result = provider.ReplyAsync(request, null, CancellationToken.None).GetAwaiter().GetResult();
                if (!handler.Verified || result != "Fixture complete.")
                    throw new InvalidOperationException("The Gemini image request did not complete.");
            }
        }

        private sealed class FixtureStore : ISecretStore
        {
            public byte[] Load() => Encoding.UTF8.GetBytes("{\"key\":\"synthetic-fixture\"}");
            public void Save(byte[] data) => throw new NotSupportedException();
        }

        private sealed class ImageFixture : HttpMessageHandler
        {
            private readonly string expected;
            public bool Verified
            {
                get; private set;
            }
            public ImageFixture(byte[] jpeg)
            {
                expected = Convert.ToBase64String(jpeg);
            }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                var payload = JObject.Parse(await request.Content.ReadAsStringAsync().ConfigureAwait(false));
                var parts = payload["contents"]?[0]?["parts"];
                Verified = (string)parts?[0]?["text"] == "Make it shiny silver." &&
                    (string)parts?[1]?["inlineData"]?["mimeType"] == "image/jpeg" &&
                    (string)parts?[1]?["inlineData"]?["data"] == expected;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                    "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"Fixture complete.\"}]}}]}")
                };
            }
        }
    }
}
