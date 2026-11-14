using System;
using System.Collections.Generic;
using Runtime.Flecs;
using UnityEngine;

namespace Runtime.Capabilities
{
    /// <summary>
    /// Unity physics for the native places module. Pinned shapes get static colliders, ghosts get trigger colliders that
    /// report crossings, free bodies get a Rigidbody that the forces of their places move. The native module decides
    /// which place a body is in; this only reports crossings, applies the resulting forces and writes poses back.
    /// A free body's view stays directly under the world root while it is simulated (a physics body must not sit under a
    /// scaled or moving parent), so its Position is written in the space of its Flecs parent. One presenter per FlecsWorld.
    /// </summary>
    public sealed class BodyPresenter : IDisposable
    {
        const string OwnerPath = "core.places";
        const string FreePath = "Free";
        const string ForcePath = "EffectiveForce";
        // The body and its Flecs parent, so a pose can be written in the parent's space.
        const string ParentQuery = "Free, (ChildOf, $parent)";
        // A tag has no data to read, so shapes are queried together with their Position.
        const string PinnedQuery = "Position, Pinned";
        const string GhostQuery = "Position, Ghost";
        const int RetryTicks = 15;
        const float MoveTolerance = 0.001f; // metres
        const float TurnTolerance = 0.05f; // degrees
        const float MinMass = 0.01f, MaxMass = 10000f;

        enum Role { Pinned, Ghost, Free }

        sealed class Item
        {
            public Role Role;
            public GameObject Root;
            public Collider Collider;
            public Rigidbody Body;
            public PlaceVolume Volume;
            public int Primitive = int.MinValue;
            public int Seen;
            public bool Settled; // Not ours to touch (another collider or pose owner), or done.
            public int NotBefore = int.MinValue; // A rebuilt role waits for the old components to be gone (Play mode destroys at frame end).
            public bool Owned;
            public Vector3 LocalSize = Vector3.one;
            public float Density = 1f, Drag = -1f;
            public ulong Parent;
            public EffectiveForceValues Force;
            public bool HasForce, Wake, Write;
            public Vector3 AppliedGravity, AppliedWind, AppliedLift;
            public float Fraction;
            public BodyContacts Contacts;
            public Vector3 Position, Written;
            public Quaternion Rotation = Quaternion.identity, WrittenRotation = Quaternion.identity;
        }

        sealed class Rows<T> where T : unmanaged
        {
            readonly string expression, variable;
            public int Count;
            public ulong[] Entities = new ulong[64], Targets = new ulong[64];
            public T[] Values = new T[64];

            public Rows(string expression, string variable)
            {
                this.expression = expression;
                this.variable = variable;
            }

            /// <summary>Reads the rows when the query reports a change. True when it did.</summary>
            public bool Refresh(FlecsSession session)
            {
                int handle = session.QueryHandle(expression);
                if (!session.QueryChanged(handle))
                    return false;
                Count = session.ReadQuery(expression, variable, ref Entities, ref Targets, ref Values);
                return true;
            }
        }

        readonly FlecsWorld world;
        readonly Dictionary<ulong, Item> items = new();
        readonly Dictionary<Rigidbody, ulong> bodies = new();
        readonly List<ulong> scratch = new();
        readonly List<ulong> waiting = new();
        readonly Rows<FreeValues> free = new(FreePath, null);
        readonly Rows<EffectiveForceValues> forces = new(ForcePath, null);
        readonly Rows<FreeValues> parents = new(ParentQuery, "parent");
        readonly Rows<PositionValues> pinned = new(PinnedQuery, null);
        readonly Rows<PositionValues> ghosts = new(GhostQuery, null);
        FlecsSession session;
        ulong owner, entered, left, touched;
        // One bump counts once: a pair is reported again only after it has been apart this long (simulated seconds).
        const float ApartSeconds = 0.3f;
        readonly Dictionary<Collider, ulong> colliders = new();
        readonly Dictionary<(ulong, ulong), float> contacts = new();
        float clock;
        int sentTouched;
        int generation, ticks, lastRetry, viewCount;
        bool rebuilding; // An entity changed role in Play mode: it is built on the next tick, not after RetryTicks.
        bool rescan; // A body was built after the rows were read: its parent and forces are read again.
        FlecsSession.Event[] events = new FlecsSession.Event[32];
        int eventCount, sentEntered, sentLeft, writes;
        string lastSkip, lastCrossing;
        int ignoredExits;
        ulong[] writeIds = new ulong[64];
        PositionValues[] writePositions = new PositionValues[64];
        OrientationValues[] writeOrientations = new OrientationValues[64];

