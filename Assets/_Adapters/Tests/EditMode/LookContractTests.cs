using Runtime.Capabilities;
using NUnit.Framework;
using UnityEngine;
namespace Game.Tests
{
    // The look adapter's pure parts: colour maths, the manifest, the template materials and the pattern generator.
    public sealed class LookContractTests
    {
        static void AssertColour(Color actual, float r, float g, float b, string what)
        {
            Assert.That(actual.r, Is.EqualTo(r).Within(0.002f), what + " red");
            Assert.That(actual.g, Is.EqualTo(g).Within(0.002f), what + " green");
            Assert.That(actual.b, Is.EqualTo(b).Within(0.002f), what + " blue");
        }

        [Test]
        public void HslGivesPrimariesAndGreys()
        {
            AssertColour(LookColour.HslToRgb(0, 1, 0.5f), 1, 0, 0, "red");
            AssertColour(LookColour.HslToRgb(120, 1, 0.5f), 0, 1, 0, "green");
            AssertColour(LookColour.HslToRgb(240, 1, 0.5f), 0, 0, 1, "blue");
            AssertColour(LookColour.HslToRgb(60, 1, 0.5f), 1, 1, 0, "yellow");
            AssertColour(LookColour.HslToRgb(180, 1, 0.5f), 0, 1, 1, "cyan");
            AssertColour(LookColour.HslToRgb(300, 1, 0.5f), 1, 0, 1, "magenta");
            AssertColour(LookColour.HslToRgb(0, 0, 0), 0, 0, 0, "black");
            AssertColour(LookColour.HslToRgb(0, 0, 1), 1, 1, 1, "white");
            AssertColour(LookColour.HslToRgb(200, 0, 0.5f), 0.5f, 0.5f, 0.5f, "grey ignores hue");
            AssertColour(LookColour.HslToRgb(360, 1, 0.5f), 1, 0, 0, "hue wraps");
            AssertColour(LookColour.HslToRgb(0, 1, 0.75f), 1, 0.5f, 0.5f, "light red");
        }

        [Test]
        public void EqualLooksShareAKeyAndPatternValuesOnlyCountWithAPattern()
        {
            var a = LookValues.Default;
            var b = a;
            b.colourL += 0.004f;
            Assert.That(new LookKey(a, 64), Is.EqualTo(new LookKey(b, 64)), "a difference below the step must not split the cache");
            b.colourL += 0.05f;
            Assert.That(new LookKey(a, 64), Is.Not.EqualTo(new LookKey(b, 64)));
            var c = a;
            c.patternShape = 1;
            c.patternH = 200;
            Assert.That(new LookKey(a, 64), Is.EqualTo(new LookKey(c, 64)), "pattern values are ignored while the pattern size is 0");
            c.patternSize = 0.5f;
            Assert.That(new LookKey(a, 64), Is.Not.EqualTo(new LookKey(c, 64)));
        }

        [Test]
        public void ManifestParsesAndNamesRealShaderProperties()
        {
            var manifest = LookManifest.Load();
            Assert.That(manifest.version, Is.GreaterThanOrEqualTo(1));
            Assert.That(manifest.quantisationSteps, Is.EqualTo(64));
            Assert.That(manifest.patternTextureSize, Is.EqualTo(128));
            var shader = Shader.Find(manifest.shader);
            Assert.That(shader, Is.Not.Null, "shader " + manifest.shader);
            foreach (var name in new[] { manifest.properties.baseColor, manifest.properties.baseMap, manifest.properties.metallic,
                manifest.properties.smoothness, manifest.properties.emissionColor, manifest.properties.bumpMap, manifest.properties.bumpScale })
                Assert.That(shader.FindPropertyIndex(name), Is.GreaterThanOrEqualTo(0), "shader property " + name);
        }

