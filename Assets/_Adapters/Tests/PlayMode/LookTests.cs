using System;
using System.Collections;
using System.Globalization;
using System.IO;
using Runtime.Flecs;
using Runtime.Capabilities;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
namespace Game.Tests
{
    // The look adapter against a real native world: shared materials, kind edits, patterns, removal and the world style.
    public sealed class LookTests
    {
        const string WorldId = "looks";
        const string Path = "worlds." + WorldId + ".";
        const string Plain = "prefab Ball {\n  Position: {0,0,0}\n  Scale: {1,1,1}\n  Color: {1,0,0,1}\n  Primitive: {1}\n";
        // A kind with this look: hue 200, saturation 0.8, lightness 0.5, polish 0.2.
        static string Kind(int hue = 200, bool look = true) => Plain + (look
            ? "  Look: {colour: {h: " + hue + ", s: 0.8, l: 0.5}, material: {polish: 0.2}}\n}" : "}");

        sealed class Scene : IDisposable
        {
            public FlecsWorld World;
            public FlecsSession Session;
            public ulong Root, Kind;
            public LookPresenter Looks;
            public WorldStylePresenter Style;
            public readonly Material Original;

            public Scene(string kind)
            {
                World = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
                Session = World.Session;
                Root = Session.CreateWorld(WorldId);
                Session.ApplySource(Path + "sources.Ball", Path + "kinds", kind);
                Kind = Session.Lookup(Path + "kinds.Ball");
                Looks = new LookPresenter(World);
            }
            public ulong Spawn(string name) => Session.Spawn(Kind, Root, name);
            public ulong Id(string name) => Session.Lookup(Path + name);
            public void SetLook(string name, string json) => Session.SetComponentJson(Path + name, "Look", json);
            public void SetKind(string text) => Session.ApplySource(Path + "sources.Ball", Path + "kinds", text);
            public void Settle()
            {
                for (int i = 0; i < 4; i++)
                {
                    Session.Tick(0.02f);
                    World.SyncViews();
                    Looks.Tick();
                    Style?.Tick();
                }
            }
            public void Sync()
            {
                Session.Tick(0.02f);
                World.SyncViews();
            }
            public Renderer RendererOf(string name) => World.FindView(Id(name)).transform.Find("Visual").GetComponent<Renderer>();
            public Material MaterialOf(string name) => RendererOf(name).sharedMaterial;
            public void Dispose()
            {
                Looks?.Dispose();
                Style?.Dispose();
                if (World)
                    Object.Destroy(World.gameObject);
            }
        }