        public BodyPresenter(FlecsWorld world)
        {
            this.world = world;
        }

        /// <summary>Entities that have a shape or a body from this presenter.</summary>
        public int TrackedCount => items.Count;

        /// <summary>Crossings waiting to be sent with the next Step.</summary>
        public int PendingCrossings => eventCount;

        /// <summary>Read-only state for failure messages: what the presenter holds and why it left something alone.</summary>
        public string Diagnostics
        {
            get
            {
                int bodiesBuilt = 0, shapes = 0, volumes = 0, pose = 0;
                foreach (var item in items.Values)
                {
                    if (item.Role == Role.Free && item.Body)
                        bodiesBuilt++;
                    else if (item.Role == Role.Ghost && item.Volume)
                        volumes++;
                    else if (item.Role == Role.Pinned && item.Collider)
                        shapes++;
                    if (item.Owned)
                        pose++;
                }
                return "bodies{owner=" + (owner != 0 ? "found" : "missing") + ", rows free=" + free.Count + " pinned=" + pinned.Count
                    + " ghosts=" + ghosts.Count + " parents=" + parents.Count + " forces=" + forces.Count + "; built bodies=" + bodiesBuilt
                    + " pinned=" + shapes + " ghosts=" + volumes + " poseOwned=" + pose + " waiting=" + waiting.Count
                    + "; ignored exits=" + ignoredExits + "; sent entered=" + sentEntered + " left=" + sentLeft + " touched=" + sentTouched + "; writes=" + writes
                    + "; last skip=" + (lastSkip ?? "none") + "; last crossing=" + (lastCrossing ?? "none") + "}";
            }
        }

        /// <summary>Also count the collisions of every free body (for tests that look into a trajectory).</summary>
        public bool TraceContacts;

        /// <summary>The forces applied to a body in its last step, its damping and its contacts: for failure messages.</summary>
        public string Trace(ulong entity)
        {
            if (!items.TryGetValue(entity, out var item) || !item.Body)
                return "no body";
            return "gravity " + item.AppliedGravity + " wind " + item.AppliedWind + " lift " + item.AppliedLift + " submerged " + item.Fraction.ToString("0.00")
                + " damping " + item.Body.linearDamping.ToString("0.00") + " parent " + item.Parent + " contacts "
                + (item.Contacts ? item.Contacts.Count + " last " + item.Contacts.Last : "not traced");
        }

        public Rigidbody BodyOf(ulong entity) => items.TryGetValue(entity, out var item) ? item.Body : null;

        /// <summary>
        /// One physics step's worth of adapter work: shapes and bodies follow the world, poses are written back, forces
        /// are applied and crossings are sent. Call it before the physics step; <paramref name="dt"/> is that step's length.
        /// </summary>
        public void Step(float dt)
        {
            if (!Ready())
                return;
            NotePoses();
            clock += dt;
            ticks++;
            bool changed = free.Refresh(session);
            changed |= parents.Refresh(session);
            changed |= pinned.Refresh(session);
            changed |= ghosts.Refresh(session);
            bool forcesChanged = forces.Refresh(session);
            if (changed)
                Reconcile();
            else if (waiting.Count > 0 && (rebuilding || world.ViewCount != viewCount || ticks - lastRetry >= RetryTicks))
                Retry();
            if (changed || forcesChanged || rescan)
                ReadForces(changed || rescan);
            rescan = false;
            WriteBack();
            ApplyForces(dt);
            Send();
        }

