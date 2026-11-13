using System;
using UnityEngine;

namespace Runtime.Capabilities
{
    /// <summary>
    /// What the look adapter needs to know about its shader and its mapping, read from Resources/Look/LookManifest.json.
    /// Nothing here is hard-coded a second time in the presenters.
    /// </summary>
    [Serializable]
    public sealed class LookManifest
    {
        public const string ResourcePath = "Look/LookManifest";

        [Serializable] public sealed class Templates { public string opaque, clear; }
        [Serializable] public sealed class Properties { public string baseColor, baseMap, metallic, smoothness, emissionColor, bumpMap, bumpScale; }
        [Serializable]
        public sealed class Mapping
        {
            public float smoothnessMin, smoothnessMax, clearThreshold, clearAlpha, glowScale, bumpScale;
        }
        [Serializable]
        public sealed class PatternSettings
        {
            public int minFeatures, maxFeatures;
            public float lineWidth, dotRadius, cellEdge, jitter, cellJitter, edgeSoftness, normalStrength;
        }
        [Serializable]
        public sealed class StyleMapping
        {
            public float temperatureScale, saturationScale, bloomScale, vignetteAir, vignetteHand, vignetteMax;
            public int volumePriority;
        }

        public int version;
        public string shader;
        public Templates templates;
        public Properties properties;
        public int quantisationSteps;
        public int patternTextureSize;
        public Mapping mapping;
        public PatternSettings pattern;
        public StyleMapping style;

        static LookManifest cached;

        /// <summary>The committed manifest, parsed once.</summary>
        public static LookManifest Load()
        {
            if (cached != null)
                return cached;
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (!asset)
                throw new InvalidOperationException("The look manifest is missing: Resources/" + ResourcePath + ".json");
            return cached = Parse(asset.text);
        }

        public static LookManifest Parse(string json)
        {
            var manifest = JsonUtility.FromJson<LookManifest>(json);
            if (manifest == null || manifest.templates == null || manifest.properties == null || manifest.mapping == null
                || manifest.pattern == null || manifest.style == null || manifest.quantisationSteps < 1 || manifest.patternTextureSize < 8)
                throw new InvalidOperationException("The look manifest is incomplete.");
            return manifest;
        }
    }
}
