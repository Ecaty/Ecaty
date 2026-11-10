using UnityEngine;

namespace Ecaty
{
    // Boing's post-update pump runs at 10000. Mesh computation must see its final brush poses.
    [DefaultExecutionOrder(20000)]
    public sealed class RuntimeMudRenderer : MudBun.MudRenderer
    {
        private new void LateUpdate() => base.LateUpdate();
    }
}
