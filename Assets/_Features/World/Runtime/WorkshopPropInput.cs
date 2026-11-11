using Ecaty.Flecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Ecaty
{
    // A short hold arms a movable prop. A drag before that belongs to the camera.
    internal sealed class WorkshopPropInput
    {
        private FlecsSession session;
        private ulong held;
        private float downAt;
        private Plane plane;
        private Vector3 offset;
        private Vector2 lastScreen;
        private bool wasDown;

        public bool IsGrabbing
        {
            get; private set;
        }

        public ulong Resolve(FlecsWorld world, Camera camera, Vector2 screen, bool pressed, bool blocked,
            ulong target, float distance, float now)
        {
            if (session != world.Session)
            {
                session = world.Session;
                held = 0;
                wasDown = IsGrabbing = false;
            }
            bool started = pressed && !wasDown;
            wasDown = pressed;
            if (blocked || !camera || session.Lookup("WorkshopProp") == 0)
            {
                held = 0;
                IsGrabbing = false;
                return target;
            }

            var ray = camera.ScreenPointToRay(screen);
            Transform picked = null;
            foreach (var row in (JArray)JObject.Parse(session.Query("WorkshopProp"))["results"])
            {
                var id = session.Lookup(row["parent"] == null ? (string)row["name"] : row["parent"] + "." + row["name"]);
                var view = world.FindView(id);
                var renderer = view ? view.GetComponentInChildren<Renderer>() : null;
                if (renderer && renderer.bounds.IntersectRay(ray, out float hit) && hit < distance)
                {
                    target = id;
                    picked = view.transform;
                    distance = hit;
                }
            }
            if (started && picked)
            {
                var position = picked.position;
                plane = new Plane(Vector3.up, position);
                if (plane.Raycast(ray, out float hit))
                {
                    held = target;
                    downAt = now;
                    lastScreen = screen;
                    offset = position - ray.GetPoint(hit);
                }
            }
            if (held == 0)
                return target;

            if (pressed && now - downAt >= .34f)
                IsGrabbing = true;
            bool suppress = IsGrabbing;
            if (suppress && screen != lastScreen && plane.Raycast(ray, out float depth))
            {
                lastScreen = screen;
                var point = ray.GetPoint(depth) + offset;
                var path = session.EntityPath(held);
                if (!string.IsNullOrEmpty(path))
                    session.SetComponentJson(path, "Position", JsonConvert.SerializeObject(new
                    {
                        x = point.x,
                        y = point.y,
                        z = point.z
                    }));
            }
            if (!pressed)
            {
                IsGrabbing = false;
                held = 0;
            }
            return suppress ? 0 : target;
        }
    }
}

