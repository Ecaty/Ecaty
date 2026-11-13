using Runtime.Capabilities;
#if CAP_FEEL
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MoreMountains.Feedbacks;
#endif
using Runtime.Flecs;
namespace Game.Adapters
{
    // Always compiled: the schema and saved configurations stay valid without the plugin.
    [Capability("CapSequence", "feedback sequencing", 45, Gate = "CAP_FEEL", Probe = "Assets/Feel/MMFeedbacks/MMFeedbacks/Sequencing/Scripts/MMSoundSequencer.cs")]
    public sealed class SequenceConfig
    {
        public string pattern = "";
        public float bpm = 120;
        public bool playing;
    }
#if CAP_FEEL
    public sealed class SequenceCapability : CapabilityInstance<SequenceConfig>
    {
        MMSoundSequencer sequencer; MMSequence sequence;
        public SequenceCapability(CapabilityContext c) : base(c) { }
        protected override void Apply(SequenceConfig j)
        {
            var tracks = JArray.Parse(j.pattern);
            if (tracks.Count < 1 || tracks.Count > 16)
                throw new ArgumentException("Sequence needs 1..16 tracks.");
            if (sequencer)
            {
                sequencer.StopSequence();
                sequencer.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(sequencer.gameObject);
            }
            var player = new GameObject("Sound sequencer");
            player.transform.SetParent(Host.transform, false);
            sequencer = player.AddComponent<MMSoundSequencer>();
            if (sequence)
                UnityEngine.Object.Destroy(sequence);
            sequence = ScriptableObject.CreateInstance<MMSequence>();
            sequence.SequenceTracks = new();
            sequence.QuantizedSequence = new();
            sequence.Quantized = true;
            sequence.TargetBPM = Mathf.Clamp((int)j.bpm, 10, 400);
            sequencer.Sounds = new();
            sequencer.TrackEvents = new();
            sequencer.Sequence = sequence;
            sequencer.BPM = sequence.TargetBPM;
            sequencer.Loop = true;
            int length = -1;
            for (int i = 0; i < tracks.Count; i++)
            {
                var track = (JArray)tracks[i];
                if (track.Count < 2 || track.Count > 129)
                    throw new ArgumentException("A track is [assetKey, beat, beat, ...], up to 128 beats.");
                if (length < 0)
                    length = track.Count - 1;
                else if (length != track.Count - 1)
                    throw new ArgumentException("Tracks must have equal beat counts.");
                var clip = Context.Asset<AudioClip>((string)track[0]);
                if (!clip)
                    throw new ArgumentException("Sequence audio asset is missing.");
                sequencer.Sounds.Add(clip);
                sequence.SequenceTracks.Add(new MMSequenceTrack { ID = i, Active = true });
                var line = new MMSequenceList { Line = new() };
                for (int beat = 0; beat < length; beat++)
                    line.Line.Add(new MMSequenceNote { ID = (int)track[beat + 1] != 0 ? i : -1, Timestamp = beat * 60f / sequencer.BPM });
                sequence.QuantizedSequence.Add(line);
                sequencer.TrackEvents.Add(new UnityEngine.Events.UnityEvent());
            }
            sequence.Length = length * 60f / sequencer.BPM;
            sequencer.SequencerLength = length;
            sequencer.SetupSounds();
            if (j.playing)
                sequencer.PlaySequence();
        }
        public override void Dispose()
        {
            if (sequencer)
                sequencer.StopSequence();
            if (sequence)
                UnityEngine.Object.Destroy(sequence);
        }
    }
#endif
}
