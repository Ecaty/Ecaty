using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runtime.Capabilities
{
    /// <summary>
    /// Shared materials for looks. Looks that round to the same values get the same instance, which is counted and destroyed
    /// with its last user, so equal looks batch. Materials are instances of the two template materials in Resources/Look (opaque
    /// and clear), so every shader variant they need is in a player build. No property blocks.
    /// </summary>
    public sealed class LookMaterials : IDisposable
    {
        sealed class Entry
        {
            public LookKey Key;
            public Material Material;
            public int References;
        }

        readonly LookManifest manifest;
        readonly Material opaqueTemplate, clearTemplate;
        readonly LookPatternTextures patterns;
        readonly Dictionary<LookKey, Entry> byKey = new();
        readonly Dictionary<Material, Entry> byMaterial = new();
        Texture2D flatNormal;
        readonly int baseColor, baseMap, metallic, smoothness, emissionColor, bumpMap, bumpScale;

        public LookManifest Manifest => manifest;
        public LookPatternTextures Patterns => patterns;
        /// <summary>Materials alive now.</summary>
        public int Count => byKey.Count;

        public LookMaterials() : this(LookManifest.Load())
        {
        }

        public LookMaterials(LookManifest manifest)
        {
            this.manifest = manifest;
            opaqueTemplate = Resources.Load<Material>(manifest.templates.opaque);
            clearTemplate = Resources.Load<Material>(manifest.templates.clear);
            if (!opaqueTemplate || !clearTemplate)
                throw new InvalidOperationException("The look template materials are missing: " + manifest.templates.opaque + ", " + manifest.templates.clear);
            patterns = new LookPatternTextures(manifest);
            var names = manifest.properties;
            baseColor = Shader.PropertyToID(names.baseColor);
            baseMap = Shader.PropertyToID(names.baseMap);
            metallic = Shader.PropertyToID(names.metallic);
            smoothness = Shader.PropertyToID(names.smoothness);
            emissionColor = Shader.PropertyToID(names.emissionColor);
            bumpMap = Shader.PropertyToID(names.bumpMap);
            bumpScale = Shader.PropertyToID(names.bumpScale);
        }

        public LookKey KeyOf(in LookValues values) => new LookKey(values, manifest.quantisationSteps);

        /// <summary>The material for a look, with one more reference taken. Release it when the element stops using it.</summary>
        public Material Acquire(in LookValues values) => Acquire(KeyOf(values));

        public Material Acquire(in LookKey key)
        {
            if (byKey.TryGetValue(key, out var existing))
            {
                existing.References++;
                return existing.Material;
            }
            var material = Build(key);
            var entry = new Entry { Key = key, Material = material, References = 1 };
            byKey.Add(key, entry);
            byMaterial.Add(material, entry);
            return material;
        }

        /// <summary>Gives back one reference. The last one destroys the material and its pattern textures.</summary>
        public void Release(Material material)
        {
            if (!material || !byMaterial.TryGetValue(material, out var entry) || --entry.References > 0)
                return;
            byKey.Remove(entry.Key);
            byMaterial.Remove(material);
            if (entry.Key.HasPattern)
                patterns.Release(entry.Key);
            LookColour.Destroy(material);
        }

        public bool TryGetKey(Material material, out LookKey key)
        {
            bool found = material && byMaterial.TryGetValue(material, out var entry);
            key = found ? byMaterial[material].Key : default;
            return found;
        }

        public int References(Material material) => material && byMaterial.TryGetValue(material, out var entry) ? entry.References : 0;

        Material Build(in LookKey key)
        {
            var m = manifest.mapping;
            int steps = manifest.quantisationSteps;
            float clear = (float)key.Clear / steps, polish = (float)key.Polish / steps, glow = (float)key.Glow / steps;
            bool glass = clear > m.clearThreshold;
            var material = new Material(glass ? clearTemplate : opaqueTemplate) { name = "Look " + key.GetHashCode().ToString("x8") };
            // Unity drops _NORMALMAP from a template that has no normal map, so the instance turns on what it needs.
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_EMISSION");
            if (glass)
            {
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            var colour = LookColour.Of(key.ColourH, key.ColourS, key.ColourL, steps);
            colour.a = glass ? 1f - m.clearAlpha * clear : 1f;
            material.SetFloat(metallic, (float)key.Metal / steps);
            material.SetFloat(smoothness, Mathf.Lerp(m.smoothnessMin, m.smoothnessMax, polish));
            material.SetColor(emissionColor, glow > 0f
                ? new Color(colour.r * glow * m.glowScale, colour.g * glow * m.glowScale, colour.b * glow * m.glowScale, 1f) : Color.black);
            if (patterns.Acquire(key, out var albedo, out var normal))
            {
                material.SetColor(baseColor, new Color(1f, 1f, 1f, colour.a));
                material.SetTexture(baseMap, albedo);
                material.SetTexture(bumpMap, normal);
                material.SetFloat(bumpScale, (1f - polish) * m.bumpScale);
            }
            else
            {
                material.SetColor(baseColor, colour);
                material.SetTexture(baseMap, null);
                material.SetTexture(bumpMap, FlatNormal());
                material.SetFloat(bumpScale, 1f);
            }
            return material;
        }

        // A normal map that does not bend anything, for looks without a pattern.
        Texture2D FlatNormal()
        {
            if (flatNormal)
                return flatNormal;
            flatNormal = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "Look flat normal" };
            flatNormal.SetPixel(0, 0, new Color32(128, 128, 255, 255));
            flatNormal.Apply(false, true);
            return flatNormal;
        }

        public void Dispose()
        {
            foreach (var entry in byKey.Values)
                LookColour.Destroy(entry.Material);
            byKey.Clear();
            byMaterial.Clear();
            patterns.Dispose();
            LookColour.Destroy(flatNormal);
            flatNormal = null;
        }
    }
}