        // The world pose of each free body, as physics left it.
        void NotePoses()
        {
            foreach (var item in items.Values)
            {
                if (item.Role != Role.Free || !item.Body || !item.Root)
                    continue;
                item.Position = item.Body.position;
                item.Rotation = item.Body.rotation;
            }
        }

        bool Ready()
        {
            if (!world)
                return false;
            var current = world.Session;
            if (current == null || current.IsDisposed)
            {
                if (session != null)
                    Forget();
                return false;
            }
            if (!ReferenceEquals(current, session))
            {
                Forget();
                session = current;
                owner = session.Lookup(OwnerPath);
                // A native library without the places module has no bodies.
                if (owner == 0)
                    return false;
                entered = session.Lookup("PlaceEntered");
                touched = session.Lookup("Touched");
                left = session.Lookup("PlaceLeft");
                // Check the mirrors against the native layout (throws on a mismatch).
                session.TryRead<FreeValues>(owner, FreePath, out _);
                session.TryRead<EffectiveForceValues>(owner, ForcePath, out _);
                session.TryRead<PositionValues>(owner, "Position", out _);
                session.TryRead<OrientationValues>(owner, "Orientation", out _);
            }
            return owner != 0;
        }

        // Shapes and bodies follow the rows: new ones are built, changed ones reshaped, vanished ones given back.
        void Reconcile()
        {
            generation++;
            waiting.Clear();
            for (int i = 0; i < free.Count; i++)
                Ensure(free.Entities[i], Role.Free, free.Values[i].density);
            for (int i = 0; i < ghosts.Count; i++)
                if (!SeenAsFree(ghosts.Entities[i]))
                    Ensure(ghosts.Entities[i], Role.Ghost, 1f);
            for (int i = 0; i < pinned.Count; i++)
                if (!SeenAsFree(pinned.Entities[i]) && !SeenNow(pinned.Entities[i]))
                    Ensure(pinned.Entities[i], Role.Pinned, 1f);
            scratch.Clear();
            foreach (var pair in items)
                if (pair.Value.Seen != generation)
                    scratch.Add(pair.Key);
            for (int i = 0; i < scratch.Count; i++)
                Remove(scratch[i]);
            lastRetry = ticks;
            viewCount = world.ViewCount;
        }

        bool SeenAsFree(ulong entity) => items.TryGetValue(entity, out var item) && item.Seen == generation && item.Role == Role.Free;
        bool SeenNow(ulong entity) => items.TryGetValue(entity, out var item) && item.Seen == generation;

        void Ensure(ulong entity, Role role, float density)
        {
            if (items.TryGetValue(entity, out var item) && item.Role != role)
            {
                Remove(entity);
                item = null;
                // Play mode destroys the old collider only at the end of the frame: building now would find it and settle for good.
                if (Application.isPlaying)
                {
                    items.Add(entity, item = new Item { Role = role, Seen = generation, NotBefore = Time.frameCount + 1, Density = role == Role.Free ? density : 1f });
                    rebuilding = true;
                    waiting.Add(entity);
                    return;
                }
            }
            if (item == null)
                items.Add(entity, item = new Item { Role = role });
            item.Seen = generation;
            if (role == Role.Free && item.Density != density)
            {
                item.Density = density;
                if (item.Body)
                    item.Body.mass = MassOf(item);
            }
            if (!Build(entity, item))
                waiting.Add(entity);
        }

