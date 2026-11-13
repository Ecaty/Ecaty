using UnityEngine;

namespace Runtime.Capabilities
{
    /// <summary>Added to a free body when the presenter traces contacts: counts collisions and remembers what was touched.</summary>
    public sealed class BodyContacts : MonoBehaviour
    {
        public int Count;
        public string Last = "none";

        void OnCollisionEnter(Collision collision)
        {
            Count++;
            var other = collision.collider;
            Last = other ? other.name + " (" + other.GetType().Name + (other.isTrigger ? ", trigger" : "") + ") at " + collision.GetContact(0).point : "?";
        }
    }
}
