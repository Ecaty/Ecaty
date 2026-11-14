using UnityEngine;

namespace Runtime.Capabilities
{
    /// <summary>Added to every free body: reports what it touches to the body presenter (and, when asked, remembers it).</summary>
    public sealed class BodyContacts : MonoBehaviour
    {
        internal BodyPresenter Presenter;
        internal ulong Entity;
        internal bool Describe;
        public int Count;
        public string Last = "none";

        void OnCollisionEnter(Collision collision)
        {
            Count++;
            if (Describe)
            {
                var other = collision.collider;
                Last = other ? other.name + " (" + other.GetType().Name + ") at " + collision.GetContact(0).point : "?";
            }
            Presenter?.Collided(Entity, collision.collider, true);
        }

        void OnCollisionStay(Collision collision) => Presenter?.Collided(Entity, collision.collider, false);
    }
}
