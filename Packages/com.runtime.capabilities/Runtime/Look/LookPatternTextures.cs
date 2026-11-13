using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Runtime.Capabilities
{
    /// <summary>Colour maths and object clean-up shared by the look classes.</summary>
    public static class LookColour
    {
        /// <summary>Hue in degrees, saturation and lightness 0..1, to display RGB 0..1.</summary>
        public static Color HslToRgb(float h, float s, float l)
        {
            h = h / 360f;
            h -= Mathf.Floor(h);
            s = Mathf.Clamp01(s);
            l = Mathf.Clamp01(l);
            float chroma = (1f - Mathf.Abs(2f * l - 1f)) * s;
            float Channel(float shift)
            {
                float k = (h * 12f + shift) % 12f;
                float a = chroma * 0.5f;
                return l - a * Mathf.Max(-1f, Mathf.Min(Mathf.Min(k - 3f, 9f - k), 1f));
            }
            return new Color(Channel(0f), Channel(8f), Channel(4f), 1f);
        }

        public static Color Of(int h, int s, int l, int steps) => HslToRgb(h * 360f / steps, (float)s / steps, (float)l / steps);

        public static void Destroy(Object value)
        {
            if (!value)
                return;
            if (Application.isPlaying)
                Object.Destroy(value);
            else
                Object.DestroyImmediate(value);
        }
    }

    /// <summary>
    /// Pattern textures for a look: one scalar field over a tileable unit square, drawn as an albedo (the two colours mixed
    /// by the field) and a normal map (the field's slope). Cached and reference-counted by the quantised pattern and both
    /// colours. Shape blends lines (0), dots (0.5) and cells (1); random scatters the features; size sets how many there
    /// are (bigger size, fewer and larger). The numbers are in the manifest and will be tuned by eye.
    /// </summary>
    public sealed class LookPatternTextures : IDisposable
    {
        readonly struct Key : IEquatable<Key>
        {
            readonly int a, b, c, d, e, f, g, h, i;
            public Key(in LookKey k)
            {
                a = k.ColourH; b = k.ColourS; c = k.ColourL; d = k.PatternH; e = k.PatternS; f = k.PatternL; g = k.Shape; h = k.Random; i = k.Size;
            }
            public bool Equals(Key o) => a == o.a && b == o.b && c == o.c && d == o.d && e == o.e && f == o.f && g == o.g && h == o.h && i == o.i;
            public override bool Equals(object obj) => obj is Key o && Equals(o);
            public override int GetHashCode() => HashCode.Combine(a, b, c, d, e, f, HashCode.Combine(g, h, i));
        }
        sealed class Entry
        {
            public Texture2D Albedo, Normal;
            public int References;
        }

        readonly LookManifest manifest;
        readonly Dictionary<Key, Entry> entries = new();
        /// <summary>Textures alive now (two per distinct pattern).</summary>
        public int TextureCount => entries.Count * 2;
        public int PatternCount => entries.Count;

        public LookPatternTextures(LookManifest manifest)
        {
            this.manifest = manifest;
        }

        /// <summary>Takes one reference to the textures of a look's pattern. False (and nothing taken) when it has no pattern.</summary>
        public bool Acquire(in LookKey look, out Texture2D albedo, out Texture2D normal)
        {
            albedo = normal = null;
            if (!look.HasPattern)
                return false;
            var key = new Key(look);
            if (!entries.TryGetValue(key, out var entry))
            {
                int steps = manifest.quantisationSteps;
                var baseColour = LookColour.Of(look.ColourH, look.ColourS, look.ColourL, steps);
                var patternColour = LookColour.Of(look.PatternH, look.PatternS, look.PatternL, steps);
                if (!Generate(manifest, (float)look.Shape / steps, (float)look.Random / steps, (float)look.Size / steps,
                        baseColour, patternColour, manifest.patternTextureSize, out var colours, out var normals))
                    return false;
                entry = new Entry { Albedo = Make("Look albedo", colours, false), Normal = Make("Look normal", normals, true) };
                entries.Add(key, entry);
            }
            entry.References++;
            albedo = entry.Albedo;
            normal = entry.Normal;
            return true;
        }

        public void Release(in LookKey look)
        {
            var key = new Key(look);
            if (!entries.TryGetValue(key, out var entry) || --entry.References > 0)
                return;
            entries.Remove(key);
            LookColour.Destroy(entry.Albedo);
            LookColour.Destroy(entry.Normal);
        }

        public void Dispose()
        {
            foreach (var entry in entries.Values)
            {
                LookColour.Destroy(entry.Albedo);
                LookColour.Destroy(entry.Normal);
            }
            entries.Clear();
        }

        int Size => manifest.patternTextureSize;

        Texture2D Make(string name, Color32[] pixels, bool linear)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true, linear)
            {
                name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        // --- The generator: pure functions, no Unity objects ---------------------------------------------------------

        /// <summary>Number of features along one side of the unit square for a size in (0, 1].</summary>
        public static int Features(LookManifest.PatternSettings settings, float size) =>
            Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(settings.maxFeatures, settings.minFeatures, Mathf.Clamp01(size))));

        /// <summary>The field at (x, y), 0 (first colour) to 1 (pattern colour). Repeats every 1 in both directions.</summary>
        public static float Field(LookManifest.PatternSettings p, float shape, float random, float size, float x, float y)
        {
            x -= Mathf.Floor(x);
            y -= Mathf.Floor(y);
            int n = Features(p, size);
            shape = Mathf.Clamp01(shape);
            if (shape <= 0.5f)
            {
                float t = shape * 2f;
                float lines = Lines(p, n, random, x, y);
                return t <= 0f ? lines : Mathf.Lerp(lines, Dots(p, n, random, x, y), t);
            }
            float u = (shape - 0.5f) * 2f;
            float dots = Dots(p, n, random, x, y);
            return u >= 1f ? Cells(p, n, random, x, y) : Mathf.Lerp(dots, Cells(p, n, random, x, y), u);
        }

        /// <summary>
        /// Albedo and normal pixels for a pattern, both from one field. False when the size is 0 (no pattern).
        /// The same inputs always give the same pixels.
        /// </summary>
        public static bool Generate(LookManifest manifest, float shape, float random, float size, Color baseColour, Color patternColour,
            int textureSize, out Color32[] albedo, out Color32[] normal)
        {
            albedo = normal = null;
            if (size <= 0f || textureSize < 2)
                return false;
            var p = manifest.pattern;
            var field = new float[textureSize * textureSize];
            for (int py = 0; py < textureSize; py++)
                for (int px = 0; px < textureSize; px++)
                    field[py * textureSize + px] = Field(p, shape, random, size, (px + 0.5f) / textureSize, (py + 0.5f) / textureSize);
            albedo = new Color32[field.Length];
            normal = new Color32[field.Length];
            float strength = p.normalStrength * 2f;
            for (int py = 0; py < textureSize; py++)
                for (int px = 0; px < textureSize; px++)
                {
                    int i = py * textureSize + px;
                    albedo[i] = Color.Lerp(baseColour, patternColour, field[i]);
                    int left = (px + textureSize - 1) % textureSize, right = (px + 1) % textureSize;
                    int down = (py + textureSize - 1) % textureSize, up = (py + 1) % textureSize;
                    float dx = (field[py * textureSize + right] - field[py * textureSize + left]) * strength;
                    float dy = (field[up * textureSize + px] - field[down * textureSize + px]) * strength;
                    var n = new Vector3(-dx, -dy, 1f).normalized;
                    normal[i] = new Color32((byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 255);
                }
            return true;
        }

        static uint Hash(int x, int y, uint salt)
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + salt * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
        // 0..1 from a cell and a salt; cell indices are always wrapped before use, so the field repeats.
        static float Rand(int x, int y, uint salt) => (Hash(x, y, salt) & 0xFFFFFF) / 16777216f;
        static float Mask(float distance, float edge, float soft) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge - soft, edge + soft, distance));
        static int Wrap(int i, int n) => ((i % n) + n) % n;

        static float Lines(LookManifest.PatternSettings p, int n, float random, float x, float y)
        {
            int row = Mathf.Min(n - 1, (int)(y * n));
            float inside = y * n - row;
            float centre = 0.5f + (Rand(0, row, 1) - 0.5f) * 2f * p.jitter * random;
            return Mask(Mathf.Abs(inside - centre), p.lineWidth, p.edgeSoftness);
        }

        static float Dots(LookManifest.PatternSettings p, int n, float random, float x, float y)
        {
            int i = Mathf.Min(n - 1, (int)(x * n)), j = Mathf.Min(n - 1, (int)(y * n));
            float dx = x * n - i - (0.5f + (Rand(i, j, 2) - 0.5f) * 2f * p.jitter * random);
            float dy = y * n - j - (0.5f + (Rand(i, j, 3) - 0.5f) * 2f * p.jitter * random);
            return Mask(Mathf.Sqrt(dx * dx + dy * dy), p.dotRadius, p.edgeSoftness);
        }

        // Cell borders: where the nearest and the second nearest feature point are almost equally far.
        static float Cells(LookManifest.PatternSettings p, int n, float random, float x, float y)
        {
            int i = Mathf.Min(n - 1, (int)(x * n)), j = Mathf.Min(n - 1, (int)(y * n));
            float px = x * n, py = y * n, nearest = float.MaxValue, second = float.MaxValue;
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int ci = Wrap(i + di, n), cj = Wrap(j + dj, n);
                    float fx = i + di + 0.5f + (Rand(ci, cj, 4) - 0.5f) * 2f * p.cellJitter * random;
                    float fy = j + dj + 0.5f + (Rand(ci, cj, 5) - 0.5f) * 2f * p.cellJitter * random;
                    float d = Mathf.Sqrt((fx - px) * (fx - px) + (fy - py) * (fy - py));
                    if (d < nearest)
                    {
                        second = nearest;
                        nearest = d;
                    }
                    else if (d < second)
                        second = d;
                }
            return Mask(second - nearest, p.cellEdge, p.edgeSoftness);
        }
    }
}
