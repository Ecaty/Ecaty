using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ecaty.Flecs;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Ecaty
{
    // Boing finishes at 10000, this layer at 15000, MudBun at 20000, observations at 25000.
    [DefaultExecutionOrder(15000)]
    public sealed class RuntimePoseBridge : MonoBehaviour
    {
        private ClayCreation creation;
        private FlecsSession session;
        private readonly Dictionary<Transform, (Vector3 Position, Quaternion Rotation)> bases = new();
        private readonly HashSet<string> applied = new(StringComparer.Ordinal);
        private struct Offset { public float X,Y,Z,Rx,Ry,Rz; }
        private ulong[] commandIds = new ulong[32], targetIds = new ulong[32];
        private Offset[] commands = new Offset[32];
        private struct Observation {
            public float X,Y,Z,Rx,Ry,Rz,Sx,Sy,Sz,Lx,Ly,Lz;
            public byte Applied;
            public ulong Frame;
        }
        private ulong[] observedIds = new ulong[128];
        private Observation[] observedValues = new Observation[128];
        public string Error { get; private set; }

        public void Initialize(ClayCreation model, FlecsSession owner)
        {
            creation = model; session = owner;
            if (session.ManagedScriptSource("PoseSchema") == null)
                session.SetManagedScript("PoseSchema", Resources.Load<TextAsset>("Runtime/Schemas/PoseSchema").text);
            var reset = GetComponent<RuntimePoseReset>();
            if (!reset) reset = gameObject.AddComponent<RuntimePoseReset>();
            reset.Bridge = this;
            var observer = GetComponent<RuntimePoseObservation>();
            if (!observer) observer = gameObject.AddComponent<RuntimePoseObservation>();
            observer.Bridge = this;
        }

        // Restore only our own additive changes before any procedural pose writer runs again.
        public void Restore()
        {
            foreach (var pair in bases)
                if (pair.Key) pair.Key.SetLocalPositionAndRotation(pair.Value.Position, pair.Value.Rotation);
            bases.Clear(); applied.Clear();
        }

        private void LateUpdate()
        {
            if (!creation || session == null || session.IsDisposed || !creation.PresentationRoot) return;
            Error = null;
            var totals = new Dictionary<string, (Vector3 Position, Vector3 Euler)>(StringComparer.Ordinal);
            var paths = new Dictionary<ulong, string>();
            foreach (var node in creation.RuntimeNodes) { ulong id = session.Lookup(node.Key); if (id != 0) paths[id] = node.Key; }
            int count = session.ReadQuery("[in] PoseOffset, (PoseTarget, $target)", "target", ref commandIds, ref targetIds, ref commands);
            for (int i = 0; i < count; i++)
            {
                if (!paths.TryGetValue(targetIds[i], out string path) || !creation.RuntimeNodes.TryGetValue(path, out var target) || !target) continue;
                var value = commands[i];
                var offset = new Vector3(value.X, value.Y, value.Z); var euler = new Vector3(value.Rx, value.Ry, value.Rz);
                if (!Finite(offset) || !Finite(euler)) { Error = "Non-finite PoseOffset targeting " + path; continue; }
                totals.TryGetValue(path, out var total); totals[path] = (total.Position + offset, total.Euler + euler);
            }
            // Sum channels once per target so command iteration order cannot change the result.
            foreach (var command in totals)
            {
                var target = creation.RuntimeNodes[command.Key]; var pose = command.Value;
                if (!Finite(pose.Position) || !Finite(pose.Euler) || pose.Position.sqrMagnitude > 100)
                { Error = "Invalid combined PoseOffset on " + command.Key + ": finite values and translation length <= 10 required."; continue; }
                bases[target] = (target.localPosition, target.localRotation);
                target.SetLocalPositionAndRotation(target.localPosition + pose.Position, target.localRotation * Quaternion.Euler(pose.Euler));
                applied.Add(command.Key);
            }
        }

        public void Publish()
        {
            if (!creation || session == null || session.IsDisposed || !creation.PresentationRoot) return;
            int count = 0;
            foreach (var node in creation.RuntimeNodes)
            {
                var target = node.Value;
                if (!target) continue;
                var p = target.position; var r = target.eulerAngles; var scale = target.lossyScale; var local = target.localPosition;
                if (!Finite(p) || !Finite(r) || !Finite(scale) || !Finite(local)) continue;
                ulong id = session.Lookup(node.Key); if (id == 0) continue;
                if (count == observedIds.Length) { Array.Resize(ref observedIds, count * 2); Array.Resize(ref observedValues, count * 2); }
                observedIds[count] = id;
                observedValues[count++] = new Observation { X=p.x,Y=p.y,Z=p.z,Rx=r.x,Ry=r.y,Rz=r.z,
                    Sx=scale.x,Sy=scale.y,Sz=scale.z,Lx=local.x,Ly=local.y,Lz=local.z,
                    Applied=applied.Contains(node.Key) ? (byte)1 : (byte)0, Frame=(ulong)Time.frameCount };
            }
            session.WriteBatch("PoseObserved", observedIds, observedValues, count);
        }

        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        private static string Vector(Vector3 v) => v.x.ToString("R", CultureInfo.InvariantCulture) + "," +
            v.y.ToString("R", CultureInfo.InvariantCulture) + "," + v.z.ToString("R", CultureInfo.InvariantCulture);
        private void OnDisable() => Restore();
    }
}