        void Retry()
        {
            for (int i = waiting.Count - 1; i >= 0; i--)
                if (!items.TryGetValue(waiting[i], out var item) || Build(waiting[i], item))
                    waiting.RemoveAt(i);
            rebuilding = false;
            for (int i = 0; i < waiting.Count; i++)
                if (items.TryGetValue(waiting[i], out var pending) && pending.NotBefore != int.MinValue)
                    rebuilding = true;
            lastRetry = ticks;
            viewCount = world.ViewCount;
        }

        // True when the entity is settled (built or deliberately left alone); false when its view or pose owner is not ready.
        bool Build(ulong entity, Item item)
        {
            if (item.Settled)
                return true;
            if (Time.frameCount < item.NotBefore)
                return false;
            var view = world.FindView(entity);
            if (!view)
            {
                lastSkip = item.Role + " " + entity + ": no view yet";
                return false;
            }
            int primitive = session.TryRead<PrimitiveValues>(entity, "Primitive", out var p) ? p.kind : -1;
            if (item.Root == view)
            {
                if (item.Primitive != primitive)
                    Reshape(entity, item, primitive);
                return true;
            }
            // A collider or body that is not ours belongs to someone else.
            if (view.TryGetComponent<Collider>(out _) || view.TryGetComponent<Rigidbody>(out _))
            {
                item.Settled = true;
                lastSkip = item.Role + " " + view.name + ": the view already has a collider or Rigidbody";
                return true;
            }
            if (item.Role == Role.Free)
            {
                if (!item.Owned)
                {
                    // Held by another owner: tried again now and then.
                    if (!world.AcquirePose(entity, owner))
                    {
                        lastSkip = "Free " + view.name + ": pose held by another owner";
                        return false;
                    }
                    item.Owned = true;
                }
                Vector3 position = view.transform.position;
                Quaternion rotation = view.transform.rotation;
                world.SetDetached(entity, true);
                item.Position = item.Written = position;
                item.Rotation = item.WrittenRotation = rotation;
                item.Write = false;
            }
            item.Root = view;
            item.Primitive = primitive;
            item.Collider = MakeCollider(view, primitive, out item.LocalSize);
            colliders[item.Collider] = entity;
            switch (item.Role)
            {
                case Role.Ghost:
                    item.Collider.isTrigger = true;
                    item.Volume = view.AddComponent<PlaceVolume>();
                    item.Volume.Presenter = this;
                    item.Volume.Entity = entity;
                    item.Volume.Shape = item.Collider;
                    break;
                case Role.Free:
                    var body = view.AddComponent<Rigidbody>();
                    body.useGravity = false;
                    body.linearDamping = 0f;
                    item.Body = body;
                    body.mass = MassOf(item);
                    item.Contacts = view.AddComponent<BodyContacts>();
                    item.Contacts.Presenter = this;
                    item.Contacts.Entity = entity;
                    item.Contacts.Describe = TraceContacts;
                    bodies[body] = entity;
                    rescan = true;
                    break;
            }
            return true;
        }

        void Reshape(ulong entity, Item item, int primitive)
        {
            if (!ReferenceEquals(item.Collider, null))
                colliders.Remove(item.Collider);
            if (item.Collider)
                Destroy(item.Collider);
            item.Primitive = primitive;
            item.Collider = MakeCollider(item.Root, primitive, out item.LocalSize);
            colliders[item.Collider] = entity;
            if (item.Role == Role.Ghost)
            {
                item.Collider.isTrigger = true;
                if (item.Volume)
                    item.Volume.Shape = item.Collider;
            }
            if (item.Body)
                item.Body.mass = MassOf(item);
        }

