using System;
using System.Collections.Generic;
using System.IO;

namespace Runtime.Capabilities
{
    /// <summary>
    /// Where the samples of a SoundClip come from, by its uri: an in-memory registry first, then 16-bit PCM WAV files in one
    /// folder the app passes in. A uri is a plain file name, never a path. Nothing is written here.
    /// </summary>
    public sealed class SoundSources
    {
        sealed class Entry
        {
            public float[] Samples;
            public int Rate;
        }

        readonly string folder;
        readonly Dictionary<string, Entry> entries = new();

        /// <param name="folder">The folder with the WAV files, or null for the registry only.</param>
        public SoundSources(string folder = null) => this.folder = folder;

        /// <summary>Raised by one on every Register and Remove, so a presenter can look again at what it could not find.</summary>
        public int Version
        {
            get; private set;
        }

        /// <summary>True for a plain file name: no separator, no "..", no drive letter, no characters a name cannot hold.</summary>
        public static bool IsPlainName(string uri)
        {
            if (string.IsNullOrEmpty(uri) || uri.Contains("..") || uri.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return false;
            return uri.IndexOf('/') < 0 && uri.IndexOf('\\') < 0 && uri.IndexOf(':') < 0;
        }

        /// <summary>Keeps mono samples under a uri. False for a name that is not plain or for no samples.</summary>
        public bool Register(string uri, float[] monoSamples, int sampleRate)
        {
            if (!IsPlainName(uri) || monoSamples == null || monoSamples.Length == 0 || sampleRate <= 0)
                return false;
            entries[uri] = new Entry { Samples = monoSamples, Rate = sampleRate };
            Version++;
            return true;
        }

        public bool Remove(string uri)
        {
            if (uri == null || !entries.Remove(uri))
                return false;
            Version++;
            return true;
        }

        /// <summary>The samples of a uri. On false, <paramref name="reason"/> says why, without any path.</summary>
        public bool TryGet(string uri, out float[] samples, out int sampleRate, out string reason)
        {
            samples = null;
            sampleRate = 0;
            reason = null;
            if (!IsPlainName(uri))
            {
                reason = "refused: the uri is not a plain name";
                return false;
            }
            if (entries.TryGetValue(uri, out var entry))
            {
                samples = entry.Samples;
                sampleRate = entry.Rate;
                return true;
            }
            if (string.IsNullOrEmpty(folder))
            {
                reason = "no source registered for the clip";
                return false;
            }
            byte[] bytes;
            try
            {
                string file = Path.Combine(folder, uri);
                if (!File.Exists(file))
                {
                    reason = "no source registered and no file for the clip";
                    return false;
                }
                bytes = File.ReadAllBytes(file);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                reason = "the sound file cannot be read (" + e.GetType().Name + ")";
                return false;
            }
            if (!TryReadWav(bytes, out samples, out sampleRate))
            {
                samples = null;
                reason = "the sound file is not a 16-bit PCM WAV, mono or stereo";
                return false;
            }
            // Kept like a registered clip, so the file is read once. Not a Register: the version stays.
            entries[uri] = new Entry { Samples = samples, Rate = sampleRate };
            return true;
        }

        /// <summary>Reads 16-bit PCM WAV bytes, mono or stereo (mixed to mono).</summary>
        public static bool TryReadWav(byte[] bytes, out float[] samples, out int sampleRate)
        {
            samples = null;
            sampleRate = 0;
            if (bytes == null || bytes.Length < 12 || !Tag(bytes, 0, "RIFF") || !Tag(bytes, 8, "WAVE"))
                return false;
            int channels = 0, bits = 0, format = 0;
            int at = 12;
            while (at + 8 <= bytes.Length)
            {
                int size = BitConverter.ToInt32(bytes, at + 4);
                int body = at + 8;
                if (size < 0)
                    return false;
                int available = Math.Min(size, bytes.Length - body);
                if (Tag(bytes, at, "fmt ") && available >= 16)
                {
                    format = BitConverter.ToUInt16(bytes, body);
                    channels = BitConverter.ToUInt16(bytes, body + 2);
                    sampleRate = BitConverter.ToInt32(bytes, body + 4);
                    bits = BitConverter.ToUInt16(bytes, body + 14);
                }
                else if (Tag(bytes, at, "data"))
                {
                    if (format != 1 || bits != 16 || (channels != 1 && channels != 2) || sampleRate <= 0)
                        return false;
                    int frames = available / (2 * channels);
                    if (frames <= 0)
                        return false;
                    samples = new float[frames];
                    for (int i = 0; i < frames; i++)
                    {
                        int index = body + i * 2 * channels;
                        float value = BitConverter.ToInt16(bytes, index);
                        if (channels == 2)
                            value = (value + BitConverter.ToInt16(bytes, index + 2)) * 0.5f;
                        samples[i] = value / 32768f;
                    }
                    return true;
                }
                // Chunks are padded to an even size.
                long next = (long)body + size + (size & 1);
                if (next > bytes.Length)
                    break;
                at = (int)next;
            }
            return false;
        }

        static bool Tag(byte[] bytes, int at, string tag)
        {
            for (int i = 0; i < 4; i++)
                if (bytes[at + i] != tag[i])
                    return false;
            return true;
        }
    }
}
