#if ECATY_CAP_FLEXALON
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Ecaty
{
    public sealed partial class CrystalWorldGallery
    {
        JObject ReturnIntent => Read("GalleryTreeSettings", "GalleryReturnIntent");
        JObject ReturnState => Read("GalleryTreeSettings", "GalleryReturnState");
        bool CinematicReturn => Returning && !N("portalReturn") && (bool)ReturnIntent["active"];
        bool ReturnStateReady => Mathf.Abs((float)ReturnState["epoch"] - (float)ReturnIntent["started"]) < .0001f;
        public float ReturnProgress => ReturnStateReady ? (float)ReturnState["progress"] : 0;
        public float ReturnReveal => ReturnStateReady ? (float)ReturnState["reveal"] : 0;
        public float RevealOpacity(string id) => id == (string)ReturnIntent["world"] ? 1 : byId.TryGetValue(id, out var item) ? RevealAt(item.Entry.Anchor.position) : 1;
        public float LoadedHighlight(string id) => id == CurrentWorldId ? profile.LoadedWorldStrength : 0;
        public float GlassProgress
        {
            get
            {
                var intent = Read("GalleryTreeSettings", "GalleryGlassIntent");
                var state = Read("GalleryTreeSettings", "GalleryGlassState");
                return Mathf.Abs((float)state["epoch"] - (float)intent["started"]) < .0001f ? (float)state["progress"] : 0;
            }
        }
        public float GlassFeedbackAmount => exitFeedback?.GlassAmount ?? 0;
        void TriggerGlass(bool entering) => Write("GalleryTreeSettings", "GalleryGlassIntent", new JObject { ["started"] = time, ["entering"] = entering });
        float ApproachHighlight(Item item)
        {
            if (!entryArmed || (string)navigation["target"] != item.Entry.Id)
                return 1;
            float coverage = item.ScreenRadius * 2 / Mathf.Max(1, Mathf.Min(output.pixelWidth, output.pixelHeight));
            return 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(profile.ApproachHighlightFade, .98f, coverage));
        }
        float RevealAt(Vector3 position)
        {
            var intent = ReturnIntent;
            if (string.IsNullOrEmpty((string)intent["world"]) || ReturnReveal >= 1)
                return 1;
            var origin = new Vector3((float)intent["x"], (float)intent["y"], (float)intent["z"]);
            float distance = Vector3.Distance(position, origin) / Mathf.Max(.1f, (float)intent["radius"]);
            return Mathf.SmoothStep(0, 1, Mathf.Clamp01((ReturnReveal - distance * .72f) / .28f));
        }
        void BeginCinematicReturn()
        {
            var returned = byId.TryGetValue(CurrentWorldId ?? "", out var found) ? found : null;
            Vector3 p = returned != null ? returned.Entry.Anchor.position : currentCenter;
            float radius = items.Where(i => i.Entry.Active).Select(i => Vector3.Distance(p, i.Entry.Anchor.position)).DefaultIfEmpty(1).Max() + Mathf.Max(F("diameter"), F("width"));
            Write("GalleryTreeSettings", "GalleryReturnIntent", new JObject { ["active"] = true, ["started"] = time, ["world"] = returned?.Entry.Id ?? "", ["x"] = p.x, ["y"] = p.y, ["z"] = p.z, ["radius"] = Mathf.Max(1, radius) });
            foreach (var item in items)
            {
                float alpha = item == returned ? 1 : 0;
                item.OpacitySpring.Reset(alpha);
                item.Alpha = alpha;
                if (item != returned)
                {
                    item.Pickable = false;
                    item.Hit.enabled = false;
                    item.PublishedOpacity = 0;
                    WriteVisibility(item, item.Coverage, 0, false);
                }
            }
            invitationSpring.Reset(0);
            invitationAlpha = 0;
            nextVisibility = 0;
            TriggerGlass(false);
        }
        void EndCinematicReturn()
        {
            var intent = ReturnIntent;
            if (!(bool)intent["active"])
                return;
            intent["active"] = false;
            Write("GalleryTreeSettings", "GalleryReturnIntent", intent);
        }
        void ResetReturnPresentation()
        {
            Write("GalleryTreeSettings", "GalleryReturnIntent", new JObject { ["active"] = false, ["started"] = time, ["world"] = "", ["x"] = 0, ["y"] = 0, ["z"] = 0, ["radius"] = 1 });
            Write("GalleryTreeSettings", "GalleryGlassIntent", new JObject { ["started"] = time - 100, ["entering"] = true });
        }
        void CancelCinematicReturn()
        {
            EndCinematicReturn();
            navigation["returning"] = false;
            WriteNavigation();
            DeactivateZoomAnchor();
            view["yaw"] = currentYaw;
            view["pitch"] = currentPitch;
            view["zoom"] = currentZoom;
            WriteView();
        }
        void ApplyCinematicReturn()
        {
            var start = Read("GalleryExitGallery", "GalleryView");
            float t = ReturnProgress;
            float eased = t * t * t * (t * (t * 6 - 15) + 10);
            currentCenter = new Vector3((float)start["x"], (float)start["y"], (float)start["z"]);
            currentZoom = (float)start["zoom"];
            currentYaw = (float)start["yaw"];
            currentPitch = Mathf.Lerp((float)start["pitch"], 90, eased);
            centerVelocity = Vector3.zero;
            zoomVelocity = 0;
        }
        void ApplyBandReveal(MaterialPropertyBlock p)
        {
            var intent = ReturnIntent;
            p.SetFloat("_RevealActive", string.IsNullOrEmpty((string)intent["world"]) || ReturnReveal >= .999f ? 0 : 1);
            p.SetFloat("_RevealProgress", ReturnReveal);
            p.SetFloat("_RevealRadius", (float)intent["radius"]);
            p.SetVector("_RevealCenter", new Vector3((float)intent["x"], (float)intent["y"], (float)intent["z"]));
        }
        public Color WaveAccent(int layer, float angle)
        {
            if (layer < 1 || layer > layers.Count)
                return Theme.Ring;
            var origin = layers[layer - 1].transform.position;
            float left = -360, right = 360;
            Color a = Theme.Ring, b = Theme.Ring;
            foreach (var item in items)
            {
                if (!item.Entry.Active || item.Level != layer)
                    continue;
                var offset = item.Entry.Anchor.position - origin;
                float delta = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, Mathf.Atan2(offset.z, offset.x) * Mathf.Rad2Deg);
                if (Mathf.Abs(delta) < .001f)
                    return item.Entry.Preview.Theme;
                float below = delta > 0 ? delta - 360 : delta, above = delta < 0 ? delta + 360 : delta;
                if (below > left)
                {
                    left = below;
                    a = item.Entry.Preview.Theme;
                }
                if (above < right)
                {
                    right = above;
                    b = item.Entry.Preview.Theme;
                }
            }
            return Color.Lerp(a, b, -left / Mathf.Max(.001f, right - left));
        }
    }
}
#endif