        // Cube, sphere and capsule match their primitive; anything else is a box around the renderer.
        static Collider MakeCollider(GameObject root, int primitive, out Vector3 size)
        {
            switch (primitive)
            {
                case 0:
                    size = Vector3.one;
                    return root.AddComponent<BoxCollider>();
                case 1:
                    size = Vector3.one;
                    return root.AddComponent<SphereCollider>().Radius(0.5f);
                case 2:
                    var capsule = root.AddComponent<CapsuleCollider>();
                    capsule.radius = 0.5f;
                    capsule.height = 2f;
                    capsule.direction = 1;
                    size = new Vector3(1f, 2f, 1f);
                    return capsule;
            }
            var box = root.AddComponent<BoxCollider>();
            size = Vector3.one;
            var visual = root.transform.Find("Visual");
            if (visual && visual.TryGetComponent<Renderer>(out var renderer))
            {
                var bounds = renderer.bounds;
                var scale = root.transform.lossyScale;
                box.center = root.transform.InverseTransformPoint(bounds.center);
                size = new Vector3(Mathf.Max(Divide(bounds.size.x, scale.x), 0.01f), Mathf.Max(Divide(bounds.size.y, scale.y), 0.01f),
                    Mathf.Max(Divide(bounds.size.z, scale.z), 0.01f));
                box.size = size;
            }
            return box;
        }

        static float Divide(float value, float by) => Mathf.Abs(by) > 1e-6f ? value / Mathf.Abs(by) : value;

        // Density times the volume of the body's bounds.
        static float MassOf(Item item)
        {
            var scale = item.Root.transform.lossyScale;
            float volume = Mathf.Abs(scale.x * scale.y * scale.z) * item.LocalSize.x * item.LocalSize.y * item.LocalSize.z;
            return Mathf.Clamp(item.Density * volume, MinMass, MaxMass);
        }

        // Parents and forces of the free bodies, as the native module resolved them.
        void ReadForces(bool shapesChanged)
        {
            if (shapesChanged)
            {
                foreach (var item in items.Values)
                    if (item.Role == Role.Free)
                        item.Parent = 0;
                for (int i = 0; i < parents.Count; i++)
                    if (items.TryGetValue(parents.Entities[i], out var item) && item.Role == Role.Free)
                    {
                        if (item.Parent != parents.Targets[i])
                            item.Write = true;
                        item.Parent = parents.Targets[i];
                    }
            }
            for (int i = 0; i < forces.Count; i++)
            {
                if (!items.TryGetValue(forces.Entities[i], out var item) || item.Role != Role.Free)
                    continue;
                if (item.HasForce && EffectiveForceValues.Same(item.Force, forces.Values[i]))
                    continue;
                item.Force = forces.Values[i];
                item.HasForce = true;
                item.Wake = true;
            }
        }

        // Positions and orientations of the bodies that moved, in the space of their Flecs parent, in one write each.
        void WriteBack()
        {
            int count = 0;
            foreach (var pair in items)
            {
                var item = pair.Value;
                if (item.Role != Role.Free || !item.Owned || !item.Body || !item.Root)
                    continue;
                if (!item.Write && (item.Position - item.Written).sqrMagnitude < MoveTolerance * MoveTolerance
                    && Quaternion.Angle(item.Rotation, item.WrittenRotation) < TurnTolerance)
                    continue;
                Transform space = world.transform;
                if (item.Parent != 0)
                {
                    var view = world.FindView(item.Parent);
                    // A parent that is not a view (a world entity has no Position) is the world's own space.
                    if (view)
                        space = view.transform;
                    else if (session.TryRead<PositionValues>(item.Parent, "Position", out _))
                        continue;
                }
                if (count == writeIds.Length)
                {
                    Array.Resize(ref writeIds, count * 2);
                    Array.Resize(ref writePositions, count * 2);
                    Array.Resize(ref writeOrientations, count * 2);
                }
                var local = space.InverseTransformPoint(item.Position);
                var turn = Quaternion.Inverse(space.rotation) * item.Rotation;
                writeIds[count] = pair.Key;
                writePositions[count] = new PositionValues { x = local.x, y = local.y, z = local.z };
                writeOrientations[count] = new OrientationValues { x = turn.x, y = turn.y, z = turn.z, w = turn.w };
                item.Written = item.Position;
                item.WrittenRotation = item.Rotation;
                item.Write = false;
                count++;
            }
            if (count == 0)
                return;
            session.WriteBatch("Position", writeIds, writePositions, count);
            session.WriteBatch("Orientation", writeIds, writeOrientations, count);
            writes += count;
        }