        static string LookJson(float h, float s = 0.8f, float l = 0.5f, float polish = 0.2f, float metal = 0, float clear = 0, float glow = 0,
            float size = 0, float shape = 0.5f, float random = 0)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"colour\":{{\"h\":{0},\"s\":{1},\"l\":{2}}},\"pattern\":{{\"colour\":{{\"h\":{0},\"s\":0.3,\"l\":0.9}},\"shape\":{8},\"random\":{9},\"size\":{7}}},"
                + "\"material\":{{\"polish\":{3},\"metal\":{4},\"clear\":{5},\"glow\":{6}}}}}", h, s, l, polish, metal, clear, glow, size, shape, random);
        }

        [UnityTest]
        public IEnumerator EqualLooksShareOneMaterial()
        {
            yield return null;
            using var scene = new Scene(Kind());
            for (int i = 0; i < 20; i++)
                scene.Spawn("b" + i);
            scene.Spawn("odd");
            scene.SetLook("odd", LookJson(300, 1));
            scene.Settle();
            var first = scene.MaterialOf("b0");
            for (int i = 0; i < 20; i++)
                Assert.That(scene.MaterialOf("b" + i), Is.SameAs(first), "b" + i);
            Assert.That(scene.MaterialOf("odd"), Is.Not.SameAs(first));
            Assert.That(scene.Looks.Materials.Count, Is.EqualTo(2), "one material for 20 equal looks, one for the odd one");
            Assert.That(scene.Looks.Materials.References(first), Is.EqualTo(20));
            // The odd one takes the same values: one material is left and the unused one is destroyed.
            var oddMaterial = scene.MaterialOf("odd");
            scene.SetLook("odd", LookJson(200));
            scene.Settle();
            Assert.That(scene.MaterialOf("odd"), Is.SameAs(first));
            Assert.That(scene.Looks.Materials.Count, Is.EqualTo(1));
            yield return null;
            Assert.That(oddMaterial == null, "the unused material is destroyed");
            Assert.That(scene.RendererOf("b0").HasPropertyBlock(), Is.False, "a look is not drawn with a property block");
        }

        [UnityTest]
        public IEnumerator KindEditReachesInstancesButNotTheirOwnLook()
        {
            yield return null;
            using var scene = new Scene(Kind());
            for (int i = 0; i < 10; i++)
                scene.Spawn("b" + i);
            scene.Spawn("own");
            scene.SetLook("own", LookJson(300, 1));
            scene.Settle();
            var before = scene.MaterialOf("b0");
            var own = scene.MaterialOf("own");
            Assert.That(scene.Looks.Materials.TryGetKey(before, out var oldKey));
            scene.SetKind(Kind(100));
            scene.Settle();
            var after = scene.MaterialOf("b0");
            Assert.That(after, Is.Not.SameAs(before));
            for (int i = 0; i < 10; i++)
                Assert.That(scene.MaterialOf("b" + i), Is.SameAs(after), "b" + i);
            Assert.That(scene.MaterialOf("own"), Is.SameAs(own), "an instance with its own look keeps it");
            Assert.That(scene.Looks.Materials.TryGetKey(after, out var newKey));
            Assert.That(newKey.ColourH, Is.Not.EqualTo(oldKey.ColourH));
            Assert.That(scene.Looks.Materials.Count, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator PatternGivesABaseMapThatEqualPatternsShare()
        {
            yield return null;
            using var scene = new Scene(Plain + "  Look: {pattern: {colour: {h: 40, s: 0.9, l: 0.6}, shape: 0.5, random: 0.3, size: 0.4}}\n}");
            scene.Spawn("a");
            scene.Spawn("b");
            scene.Settle();
            var a = scene.MaterialOf("a");
            Assert.That(a.GetTexture("_BaseMap"), Is.Not.Null, "pattern albedo");
            Assert.That(a.GetTexture("_BumpMap"), Is.Not.Null, "pattern normal map");
            Assert.That(scene.MaterialOf("b").GetTexture("_BaseMap"), Is.SameAs(a.GetTexture("_BaseMap")));
            Assert.That(scene.Looks.Materials.Patterns.PatternCount, Is.EqualTo(1));
            // Another pattern of its own, then size 0: no base map, and the texture is given back.
            scene.SetLook("b", LookJson(10, size: 0.8f, shape: 0));
            scene.Settle();
            Assert.That(scene.MaterialOf("b").GetTexture("_BaseMap"), Is.Not.SameAs(a.GetTexture("_BaseMap")));
            Assert.That(scene.Looks.Materials.Patterns.PatternCount, Is.EqualTo(2));
            scene.SetLook("b", LookJson(10, size: 0));
            scene.Settle();
            Assert.That(scene.MaterialOf("b").GetTexture("_BaseMap"), Is.Null, "size 0 removes the pattern");
            Assert.That(scene.Looks.Materials.Patterns.PatternCount, Is.EqualTo(1));
            Assert.That(scene.MaterialOf("a").GetTexture("_BaseMap"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator ClearUsesTheTransparentQueueAndGlowLightsUp()
        {
            yield return null;
            using var scene = new Scene(Kind());
            scene.Spawn("solid");
            scene.Spawn("glass");
            scene.Spawn("lamp");
            scene.SetLook("glass", LookJson(200, clear: 0.5f));
            scene.SetLook("lamp", LookJson(30, glow: 0.5f));
            scene.Settle();
            var solid = scene.MaterialOf("solid");
            var glass = scene.MaterialOf("glass");
            var lamp = scene.MaterialOf("lamp");
            Assert.That(solid.renderQueue, Is.LessThan(2500));
            Assert.That(glass.renderQueue, Is.EqualTo(3000));
            Assert.That(glass.GetColor("_BaseColor").a, Is.EqualTo(1f - 0.85f * 0.5f).Within(0.02f));
            Assert.That(solid.GetColor("_EmissionColor"), Is.EqualTo(Color.black));
            var emission = lamp.GetColor("_EmissionColor");
            Assert.That(emission.r + emission.g + emission.b, Is.GreaterThan(0.1f), "glow gives an emission colour");
        }

        [UnityTest]
        public IEnumerator RemovingLookRestoresTheOriginalMaterialAndColor()
        {
            yield return null;
            using var scene = new Scene(Kind());
            scene.Spawn("a");
            scene.Sync();
            var original = scene.MaterialOf("a");
            scene.Settle();
            Assert.That(scene.MaterialOf("a"), Is.Not.SameAs(original));
            scene.SetKind(Kind(look: false));
            scene.Settle();
            Assert.That(scene.MaterialOf("a"), Is.SameAs(original));
            Assert.That(scene.Looks.Materials.Count, Is.EqualTo(0));
            Assert.That(scene.Looks.TrackedCount, Is.EqualTo(0));
            var block = new MaterialPropertyBlock();
            scene.RendererOf("a").GetPropertyBlock(block);
            Assert.That(block.GetColor("_BaseColor"), Is.EqualTo(new Color(1, 0, 0, 1)), "the view takes its Color again");
            // The appearance can be claimed by someone else now, and a Look coming back is shown again.
            scene.SetKind(Kind());
            scene.Settle();
            Assert.That(scene.MaterialOf("a"), Is.Not.SameAs(original));
        }

        [UnityTest]
        public IEnumerator AnotherAppearanceOwnerKeepsItsElement()
        {
            yield return null;
            using var scene = new Scene(Kind());
            scene.Spawn("a");
            scene.Spawn("b");
            scene.Sync();
            var original = scene.MaterialOf("a");
            Assert.That(scene.World.AcquireAppearance(scene.Id("a"), scene.Session.Lookup("worlds")), "a stand-in owner");
            scene.Settle();
            Assert.That(scene.MaterialOf("a"), Is.SameAs(original), "an element held by another owner is left alone");
            Assert.That(scene.MaterialOf("b"), Is.Not.SameAs(original));
        }

        [UnityTest]
        public IEnumerator WorldStyleSetsTheOverridesOfTheEnteredWorld()
        {
            yield return null;
            using var scene = new Scene(Kind());
            scene.Style = new WorldStylePresenter(scene.World);
            scene.Settle();
            Assert.That(scene.Style.Active, Is.False);
            Assert.That(scene.Style.WhiteBalance.active || scene.Style.ColourAdjustments.active || scene.Style.Bloom.active || scene.Style.Vignette.active, Is.False);
            scene.Session.SetComponentJson("worlds." + WorldId, "WorldStyle",
                "{\"hand\":0.5,\"chunk\":0.2,\"ink\":0,\"warmth\":0.5,\"vivid\":0.75,\"air\":0.5,\"glow\":0.5}");
            scene.Settle();
            Assert.That(scene.Style.Active, Is.False, "the world is not entered yet");
            scene.Session.EnterWorld(scene.Root);
            scene.Settle();
            var style = scene.Style;
            Assert.That(style.Active);
            Assert.That(style.WhiteBalance.active && style.WhiteBalance.temperature.overrideState);
            Assert.That(style.WhiteBalance.temperature.value, Is.EqualTo(30f).Within(0.01f));
            Assert.That(style.ColourAdjustments.saturation.value, Is.EqualTo(50f).Within(0.01f));
            Assert.That(style.Bloom.active);
            Assert.That(style.Bloom.intensity.value, Is.EqualTo(1.5f).Within(0.01f));
            Assert.That(style.Vignette.intensity.value, Is.EqualTo(0.5f * 0.35f + 0.5f * 0.1f).Within(0.001f));
            CollectionAssert.AreEqual(new[] { "hand", "chunk" }, style.NotRendered);
            // A change while entered is followed.
            scene.Session.SetComponentJson("worlds." + WorldId, "WorldStyle", "{\"warmth\":-1,\"vivid\":0.5,\"glow\":0}");
            scene.Settle();
            Assert.That(style.WhiteBalance.temperature.value, Is.EqualTo(-60f).Within(0.01f));
            Assert.That(style.ColourAdjustments.saturation.value, Is.EqualTo(0f).Within(0.01f));
            Assert.That(style.Bloom.active, Is.False, "bloom is off at glow 0");
            scene.Session.EnterWorld(0);
            scene.Settle();
            Assert.That(style.Active, Is.False);
            Assert.That(style.WhiteBalance.active || style.ColourAdjustments.active || style.Bloom.active || style.Vignette.active, Is.False, "leaving the world turns the style off");
            Assert.That(style.NotRendered, Is.Empty);
        }

        // For a person to look at: a few spheres across the look values. No assertion on pixels.
        [UnityTest]
        public IEnumerator WritesScreenshotsOfSomeLooks()
        {
            yield return null;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                yield break;
            using var scene = new Scene(Kind());
            var origin = new Vector3(0, 500, 0);
            var shots = new (string name, (string look, float x, float y)[] items)[]
            {
                ("polish-metal", new[]
                {
                    (LookJson(30, 0.8f, 0.5f, 0.0f), -4f, 0.8f), (LookJson(30, 0.8f, 0.5f, 0.25f), -2f, 0.8f), (LookJson(30, 0.8f, 0.5f, 0.5f), 0f, 0.8f),
                    (LookJson(30, 0.8f, 0.5f, 0.75f), 2f, 0.8f), (LookJson(30, 0.8f, 0.5f, 1.0f), 4f, 0.8f),
                    (LookJson(30, 0.8f, 0.5f, 0.8f, metal: 0.0f), -4f, -0.8f), (LookJson(30, 0.8f, 0.5f, 0.8f, metal: 0.25f), -2f, -0.8f),
                    (LookJson(30, 0.8f, 0.5f, 0.8f, metal: 0.5f), 0f, -0.8f), (LookJson(30, 0.8f, 0.5f, 0.8f, metal: 0.75f), 2f, -0.8f),
                    (LookJson(30, 0.8f, 0.5f, 0.8f, metal: 1.0f), 4f, -0.8f)
                }),
                ("glass-glow-pattern", new[]
                {
                    (LookJson(200, 0.7f, 0.6f, 0.9f, clear: 0.8f), -4f, 0f), (LookJson(40, 1, 0.5f, 0.5f, glow: 0.7f), -2f, 0f),
                    (LookJson(120, 0.5f, 0.5f, 0.3f, size: 0.4f, shape: 0f), 0f, 0f), (LookJson(280, 0.5f, 0.5f, 0.3f, size: 0.4f, shape: 0.5f, random: 0.5f), 2f, 0f),
                    (LookJson(340, 0.5f, 0.5f, 0.3f, size: 0.4f, shape: 1f), 4f, 0f)
                })
            };
            string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Logs/Look"));
            // One camera and light for both shots, given a few frames before the first picture, so both shots wait the same way.
            // Rows are 1.6 apart and columns 2, spheres are 1 wide; the camera sees about 15 x 8.5 units at the spheres.
            var cameraObject = new GameObject("Look screenshot camera");
            var lightObject = new GameObject("Look screenshot light");
            var target = new RenderTexture(960, 540, 24);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.78f, 0.78f, 0.8f);
            camera.fieldOfView = 30;
            camera.transform.position = origin + new Vector3(0, 0, -16);
            camera.targetTexture = target;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);
            string[] written = new string[shots.Length];
            int n = 0;
            foreach (var shot in shots)
            {
                var names = new string[shot.items.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    names[i] = "s" + n + "_" + i;
                    scene.Spawn(names[i]);
                    scene.SetLook(names[i], shot.items[i].look);
                    var p = origin + new Vector3(shot.items[i].x, shot.items[i].y, 0);
                    scene.Session.SetComponentJson(Path + names[i], "Position", string.Format(CultureInfo.InvariantCulture, "{{\"x\":{0},\"y\":{1},\"z\":{2}}}", p.x, p.y, p.z));
                }
                for (int frame = 0; frame < 3; frame++)
                {
                    scene.Settle();
                    yield return null;
                }
                Texture2D picture = null;
                try
                {
                    camera.Render();
                    camera.Render();
                    RenderTexture.active = target;
                    picture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                    picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    picture.Apply();
                    Directory.CreateDirectory(folder);
                    string file = System.IO.Path.Combine(folder, shot.name + ".png");
                    File.WriteAllBytes(file, picture.EncodeToPNG());
                    written[n] = file;
                }
                catch (Exception)
                {
                    // Not every test run can render; the screenshots are optional.
                }
                finally
                {
                    RenderTexture.active = null;
                    if (picture)
                        Object.Destroy(picture);
                }
                // The next shot starts from an empty scene.
                foreach (var name in names)
                    scene.Session.DeleteEntity(Path + name);
                scene.Settle();
                n++;
            }
            Object.Destroy(cameraObject);
            Object.Destroy(lightObject);
            target.Release();
            foreach (var file in written)
                if (file != null)
                    Assert.That(new FileInfo(file).Length, Is.GreaterThan(15 * 1024), file + " looks like an empty frame");
        }
    }
}
