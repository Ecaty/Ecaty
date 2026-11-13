using Runtime.Capabilities;
#if CAP_FLEXALON
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Flexalon;
#endif
using Runtime.Flecs;
namespace Game.Adapters
{
    // Always compiled: the schema and saved configurations stay valid without the plugin.
    [Capability("CapLayout", "spatial layouts", 45, Gate = "CAP_FLEXALON", Probe = "Assets/Flexalon/Runtime/Layouts/FlexalonCircleLayout.cs")]
    public sealed class LayoutConfig
    {
        public float radius = 1, spacing = 45;
    }
#if CAP_FLEXALON
    // Position { x, y, z } of the native core; only its size matters for the typed child query.
    struct PositionData
    {
        public float x, y, z;
    }
    public sealed class LayoutCapability : CapabilityInstance<LayoutConfig>
    {
        FlexalonCircleLayout layout; bool owned; readonly System.Collections.Generic.Dictionary<ulong, FlexalonObject> children = new(); readonly System.Collections.Generic.List<FlexalonObject> created = new();
        // Typed child query read only when Flecs reports a change (or a child still has no view); no JSON, nothing rebuilt per tick.
        string childQuery; ulong[] childIds = new ulong[16], childParents = new ulong[16]; PositionData[] childPositions = new PositionData[16];
        readonly System.Collections.Generic.HashSet<ulong> seenChildren = new(); readonly System.Collections.Generic.List<ulong> staleChildren = new();
        bool pendingChildren; int unityChildren = int.MinValue;
        public LayoutCapability(CapabilityContext c) : base(c) { layout = Target.GetComponent<FlexalonCircleLayout>(); if (layout) throw new InvalidOperationException("Target already has a layout owner."); layout = Target.gameObject.AddComponent<FlexalonCircleLayout>(); owned = true; }
        void BindChildren()
        {
            childQuery ??= "Position, (ChildOf, " + Context.Session.EntityPath(Context.TargetEntity) + ")";
            pendingChildren = false;
            int rows = Context.Session.ReadQuery(childQuery, "", ref childIds, ref childParents, ref childPositions);
            seenChildren.Clear();
            for (int i = 0; i < rows; i++)
            {
                ulong id = childIds[i];
                seenChildren.Add(id);
                if (children.ContainsKey(id))
                    continue;
                var child = Context.Resolve(id);
                if (!child)
                {
                    pendingChildren = true; // no view yet: look again next tick
                    continue;
                }
                if (child.GetComponent<CharacterController>() || child.GetComponent<Rigidbody>())
                    throw new InvalidOperationException("Layout cannot own an active motor/body child.");
                if (!Context.Runtime.World.AcquirePose(id, Context.Entity))
                    throw new InvalidOperationException("Layout child already has a movement owner.");
                var item = child.GetComponent<FlexalonObject>();
                if (!item)
                {
                    item = child.gameObject.AddComponent<FlexalonObject>();
                    created.Add(item);
                }
                children[id] = item;
            }
            staleChildren.Clear();
            foreach (var id in children.Keys)
                if (!seenChildren.Contains(id))
                    staleChildren.Add(id);
            foreach (var id in staleChildren)
            {
                Context.Runtime.World.ReleasePose(id, Context.Entity);
                children.Remove(id);
            }
            SkipOwnChildren();
        }
        // Unity-side children (the binding host, "Visual") are not laid out. Transform.name allocates, so the names
        // are only inspected when the set of child transforms changed.
        void SkipOwnChildren()
        {
            int signature = Target.childCount;
            for (int i = 0; i < Target.childCount; i++)
                signature = signature * 31 + Target.GetChild(i).GetEntityId().GetHashCode();
            if (signature == unityChildren)
                return;
            unityChildren = signature;
            for (int i = 0; i < Target.childCount; i++)
            {
                var child = Target.GetChild(i);
                if (child == Host.transform || child.name == "Visual")
                {
                    var skip = child.GetComponent<FlexalonObject>();
                    if (!skip)
                    {
                        skip = child.gameObject.AddComponent<FlexalonObject>();
                        created.Add(skip);
                    }
                    skip.SkipLayout = true;
                }
            }
        }
        protected override void Apply(LayoutConfig j)
        {
            layout.Radius = Mathf.Max(.01f, j.radius);
            layout.SpacingDegrees = Mathf.Clamp(j.spacing, 0, 360);
            BindChildren();
        }
        public override void Tick(float delta)
        {
            if (childQuery == null || pendingChildren || Context.Session.QueryChanged(Context.Session.QueryHandle(childQuery)))
                BindChildren();
            else
                SkipOwnChildren();
        }
        public override void Dispose()
        {
            foreach (var id in children.Keys)
                Context.Runtime.World.ReleasePose(id, Context.Entity);
            foreach (var item in created)
            if (item)
                UnityEngine.Object.Destroy(item);
            if (owned && layout)
                UnityEngine.Object.Destroy(layout);
        }
    }
#endif
}