        // Gravity and wind, lift in a fluid and drag, for bodies that are awake (or whose forces just changed).
        void ApplyForces(float dt)
        {
            foreach (var item in items.Values)
            {
                if (item.Role != Role.Free || !item.Body || !item.HasForce)
                    continue;
                var body = item.Body;
                if (item.Wake)
                {
                    body.WakeUp();
                    item.Wake = false;
                }
                else if (body.IsSleeping())
                    continue;
                var force = item.Force;
                Vector3 gravity = force.gravity.ToVector();
                Vector3 acceleration = gravity + force.wind.ToVector();
                item.AppliedGravity = gravity;
                item.AppliedWind = force.wind.ToVector();
                item.AppliedLift = Vector3.zero;
                item.Fraction = 0f;
                if (force.fluid > 0f && force.volume != 0 && items.TryGetValue(force.volume, out var place) && place.Collider)
                {
                    var box = item.Collider.bounds;
                    float height = box.size.y;
                    float fraction = height > 1e-6f ? Mathf.Clamp01((place.Collider.bounds.max.y - box.min.y) / height) : 0f;
                    float density = force.density > 0f ? force.density : 1f;
                    item.AppliedLift = -gravity * (force.fluid / density) * fraction;
                    item.Fraction = fraction;
                    acceleration += item.AppliedLift;
                }
                // Damping above 1 / dt would reverse the velocity.
                float drag = dt > 0f ? Mathf.Min(force.drag, 0.9f / dt) : force.drag;
                if (item.Drag != drag)
                {
                    body.linearDamping = drag;
                    item.Drag = drag;
                }
                body.AddForce(acceleration, ForceMode.Acceleration);
            }
        }

        // Called by a ghost's trigger while physics runs: the crossing is sent with the next Step.
        internal void Crossed(PlaceVolume volume, Collider other, bool enter)
        {
            var body = other.attachedRigidbody;
            if (!body || !bodies.TryGetValue(body, out ulong entity) || !volume.Shape || session == null || session.IsDisposed)
                return;
            // A trigger can report an exit although the shapes still overlap (for example when a body falls asleep): not a crossing.
            if (!enter)
            {
                var own = bodyCollider(body);
                if (own && volume.Shape && Physics.ComputePenetration(own, body.position, body.rotation, volume.Shape,
                    volume.Shape.transform.position, volume.Shape.transform.rotation, out _, out _))
                {
                    ignoredExits++;
                    return;
                }
            }
            if (enter)
                Touch(entity, volume.Entity);
            ulong kind = enter ? entered : left;
            if (kind == 0)
                return;
            Reserve();
            var size = volume.Shape.bounds.size;
            var point = world.transform.InverseTransformPoint(body.position);
            lastCrossing = (enter ? "entered " : "left ") + volume.name + " by " + body.name + " at " + point;
            events[eventCount++] = new FlecsSession.Event
            {
                Kind = kind,
                Entity = entity,
                Other = volume.Entity,
                X = point.x,
                Y = point.y,
                Z = point.z,
                Value = Mathf.Pow(Mathf.Max(size.x * size.y * size.z, 0f), 1f / 3f)
            };
        }

        void Reserve()
        {
            if (eventCount == events.Length)
                Array.Resize(ref events, eventCount * 2);
        }

        // A free body began to touch (or keeps touching) a collider of another element.
        internal void Collided(ulong entity, Collider other, bool beginning)
        {
            if (other && colliders.TryGetValue(other, out ulong second))
                Touch(entity, second, beginning);
        }