        [Test]
        public void TemplateMaterialsUseTheShaderAndHaveTheirKeywordsOn()
        {
            var manifest = LookManifest.Load();
            var opaque = Resources.Load<Material>(manifest.templates.opaque);
            var clear = Resources.Load<Material>(manifest.templates.clear);
            Assert.That(opaque && clear, "template materials");
            Assert.That(opaque.shader.name, Is.EqualTo(manifest.shader));
            Assert.That(clear.shader.name, Is.EqualTo(manifest.shader));
            foreach (var template in new[] { opaque, clear })
            {
                Assert.That(template.IsKeywordEnabled("_EMISSION"), template.name + " emission keyword");
                Assert.That(template.IsKeywordEnabled("_NORMALMAP"), template.name + " normal map keyword");
            }
            Assert.That(clear.renderQueue, Is.EqualTo(3000));
            Assert.That(opaque.renderQueue, Is.LessThan(2500));
            Assert.That(clear.GetFloat("_ZWrite"), Is.EqualTo(0f));
            Assert.That(clear.GetFloat("_Surface"), Is.EqualTo(1f));
        }

        [Test]
        public void PatternGeneratorIsDeterministicAndTileable()
        {
            var manifest = LookManifest.Load();
            var p = manifest.pattern;
            foreach (float shape in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                foreach (float random in new[] { 0f, 0.6f, 1f })
                {
                    Assert.That(LookPatternTextures.Generate(manifest, shape, random, 0.5f, Color.white, Color.black, 32, out var a1, out var n1));
                    Assert.That(LookPatternTextures.Generate(manifest, shape, random, 0.5f, Color.white, Color.black, 32, out var a2, out var n2));
                    CollectionAssert.AreEqual(a1, a2, "albedo shape " + shape + " random " + random);
                    CollectionAssert.AreEqual(n1, n2, "normal shape " + shape + " random " + random);
                    // The field repeats, and is continuous across the border of the square.
                    for (int i = 0; i < 20; i++)
                    {
                        float x = (i * 0.137f) % 1f, y = (i * 0.291f + 0.05f) % 1f;
                        float v = LookPatternTextures.Field(p, shape, random, 0.5f, x, y);
                        Assert.That(LookPatternTextures.Field(p, shape, random, 0.5f, x + 1f, y), Is.EqualTo(v).Within(1e-4f));
                        Assert.That(LookPatternTextures.Field(p, shape, random, 0.5f, x, y + 2f), Is.EqualTo(v).Within(1e-4f));
                        float across = LookPatternTextures.Field(p, shape, random, 0.5f, 0.0001f, y) - LookPatternTextures.Field(p, shape, random, 0.5f, -0.0001f, y);
                        Assert.That(Mathf.Abs(across), Is.LessThan(0.05f), "left and right borders must meet (shape " + shape + ")");
                        across = LookPatternTextures.Field(p, shape, random, 0.5f, x, 0.0001f) - LookPatternTextures.Field(p, shape, random, 0.5f, x, -0.0001f);
                        Assert.That(Mathf.Abs(across), Is.LessThan(0.05f), "top and bottom borders must meet (shape " + shape + ")");
                    }
                }
        }

        [Test]
        public void PatternGeneratorDrawsBothColoursAndRandomChangesIt()
        {
            var manifest = LookManifest.Load();
            LookPatternTextures.Generate(manifest, 0.5f, 0f, 0.5f, Color.white, Color.black, 64, out var regular, out _);
            LookPatternTextures.Generate(manifest, 0.5f, 1f, 0.5f, Color.white, Color.black, 64, out var scattered, out _);
            Assert.That(System.Array.Exists(regular, c => c.r > 240) && System.Array.Exists(regular, c => c.r < 15), "both colours appear");
            CollectionAssert.AreNotEqual(regular, scattered, "random changes the pattern");
        }

        [Test]
        public void PatternSizeZeroGivesNothing()
        {
            var manifest = LookManifest.Load();
            Assert.That(LookPatternTextures.Generate(manifest, 0.5f, 0.5f, 0f, Color.white, Color.black, 32, out var albedo, out var normal), Is.False);
            Assert.That(albedo, Is.Null);
            Assert.That(normal, Is.Null);
            using var textures = new LookPatternTextures(manifest);
            var key = new LookKey(LookValues.Default, manifest.quantisationSteps);
            Assert.That(textures.Acquire(key, out var a, out var n), Is.False, "no pattern, no textures");
            Assert.That(a == null && n == null);
            Assert.That(textures.TextureCount, Is.EqualTo(0));
        }
    }
}
