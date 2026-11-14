using System;
using System.Runtime.InteropServices;

namespace Runtime.Capabilities
{
    // Managed mirrors of the native Look and WorldStyle components (Native/Core/src/Look.cpp). FlecsSession checks the
    // size and every field offset against the native layout the first time a mirror is read. Hue is in degrees,
    // everything else 0..1 (warmth -1..1).
    [StructLayout(LayoutKind.Sequential)]
    public struct LookValues
    {
        public float colourH, colourS, colourL;
        public float patternH, patternS, patternL, patternShape, patternRandom, patternSize;
        public float polish, metal, clear, glow;

        /// <summary>What the native side gives a Look that was added without values.</summary>
        public static LookValues Default => new LookValues
        {
            colourL = 0.5f, patternL = 0.2f, polish = 0.5f
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WorldStyleValues
    {
        public float hand, chunk, ink, warmth, vivid, air, glow;
    }

    /// <summary>A look with every value rounded to the cache step, so equal-looking elements share one material.</summary>
    public readonly struct LookKey : IEquatable<LookKey>
    {
        public readonly int ColourH, ColourS, ColourL, PatternH, PatternS, PatternL, Shape, Random, Size, Polish, Metal, Clear, Glow;

        public LookKey(in LookValues v, int steps)
        {
            ColourH = Hue(v.colourH, steps);
            ColourS = Step(v.colourS, steps);
            ColourL = Step(v.colourL, steps);
            // Without a pattern its values cannot matter; keep them out of the key so such looks stay equal.
            Size = Step(v.patternSize, steps);
            bool pattern = Size > 0;
            PatternH = pattern ? Hue(v.patternH, steps) : 0;
            PatternS = pattern ? Step(v.patternS, steps) : 0;
            PatternL = pattern ? Step(v.patternL, steps) : 0;
            Shape = pattern ? Step(v.patternShape, steps) : 0;
            Random = pattern ? Step(v.patternRandom, steps) : 0;
            Polish = Step(v.polish, steps);
            Metal = Step(v.metal, steps);
            Clear = Step(v.clear, steps);
            Glow = Step(v.glow, steps);
        }

        public bool HasPattern => Size > 0;

        public static int Step(float value, int steps) =>
            (int)MathF.Round(Math.Clamp(float.IsNaN(value) ? 0f : value, 0f, 1f) * steps);

        public static int Hue(float degrees, int steps)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees))
                degrees = 0f;
            float turns = degrees / 360f;
            turns -= MathF.Floor(turns);
            return (int)MathF.Round(turns * steps) % steps;
        }

        public bool Equals(LookKey o) =>
            ColourH == o.ColourH && ColourS == o.ColourS && ColourL == o.ColourL && PatternH == o.PatternH && PatternS == o.PatternS
            && PatternL == o.PatternL && Shape == o.Shape && Random == o.Random && Size == o.Size && Polish == o.Polish
            && Metal == o.Metal && Clear == o.Clear && Glow == o.Glow;
        public override bool Equals(object obj) => obj is LookKey o && Equals(o);
        public static bool operator ==(LookKey a, LookKey b) => a.Equals(b);
        public static bool operator !=(LookKey a, LookKey b) => !a.Equals(b);
        public override int GetHashCode()
        {
            var h = new HashCode();
            h.Add(ColourH); h.Add(ColourS); h.Add(ColourL); h.Add(PatternH); h.Add(PatternS); h.Add(PatternL); h.Add(Shape);
            h.Add(Random); h.Add(Size); h.Add(Polish); h.Add(Metal); h.Add(Clear); h.Add(Glow);
            return h.ToHashCode();
        }
    }
}
