#if ECATY_CAP_FLEXALON
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Ecaty
{
    public sealed partial class CrystalWorldGallery
    {
        Item pressed, dragged, swapTarget, hovered; Transform ghost, destination;
        readonly List<Material> ghostMaterials = new();
        bool leftWas, rightWas, middleWas; Vector2 previous, down;
        float destinationOpacity; float dragHeight; int selectedLayer = 1; float previousRingAngle, ringDragTurn; bool ringAngleValid;
        UIDocument uiDocument; PanelSettings uiPanel; Button settingsButton;
        public string SwapTargetId => swapTarget?.Entry.Id;
        public Vector3 InvitationPosition => invitation.position;
        public static float RearMultiplier(Vector3 offset, Vector3 facing, float amount)
        {
            offset.y = 0;
            facing.y = 0;
            if (facing.sqrMagnitude < .0001f)
                return 1;
            float back = .5f - .5f * Vector3.Dot(offset.normalized, facing.normalized);
            return 1 - Mathf.Clamp01(amount) * Mathf.Clamp01(facing.magnitude * 3) * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, .95f, back));
        }
        bool HitsInvitation(Vector2 point) => invitation.gameObject.activeInHierarchy && invitation.GetComponent<SphereCollider>().Raycast(output.ScreenPointToRay(point), out _, 300);
        bool PlanePoint(Vector2 point, float height, out Vector3 position)
        {
            var ray = output.ScreenPointToRay(point);
            bool valid = new Plane(Vector3.up, new Vector3(0, height, 0)).Raycast(ray, out float d);
            position = valid ? ray.GetPoint(d) : Vector3.zero;
            return valid;
        }
        void SetupScreenControls()
        {
            uiPanel = ScriptableObject.CreateInstance<PanelSettings>();
            uiPanel.scaleMode = PanelScaleMode.ConstantPixelSize;
            uiPanel.themeStyleSheet = RuntimeComposition.Current.AgentUi.Panel.themeStyleSheet;
            uiPanel.sortingOrder = 80;
            var go = new GameObject("Gallery screen controls", typeof(UIDocument));
            go.transform.SetParent(transform, false);
            uiDocument = go.GetComponent<UIDocument>();
            uiDocument.panelSettings = uiPanel;
            var root = uiDocument.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.right = 0;
            root.style.top = 0;
            root.style.bottom = 0;
            root.style.height = Length.Percent(100);
            var row = new VisualElement { name = "galleryScreenControls" };
            row.style.position = Position.Absolute;
            row.style.right = 18;
            row.style.bottom = 18;
            row.style.flexDirection = FlexDirection.Row;
            root.Add(row);
            settingsButton = Icon("gallerySettings", "\ue8b8", "Settings", () => experience.Dock.ShowSettings());
            row.Add(settingsButton);
            Theme.UI.Apply(root);
        }
        Button Icon(string name, string glyph, string tooltip, Action action)
        {
            var button = new Button(action) { name = name, tooltip = tooltip };
            button.style.width = 44;
            button.style.height = 44;
            button.style.marginLeft = 8;
            button.style.backgroundColor = new Color(.16f, .23f, .25f, .9f);
            button.style.borderTopLeftRadius = button.style.borderTopRightRadius = button.style.borderBottomLeftRadius = button.style.borderBottomRightRadius = 10;
            var label = new Label(glyph) { pickingMode = PickingMode.Ignore };
            label.style.unityFontDefinition = FontDefinition.FromFont(Resources.Load<Font>("MaterialSymbolsRounded"));
            label.style.fontSize = 21;
            label.style.color = new Color(.8f, .88f, .85f);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.flexGrow = 1;
            button.Add(label);
            return button;
        }
        Rect ScreenRect(VisualElement element)
        {
            if (element?.panel == null)
                return new Rect(-100, -100, 1, 1);
            var r = element.worldBound;
            return new Rect(r.x, Screen.height - r.yMax, r.width, r.height);
        }
        bool OnSettings(Vector2 point) => ScreenRect(settingsButton).Contains(point);
        public void StepPointer(Vector2 point, bool left, bool right = false, bool middle = false, float scroll = 0, bool cancel = false)
        {
            bool start = left && !leftWas, release = !left && leftWas;
            Vector2 delta = point - previous;
            if (cancel && experience.Dock && experience.Dock.SettingsOpen)
            {
                experience.Dock.CloseSettings();
                Finish(point, left, right, middle);
                return;
            }
            if (cancel && experience.Dock && experience.Dock.ConsumedEscape)
            {
                Finish(point, left, right, middle);
                return;
            }
            if (cancel)
            {
                if (IsDragging)
                    CancelDrag();
                else if (Creating)
                    LeaveDraft();
                else if (InsideWorld)
                    ReturnToGallery();
                Finish(point, left, right, middle);
                return;
            }
            if (!IsDragging && (chat?.Expanded == true || experience.CoversWorldPointer(point) || OnSettings(point)))
            {
                pressed = null;
                hovered = null;
                Preview("");
                Finish(point, left, right, middle);
                return;
            }
            float ticks = InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange ? scroll / 120 : scroll;
            if (N("portalReturn"))
            {
                Finish(point, left, right, middle);
                return;
            }
            if (Creating)
            {
                if (release)
                    CreateFirstObject();
                if (ticks < 0)
                    LeaveDraft();
                Finish(point, left, right, middle);
                return;
            }
            if (InsideWorld)
            {
                StepExperiencePointer(point, start, release, left, delta);
                StepEnteredCamera(delta, right && rightWas, middle && middleWas, ticks);
                Finish(point, left, right, middle);
                return;
            }
            if (CinematicReturn && (ticks > 0 || start || right && rightWas || middle && middleWas))
                CancelCinematicReturn();
            if (start)
            {
                down = point;
                pressed = HitsInvitation(point) ? null : Pick(point);
                if (pressed != null)
                {
                    selectedLayer = Mathf.Max(1, pressed.Level);
                    Preview(pressed.Entry.Id);
                }
            }
            if (!IsDragging && left && pressed != null && (point - down).sqrMagnitude > 49)
                BeginDrag(pressed);
            if (IsDragging)
            {
                if (PlanePoint(point, dragHeight, out var p))
                    ghost.position = p;
                var previousTarget = swapTarget;
                var hit = Pick(point);
                swapTarget = hit != dragged && hit?.Entry.Active == dragged.Entry.Active ? hit : null;
                if (HitsInvitation(point))
                {
                    swapTarget = null;
                }
                Write("GalleryTreeSettings", "GalleryDrag", new JObject { ["source"] = dragged.Entry.Id, ["target"] = swapTarget?.Entry.Id ?? "", ["active"] = true });
                if (previousTarget != swapTarget)
                {
                    destinationOpacity = 0;
                    destinationSpring.Reset(0);
                    ApplyDestinationOpacity();
                }
                if (swapTarget != null)
                {
                    destination.position = swapTarget.Entry.Anchor.position;
                    destination.rotation = swapTarget.Entry.Anchor.rotation;
                }
                if (release)
                {
                    var source = dragged;
                    var target = swapTarget;
                    ClearGhost();
                    if (target != null)
                        Swap(source.Entry.Id, target.Entry.Id);
                    pressed = null;
                }
                Finish(point, left, right, middle);
                return;
            }
            if (right && rightWas)
            {
                DeactivateZoomAnchor();
                QueueSave();
                entryArmed = false;
                view["yaw"] = (float)view["yaw"] + delta.x * .22f;
                view["pitch"] = Mathf.Clamp((float)view["pitch"] - delta.y * .20f, 0, 90);
                WriteView();
            }
            if (middle && !middleWas)
            {
                selectedLayer = PickLayer(point);
                SetPaused(true);
                ringAngleValid = TryRingAngle(point, selectedLayer, out previousRingAngle);
                if (selectedLayer > 0)
                    ringDragTurn = -layers[selectedLayer - 1].transform.localEulerAngles.y * Mathf.Deg2Rad;
            }
            if (middle && middleWas && selectedLayer > 0)
            {
                if (TryRingAngle(point, selectedLayer, out float angle))
                {
                    if (ringAngleValid)
                    {
                        QueueSave();
                        entryArmed = false;
                        var intent = Read(LayerId(selectedLayer), "GalleryLayerIntent");
                        ringDragTurn += Mathf.DeltaAngle(previousRingAngle, angle) * Mathf.Deg2Rad;
                        intent["angle"] = ringDragTurn;
                        intent["started"] = time;
                        Write(LayerId(selectedLayer), "GalleryLayerIntent", intent);
                    }
                    previousRingAngle = angle;
                    ringAngleValid = true;
                }
                else
                    ringAngleValid = false;
            }
            if (Mathf.Abs(ticks) > .01f)
            {
                QueueSave();
                if (ticks < 0)
                {
                    entryArmed = false;
                    ZoomOutGallery(ticks);
                }
                else
                {
                    DeactivateZoomAnchor();
                    var item = Pick(point);
                    bool invitationHit = HitsInvitation(point);
                    if (item?.Entry.Active == true || invitationHit)
                    {
                        if (Returning)
                        {
                            navigation["returning"] = false;
                            WriteNavigation();
                        }
                        if (!entryArmed)
                            Write("GalleryTreeReturn", "GalleryView", view);
                        string id = invitationHit ? "Invitation" : item.Entry.Id;
                        navigation["target"] = id;
                        WriteNavigation();
                        if (!invitationHit)
                            Preview(item.Entry.Id);
                        Vector3 target = invitationHit ? invitation.position : item.Entry.Anchor.position;
                        view["x"] = target.x;
                        view["y"] = target.y;
                        view["z"] = target.z;
                        entryArmed = true;
                        approachingInvitation = invitationHit;
                        SetPaused(true);
                        view["zoom"] = Mathf.Clamp((float)view["zoom"] * Mathf.Exp(Mathf.Clamp(ticks, 0, 4) * .12f), .65f, 35);
                        WriteView();
                        WriteFocus();
                    }
                    else
                    {
                        if (Returning)
                        {
                            navigation["returning"] = false;
                            WriteNavigation();
                        }
                        view["zoom"] = Mathf.Clamp((float)view["zoom"] * Mathf.Exp(Mathf.Clamp(ticks, 0, 4) * .12f), .65f, 35);
                        WriteView();
                    }
                }

            }
            if (!left && !right && !middle)
            {
                hovered = point.x < 0 || point.y < 0 || point.x > output.pixelWidth || point.y > output.pixelHeight || HitsInvitation(point) ? null : Pick(point);
                Preview(hovered?.Entry.Active == true ? hovered.Entry.Id : "");
            }
            if (release)
            {
                if (pressed == null && HitsInvitation(point) && (point - down).sqrMagnitude < 49)
                    BeginCreation();
                pressed = null;
                SaveArrangement();
            }
            Finish(point, left, right, middle);
        }
        void Finish(Vector2 p, bool l, bool r, bool m)
        {
            previous = p;
            leftWas = l;
            rightWas = r;
            middleWas = m;
        }
        void BeginDrag(Item item)
        {
            dragged = item;
            entryArmed = false;
            dragHeight = item.Entry.Anchor.position.y;
            SetPaused(true);
            view["x"] = currentCenter.x;
            view["y"] = currentCenter.y;
            view["z"] = currentCenter.z;
            view["yaw"] = currentYaw;
            view["pitch"] = currentPitch;
            view["zoom"] = currentZoom;
            WriteView();
            foreach (var i in items)
                i.Entry.Anchor.GetComponent<Flexalon.FlexalonLerpAnimator>().InterpolationSpeed = 0;
            ghost = CloneGhost(item, profile.HeldGhostOpacity, false);
            destination = CloneGhost(item, 0, true);
            destinationOpacity = 0;
            destinationSpring.Reset(0);
            destination.gameObject.SetActive(false);
            Write("GalleryTreeSettings", "GalleryDrag", new JObject { ["source"] = item.Entry.Id, ["target"] = "", ["active"] = true });
        }
        Transform CloneGhost(Item item, float opacity, bool shellOnly)
        {
            var clone = new GameObject("World ghost").transform;
            clone.SetParent(transform, false);
            clone.SetPositionAndRotation(item.Entry.Anchor.position, item.Entry.Anchor.rotation);
            clone.localScale = item.Entry.Anchor.lossyScale;
            if (!shellOnly)
                Instantiate(item.Entry.Content.gameObject, clone, false);
            Instantiate(item.Shell.gameObject, clone, false);
            clone.name = shellOnly ? "World destination ghost" : "Held world ghost";
            foreach (var c in clone.GetComponentsInChildren<Collider>())
                c.enabled = false;
            foreach (var c in clone.GetComponents<MonoBehaviour>())
            {
                c.enabled = false;
                Destroy(c);
            }
            var features = clone.Find("Identifying world features");
            if (shellOnly && features)
                features.gameObject.SetActive(false);
            foreach (var r in clone.GetComponentsInChildren<Renderer>())
            {
                r.forceRenderingOff = false;
                r.SetPropertyBlock(null);
                r.sharedMaterials = r.sharedMaterials.Select(m => { var v = new Material(m); ghostMaterials.Add(v); if (v.HasProperty("_WorldOpacity")) { v.SetFloat("_WorldOpacity", opacity); v.SetFloat("_Opacity", F("sphereOpacity")); v.SetFloat("_Rim", F("rim")); v.SetFloat("_Frost", F("frost")); } if (v.HasProperty("_BaseColor")) { var color = v.color; color.a = opacity; v.color = color; } return v; }).ToArray();
            }
            return clone;
        }
        void ClearGhost()
        {
            if (ghost)
                Destroy(ghost.gameObject);
            if (destination)
                Destroy(destination.gameObject);
            ghost = destination = null;
            dragged = swapTarget = null;
            foreach (var m in ghostMaterials)
                if (m)
                    Destroy(m);
            ghostMaterials.Clear();
            foreach (var item in items)
                if (item.Entry.Anchor)
                    item.Entry.Anchor.GetComponent<Flexalon.FlexalonLerpAnimator>().InterpolationSpeed = F("response");
            if (world && world.Session != null && !world.Session.IsDisposed && world.Session.Lookup("GalleryTreeSettings") != 0)
                Write("GalleryTreeSettings", "GalleryDrag", new JObject { ["source"] = "", ["target"] = "", ["active"] = false });
            if (!disposed && initialized && Returning)
                StartTreeReturn();
        }
        public void CancelDrag()
        {
            ClearGhost();
            SetPaused(InsideWorld || Creating || entryArmed || Returning || FocusedGallery);
        }
        public void Swap(string first, string second)
        {
            var a = byId[first];
            var b = byId[second];
            if (a == b || a.Entry.Active != b.Entry.Active)
                return;
            (a.Entry.Order, b.Entry.Order) = (b.Entry.Order, a.Entry.Order);
            WriteEntry(a);
            WriteEntry(b);
            ReadState();
            Arrange();
            SaveArrangement();
        }
        public void SetVisible(string id, bool visible)
        {
            var item = byId[id];
            if (item.Entry.Active == visible)
                return;
            item.Entry.Active = visible;
            item.Entry.Order = items.Where(i => i != item && i.Entry.Active == visible).Select(i => i.Entry.Order + 1).DefaultIfEmpty(0).Max();
            WriteEntry(item);
            if (!visible)
                Write(id, "GallerySample", new JObject { ["time"] = motionTime });
            foreach (bool active in new[] { true, false })
            {
                int n = 0;
                foreach (var i in items.Where(i => i.Entry.Active == active).OrderBy(i => i.Entry.Order))
                {
                    i.Entry.Order = n++;
                    WriteEntry(i);
                }
            }
            layoutValid = false;
            Arrange();
            SaveArrangement();
        }

        bool approachingInvitation;
        void Preview(string id)
        {
            if (IsDragging || InsideWorld || (string)focus["preview"] == id)
                return;
            focus["preview"] = id;
            WriteFocus();
            nextVisibility = 0;
        }
        void CheckEntry()
        {
            if (!entryArmed || IsDragging || InsideWorld || Creating)
                return;
            Vector3 target = approachingInvitation ? invitation.position : byId.TryGetValue((string)navigation["target"] ?? "", out var i) ? i.Entry.Anchor.position : Vector3.positiveInfinity;
            float threshold = F("diameter") * .5f / (Mathf.Tan(16 * Mathf.Deg2Rad) * Mathf.Max(1, output.aspect) * 1.12f);
            var a = new JObject { ["threshold"] = threshold, ["measured"] = Vector3.Distance(output.transform.position, target), ["armed"] = true };
            Write("GallerySettings", "GalleryApproach", a);
            if ((bool?)Read("GalleryEntryGate", "GalleryEntryRequest")["ready"] == true && (float)a["measured"] <= threshold)
            {
                entryArmed = false;
                if (approachingInvitation)
                    BeginCreation(true);
                else
                    EnterWorld((string)navigation["target"], true);
            }
        }
        public void BeginCreation(bool preserveReturn = false)
        {
            if (IsDragging)
                return;
            if (!preserveReturn)
                Write("GalleryTreeReturn", "GalleryView", view);
            if (InsideWorld)
                ReturnToGallery();
            activity["draft"] = true;
            Write("GalleryTreeSettings", "GalleryTreeActivity", activity);
            SetPaused(true);
            view["x"] = 0;
            view["y"] = 2.1f;
            view["z"] = 0;
            view["zoom"] = 12;
            WriteView();
        }
        void LeaveDraft()
        {
            activity["draft"] = false;
            Write("GalleryTreeSettings", "GalleryTreeActivity", activity);
            StartTreeReturn();
        }
        public string CreateFirstObject()
        {
            if (!Creating)
                return null;
            int sequence = (int)activity["sequence"] + 1;
            string id = "GalleryCreation" + sequence;
            while (world.Session.Lookup(id) != 0)
            {
                id = "GalleryCreation" + (++sequence);
            }
            foreach (var existing in items.Where(i => i.Entry.Active))
            {
                existing.Entry.Order++;
                WriteEntry(existing);
            }
            EnsureEntity(id, 0, 8, true);
            experiences.Create(id);
            AddItem(id);
            activity["sequence"] = sequence;
            activity["draft"] = false;
            Write("GalleryTreeSettings", "GalleryTreeActivity", activity);
            ReadState();
            Arrange(true);
            byId[id].Entry.Anchor.position = invitation.position;
            EnterWorld(id, true);
            layoutValid = false;
            return id;
        }
        public bool EnterWorld(string id, bool preserveReturn = false)
        {
            if (IsDragging || !byId.TryGetValue(id, out var item) || !item.Entry.Active)
                return false;
            if (!preserveReturn)
                Write("GalleryTreeReturn", "GalleryView", view);
            bool resume = CurrentWorldId == id && stage.Resident != null || (float)Read(id, "GalleryResidence")["elapsed"] > 0;
            focus["preview"] = id;
            focus["visit"] = (int)focus["visit"] + 1;
            int target = resume ? (int)Read(id, "GalleryResidence")["target"] : Math.Abs((int)focus["visit"] * 31 + item.Entry.Kind) % item.Entry.Preview.FocusCount;
            focus["target"] = target;
            DeactivateZoomAnchor();
            TriggerGlass(true);
            StoreEntryPortal(item);
            navigation["portalReturn"] = false;
            if (!resume)
            {
                var rotation = Quaternion.Inverse(item.Entry.Content.rotation) * output.transform.rotation;
                var forward = rotation * Vector3.forward;
                var position = item.Entry.Content.InverseTransformPoint(output.transform.position);
                float d = Mathf.Max(.2f, Vector3.Dot(item.Entry.NaturalBounds.center - position, forward));
                var center = position + forward * d;
                Write(id, "GalleryView", new JObject { ["x"] = center.x, ["y"] = center.y, ["z"] = center.z, ["yaw"] = OrbitYaw(rotation), ["pitch"] = OrbitPitch(rotation), ["zoom"] = d, ["columns"] = 6 });
            }
            entryArmed = false;
            navigation["returning"] = false;
            navigation["exitArmed"] = false;
            navigation["exitIdle"] = false;
            navigation["exitPull"] = 0;
            navigation["entryDistance"] = (float)Read(id, "GalleryView")["zoom"];
            navigation["boundary"] = Mathf.Max(item.Entry.NaturalBounds.extents.magnitude * 4, (float)navigation["entryDistance"] * profile.ExitDistanceMultiplier);
            WriteNavigation();
            float elapsed = WorldTime(id);
            stage.Prepare(item.Entry, elapsed);
            stage.Pose(Read(id, "GalleryView"), target, 0, true, false);
            stage.Render(true);
            stage.Promote();
            var residence = Read(id, "GalleryResidence");
            residence["elapsed"] = elapsed;
            residence["started"] = time;
            residence["target"] = target;
            Write(id, "GalleryResidence", residence);
            focus["current"] = id;
            focus["inside"] = true;
            focus["following"] = false;
            WriteFocus();
            traveling = !resume;
            travel = 0;
            travelStart = Time.unscaledTime + .8f;
            SetPaused(true);
            SaveArrangement();
            return true;
        }
        float WorldTime(string id)
        {
            var data = Read(id, "GalleryResidence");
            return (float)data["elapsed"] + (InsideWorld && CurrentWorldId == id ? Mathf.Max(0, time - (float)data["started"]) : 0);
        }
        public void ReturnToGallery()
        {
            if (!InsideWorld)
                return;
            experiences.CaptureResident();
            EnsureEntryPortal();
            if (stage.ActualView != null)
                Write(CurrentWorldId, "GalleryView", stage.ActualView);
            var data = Read(CurrentWorldId, "GalleryResidence");
            data["elapsed"] = WorldTime(CurrentWorldId);
            data["started"] = 0;
            Write(CurrentWorldId, "GalleryResidence", data);
            focus["inside"] = false;
            focus["following"] = false;
            focus["preview"] = "";
            WriteFocus();
            traveling = false;
            Write("GalleryExitStart", "GalleryView", stage.ActualView ?? Read(CurrentWorldId, "GalleryView"));
            navigation["portalReturn"] = true;
            navigation["portalAt"] = time;
            navigation["returning"] = true;
            navigation["exitArmed"] = false;
            navigation["exitPull"] = 0;
            WriteNavigation();
            SetPaused(true);
            SaveArrangement();
        }
        void ResetGalleryCamera()
        {
            view["zoom"] = 1;
            view["x"] = 0;
            view["y"] = GalleryCenterY;
            view["z"] = 0;
            WriteView();
        }
        float GalleryCenterY
        {
            get
            {
                int deepest = 0;
                foreach (var item in items)
                    if (item.Entry.Active)
                        deepest = Mathf.Max(deepest, item.Level);
                return 2.1f - deepest * F("layerGap") * .5f;
            }
        }
        void StepEnteredCamera(Vector2 delta, bool orbit, bool pan, float ticks)
        {
            if (Mathf.Abs(ticks) < .01f && N("exitArmed"))
            {
                navigation["exitIdle"] = true;
                WriteNavigation();
            }
            if (!orbit && !pan && Mathf.Abs(ticks) < .01f)
                return;
            QueueSave();
            if (stage.ActualView != null && (traveling || (bool?)focus["following"] == true))
                Write(CurrentWorldId, "GalleryView", stage.ActualView);
            traveling = false;
            focus["following"] = false;
            WriteFocus();
            var v = Read(CurrentWorldId, "GalleryView");
            if (orbit)
            {
                v["yaw"] = (float)v["yaw"] + delta.x * .22f;
                v["pitch"] = Mathf.Clamp((float)v["pitch"] - delta.y * .18f, -90, 90);
            }
            if (pan)
            {
                var shift = (-stage.Camera.transform.right * delta.x - stage.Camera.transform.up * delta.y) * ((float)v["zoom"] / Mathf.Max(1, output.pixelHeight));
                v["x"] = (float)v["x"] + shift.x;
                v["y"] = (float)v["y"] + shift.y;
                v["z"] = (float)v["z"] + shift.z;
            }
            float desired = Mathf.Clamp((float)v["zoom"] * Mathf.Exp(-Mathf.Clamp(ticks, -4, 4) * .12f), .10f, 10000);
            float boundary = (float)navigation["boundary"];
            if (ticks > 0)
            {
                navigation["exitArmed"] = false;
                navigation["exitIdle"] = false;
                navigation["exitPull"] = 0;
                WriteNavigation();
            }
            if (ticks < 0 && desired >= boundary)
            {
                if (N("exitArmed") && N("exitIdle") && time - (float)navigation["exitAt"] >= profile.ExitBoundaryPause)
                {
                    ReturnToGallery();
                    return;
                }
                desired = boundary;
                if (!N("exitArmed"))
                {
                    navigation["exitAt"] = time;
                    navigation["exitArmed"] = true;
                    navigation["exitIdle"] = false;
                }
                navigation["exitPull"] = 1;
                WriteNavigation();
            }
            v["zoom"] = desired;
            Write(CurrentWorldId, "GalleryView", v);
        }
        void UpdateWorld()
        {
            if (N("portalReturn"))
            {
                UpdatePortalReturn();
                return;
            }
            entryBlend = Mathf.Lerp(entryBlend, InsideWorld ? 1 : 0, 1 - Mathf.Exp(-7 * Time.unscaledDeltaTime));
            if (!InsideWorld)
            {
                if (entryBlend < .005f)
                {
                    bool wasShown = stage.Shown != null;
                    stage.ClearPreview();
                    if (wasShown)
                        RefreshExperiencePreview(CurrentWorldId);
                }
                return;
            }
            var item = byId[CurrentWorldId];
            stage.Prepare(item.Entry, WorldTime(CurrentWorldId));
            if (traveling)
                travel = Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.unscaledTime - travelStart) / 1.8f));
            var pose = Read(CurrentWorldId, "GalleryView");
            if (!traveling && (bool?)focus["following"] == true)
            {
                var track = Read(CurrentWorldId, "GalleryTrack");
                Vector3 next = stage.FocusLocalPosition((int)focus["target"]);
                Vector3 delta = next - new Vector3((float)track["x"], (float)track["y"], (float)track["z"]);
                pose["x"] = (float)pose["x"] + delta.x;
                pose["y"] = (float)pose["y"] + delta.y;
                pose["z"] = (float)pose["z"] + delta.z;
                Write(CurrentWorldId, "GalleryView", pose);
                Write(CurrentWorldId, "GalleryTrack", new JObject { ["x"] = next.x, ["y"] = next.y, ["z"] = next.z });
            }
            if (N("exitArmed"))
                pose = (JObject)pose.DeepClone();
            if (N("exitArmed"))
                pose["zoom"] = (float)pose["zoom"] * (1 + exitFeedback.Value * profile.ExitElasticZoom);
            stage.Pose(pose, (int)focus["target"], traveling ? travel : 0, false, false);
            stage.Render(true);
            if (traveling && travel >= .999f)
            {
                Write(CurrentWorldId, "GalleryView", stage.ActualView);
                Vector3 target = stage.FocusLocalPosition((int)focus["target"]);
                Write(CurrentWorldId, "GalleryTrack", new JObject { ["x"] = target.x, ["y"] = target.y, ["z"] = target.z });
                focus["following"] = true;
                WriteFocus();
                traveling = false;
            }
        }
        JObject CaptureLayer(int layer)
        {
            var state = Read(LayerId(layer), "GalleryLayerState");
            var intent = Read(LayerId(layer), "GalleryLayerIntent");
            return new JObject { ["turn"] = (float)state["turn"], ["glowPhase"] = (float)intent["glowPhase"], ["glowRate"] = (float)intent["glowRate"] };
        }
        public string CaptureArrangement()
        {
            var savedActivity = (JObject)activity.DeepClone();
            savedActivity["elapsed"] = motionTime;
            savedActivity["started"] = 0;
            return new JObject { ["version"] = 1, ["worlds"] = experiences.Capture(), ["view"] = view, ["returnView"] = Read("GalleryTreeReturn", "GalleryView"), ["focus"] = focus, ["activity"] = savedActivity, ["navigation"] = navigation, ["cameraAnchor"] = Read("GalleryTreeSettings", "GalleryCameraAnchor"), ["portal"] = new JObject { ["world"] = Read("GalleryTreeSettings", "GalleryPortal"), ["gallery"] = Read("GalleryEntryPortal", "GalleryView"), ["local"] = Read("GalleryPortalWorld", "GalleryView") }, ["layers"] = new JArray(Enumerable.Range(1, 6).Select(i => (JToken)CaptureLayer(i))), ["entries"] = new JArray(items.Select(i => new JObject { ["id"] = i.Entry.Id, ["state"] = Read(i.Entry.Id, "GalleryEntry"), ["track"] = Read(i.Entry.Id, "GalleryTrack"), ["camera"] = InsideWorld && CurrentWorldId == i.Entry.Id && stage.ActualView != null ? stage.ActualView : Read(i.Entry.Id, "GalleryView"), ["sample"] = Read(i.Entry.Id, "GallerySample"), ["residence"] = new JObject { ["elapsed"] = WorldTime(i.Entry.Id), ["started"] = 0, ["target"] = (int)Read(i.Entry.Id, "GalleryResidence")["target"] } })) }.ToString();
        }
        public void RestoreArrangement(string json, bool launching = false)
        {
            var data = JObject.Parse(json);
            if ((int?)data["version"] != 1 || data["entries"] is not JArray entries || entries.Count > 80)
                throw new ArgumentException("Invalid layered gallery arrangement");
            foreach (var token in entries)
            {
                string id = (string)token["id"];
                var d = (JObject)token["state"];
                if (!System.Text.RegularExpressions.Regex.IsMatch(id ?? "", "^Gallery[A-Za-z0-9_]+$") || (int)d["kind"] < 0 || (int)d["kind"] > 8)
                    throw new ArgumentException("Invalid world entry");
            }
            if (data["worlds"] is JArray definitions && definitions.Any(d => !entries.Any(e => (string)e["id"] == (string)d["id"])))
                throw new ArgumentException("Saved content is missing its gallery entry");
            CancelDrag();
            stage.ResetContexts();
            foreach (var token in entries)
            {
                var state = (JObject)token["state"];
                EnsureEntity((string)token["id"], (int)state["order"], (int)state["kind"], (bool)state["active"]);
            }
            experiences.Restore(data["worlds"] as JArray);
            navigation = (JObject)Read("GalleryTreeSettings", "GalleryTreeNavigation").DeepClone();
            if (data["navigation"] is JObject savedNavigation)
                navigation.Merge(savedNavigation);
            navigation["returning"] = false;
            navigation["portalReturn"] = false;
            navigation["exitPull"] = 0;
            navigation["approach"] = false;
            navigation["exitArmed"] = false;
            navigation["exitIdle"] = false;
            navigation["exitPull"] = 0;
            WriteNavigation();
            centerVelocity = Vector3.zero;
            zoomVelocity = 0;
            var wanted = new System.Collections.Generic.HashSet<string>(entries.Select(e => (string)e["id"]));
            foreach (var extra in items.Where(i => !wanted.Contains(i.Entry.Id)).ToArray())
            {
                extra.Entry.Preview.Dispose();
                Destroy(extra.Entry.Anchor.gameObject);
                items.Remove(extra);
                byId.Remove(extra.Entry.Id);
                world.Session.DeleteEntity("GalleryCatalog_" + extra.Entry.Id);
                world.Session.DeleteEntity(extra.Entry.Id);
            }
            foreach (var token in entries)
            {
                string id = (string)token["id"];
                var d = (JObject)token["state"];
                EnsureEntity(id, (int)d["order"], (int)d["kind"], (bool)d["active"]);
                Write(id, "GalleryEntry", d);
                if (!byId.ContainsKey(id))
                    AddItem(id);
                Write(id, "GalleryView", (JObject)token["camera"]);
                Write(id, "GallerySample", (JObject)token["sample"]);
                if (token["track"] is JObject track)
                    Write(id, "GalleryTrack", track);
                var residence = (JObject)token["residence"];
                residence["started"] = time;
                Write(id, "GalleryResidence", residence);
            }
            view = (JObject)data["view"];
            Write("GalleryTreeReturn", "GalleryView", data["returnView"] as JObject ?? view);
            focus = (JObject)data["focus"];
            focus["preview"] = "";
            activity = (JObject)data["activity"];
            activity["rate"] = 1;
            activity["elapsed"] = (float?)activity["elapsed"] ?? 0;
            activity["started"] = time;
            WriteView();
            WriteFocus();
            Write("GalleryTreeSettings", "GalleryTreeActivity", activity);
            if (data["layers"] is JArray turns)
                for (int i = 0; i < Mathf.Min(6, turns.Count); i++)
                {
                    var intent = Read(LayerId(i + 1), "GalleryLayerIntent");
                    intent["angle"] = (float)turns[i]["turn"];
                    intent["started"] = time;
                    if (turns[i]["glowPhase"] != null)
                        intent["glowPhase"] = (float)turns[i]["glowPhase"];
                    if (turns[i]["glowRate"] != null)
                        intent["glowRate"] = (float)turns[i]["glowRate"];
                    Write(LayerId(i + 1), "GalleryLayerIntent", intent);
                }
            if (data["portal"] is JObject portal)
            {
                Write("GalleryTreeSettings", "GalleryPortal", (JObject)portal["world"]);
                Write("GalleryEntryPortal", "GalleryView", (JObject)portal["gallery"]);
                Write("GalleryPortalWorld", "GalleryView", (JObject)portal["local"]);
            }
            foreach (var savedItem in items)
                if (experiences.Has(savedItem.Entry.Id))
                    RefreshExperiencePreview(savedItem.Entry.Id);
            if (data["cameraAnchor"] is JObject savedAnchor)
                Write("GalleryTreeSettings", "GalleryCameraAnchor", savedAnchor);
            else
                WriteAnchor(false, new Vector3((float)view["x"], (float)view["y"], (float)view["z"]), (float)view["zoom"]);
            if (launching && !InsideWorld)
            {
                view["pitch"] = 90;
                WriteView();
            }
            ResetReturnPresentation();
            foreach (var restoredItem in items)
            {
                restoredItem.Alpha = 1;
                restoredItem.Highlight = 0;
                restoredItem.OpacitySpring.Reset(1);
                restoredItem.HighlightSpring.Reset(0);
            }
            ReadState();
            Arrange(true);
            for (int layer = 0; layer < layers.Count; layer++)
                layers[layer].transform.localRotation = Quaternion.Euler(0, -(float)Read(LayerId(layer + 1), "GalleryLayerState")["turn"] * Mathf.Rad2Deg, 0);
            ApplyCamera(true);
            nextVisibility = 0;
            if (InsideWorld && byId.TryGetValue(CurrentWorldId, out var item))
            {
                stage.Prepare(item.Entry, WorldTime(CurrentWorldId));
                stage.Promote();
                stage.Pose(Read(CurrentWorldId, "GalleryView"), (int)focus["target"], 0, true, false);
                stage.Render(true);
                entryBlend = 1;
            }
        }
        public void SaveArrangement()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
            string temp = SavePath + ".tmp";
            File.WriteAllText(temp, CaptureArrangement());
            if (File.Exists(SavePath))
                File.Replace(temp, SavePath, null);
            else
                File.Move(temp, SavePath);
        }
        static string PreviousArrangementPath => Path.Combine(Application.persistentDataPath, "GalleryPrototype", "arrangement.json");
        void LoadArrangement()
        {
            if (File.Exists(SavePath))
            {
                try
                {
                    RestoreArrangement(File.ReadAllText(SavePath), true);
                }
                catch (Exception e) { Debug.LogWarning("Layered gallery could not resume: " + e.Message); }
                return;
            }
            // Import the eight existing worlds without rewriting the earlier flat-gallery save.
            if (File.Exists(PreviousArrangementPath))
            {
                try
                {
                    var old = JObject.Parse(File.ReadAllText(PreviousArrangementPath));
                    foreach (var item in (JArray)old["entries"])
                    {
                        string id = (string)item["id"];
                        if (byId.TryGetValue(id, out var known))
                        {
                            Write(id, "GalleryEntry", (JObject)item["state"]);
                            foreach (string key in new[] { "camera", "sample", "residence" })
                                if (item[key] is JObject c)
                                    Write(id, key == "camera" ? "GalleryView" : key == "sample" ? "GallerySample" : "GalleryResidence", c);
                        }
                    }
                }
                catch (Exception e) { Debug.LogWarning("Earlier gallery import: " + e.Message); }
            }
        }
    }
}
#endif
