using UnityEngine;

namespace Runtime.Capabilities
{
    /// <summary>Added to a ghost's trigger collider: reports bodies crossing its edge to the body presenter.</summary>
    public sealed class PlaceVolume : MonoBehaviour
    {
        internal BodyPresenter Presenter;
        internal ulong Entity;
        internal Collider Shape;

        void OnTriggerEnter(Collider other) => Presenter?.Crossed(this, other, true);

        void OnTriggerExit(Collider other) => Presenter?.Crossed(this, other, false);
    }
}
