using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ecaty
{
    [DefaultExecutionOrder(-500)]
    public sealed class WorkshopOrbit : MonoBehaviour
    {
        private Camera output;
        private RuntimeExperience experience;
        private CinemachineBrain brain;
        private CinemachineCamera rig;
        private CinemachineOrbitalFollow orbit;
        private CinemachineCameraOffset zoomOffset;
        private Vector2 zoomFraming;
        private Transform pivot, rigRoot;
        private float zoom, zoomVelocity, yaw, pitch;
        private Vector2 origin, previous, previousClick, wheelAnchor;
        private float lastWheelTime = float.NegativeInfinity;
        private bool dragZoomAnchored;
        private const float DragThreshold = 9f;
        private const float DoubleClickWindow = .32f;
        private float previousClickTime = float.NegativeInfinity;
        private bool leftWasDown, middleWasDown, rightWasDown, ownsPointer;
        private enum Gesture { Idle, PendingClick, Orbit, Zoom, Pan }
        private Gesture gesture;

        public Vector3 Center => pivot.position;
        public float TargetZoom => zoom;
        public bool IsNavigating => ownsPointer;

        public void Initialize(Camera camera, RuntimeExperience world)
        {
            output = camera;
            experience = world;
            brain = camera.GetComponent<CinemachineBrain>();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.45f);
            rigRoot = new GameObject("Workshop camera targets").transform;
            DontDestroyOnLoad(rigRoot.gameObject);
            pivot = new GameObject("Orbit center").transform;
            pivot.SetParent(rigRoot, false);
            pivot.position = new Vector3(0, .5f, 0);
            zoom = camera.orthographicSize;
            var offset = camera.transform.position - pivot.position;
            yaw = Mathf.Atan2(-offset.x, -offset.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(offset.normalized.y) * Mathf.Rad2Deg;

            var rigObject = new GameObject("Workshop orbit rig");
            rigObject.SetActive(false);
            rigObject.transform.SetParent(rigRoot, false);
            rigObject.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
            rig = rigObject.AddComponent<CinemachineCamera>();
            rig.Priority = -10; // An explicit world camera can take over through Cinemachine's normal blend.
            rig.Follow = pivot;
            rig.LookAt = pivot;
            rig.Lens = LensSettings.FromCamera(camera);
            orbit = rigObject.AddComponent<CinemachineOrbitalFollow>();
            orbit.Radius = offset.magnitude;
            orbit.HorizontalAxis.Value = yaw;
            orbit.VerticalAxis.Range = new Vector2(8, 84);
            orbit.VerticalAxis.Value = pitch;
            var tracking = orbit.TrackerSettings;
            tracking.BindingMode = BindingMode.WorldSpace;
            tracking.PositionDamping = Vector3.zero;
            orbit.TrackerSettings = tracking;
            rigObject.AddComponent<CinemachineHardLookAt>();
            zoomOffset = rigObject.AddComponent<CinemachineCameraOffset>();
            zoomOffset.ApplyAfter = CinemachineCore.Stage.Aim;
            rigObject.SetActive(true);
        }

        private void Update()
        {
            if (!rig) return;
            bool live = brain.IsLiveChild(rig, true);
            bool reset = live && !experience.EditingText && Keyboard.current?.escapeKey.wasPressedThisFrame == true;
            var mouse = Mouse.current;
            if (mouse != null)
                StepPointer(mouse.position.ReadValue(), mouse.leftButton.isPressed, mouse.middleButton.isPressed,
                    mouse.rightButton.isPressed, mouse.scroll.ReadValue().y, !live || reset);
            else
            {
                gesture = Gesture.Idle;
                ownsPointer = false;
            }

            var lens = rig.Lens;
            lens.OrthographicSize = Mathf.SmoothDamp(lens.OrthographicSize, zoom, ref zoomVelocity, .075f,
                Mathf.Infinity, Time.unscaledDeltaTime);
            rig.Lens = lens;
            // Keep the focus point at the same screen coordinate as the lens changes.
            zoomOffset.Offset = new Vector3(-zoomFraming.x * lens.OrthographicSize * output.aspect,
                -zoomFraming.y * lens.OrthographicSize, 0);
        }

        public void StepPointer(Vector2 point, bool left, bool middle, bool right, float scroll = 0, bool cancel = false, float now = -1f)
        {
            bool leftStarted = left && !leftWasDown;
            bool middleStarted = middle && !middleWasDown;
            bool rightStarted = right && !rightWasDown;
            leftWasDown = left;
            middleWasDown = middle;
            rightWasDown = right;
            ownsPointer = gesture == Gesture.Orbit || gesture == Gesture.Zoom || gesture == Gesture.Pan;
            if (cancel) { gesture = Gesture.Idle; previousClickTime = float.NegativeInfinity; return; }

            bool blocked = experience.CoversWorldPointer(point) || experience.WorkshopOwnsPointer || experience.WorldDragActive ||
                experience.Dock && experience.Dock.OwnsSeedPointer;
            if (blocked && leftStarted) previousClickTime = float.NegativeInfinity;
            if (gesture == Gesture.Idle && !blocked)
            {
                origin = previous = point;
                if (leftStarted)
                {
                    if (!experience.Workshop || !experience.Workshop.IsOverPort(point))
                        gesture = Gesture.PendingClick;
                    else previousClickTime = float.NegativeInfinity;
                }
                else if (!left)
                {
                    if (rightStarted) { gesture = Gesture.Zoom; dragZoomAnchored = false; lastWheelTime = float.NegativeInfinity; previousClickTime = float.NegativeInfinity; }
                    else if (middleStarted) { gesture = Gesture.Pan; previousClickTime = float.NegativeInfinity; }
                    else if (Mathf.Abs(scroll) > .01f) { Scroll(point, scroll, now < 0 ? Time.unscaledTime : now); previousClickTime = float.NegativeInfinity; }
                }
            }

            switch (gesture)
            {
                case Gesture.PendingClick:
                    if (blocked) { gesture = Gesture.Idle; previousClickTime = float.NegativeInfinity; }
                    else if ((point - origin).sqrMagnitude > DragThreshold * DragThreshold)
                    {
                        previousClickTime = float.NegativeInfinity;
                        if (left)
                        {
                            gesture = Gesture.Orbit;
                            ownsPointer = true;
                            Orbit(point - origin);
                        }
                        else gesture = Gesture.Idle;
                    }
                    else if (!left)
                    {
                        gesture = Gesture.Idle;
                        float time = now < 0 ? Time.unscaledTime : now;
                        bool doubleClick = time >= previousClickTime && time - previousClickTime <= DoubleClickWindow &&
                            (point - previousClick).sqrMagnitude <= DragThreshold * DragThreshold;
                        previousClick = point;
                        previousClickTime = doubleClick ? float.NegativeInfinity : time;
                        if (doubleClick && experience.TryWorldBounds(output.ScreenPointToRay(point), out Bounds bounds))
                        {
                            Frame(bounds);
                            ownsPointer = true;
                        }
                    }
                    break;
                case Gesture.Orbit:
                    if (!left) gesture = Gesture.Idle;
                    else Orbit(point - previous);
                    break;
                case Gesture.Zoom:
                    ownsPointer = true;
                    if (!right) gesture = Gesture.Idle;
                    else if (Mathf.Abs(point.y - previous.y) > .01f)
                    {
                        if (!dragZoomAnchored)
                        {
                            SetPivotAt(origin);
                            dragZoomAnchored = true;
                        }
                        Zoom(Mathf.Exp(-Mathf.Clamp((point.y - previous.y) * 4.5f / Mathf.Max(1, output.pixelHeight), -3f, 3f)));
                    }
                    break;
                case Gesture.Pan:
                    ownsPointer = true;
                    if (!middle) gesture = Gesture.Idle;
                    else Pan(previous, point);
                    break;
            }
            previous = point;
        }

        private void SetPivotAt(Vector2 point)
        {
            if (!output.pixelRect.Contains(point) ||
                !experience.TryWorldPoint(output.ScreenPointToRay(point), out Vector3 hit)) return;
            var viewport = output.ScreenToViewportPoint(point);
            pivot.position = hit;
            zoomFraming = new Vector2(viewport.x * 2 - 1, viewport.y * 2 - 1);
        }
        private void Scroll(Vector2 point, float delta, float time)
        {
            // Unity normalizes wheel notches to 1 by default. Native Windows mode reports 120.
            bool nativeWindows = InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange &&
                (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer);
            float ticks = delta / (nativeWindows ? 120f : 1f);
            if (time - lastWheelTime > .28f || (point - wheelAnchor).sqrMagnitude > 36f)
            {
                wheelAnchor = point;
                SetPivotAt(point);
            }
            lastWheelTime = time;
            Zoom(Mathf.Exp(-Mathf.Clamp(ticks, -5f, 5f) * .23f));
        }

        private void Frame(Bounds bounds)
        {
            var extents = bounds.extents;
            float ProjectedRadius(Vector3 axis) =>
                Mathf.Abs(axis.x) * extents.x + Mathf.Abs(axis.y) * extents.y + Mathf.Abs(axis.z) * extents.z;
            float height = ProjectedRadius(output.transform.up);
            float width = ProjectedRadius(output.transform.right) / Mathf.Max(.1f, output.aspect);
            pivot.position = bounds.center;
            zoom = Mathf.Clamp(Mathf.Max(height, width) * 1.20f, .5f, 24f);
            zoomFraming = Vector2.zero;
            lastWheelTime = float.NegativeInfinity;
        }

        private void Zoom(float factor) => zoom = Mathf.Clamp(zoom * factor, .5f, 24f);


        public void Orbit(Vector2 delta)
        {
            lastWheelTime = float.NegativeInfinity;
            yaw = Mathf.DeltaAngle(0, yaw + delta.x * (200f / Mathf.Max(1, output.pixelWidth)));
            pitch = Mathf.Clamp(pitch - delta.y * (110f / Mathf.Max(1, output.pixelHeight)), 8, 84);
            orbit.HorizontalAxis.Value = yaw;
            orbit.VerticalAxis.Value = pitch;
        }

        public void Pan(Vector2 from, Vector2 to)
        {
            lastWheelTime = float.NegativeInfinity;
            float unitsPerPixel = output.orthographicSize * 2 / Mathf.Max(1, output.pixelHeight);
            Vector2 delta = (from - to) * unitsPerPixel;
            pivot.position += output.transform.right * delta.x + output.transform.up * delta.y;
        }

        public void FrameSeed(Vector3 center)
        {
            pivot.position = center;
            zoom = 2.4f / Mathf.Min(1, output.aspect);
            zoomFraming = Vector2.zero;
            lastWheelTime = float.NegativeInfinity;
            previousClickTime = float.NegativeInfinity;
        }

        public void ResetView()
        {
            pivot.position = new Vector3(0, .5f, 0);
            zoom = experience.Dock && !experience.Dock.HasWorld ? 2.4f : 5.5f;
            zoomFraming = Vector2.zero;
            zoomOffset.Offset = Vector3.zero;
            orbit.HorizontalAxis.Value = yaw = 0;
            orbit.VerticalAxis.Value = pitch = 33;
            zoomVelocity = 0;
            lastWheelTime = float.NegativeInfinity;
            previousClickTime = float.NegativeInfinity;
            var lens = rig.Lens;
            lens.OrthographicSize = zoom;
            rig.Lens = lens;
            rig.PreviousStateIsValid = false;
        }

        private void OnDisable()
        {
            gesture = Gesture.Idle;
            ownsPointer = false;
            leftWasDown = middleWasDown = rightWasDown = false;
        }
        private void OnDestroy()
        {
            if (rigRoot) Destroy(rigRoot.gameObject);
        }
    }
}