        // Sends one Touched fact for a pair, and not again until the two have been apart for ApartSeconds.
        void Touch(ulong a, ulong b, bool beginning = true)
        {
            if (a == b || touched == 0 || session == null || session.IsDisposed)
                return;
            var key = a < b ? (a, b) : (b, a);
            bool known = contacts.TryGetValue(key, out float last);
            contacts[key] = clock;
            if (!beginning || (known && clock - last < ApartSeconds))
                return;
            Reserve();
            lastCrossing = "touched " + a + " and " + b;
            events[eventCount++] = new FlecsSession.Event { Kind = touched, Entity = a, Other = b };
        }

        void ForgetContacts(ulong entity)
        {
            if (contacts.Count == 0)
                return;
            scratchPairs.Clear();
            foreach (var key in contacts.Keys)
                if (key.Item1 == entity || key.Item2 == entity)
                    scratchPairs.Add(key);
            for (int i = 0; i < scratchPairs.Count; i++)
                contacts.Remove(scratchPairs[i]);
        }

        readonly List<(ulong, ulong)> scratchPairs = new();

        Collider bodyCollider(Rigidbody body) => bodies.TryGetValue(body, out ulong id) && items.TryGetValue(id, out var item) ? item.Collider : null;

        void Send()
        {
            if (eventCount == 0)
                return;
            int count = eventCount;
            eventCount = 0;
            for (int i = 0; i < count; i++)
                if (events[i].Kind == entered)
                    sentEntered++;
                else if (events[i].Kind == touched)
                    sentTouched++;
                else
                    sentLeft++;
            session.Emit(events, count);
            // The native side re-parents and writes the position itself: the next pose is written again in the new space.
            for (int i = 0; i < count; i++)
                if (events[i].Kind != touched && items.TryGetValue(events[i].Entity, out var item))
                    item.Write = true;
        }

        // Takes the added components off an entity and gives its pose back. Not called for views that are gone.
        void Strip(ulong entity, Item item, bool alive)
        {
            if (!ReferenceEquals(item.Collider, null))
                colliders.Remove(item.Collider);
            if (!ReferenceEquals(item.Body, null))
                bodies.Remove(item.Body);
            if (item.Volume)
                Destroy(item.Volume);
            if (item.Body)
                Destroy(item.Body);
            if (item.Contacts)
                Destroy(item.Contacts);
            if (item.Collider)
                Destroy(item.Collider);
            if (!item.Owned)
                return;
            item.Owned = false;
            if (!alive)
                return;
            world.SetDetached(entity, false);
            world.ReleasePose(entity, owner);
        }

        void Remove(ulong entity)
        {
            if (!items.Remove(entity, out var item))
                return;
            ForgetContacts(entity);
            Strip(entity, item, session != null && !session.IsDisposed);
        }

        static void Destroy(UnityEngine.Object value)
        {
            if (!value)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(value);
            else
                UnityEngine.Object.DestroyImmediate(value);
        }

        // The session was replaced or disposed: its views are gone with it.
        void Forget()
        {
            foreach (var pair in items)
                Strip(pair.Key, pair.Value, false);
            items.Clear();
            bodies.Clear();
            colliders.Clear();
            contacts.Clear();
            waiting.Clear();
            rebuilding = false;
            eventCount = 0;
            session = null;
            owner = entered = left = 0;
            free.Count = parents.Count = forces.Count = pinned.Count = ghosts.Count = 0;
        }

        public void Dispose()
        {
            if (session != null && !session.IsDisposed)
            {
                scratch.Clear();
                scratch.AddRange(items.Keys);
                for (int i = 0; i < scratch.Count; i++)
                    Remove(scratch[i]);
            }
            Forget();
        }
    }

    static class ColliderExtensions
    {
        public static SphereCollider Radius(this SphereCollider collider, float radius)
        {
            collider.radius = radius;
            return collider;
        }
    }
}
