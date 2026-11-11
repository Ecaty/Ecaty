using Ecaty.Flecs;
using UnityEngine;

namespace Ecaty
{
    internal sealed class WorkshopSeed : MonoBehaviour
    {
        private const string Motion = "WorkshopSeedMotion";
        private const string Request = "WorkshopSeedRequest";
        private FlecsWorld world;
        private FlecsSession session;
        private Transform seed;
        private Renderer visual;
        private ulong motionId;
        private int opened;
        public Transform View => seed;
        public Vector3 FocusPosition => world.FindView(motionId).transform.position + Vector3.up * Lift;
        public bool Highlighted
        {
            get; set;
        }
        public bool Visible { get; set; } = true;
        public float Lift
        {
            get; set;
        }

        public void Initialize(FlecsWorld owner)
        {
            world = owner;
            var art = Resources.Load<GameObject>("Workshop/Art/Seed/WorkshopSeedArt");
            seed = art ? Instantiate(art, transform, false).transform : CreateFallback();
            seed.name = "World seed";
            visual = seed.GetComponentInChildren<Renderer>();
            BindSession();
            Sync();
        }

        private Transform CreateFallback()
        {
            var shape = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shape.transform.SetParent(transform, false);
            Destroy(shape.GetComponent<Collider>());
            shape.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("Shared/Rendering/PresentationMaterial");
            return shape.transform;
        }

        public void BindSession()
        {
            if (session == world.Session)
                return;
            session = world.Session;
            if (session.Lookup("WorldSeed") == 0)
                session.SetManagedScript("WorkshopSeedAnchor", Resources.Load<TextAsset>("Workshop/Runtime/WorkshopLobby").text);
            if (session.ManagedScriptSource(Motion + "Source") == null)
                session.SetManagedScript(Motion + "Source", Resources.Load<TextAsset>("Workshop/Runtime/WorkshopSeedMotion").text);
            motionId = session.Lookup(Motion);
            opened = 0;
        }

        public bool ConsumeActivation()
        {
            if (!session.TryRead(motionId, Request, out int count) || count == opened)
                return false;
            opened = count;
            return Visible;
        }

        private void LateUpdate() => Sync();

        private void Sync()
        {
            if (!world || !seed)
                return;
            var pose = world.FindView(motionId);
            seed.gameObject.SetActive(Visible && pose != null);
            if (!seed.gameObject.activeSelf)
                return;
            seed.SetPositionAndRotation(pose.transform.position + Vector3.up * Lift, pose.transform.rotation);
            float scale = Highlighted ? 1.12f : 1f;
            seed.localScale = Vector3.Lerp(seed.localScale, Vector3.one * scale,
                1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
        }

        public bool Hit(Ray ray, out float distance)
        {
            distance = 0;
            return seed && seed.gameObject.activeInHierarchy && visual && visual.bounds.IntersectRay(ray, out distance);
        }
    }
}
