using System.Collections.Generic;
using System.Linq;
using Ecaty.Flecs;
using Ecaty.Flecs.Capabilities;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
namespace Ecaty.Tests
{
    // Generic checks over every [Capability] configuration, including optional plugin configurations.
    public sealed class CapabilityContractTests
    {
        // Struct declarations shipped before configurations were typed. Saved sessions contain these exact sources.
        static readonly Dictionary<string, string> Legacy = new()
        {
            ["CapBody"] = "struct CapBody(mass: f32, radius: f32, height: f32, gravity: f32, kinematic: bool)",
            ["CapMaterial"] = "struct CapMaterial(colorR: f32, colorG: f32, colorB: f32, metallic: f32, smoothness: f32, material: string)",
            ["CapParticles"] = "struct CapParticles(rate: f32, lifetime: f32, speed: f32, size: f32, colorR: f32, colorG: f32, colorB: f32)",
            ["CapLight"] = "struct CapLight(kind: i32, intensity: f32, range: f32, colorR: f32, colorG: f32, colorB: f32)",
            ["CapAudio"] = "struct CapAudio(program: i32, note: i32, gain: f32, spatial: f32, pitch: f32, lowPass: f32, trigger: i32, synthesis: i32)",
            ["CapNavigation"] = "struct CapNavigation(command: entity, x: f32, y: f32, z: f32, speed: f32, stopping: f32)",
            ["CapCamera"] = "struct CapCamera(distance: f32, height: f32, priority: i32)",
            ["CapTimeline"] = "struct CapTimeline(duration: f32, playing: bool, time: f32, audio: string)",
            ["CapRig"] = "struct CapRig(root: entity, mid: entity, tip: entity, goal: entity, weight: f32)",
            ["CapVfx"] = "struct CapVfx(asset: string, rate: f32, event: string, trigger: i32)",
            ["CapSpline"] = "struct CapSpline(points: string, width: f32, profile: string, closed: bool, fill: bool)",
            ["CapTerrain"] = "struct CapTerrain(source: entity, points: string, size: f32, resolution: i32, height: f32, falloff: f32)",
            ["CapBuilding"] = "struct CapBuilding(prefab: string, points: string)",
            ["CapVegetation"] = "struct CapVegetation(pipeline: string, seed: i32)",
            ["CapLayout"] = "struct CapLayout(radius: f32, spacing: f32)",
            ["CapFeedback"] = "struct CapFeedback(prefab: string, trigger: i32)",
            ["CapWeather"] = "struct CapWeather(hour: i32, minute: i32, cloud: i32, weather: string)",
            ["CapWater"] = "struct CapWater(level: f32, density: f32)",
            ["CapBuoyancy"] = "struct CapBuoyancy(volume: entity, displacement: f32, drag: f32)",
            ["CapHandle"] = "struct CapHandle(radius: f32, grid: f32, enabled: bool, edit: entity, component: string, index: i32)",
            ["CapSequence"] = "struct CapSequence(pattern: string, bpm: f32, playing: bool)",
            ["CapNavSurface"] = "struct CapNavSurface(radius: f32, height: f32, rebuild: i32)",
            ["CapOcean"] = "struct CapOcean(prefab: string, level: f32)",
            ["CapPostProcessing"] = "struct CapPostProcessing(exposure: f32, contrast: f32, saturation: f32, temperature: f32, bloom: f32, threshold: f32, vignette: f32, tonemap: i32)",
        };
        FlecsSession session;
        [SetUp]
        public void Setup()
        {
            session = new FlecsSession(0);
            CapabilityRuntime.InstallSchema(session);
            session.SetManagedScript("Fixture", "ProbeRef {}\nProbe {}\nRoot {\n    Target {}\n}");
        }
        [TearDown]
        public void Teardown() => session?.Dispose();
        [Test]
        public void GeneratedSchemaMatchesSavedSchema()
        {
            foreach (var pair in Legacy)
            {
                var descriptor = CapabilityCatalog.Find(pair.Key);
                Assert.That(descriptor, Is.Not.Null, pair.Key + " has no [Capability] configuration");
                Assert.That(descriptor.Schema, Is.EqualTo(pair.Value), pair.Key);
            }
            Assert.That(CapabilityCatalog.All.Where(d => d.Instance != null).Select(d => d.Info.Component), Does.Contain("CapBody").And.Contain("CapTerrain").And.Contain("CapPostProcessing"));
        }
        [Test]
        public void CommittedSchemaExportIsCurrentAndLoads()
        {
            // Tools without Unity (Verification/*.py) load this file; it must match the [Capability] classes.
            string text = System.IO.File.ReadAllText(CapabilityCatalog.ExportPath).Replace("\r\n", "\n");
            Assert.That(text, Is.EqualTo(CapabilityCatalog.ExportText()), "Recompile in Unity or run Runtime > Runtime engine > Export capability schema");
            using var probe = new FlecsSession(0);
            probe.SetManagedScript("Contracts", UnityEngine.Resources.Load<UnityEngine.TextAsset>(CapabilityRuntime.SchemaModule).text);
            probe.SetManagedScript("Configurations", text);
            Assert.That(probe.Lookup("CapSpline"), Is.Not.EqualTo(0));
        }
        [Test]
        public void LegacySchemaSessionGainsOnlyMissingConfigurations()
        {
            using var legacy = new FlecsSession(0);
            legacy.SetManagedScript(CapabilityRuntime.SchemaModule, "struct CapBinding(target: entity, enabled: bool, revision: i32)\n" + Legacy["CapLight"]);
            CapabilityRuntime.InstallSchema(legacy);
            Assert.That(legacy.ManagedScriptSource(CapabilityRuntime.SchemaModule + "Additions.CapLight"), Is.Null);
            Assert.That(legacy.Lookup("CapBody"), Is.Not.EqualTo(0));
            Assert.That(legacy.Lookup("CapPostProcessing"), Is.Not.EqualTo(0));
        }
        [Test]
        public void EveryConfigurationRoundTripsThroughFlecs()
        {
            foreach (var descriptor in CapabilityCatalog.All)
            {
                var config = System.Activator.CreateInstance(descriptor.Config);
                int index = 0;
                foreach (var field in CapabilityConfig.Members(descriptor.Config))
                {
                    index++;
                    object value = field.FieldType == typeof(float) ? 1.25f + index
                        : field.FieldType == typeof(int) ? 3 + index
                        : field.FieldType == typeof(bool) ? true
                        : field.FieldType == typeof(ulong) ? (object)(ulong)(7 + index)
                        : descriptor.EntityFields.Contains(field.Name) ? "ProbeRef" : "text" + index;
                    field.SetValue(config, value);
                }
                session.SetComponentJson("Probe", descriptor.Info.Component, CapabilityConfig.Write(config).ToString());
                var json = JObject.Parse(session.ComponentJson(session.Lookup("Probe"), descriptor.Info.Component));
                var read = typeof(CapabilityConfig).GetMethod(nameof(CapabilityConfig.Read)).MakeGenericMethod(descriptor.Config).Invoke(null, new object[] { json });
                foreach (var field in CapabilityConfig.Members(descriptor.Config))
                    Assert.That(field.GetValue(read), Is.EqualTo(field.GetValue(config)), descriptor.Info.Component + "." + field.Name);
            }
        }
        [Test]
        public void MissingMembersUseDeclaredDefaults()
        {
            var light = CapabilityConfig.Read<LightConfig>(new JObject());
            Assert.That(light.intensity, Is.EqualTo(1));
            Assert.That(light.range, Is.EqualTo(10));
            Assert.Throws<System.ArgumentException>(() => CapabilityConfig.Read<LightConfig>(new JObject { ["intensity"] = float.NaN }));
        }
        [Test]
        public void GraphRestoresRuntimeEntitiesDependenciesAndConfiguration()
        {
            session.SetManagedScript("Live", "Root {\n    Road { CapBinding: {target: Root.Target, enabled: true, revision: 1}; CapLight: {2, 3, 4, 1, 1, 1} }\n    Ground { CapBinding: {target: Root.Target, enabled: true, revision: 2}; (CapDependsOn, Root.Road) }\n}\nOutside { CapBinding: {target: ProbeRef, enabled: true, revision: 1} }");
            session.SetComponentJson("Root.Ground", "CapTerrain", CapabilityConfig.Write(new TerrainConfig { source = "Root.Road", size = 12 }).ToString());
            var graph = CapabilityGraph.Capture(session, null, "Root");
            Assert.That(graph["entities"].Select(e => (string)e["path"]), Is.EquivalentTo(new[] { "Root.Ground", "Root.Road" }));
            Assert.That(CapabilityGraph.Validate(graph, "Root"), Is.Null);
            Assert.That(CapabilityGraph.Validate(graph, "Elsewhere"), Is.Not.Null, "Entities outside the owner scope must be rejected");
            using var restored = new FlecsSession(0);
            CapabilityRuntime.InstallSchema(restored);
            restored.SetManagedScript("Fixture", "ProbeRef {}\nRoot {\n    Target {}\n}");
            var issues = CapabilityGraph.Restore(restored, null, (JObject)graph.DeepClone(), "Root", "RootGraph");
            Assert.That(issues, Is.Empty);
            Assert.That(restored.Lookup("Root.Road"), Is.Not.EqualTo(0));
            var ground = JObject.Parse(restored.EntityJson("Root.Ground"));
            Assert.That((string)ground["pairs"]["CapDependsOn"], Is.EqualTo("Root.Road"));
            Assert.That((float)ground["components"]["CapTerrain"]["size"], Is.EqualTo(12));
            Assert.That((string)ground["components"]["CapTerrain"]["source"], Is.EqualTo("Root.Road"));
            Assert.That((int)ground["components"]["CapBinding"]["revision"], Is.EqualTo(2));
            Assert.That(JToken.DeepEquals(CapabilityGraph.Capture(restored, null, "Root")["entities"], graph["entities"]), Is.True, "Second capture differs from the first");
        }
        [Test]
        public void GraphRestoreReportsDroppedPrerequisitesWithoutThrowing()
        {
            var graph = new JObject
            {
                ["version"] = CapabilityGraph.Version,
                ["entities"] = new JArray(new JObject
                {
                    ["path"] = "Root.Ground",
                    ["components"] = new JObject { ["CapBinding"] = new JObject { ["target"] = "Root.Target", ["enabled"] = true, ["revision"] = 1 } },
                    ["dependsOn"] = new JArray("Root.Missing")
                })
            };
            var issues = CapabilityGraph.Restore(session, null, graph, "Root", "RootGraph");
            Assert.That(issues.Single(), Does.Contain("Root.Missing"));
            Assert.That(session.Lookup("Root.Ground"), Is.Not.EqualTo(0));
            graph["entities"][0]["components"]["Unknown"] = new JObject();
            Assert.Throws<System.ArgumentException>(() => CapabilityGraph.Restore(session, null, graph, "Root", "RootGraph2"));
        }
    }
}
