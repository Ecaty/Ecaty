using System;
using System.Collections.Generic;
using Runtime.Flecs;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Runtime.Capabilities
{
    /// <summary>
    /// Shows the entered world's WorldStyle as post-processing on one global Volume. With no world entered, or a world without a
    /// WorldStyle, every override is off. Nothing runs when neither the entered world nor a style changed.
    /// </summary>
    public sealed class WorldStylePresenter : IDisposable
    {
        const string Query = "WorldStyle";

        readonly FlecsWorld world;
        readonly LookManifest.StyleMapping map;
        readonly Camera camera;
        readonly GameObject host;
        readonly Volume volume;
        readonly VolumeProfile profile;
        readonly WhiteBalance whiteBalance;
        readonly ColorAdjustments colourAdjustments;
        readonly Bloom bloom;
        readonly Vignette vignette;
        readonly List<string> notRendered = new();
        FlecsSession session;
        int handle, count;
        ulong entered;
        ulong[] entities = new ulong[8], targets = new ulong[8];
        WorldStyleValues[] rows = new WorldStyleValues[8];
        bool cameraChanged, previousPostProcessing;

        public Volume Volume => volume;
        public VolumeProfile Profile => profile;
        public WhiteBalance WhiteBalance => whiteBalance;
        public ColorAdjustments ColourAdjustments => colourAdjustments;
        public Bloom Bloom => bloom;
        public Vignette Vignette => vignette;
        /// <summary>True while a world with a style is entered.</summary>
        public bool Active
        {
            get; private set;
        }
        /// <summary>Style values that are set but not drawn yet (hand beyond its share of the vignette, chunk, ink).</summary>
        public IReadOnlyList<string> NotRendered => notRendered;

        public WorldStylePresenter(FlecsWorld world, Camera camera = null) : this(world, camera, LookManifest.Load())
        {
        }

        public WorldStylePresenter(FlecsWorld world, Camera camera, LookManifest manifest)
        {
            this.world = world;
            this.camera = camera;
            map = manifest.style;
            host = new GameObject("World style");
            host.transform.SetParent(world ? world.transform : null, false);
            host.layer = VolumeLayer(camera);
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            whiteBalance = profile.Add<WhiteBalance>();
            colourAdjustments = profile.Add<ColorAdjustments>();
            bloom = profile.Add<Bloom>();
            vignette = profile.Add<Vignette>();
            volume = host.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = map.volumePriority;
            volume.sharedProfile = profile;
            Apply(null);
        }

        // The first layer the camera's volumes are read from, as the other post-processing does.
        static int VolumeLayer(Camera camera)
        {
            if (!camera || !UniversalRenderPipeline.asset)
                return 0;
            int mask = camera.GetUniversalAdditionalCameraData().volumeLayerMask.value;
            for (int layer = 0; layer < 32; layer++)
                if ((mask & (1 << layer)) != 0)
                    return layer;
            return 0;
        }

        /// <summary>Call once per frame.</summary>
        public void Tick()
        {
            if (!world)
                return;
            var current = world.Session;
            if (current == null || current.IsDisposed)
            {
                if (session != null)
                {
                    session = null;
                    handle = count = 0;
                    entered = 0;
                    Apply(null);
                }
                return;
            }
            if (!ReferenceEquals(current, session))
            {
                session = current;
                handle = count = 0;
                entered = ulong.MaxValue;
                // A native library without the WorldStyle component has nothing to show.
                ulong type = session.TypeId("WorldStyle");
                if (type == 0)
                    return;
                session.TryRead<WorldStyleValues>(session.Lookup("core.look"), "WorldStyle", out _);
                handle = session.QueryHandle(Query);
            }
            if (handle == 0)
                return;
            bool changed = session.QueryChanged(handle);
            ulong now = session.EnteredWorld;
            if (!changed && now == entered)
                return;
            if (changed)
                count = session.ReadQuery(Query, null, ref entities, ref targets, ref rows);
            entered = now;
            for (int i = 0; i < count; i++)
                if (entities[i] == now && now != 0)
                {
                    Apply(rows[i]);
                    return;
                }
            Apply(null);
        }

        void Apply(WorldStyleValues? style)
        {
            bool had = Active;
            Active = style.HasValue;
            notRendered.Clear();
            if (!style.HasValue)
            {
                whiteBalance.active = colourAdjustments.active = bloom.active = vignette.active = false;
                SetPostProcessing(false);
                return;
            }
            var v = style.Value;
            whiteBalance.active = colourAdjustments.active = vignette.active = true;
            whiteBalance.temperature.Override(Mathf.Clamp(v.warmth * map.temperatureScale, -100f, 100f));
            colourAdjustments.saturation.Override(Mathf.Clamp((v.vivid - 0.5f) * map.saturationScale, -100f, 100f));
            bloom.active = v.glow > 0f;
            bloom.intensity.Override(Mathf.Max(0f, v.glow * map.bloomScale));
            vignette.intensity.Override(Mathf.Clamp(v.air * map.vignetteAir + v.hand * map.vignetteHand, 0f, map.vignetteMax));
            if (v.hand > 0f)
                notRendered.Add("hand");
            if (v.chunk > 0f)
                notRendered.Add("chunk");
            if (v.ink > 0f)
                notRendered.Add("ink");
            SetPostProcessing(true);
            if (!had && notRendered.Count > 0)
                Debug.Log("World style values not drawn yet: " + string.Join(", ", notRendered));
        }

        // Post-processing must be on for the camera while a style is shown; the camera's own setting comes back afterwards.
        void SetPostProcessing(bool on)
        {
            if (!camera || !UniversalRenderPipeline.asset)
                return;
            var data = camera.GetUniversalAdditionalCameraData();
            if (on && !cameraChanged)
            {
                previousPostProcessing = data.renderPostProcessing;
                cameraChanged = true;
                data.renderPostProcessing = true;
            }
            else if (!on && cameraChanged)
            {
                data.renderPostProcessing = previousPostProcessing;
                cameraChanged = false;
            }
        }

        public void Dispose()
        {
            SetPostProcessing(false);
            LookColour.Destroy(host);
            LookColour.Destroy(profile);
        }
    }
}
