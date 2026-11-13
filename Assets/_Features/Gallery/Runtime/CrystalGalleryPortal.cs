#if CAP_FLEXALON
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Game
{
    public sealed partial class CrystalWorldGallery
    {
        GalleryExitFeedback exitFeedback;
        public float ExitFeedbackAmount => exitFeedback?.Value ?? 0;
        public float ExitProjectionError
        {
            get; private set;
        }
        void StoreEntryPortal(Item item)
        {
            Write("GalleryEntryPortal", "GalleryView", new JObject { ["x"] = currentCenter.x, ["y"] = currentCenter.y, ["z"] = currentCenter.z, ["yaw"] = currentYaw, ["pitch"] = currentPitch, ["zoom"] = currentZoom, ["columns"] = 6 });
            Write("GalleryTreeSettings", "GalleryPortal", new JObject { ["world"] = item.Entry.Id });
            var rotation = Quaternion.Inverse(item.Entry.Content.rotation) * output.transform.rotation;
            var forward = rotation * Vector3.forward;
            var position = item.Entry.Content.InverseTransformPoint(output.transform.position);
            float distance = Mathf.Max(.2f, Vector3.Dot(item.Entry.NaturalBounds.center - position, forward));
            var center = position + forward * distance;
            Write("GalleryPortalWorld", "GalleryView", new JObject { ["x"] = center.x, ["y"] = center.y, ["z"] = center.z, ["yaw"] = OrbitYaw(rotation), ["pitch"] = OrbitPitch(rotation), ["zoom"] = distance, ["columns"] = 6 });
        }
        void EnsureEntryPortal()
        {
            if ((string)Read("GalleryTreeSettings", "GalleryPortal")["world"] == CurrentWorldId)
                return;
            if (!byId.TryGetValue(CurrentWorldId, out var item))
                return;
            var local = Read(CurrentWorldId, "GalleryView");
            Write("GalleryPortalWorld", "GalleryView", local);
            Write("GalleryTreeSettings", "GalleryPortal", new JObject { ["world"] = CurrentWorldId });
        }
        void UpdatePortalReturn()
        {
            entryBlend = 1;
            var start = Read("GalleryExitStart", "GalleryView");
            var goal = (JObject)Read("GalleryPortalWorld", "GalleryView").DeepClone();
            var item = byId[CurrentWorldId];
            // Keep the deeper exit distance. Map that lens/scale into the gallery instead of growing the world to the old entry size.
            goal["zoom"] = Mathf.Max((float)start["zoom"], F("diameter") * .6f / Mathf.Max(.0001f, item.Entry.TableScale));
            float blend = Mathf.SmoothStep(0, 1, Mathf.Clamp01((time - (float)navigation["portalAt"]) / profile.ExitAlignmentSeconds));
            var pose = (JObject)goal.DeepClone();
            foreach (string key in new[] { "x", "y", "z", "pitch" })
                pose[key] = Mathf.Lerp((float)start[key], (float)goal[key], blend);
            pose["zoom"] = Mathf.Exp(Mathf.Lerp(Mathf.Log((float)start["zoom"]), Mathf.Log((float)goal["zoom"]), blend));
            pose["yaw"] = Mathf.LerpAngle((float)start["yaw"], (float)goal["yaw"], blend);
            stage.Pose(pose, 0, 0, true);
            stage.Render(true);
            if (blend >= 1)
            {
                var center = item.Entry.Content.TransformPoint(new Vector3((float)goal["x"], (float)goal["y"], (float)goal["z"]));
                var rotation = item.Entry.Content.rotation * OrbitRotation((float)goal["yaw"], (float)goal["pitch"]);
                float galleryDistance = (float)goal["zoom"] * item.Entry.TableScale;
                var landmark = item.Entry.NaturalBounds.center + item.Entry.NaturalBounds.extents * .2f;
                var worldPoint = stage.Camera.WorldToViewportPoint(stage.Shown.Root.transform.TransformPoint(landmark));
                var local = Quaternion.Inverse(rotation) * (item.Entry.Content.TransformPoint(landmark) - (center + rotation * Vector3.back * galleryDistance));
                float lens = 2 * Mathf.Tan(output.fieldOfView * Mathf.Deg2Rad * .5f);
                ExitProjectionError = Vector2.Distance(new Vector2(worldPoint.x, worldPoint.y), new Vector2(.5f + local.x / (local.z * lens * output.aspect), .5f + local.y / (local.z * lens)));
                Write("GalleryExitGallery", "GalleryView", new JObject { ["x"] = center.x, ["y"] = center.y, ["z"] = center.z, ["yaw"] = OrbitYaw(rotation), ["pitch"] = OrbitPitch(rotation), ["zoom"] = Mathf.Max(profile.FullTreeZoom, HomeDistance / ((float)goal["zoom"] * item.Entry.TableScale)), ["columns"] = 6 });
                navigation["portalReturn"] = false;
                WriteNavigation();
                MatchReturnCamera();
                StartTreeReturn(true);
            }
        }
    }
}
#endif
