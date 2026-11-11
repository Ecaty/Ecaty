using System.Collections.Generic;
using UnityEngine;
#if ECATY_CAP_FLEXALON
using Flexalon;
#endif

namespace Ecaty
{
    public enum WorkshopIcon
    {
        Seed, NewWorld, OpenWorld, SaveWorld, Bell, Playground, Environment, Confirm,
        Paper, Honey, Twilight, Neutral
    }

    internal sealed class WorkshopRadialMenu : MonoBehaviour
    {
        private readonly List<(WorkshopIcon Icon, Transform View, Renderer Surface)> entries = new();
        private Camera camera;
        private Transform center;
        private Transform ring;
        public bool IsOpen => entries.Count > 0;

        public void Initialize(Camera view, Transform anchor)
        {
            camera = view;
            center = anchor;
            ring = new GameObject("Seed choices").transform;
            ring.SetParent(transform, false);
#if ECATY_CAP_FLEXALON
            var layout = ring.gameObject.AddComponent<FlexalonCircleLayout>();
            layout.Plane = global::Flexalon.Plane.XY;
            layout.Radius = .55f;
            layout.SpacingType = FlexalonCircleLayout.SpacingOptions.Evenly;
            layout.StartAtDegrees = 90;
            layout.Rotate = FlexalonCircleLayout.RotateOptions.None;
#endif
        }

        public static string Resource(WorkshopIcon icon) =>
            icon == WorkshopIcon.Seed ? "Workshop/Art/Seed/WorkshopSeedArt" : "Workshop/Art/Icons/" + icon;

        public void Show(IReadOnlyList<WorkshopIcon> icons)
        {
            Close();
            foreach (var icon in icons)
            {
                var prefab = Resources.Load<GameObject>(Resource(icon));
                if (!prefab)
                {
                    Debug.LogError("Missing workshop icon: " + icon);
                    continue;
                }
                var view = Instantiate(prefab, ring, false).transform;
                view.name = "Seed choice " + icon;
                view.localScale = Vector3.one * .22f;
#if ECATY_CAP_FLEXALON
                view.gameObject.AddComponent<FlexalonObject>();
#endif
                entries.Add((icon, view, view.GetComponentInChildren<Renderer>()));
            }
#if ECATY_CAP_FLEXALON
            global::Flexalon.Flexalon.GetOrCreate().UpdateDirtyNodes();
#endif
            Layout();
        }

        public void Close()
        {
            foreach (var entry in entries)
            {
                entry.View.gameObject.SetActive(false);
                Destroy(entry.View.gameObject);
            }
            entries.Clear();
        }

        public WorkshopIcon? Pick(Vector2 point)
        {
            var ray = camera.ScreenPointToRay(point);
            WorkshopIcon? hit = null;
            float nearest = float.PositiveInfinity;
            foreach (var entry in entries)
            {
                if (!entry.Surface)
                    continue;
                var bounds = entry.Surface.bounds;
                bounds.Expand(.065f * ring.localScale.x);
                if (!bounds.IntersectRay(ray, out float distance) || distance >= nearest)
                    continue;
                nearest = distance;
                hit = entry.Icon;
            }
            return hit;
        }

        public void Highlight(WorkshopIcon? icon)
        {
            foreach (var entry in entries)
                entry.View.localScale = Vector3.one * (entry.Icon == icon ? .26f : .22f);
        }

        private void LateUpdate()
        {
            if (IsOpen)
                Layout();
        }

        private void Layout()
        {
            ring.SetPositionAndRotation(center.position, camera.transform.rotation);
            ring.localScale = Vector3.one * (camera.orthographicSize * Mathf.Min(1, camera.aspect));
#if !ECATY_CAP_FLEXALON
            for (int i = 0; i < entries.Count; i++)
            {
                float angle = Mathf.PI * (.5f - 2f * i / entries.Count);
                entries[i].View.localPosition = new Vector3(Mathf.Cos(angle) * .55f, Mathf.Sin(angle) * .55f, 0);
            }
#endif
        }
    }
}
